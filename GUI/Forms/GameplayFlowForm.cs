using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GameplayFlow;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Forms;

internal sealed class GameplayFlowForm : ThemedForm
{
    private enum DirtyChoice
    {
        Save,
        Discard,
        Cancel,
    }

    private sealed record DisplayItem<T>(string Text, T Value)
    {
        public override string ToString() => Text;
    }

    private sealed record GroupRun(Guid GroupId, int StartIndex);

    private readonly GameplayFlowSession session;
    private readonly GameplayFlowStateAdapter adapter;
#pragma warning disable CA2213 // Controls are owned and disposed by the form's Controls collection
    private readonly ComboBox flowComboBox;
    private readonly Label breadcrumbLabel;
    private readonly ThemedButton backButton;
    private readonly TreeView timelineTree;
    private readonly ThemedTextBox titleTextBox;
    private readonly ThemedTextBox descriptionTextBox;
    private readonly ThemedTextBox tagsTextBox;
    private readonly ComboBox groupComboBox;
    private readonly CheckedListBox nextNodesList;
    private readonly ListBox annotationList;
    private readonly ThemedTextBox annotationNameTextBox;
    private readonly ThemedTextBox annotationDescriptionTextBox;
    private readonly ThemedTextBox annotationPositionTextBox;
    private readonly Label snapshotLabel;
    private readonly ThemedTextBox issuesTextBox;
    private readonly ThemedButton childFlowButton;
#pragma warning restore CA2213
    private readonly HashSet<(Guid GroupId, int StartIndex)> collapsedGroupRuns = [];
    private IReadOnlyList<GameplayFlowIssue> restoreIssues = [];
    private Guid? selectedGroupId;
    private bool updatingControls;
    private bool draggingNode;
    private bool skipClosePrompt;

#pragma warning disable CA2000 // Controls are transferred to the form and disposed through its Controls collection
    public GameplayFlowForm(GameplayFlowSession session, GameplayFlowStateAdapter adapter)
    {
        this.session = session;
        this.adapter = adapter;

        Text = "Gameplay Flows";
        ClientSize = new Size(1050, 720);
        MinimumSize = new Size(780, 520);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6),
            ColumnCount = 2,
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var documentTools = AddToolbarRow(toolbar, "Flow document");
        AddButton(documentTools, "Open", OnOpen);
        AddButton(documentTools, "Save", (_, _) => SaveDocument(saveAs: false));
        AddButton(documentTools, "Save as", (_, _) => SaveDocument(saveAs: true));
        AddButton(documentTools, "Copy JSON", OnCopyJson);
        AddButton(documentTools, "Paste JSON", OnPasteJson);

        var flowTools = AddToolbarRow(toolbar, "Current flow");
        flowComboBox = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
        flowComboBox.SelectedIndexChanged += OnFlowSelectionChanged;
        flowTools.Controls.Add(flowComboBox);
        AddButton(flowTools, "New", OnNewFlow);
        AddButton(flowTools, "Rename", OnRenameFlow);
        AddButton(flowTools, "Delete", OnDeleteFlow);

        var breadcrumbPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 5),
        };
        backButton = new ThemedButton { Text = "Back", AutoSize = true };
        backButton.Click += OnReturnToParentFlow;
        breadcrumbLabel = new Label { AutoSize = true, Padding = new Padding(6, 7, 0, 0) };
        breadcrumbPanel.Controls.Add(backButton);
        breadcrumbPanel.Controls.Add(breadcrumbLabel);

        timelineTree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            AllowDrop = true,
        };
        timelineTree.AfterSelect += OnTimelineSelectionChanged;
        timelineTree.MouseDown += OnTimelineMouseDown;
        timelineTree.ItemDrag += OnTimelineItemDrag;
        timelineTree.DragEnter += OnTimelineDragEnter;
        timelineTree.DragOver += OnTimelineDragEnter;
        timelineTree.DragDrop += OnTimelineDragDrop;
        timelineTree.BeforeCollapse += OnGroupCollapsing;
        timelineTree.AfterExpand += OnGroupExpanded;

        var timelineTools = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0),
            ColumnCount = 2,
        };
        timelineTools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        timelineTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        var nodeTools = AddToolbarRow(timelineTools, "Nodes");
        AddButton(nodeTools, "Capture", OnCaptureNode);
        AddButton(nodeTools, "Delete", OnDeleteNode);
        var groupTools = AddToolbarRow(timelineTools, "Groups");
        AddButton(groupTools, "New", OnNewGroup);
        AddButton(groupTools, "Rename", OnRenameGroup);
        AddButton(groupTools, "Delete", OnDeleteGroup);

        var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        leftPanel.Controls.Add(timelineTree);
        leftPanel.Controls.Add(timelineTools);
        leftPanel.Controls.Add(breadcrumbPanel);

        var nodeDetails = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(6),
        };
        nodeDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var contentDetails = AddDetailsSection(nodeDetails, "Content");
        titleTextBox = AddTextField(contentDetails, "Title", multiline: false, height: 28);
        descriptionTextBox = AddTextField(contentDetails, "Narrative", multiline: true, height: 100);
        tagsTextBox = AddTextField(contentDetails, "Tags", multiline: false, height: 28);
        groupComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        groupComboBox.SelectedIndexChanged += OnGroupSelectionChanged;
        AddRow(contentDetails, "Group", groupComboBox);

        var recordedStateDetails = AddDetailsSection(nodeDetails, "Recorded state");
        snapshotLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 6) };
        AddRow(recordedStateDetails, "Contains", snapshotLabel);
        var snapshotButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        AddButton(snapshotButtons, "Replace with current view", OnUpdateSnapshot);
        AddRow(recordedStateDetails, string.Empty, snapshotButtons);

        var flowPathDetails = AddDetailsSection(nodeDetails, "Flow path");
        nextNodesList = new CheckedListBox { Dock = DockStyle.Fill, Height = 95, CheckOnClick = true };
        nextNodesList.ItemCheck += OnNextNodeItemCheck;
        AddRow(flowPathDetails, "Leads to", nextNodesList);
        var childFlowButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        childFlowButton = AddButton(childFlowButtons, "Create child flow", OnChildFlow);
        AddRow(flowPathDetails, "Child flow", childFlowButtons);

        var navigationDetails = AddDetailsSection(nodeDetails, "Navigation");
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        AddButton(navigation, "Previous", (_, _) => SelectReadingNeighbor(-1));
        AddButton(navigation, "Next", (_, _) => SelectReadingNeighbor(1));
        AddButton(navigation, "Follow flow", OnFollowFlow);
        AddRow(navigationDetails, string.Empty, navigation);

        var statusDetails = AddDetailsSection(nodeDetails, "Status");
        issuesTextBox = AddTextField(statusDetails, "Messages", multiline: true, height: 78);
        issuesTextBox.ReadOnly = true;

        var annotationDetails = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(10),
        };
        annotationDetails.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        annotationDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        annotationList = new ListBox { Dock = DockStyle.Fill, Height = 85 };
        annotationList.SelectedIndexChanged += OnAnnotationSelectionChanged;
        AddRow(annotationDetails, "Annotations", annotationList);
        var annotationButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        AddButton(annotationButtons, "Add at camera", OnAddAnnotation);
        AddButton(annotationButtons, "From marker", OnConvertMarker);
        AddButton(annotationButtons, "Delete", OnDeleteAnnotation);
        AddRow(annotationDetails, string.Empty, annotationButtons);
        annotationNameTextBox = AddTextField(annotationDetails, "Name", multiline: false, height: 28);
        annotationDescriptionTextBox = AddTextField(annotationDetails, "Narrative", multiline: true, height: 75);
        annotationPositionTextBox = AddTextField(annotationDetails, "Position", multiline: false, height: 28);
        var updateAnnotationButton = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        AddButton(updateAnnotationButton, "Update annotation", OnUpdateAnnotation);
        AddRow(annotationDetails, string.Empty, updateAnnotationButton);

        titleTextBox.TextChanged += OnNodeTextChanged;
        descriptionTextBox.TextChanged += OnNodeTextChanged;
        tagsTextBox.TextChanged += OnNodeTextChanged;

        var nodeScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        nodeScroll.Controls.Add(nodeDetails);
        var annotationScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        annotationScroll.Controls.Add(annotationDetails);
        var detailsTabs = new ThemedTabControl { Dock = DockStyle.Fill };
        var nodeTab = new TabPage("Node");
        nodeTab.Controls.Add(nodeScroll);
        var annotationTab = new TabPage("Annotations");
        annotationTab.Controls.Add(annotationScroll);
        detailsTabs.TabPages.Add(nodeTab);
        detailsTabs.TabPages.Add(annotationTab);

        var initialTimelineWidth = Math.Max(400, timelineTools.PreferredSize.Width + leftPanel.Padding.Horizontal);
        ClientSize = new Size(Math.Max(ClientSize.Width, initialTimelineWidth + 650), ClientSize.Height);
        var splitter = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = ClientSize,
            Panel1MinSize = 240,
            Panel2MinSize = 390,
            SplitterDistance = initialTimelineWidth,
        };
        splitter.Panel1.Controls.Add(leftPanel);
        splitter.Panel2.Controls.Add(detailsTabs);

        Controls.Add(splitter);
        Controls.Add(toolbar);

        session.Changed += OnSessionChanged;
        RefreshAll(restoreSelection: false);
    }
#pragma warning restore CA2000

    public void CloseWithoutPrompt()
    {
        skipClosePrompt = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!skipClosePrompt && !ConfirmDirtyChanges())
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            session.Changed -= OnSessionChanged;
        }

        base.Dispose(disposing);
    }

    private void OnSessionChanged(object? sender, EventArgs e) => UpdateWindowTitle();

    private void RefreshAll(bool restoreSelection)
    {
        updatingControls = true;
        try
        {
            RefreshFlows();
            RefreshBreadcrumb();
            RefreshTimeline();
            RefreshDetails();
            UpdateWindowTitle();
        }
        finally
        {
            updatingControls = false;
        }

        if (restoreSelection)
        {
            RestoreCurrentNode();
        }
    }

    private void RefreshFlows()
    {
        flowComboBox.Items.Clear();
        foreach (var flow in session.GetRootFlows())
        {
            flowComboBox.Items.Add(new DisplayItem<GameplayFlow>(flow.Title, flow));
        }

        var currentRoot = FindRootFlow(session.CurrentFlow);
        flowComboBox.SelectedItem = flowComboBox.Items
            .Cast<DisplayItem<GameplayFlow>>()
            .FirstOrDefault(item => ReferenceEquals(item.Value, currentRoot));
    }

    private void RefreshBreadcrumb()
    {
        var chain = GetFlowChain(session.CurrentFlow);
        breadcrumbLabel.Text = chain.Count == 0
            ? "No flow"
            : string.Join(" > ", chain.Select(static item => item.Flow.Title));
        backButton.Enabled = chain.Count > 1;
    }

    private void RefreshTimeline()
    {
        timelineTree.BeginUpdate();
        timelineTree.Nodes.Clear();
        if (session.CurrentFlow is not { } flow)
        {
            timelineTree.EndUpdate();
            return;
        }

        var prefix = GetNumberPrefix(flow);
        var currentGroups = new List<GameplayFlowGroup>();
        var groupNodes = new List<TreeNode>();
        for (var i = 0; i < flow.Nodes.Count; i++)
        {
            var node = flow.Nodes[i];
            var groups = GetGroupPath(flow, node.GroupId);
            var commonGroupCount = 0;
            while (commonGroupCount < currentGroups.Count
                && commonGroupCount < groups.Count
                && ReferenceEquals(currentGroups[commonGroupCount], groups[commonGroupCount]))
            {
                commonGroupCount++;
            }

            currentGroups.RemoveRange(commonGroupCount, currentGroups.Count - commonGroupCount);
            groupNodes.RemoveRange(commonGroupCount, groupNodes.Count - commonGroupCount);
            for (var groupIndex = commonGroupCount; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                var newGroupNode = new TreeNode(group.Title) { Tag = new GroupRun(group.Id, i) };
                if (groupNodes.Count == 0)
                {
                    timelineTree.Nodes.Add(newGroupNode);
                }
                else
                {
                    groupNodes[^1].Nodes.Add(newGroupNode);
                }

                currentGroups.Add(group);
                groupNodes.Add(newGroupNode);
            }

            var number = $"{prefix}{i + 1:00}";
            var indicators = GetNodeIndicators(flow, node);
            var treeNode = new TreeNode($"{number}  {node.Title}{indicators}") { Tag = node };
            if (groupNodes.Count == 0)
            {
                timelineTree.Nodes.Add(treeNode);
            }
            else
            {
                groupNodes[^1].Nodes.Add(treeNode);
            }

            if (ReferenceEquals(node, session.CurrentNode))
            {
                timelineTree.SelectedNode = treeNode;
            }
        }

        var displayedGroups = new Dictionary<Guid, TreeNode>();
        foreach (var treeNode in EnumerateTreeNodes(timelineTree.Nodes))
        {
            if (treeNode.Tag is GroupRun run)
            {
                displayedGroups.TryAdd(run.GroupId, treeNode);
            }
        }

        foreach (var (group, _) in EnumerateGroups(flow))
        {
            if (displayedGroups.ContainsKey(group.Id))
            {
                continue;
            }

            var groupNode = new TreeNode(group.Title) { Tag = new GroupRun(group.Id, -1) };
            if (group.ParentGroupId is { } parentGroupId && displayedGroups.TryGetValue(parentGroupId, out var parentNode))
            {
                parentNode.Nodes.Add(groupNode);
            }
            else
            {
                timelineTree.Nodes.Add(groupNode);
            }

            displayedGroups.Add(group.Id, groupNode);
        }

        if (selectedGroupId is { } selectedId && displayedGroups.TryGetValue(selectedId, out var selectedGroupNode))
        {
            timelineTree.SelectedNode = selectedGroupNode;
        }
        else if (selectedGroupId != null)
        {
            selectedGroupId = null;
        }

        foreach (var groupNode in EnumerateTreeNodes(timelineTree.Nodes))
        {
            if (groupNode.Tag is GroupRun run && !collapsedGroupRuns.Contains((run.GroupId, run.StartIndex)))
            {
                groupNode.Expand();
            }
        }

        timelineTree.EndUpdate();
    }

    private void RefreshDetails()
    {
        var node = session.CurrentNode;
        var enabled = node != null;
        titleTextBox.Enabled = enabled;
        descriptionTextBox.Enabled = enabled;
        tagsTextBox.Enabled = enabled;
        groupComboBox.Enabled = enabled;
        nextNodesList.Enabled = enabled;
        annotationList.Enabled = enabled;
        childFlowButton.Enabled = enabled;

        titleTextBox.Text = node?.Title ?? string.Empty;
        descriptionTextBox.Text = node?.Description ?? string.Empty;
        tagsTextBox.Text = node == null ? string.Empty : string.Join(", ", node.Tags);
        snapshotLabel.Text = node == null
            ? "Select or capture a node."
            : $"Camera; {node.SelectedEntities.Count} selected; {node.Annotations.Count} annotations; visibility in {node.Visibility.Count} scenes";
        childFlowButton.Text = node?.ChildFlowId == null ? "Create child flow" : "Enter child flow";

        groupComboBox.Items.Clear();
        groupComboBox.Items.Add(new DisplayItem<GameplayFlowGroup?>("(No group)", null));
        if (session.CurrentFlow is { } flow)
        {
            foreach (var (group, depth) in EnumerateGroups(flow))
            {
                groupComboBox.Items.Add(new DisplayItem<GameplayFlowGroup?>($"{new string(' ', depth * 2)}{group.Title}", group));
            }
        }

        groupComboBox.SelectedItem = groupComboBox.Items
            .Cast<DisplayItem<GameplayFlowGroup?>>()
            .FirstOrDefault(item => item.Value?.Id == node?.GroupId)
            ?? groupComboBox.Items[0];

        nextNodesList.Items.Clear();
        if (node != null && session.CurrentFlow is { } currentFlow)
        {
            foreach (var candidate in currentFlow.Nodes.Where(candidate => !ReferenceEquals(candidate, node)))
            {
                var item = new DisplayItem<GameplayFlowNode>(candidate.Title, candidate);
                nextNodesList.Items.Add(item, node.NextNodeIds.Contains(candidate.Id));
            }
        }

        annotationList.Items.Clear();
        if (node != null)
        {
            foreach (var annotation in node.Annotations)
            {
                annotationList.Items.Add(new DisplayItem<GameplayFlowAnnotation>(annotation.Name, annotation));
            }
        }

        ClearAnnotationEditor();
        RefreshIssues();
    }

    private void RestoreCurrentNode()
    {
        if (session.CurrentNode is not { } node
            || session.Document.Maps.FirstOrDefault(map => map.Id == node.MapId) is not { } map)
        {
            restoreIssues = [];
            RefreshIssues();
            return;
        }

        restoreIssues = adapter.Restore(node, map);
        RefreshIssues();
    }

    private void OnCaptureNode(object? sender, EventArgs e)
    {
        if (session.CurrentFlow is not { } flow || session.Document.Maps.Count == 0)
        {
            return;
        }

        var predecessor = session.CurrentNode;
        var capture = adapter.Capture(session.Document.Maps[0].Id);
        var node = new GameplayFlowNode
        {
            MapId = session.Document.Maps[0].Id,
            GroupId = GetSelectedGroup(flow)?.Id ?? predecessor?.GroupId,
            Title = $"Node {flow.Nodes.Count + 1:00}",
            Camera = capture.Camera,
            SelectedEntities = capture.SelectedEntities,
            Annotations = capture.Annotations,
            Visibility = capture.Visibility,
        };
        session.AddNode(node, predecessor);
        selectedGroupId = null;

        if (predecessor != null)
        {
            if (predecessor.NextNodeIds.Count == 0)
            {
                session.ConnectNodes(predecessor, node, out _);
            }
            else
            {
                session.ConnectNodes(predecessor, node, out _);
            }
        }

        restoreIssues = capture.Issues;
        RefreshAll(restoreSelection: false);
        titleTextBox.Focus();
        titleTextBox.SelectAll();
    }

    private void OnUpdateSnapshot(object? sender, EventArgs e)
    {
        if (session.CurrentNode is not { } node)
        {
            return;
        }

        var capture = adapter.Capture(node.MapId);
        node.Camera = capture.Camera;
        node.SelectedEntities = capture.SelectedEntities;
        node.Annotations = capture.Annotations;
        node.Visibility = capture.Visibility;
        restoreIssues = capture.Issues;
        session.MarkDirty(node);
        RefreshAll(restoreSelection: false);
    }

    private void OnDeleteNode(object? sender, EventArgs e)
    {
        if (session.CurrentNode is not { } node)
        {
            return;
        }

        session.RemoveNode(node, removeChildFlow: false);
        RefreshAll(restoreSelection: true);
    }

    private void OnTimelineSelectionChanged(object? sender, TreeViewEventArgs e)
    {
        if (updatingControls || draggingNode)
        {
            return;
        }

        if (e.Node?.Tag is GroupRun groupRun)
        {
            selectedGroupId = groupRun.GroupId;
            return;
        }

        if (e.Node?.Tag is not GameplayFlowNode node)
        {
            return;
        }

        selectedGroupId = null;
        session.SelectNode(node);
        updatingControls = true;
        try
        {
            RefreshDetails();
        }
        finally
        {
            updatingControls = false;
        }

        RestoreCurrentNode();
    }

    private void OnTimelineMouseDown(object? sender, MouseEventArgs e)
    {
        if (updatingControls
            || draggingNode
            || timelineTree.GetNodeAt(e.Location)?.Tag is not GameplayFlowNode node
            || !ReferenceEquals(node, session.CurrentNode))
        {
            return;
        }

        RestoreCurrentNode();
    }

    private void OnNodeTextChanged(object? sender, EventArgs e)
    {
        if (updatingControls || session.CurrentNode is not { } node)
        {
            return;
        }

        node.Title = titleTextBox.Text;
        node.Description = descriptionTextBox.Text;
        node.Tags = tagsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        session.MarkDirty(node);
        if (ReferenceEquals(sender, titleTextBox) && timelineTree.SelectedNode is { } treeNode)
        {
            var number = treeNode.Text.Split("  ", 2, StringSplitOptions.None)[0];
            treeNode.Text = $"{number}  {node.Title}{GetNodeIndicators(session.CurrentFlow!, node)}";
        }
    }

    private void OnGroupSelectionChanged(object? sender, EventArgs e)
    {
        if (updatingControls
            || session.CurrentNode is not { } node
            || groupComboBox.SelectedItem is not DisplayItem<GameplayFlowGroup?> item)
        {
            return;
        }

        session.SetNodeGroup(node, item.Value?.Id);
        RefreshAll(restoreSelection: false);
    }

    private void OnNextNodeItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (updatingControls || session.CurrentNode is not { } source)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (IsDisposed || nextNodesList.Items[e.Index] is not DisplayItem<GameplayFlowNode> item)
            {
                return;
            }

            if (e.NewValue == CheckState.Checked)
            {
                if (!session.ConnectNodes(source, item.Value, out var error))
                {
                    _ = AppMessageDialogs.ShowMessageAsync(error, "Invalid Connection", MessageIcon.Warning);
                }
            }
            else
            {
                session.DisconnectNodes(source, item.Value);
            }

            RefreshAll(restoreSelection: false);
        });
    }

    private void OnTimelineItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is TreeNode { Tag: GameplayFlowNode })
        {
            draggingNode = true;
            timelineTree.DoDragDrop(e.Item, DragDropEffects.Move);
            draggingNode = false;
        }
    }

    private void OnTimelineDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetData(typeof(TreeNode)) is TreeNode { Tag: GameplayFlowNode }
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void OnTimelineDragDrop(object? sender, DragEventArgs e)
    {
        var clientPoint = timelineTree.PointToClient(new Point(e.X, e.Y));
        if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode { Tag: GameplayFlowNode source }
            || timelineTree.GetNodeAt(clientPoint) is not { Tag: GameplayFlowNode target }
            || session.CurrentFlow is not { } flow)
        {
            return;
        }

        session.MoveNode(source, flow.Nodes.IndexOf(target));
        RefreshAll(restoreSelection: false);
    }

    private void OnGroupCollapsing(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node?.Tag is GroupRun run)
        {
            collapsedGroupRuns.Add((run.GroupId, run.StartIndex));
        }
    }

    private void OnGroupExpanded(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is GroupRun run)
        {
            collapsedGroupRuns.Remove((run.GroupId, run.StartIndex));
        }
    }

    private void OnFlowSelectionChanged(object? sender, EventArgs e)
    {
        if (!updatingControls && flowComboBox.SelectedItem is DisplayItem<GameplayFlow> item)
        {
            selectedGroupId = null;
            session.SelectFlow(item.Value);
            RefreshAll(restoreSelection: true);
        }
    }

    private void OnNewFlow(object? sender, EventArgs e)
    {
        session.CreateFlow(GetUniqueTitle(session.Document.Flows.Select(static flow => flow.Title), "Gameplay Flow"));
        selectedGroupId = null;
        RefreshAll(restoreSelection: false);
    }

    private void OnRenameFlow(object? sender, EventArgs e)
    {
        if (session.CurrentFlow is { } flow && PromptForText("Rename Gameplay Flow", out var title))
        {
            flow.Title = title;
            session.MarkDirty();
            RefreshAll(restoreSelection: false);
        }
    }

    private void OnDeleteFlow(object? sender, EventArgs e)
    {
        if (session.CurrentFlow is not { } flow
            || !session.GetRootFlows().Contains(flow))
        {
            return;
        }

        session.RemoveFlow(flow, removeChildFlows: true);
        RefreshAll(restoreSelection: false);
    }

    private void OnNewGroup(object? sender, EventArgs e)
    {
        if (session.CurrentFlow is { } flow)
        {
            var parentGroupId = GetSelectedGroup(flow)?.Id ?? session.CurrentNode?.GroupId;
            var group = new GameplayFlowGroup
            {
                Title = GetUniqueTitle(flow.Groups.Select(static group => group.Title), "Group"),
                ParentGroupId = parentGroupId,
            };
            flow.Groups.Add(group);
            if (selectedGroupId == null && session.CurrentNode is { } node)
            {
                node.GroupId = group.Id;
            }

            selectedGroupId = group.Id;
            session.MarkDirty(session.CurrentNode);
            RefreshAll(restoreSelection: false);
        }
    }

    private void OnRenameGroup(object? sender, EventArgs e)
    {
        if (session.CurrentFlow is { } flow
            && GetSelectedGroup(flow) is { } group
            && PromptForText("Rename Gameplay Group", out var title))
        {
            group.Title = title;
            session.MarkDirty();
            RefreshAll(restoreSelection: false);
        }
    }

    private void OnDeleteGroup(object? sender, EventArgs e)
    {
        if (session.CurrentFlow is not { } flow
            || GetSelectedGroup(flow) is not { } group)
        {
            return;
        }

        foreach (var node in flow.Nodes.Where(node => node.GroupId == group.Id))
        {
            node.GroupId = group.ParentGroupId;
        }

        foreach (var childGroup in flow.Groups.Where(candidate => candidate.ParentGroupId == group.Id))
        {
            childGroup.ParentGroupId = group.ParentGroupId;
        }

        flow.Groups.Remove(group);
        selectedGroupId = group.ParentGroupId;
        session.MarkDirty();
        RefreshAll(restoreSelection: false);
    }

    private void OnChildFlow(object? sender, EventArgs e)
    {
        if (session.CurrentNode is not { } node)
        {
            return;
        }

        if (node.ChildFlowId != null)
        {
            session.EnterChildFlow(node);
            RefreshAll(restoreSelection: false);
            return;
        }

        session.CreateChildFlow(
            node,
            GetUniqueTitle(session.Document.Flows.Select(static flow => flow.Title), "Child Flow"));
        selectedGroupId = null;
        RefreshAll(restoreSelection: false);
    }

    private void OnReturnToParentFlow(object? sender, EventArgs e)
    {
        if (session.ReturnToParentFlow())
        {
            RefreshAll(restoreSelection: true);
        }
    }

    private void OnAddAnnotation(object? sender, EventArgs e)
    {
        if (session.CurrentNode is not { } node)
        {
            return;
        }

        node.Annotations.Add(adapter.CreateAnnotationAtCamera($"Annotation {node.Annotations.Count + 1}"));
        session.MarkDirty(node);
        RefreshAll(restoreSelection: true);
    }

    private void OnConvertMarker(object? sender, EventArgs e)
    {
        if (session.CurrentNode is not { } node || adapter.ConvertSelectedCoordinateMarker() is not { } annotation)
        {
            _ = AppMessageDialogs.ShowMessageAsync(
                "Select exactly one coordinate marker in the world viewer.",
                "Convert Coordinate Marker",
                MessageIcon.Warning);
            return;
        }

        node.Annotations.Add(annotation);
        session.MarkDirty(node);
        RefreshAll(restoreSelection: true);
    }

    private void OnDeleteAnnotation(object? sender, EventArgs e)
    {
        if (session.CurrentNode is { } node
            && annotationList.SelectedItem is DisplayItem<GameplayFlowAnnotation> item)
        {
            node.Annotations.Remove(item.Value);
            session.MarkDirty(node);
            RefreshAll(restoreSelection: true);
        }
    }

    private void OnAnnotationSelectionChanged(object? sender, EventArgs e)
    {
        updatingControls = true;
        try
        {
            if (annotationList.SelectedItem is DisplayItem<GameplayFlowAnnotation> item)
            {
                annotationNameTextBox.Text = item.Value.Name;
                annotationDescriptionTextBox.Text = item.Value.Description;
                annotationPositionTextBox.Text = FormattableString.Invariant(
                    $"{item.Value.Position.X:R} {item.Value.Position.Y:R} {item.Value.Position.Z:R}");
            }
            else
            {
                ClearAnnotationEditor();
            }
        }
        finally
        {
            updatingControls = false;
        }
    }

    private void OnUpdateAnnotation(object? sender, EventArgs e)
    {
        if (session.CurrentNode is not { } node
            || annotationList.SelectedItem is not DisplayItem<GameplayFlowAnnotation> item
            || !EntityTransformHelper.TryParseVector3(annotationPositionTextBox.Text, out var position)
            || !float.IsFinite(position.X)
            || !float.IsFinite(position.Y)
            || !float.IsFinite(position.Z))
        {
            _ = AppMessageDialogs.ShowMessageAsync(
                "Enter a finite annotation position as X Y Z.",
                "Invalid Annotation",
                MessageIcon.Warning);
            return;
        }

        item.Value.Name = annotationNameTextBox.Text.Trim();
        item.Value.Description = annotationDescriptionTextBox.Text;
        item.Value.Position = new(position.X, position.Y, position.Z);
        session.MarkDirty(node);
        RefreshAll(restoreSelection: true);
    }

    private void OnOpen(object? sender, EventArgs e)
    {
        if (!ConfirmDirtyChanges())
        {
            return;
        }

        var path = AppFileDialogs.OpenFile("Open Gameplay Flow", "Gameplay flow JSON (*.json)|*.json");
        if (path == null)
        {
            return;
        }

        try
        {
            var result = GameplayFlowJsonSerializer.Load(path);
            session.ReplaceDocument(result.Document, path, isDirty: false);
            ShowSoftIssues(result.Issues);
            RefreshAll(restoreSelection: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or GameplayFlowDocumentException)
        {
            _ = AppMessageDialogs.ShowMessageAsync(exception.Message, "Open Gameplay Flow", MessageIcon.Error);
        }
    }

    private void OnCopyJson(object? sender, EventArgs e)
    {
        try
        {
            AppClipboard.SetText(GameplayFlowJsonSerializer.Serialize(session.Document));
        }
        catch (Exception exception) when (exception is GameplayFlowDocumentException or ExternalException)
        {
            _ = AppMessageDialogs.ShowMessageAsync(exception.Message, "Copy Gameplay Flow JSON", MessageIcon.Error);
        }
    }

    private void OnPasteJson(object? sender, EventArgs e)
    {
        if (!ConfirmDirtyChanges())
        {
            return;
        }

        try
        {
            var result = GameplayFlowJsonSerializer.Deserialize(AppClipboard.GetText());
            session.ReplaceDocument(result.Document, filePath: null, isDirty: true);
            ShowSoftIssues(result.Issues);
            RefreshAll(restoreSelection: true);
        }
        catch (Exception exception) when (exception is GameplayFlowDocumentException or ExternalException)
        {
            _ = AppMessageDialogs.ShowMessageAsync(exception.Message, "Paste Gameplay Flow JSON", MessageIcon.Error);
        }
    }

    private bool SaveDocument(bool saveAs)
    {
        var path = saveAs ? null : session.FilePath;
        path ??= AppFileDialogs.SaveFile(
            "Save Gameplay Flow",
            GetDefaultFileName(),
            "json",
            "Gameplay flow JSON (*.json)|*.json");
        if (path == null)
        {
            return false;
        }

        try
        {
            GameplayFlowJsonSerializer.Save(path, session.Document);
            session.MarkSaved(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or GameplayFlowDocumentException)
        {
            _ = AppMessageDialogs.ShowMessageAsync(exception.Message, "Save Gameplay Flow", MessageIcon.Error);
            return false;
        }
    }

    private bool ConfirmDirtyChanges()
    {
        if (!session.IsDirty)
        {
            return true;
        }

        return ShowDirtyDialog() switch
        {
            DirtyChoice.Save => SaveDocument(saveAs: false),
            DirtyChoice.Discard => true,
            _ => false,
        };
    }

    private DirtyChoice ShowDirtyDialog()
    {
        using var dialog = new ThemedForm
        {
            Text = "Unsaved Gameplay Flow",
            ClientSize = new Size(430, 125),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
        };
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Save changes to the gameplay flow document?",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12),
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
        };
        DirtyChoice choice = DirtyChoice.Cancel;
        void AddChoice(string text, DirtyChoice value, DialogResult result)
        {
            var button = new ThemedButton { Text = text, AutoSize = true, DialogResult = result };
            button.Click += (_, _) => choice = value;
            buttons.Controls.Add(button);
        }

        AddChoice("Keep editing", DirtyChoice.Cancel, DialogResult.Cancel);
        AddChoice("Discard changes", DirtyChoice.Discard, DialogResult.No);
        AddChoice("Save changes", DirtyChoice.Save, DialogResult.Yes);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.ShowDialog(this);
        return choice;
    }

    private void SelectReadingNeighbor(int offset)
    {
        if (session.CurrentFlow is not { } flow || session.CurrentNode is not { } node)
        {
            return;
        }

        var index = flow.Nodes.IndexOf(node) + offset;
        if (index >= 0 && index < flow.Nodes.Count)
        {
            session.SelectNode(flow.Nodes[index]);
            RefreshAll(restoreSelection: true);
        }
    }

    private void OnFollowFlow(object? sender, EventArgs e)
    {
        if (session.CurrentFlow is not { } flow || session.CurrentNode is not { } node)
        {
            return;
        }

        if (node.ChildFlowId != null)
        {
            session.EnterChildFlow(node);
            RefreshAll(restoreSelection: false);
            return;
        }

        var nextNodes = node.NextNodeIds
            .Select(id => flow.Nodes.FirstOrDefault(candidate => candidate.Id == id))
            .OfType<GameplayFlowNode>()
            .ToList();
        if (nextNodes.Count == 0 && flow.Nodes.All(static candidate => candidate.NextNodeIds.Count == 0))
        {
            var currentIndex = flow.Nodes.IndexOf(node);
            if (currentIndex + 1 < flow.Nodes.Count)
            {
                SelectReadingNeighbor(1);
            }
            else if (session.ReturnToParentFlow())
            {
                RefreshAll(restoreSelection: true);
            }

            return;
        }

        if (nextNodes.Count == 0)
        {
            if (session.ReturnToParentFlow())
            {
                RefreshAll(restoreSelection: true);
            }

            return;
        }

        if (nextNodes.Count == 1)
        {
            session.SelectNode(nextNodes[0]);
            RefreshAll(restoreSelection: true);
            return;
        }

        if (nextNodes.Count > 1)
        {
            using var chooser = new ThemedForm
            {
                Text = "Choose Next Gameplay Node",
                ClientSize = new Size(420, 300),
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
            };
            var list = new ListBox { Dock = DockStyle.Fill, DataSource = nextNodes, DisplayMember = nameof(GameplayFlowNode.Title) };
            var button = new ThemedButton { Dock = DockStyle.Bottom, Text = "Open", DialogResult = DialogResult.OK };
            chooser.Controls.Add(list);
            chooser.Controls.Add(button);
            chooser.AcceptButton = button;
            if (chooser.ShowDialog(this) == DialogResult.OK && list.SelectedItem is GameplayFlowNode selected)
            {
                session.SelectNode(selected);
                RefreshAll(restoreSelection: true);
            }
        }
    }

    private void RefreshIssues()
    {
        var validationIssues = GameplayFlowSession.Validate(session.Document)
            .Where(issue => issue.NodeId == null || issue.NodeId == session.CurrentNode?.Id);
        issuesTextBox.Text = string.Join(
            Environment.NewLine,
            validationIssues.Concat(restoreIssues).Select(static issue => issue.Message).Distinct());
    }

    private static void ShowSoftIssues(IReadOnlyList<GameplayFlowIssue> issues)
    {
        if (issues.Count > 0)
        {
            _ = AppMessageDialogs.ShowMessageAsync(
                string.Join(Environment.NewLine, issues.Select(static issue => issue.Message).Distinct()),
                "Gameplay Flow Notices",
                MessageIcon.Warning);
        }
    }

    private GameplayFlow? FindRootFlow(GameplayFlow? flow)
    {
        if (flow == null)
        {
            return null;
        }

        var current = flow;
        while (FindOwner(current) is { ParentFlow: { } parentFlow })
        {
            current = parentFlow;
        }

        return current;
    }

    private List<(GameplayFlow Flow, GameplayFlowNode? Owner)> GetFlowChain(GameplayFlow? flow)
    {
        var chain = new List<(GameplayFlow, GameplayFlowNode?)>();
        var current = flow;
        while (current != null)
        {
            var owner = FindOwner(current);
            chain.Add((current, owner?.Node));
            current = owner?.ParentFlow;
        }

        chain.Reverse();
        return chain;
    }

    private (GameplayFlow ParentFlow, GameplayFlowNode Node)? FindOwner(GameplayFlow flow)
    {
        foreach (var parentFlow in session.Document.Flows)
        {
            if (parentFlow.Nodes.FirstOrDefault(node => node.ChildFlowId == flow.Id) is { } node)
            {
                return (parentFlow, node);
            }
        }

        return null;
    }

    private static List<GameplayFlowGroup> GetGroupPath(GameplayFlow flow, Guid? groupId)
    {
        var path = new List<GameplayFlowGroup>();
        var visited = new HashSet<Guid>();
        while (groupId is { } id
            && visited.Add(id)
            && flow.Groups.FirstOrDefault(group => group.Id == id) is { } group)
        {
            path.Add(group);
            groupId = group.ParentGroupId;
        }

        path.Reverse();
        return path;
    }

    private GameplayFlowGroup? GetSelectedGroup(GameplayFlow flow)
    {
        if (selectedGroupId is { } groupId)
        {
            return flow.Groups.FirstOrDefault(group => group.Id == groupId);
        }

        return groupComboBox.SelectedItem is DisplayItem<GameplayFlowGroup?> item ? item.Value : null;
    }

    private static IEnumerable<(GameplayFlowGroup Group, int Depth)> EnumerateGroups(GameplayFlow flow)
    {
        var visited = new HashSet<Guid>();
        foreach (var item in EnumerateChildren(null, 0))
        {
            yield return item;
        }

        IEnumerable<(GameplayFlowGroup Group, int Depth)> EnumerateChildren(Guid? parentGroupId, int depth)
        {
            foreach (var group in flow.Groups.Where(group => group.ParentGroupId == parentGroupId && visited.Add(group.Id)))
            {
                yield return (group, depth);
                foreach (var child in EnumerateChildren(group.Id, depth + 1))
                {
                    yield return child;
                }
            }
        }
    }

    private static IEnumerable<TreeNode> EnumerateTreeNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateTreeNodes(node.Nodes))
            {
                yield return child;
            }
        }
    }

    private string GetNumberPrefix(GameplayFlow flow)
    {
        var parts = new Stack<int>();
        var current = flow;
        while (FindOwner(current) is { ParentFlow: { } parentFlow, Node: { } owner })
        {
            parts.Push(parentFlow.Nodes.IndexOf(owner) + 1);
            current = parentFlow;
        }

        return parts.Count == 0 ? string.Empty : string.Concat(parts.Select(static number => $"{number:00}."));
    }

    private static string GetNodeIndicators(GameplayFlow flow, GameplayFlowNode node)
    {
        var incoming = flow.Nodes.Count(candidate => candidate.NextNodeIds.Contains(node.Id));
        var labels = new List<string>();
        if (node.ChildFlowId != null)
        {
            labels.Add("child");
        }

        if (node.NextNodeIds.Count > 1)
        {
            labels.Add("branch");
        }

        if (incoming > 1)
        {
            labels.Add("merge");
        }

        return labels.Count == 0 ? string.Empty : $"  [{string.Join(", ", labels)}]";
    }

    private void ClearAnnotationEditor()
    {
        annotationNameTextBox.Text = string.Empty;
        annotationDescriptionTextBox.Text = string.Empty;
        annotationPositionTextBox.Text = string.Empty;
    }

    private void UpdateWindowTitle()
    {
        var fileName = session.FilePath == null ? "Unsaved" : Path.GetFileName(session.FilePath);
        Text = $"Gameplay Flows - {fileName}{(session.IsDirty ? " *" : string.Empty)}";
    }

    private string GetDefaultFileName()
    {
        var mapName = session.Document.Maps.Count > 0
            ? Path.GetFileNameWithoutExtension(session.Document.Maps[0].DisplayName)
            : "map";
        return $"{mapName}_gameplay_flows.json";
    }

    private static string GetUniqueTitle(IEnumerable<string> existingTitles, string prefix)
    {
        var titles = existingTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var title = $"{prefix} {index}";
            if (!titles.Contains(title))
            {
                return title;
            }
        }
    }

    private static bool PromptForText(string title, out string text)
    {
        using var prompt = new PromptForm(title);
        if (prompt.ShowDialog(Program.MainForm) != DialogResult.OK || string.IsNullOrWhiteSpace(prompt.ResultText))
        {
            text = string.Empty;
            return false;
        }

        text = prompt.ResultText.Trim();
        return true;
    }

    private static ThemedButton AddButton(Control parent, string text, EventHandler handler)
    {
        var button = new ThemedButton { Text = text, AutoSize = true };
        button.Click += handler;
        parent.Controls.Add(button);
        return button;
    }

    private static FlowLayoutPanel AddToolbarRow(TableLayoutPanel toolbar, string label)
    {
        var row = toolbar.RowCount++;
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbar.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 7, 0, 0),
        }, 0, row);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        toolbar.Controls.Add(controls, 1, row);
        return controls;
    }

    private static TableLayoutPanel AddDetailsSection(TableLayoutPanel sections, string title)
    {
        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(6),
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var section = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6),
        };
        section.Controls.Add(details);
        var row = sections.RowCount++;
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sections.Controls.Add(section, 0, row);
        return details;
    }

    private static ThemedTextBox AddTextField(
        TableLayoutPanel panel,
        string label,
        bool multiline,
        int height)
    {
        var textBox = new ThemedTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = multiline,
            Height = height,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
        };
        AddRow(panel, label, textBox);
        return textBox;
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 7, 6, 0),
        }, 0, row);
        panel.Controls.Add(control, 1, row);
    }
}
