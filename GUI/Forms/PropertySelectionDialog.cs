using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace GUI.Forms;

public class PropertySelectionDialog : ThemedForm
{
    private readonly List<EntityLump.Entity> _allEntities;
    private readonly HashSet<string> _customExportProperties;
    private readonly List<EntityLump.Entity> _filteredEntities;
    private readonly TextBox _searchTextBox;
    private readonly TreeView _availableTreeView;
    private readonly ListBox _selectedListBox;
    private readonly CheckBox _includePropertyRelatedCheckBox;
    private readonly CheckBox _includeConnectionRelatedCheckBox;

    public HashSet<string> SelectedProperties { get; } = [];
    public List<EntityLump.Entity> FinalEntityList { get; private set; } = [];
    public string ExportedJson { get; private set; } = string.Empty;

#pragma warning disable CA2000 // Controls are transferred to the form's Controls collection and disposed with it
    public PropertySelectionDialog(List<EntityLump.Entity> filteredEntities,
        HashSet<string>? preSelectedProperties = null,
        List<EntityLump.Entity>? allEntities = null)
    {
        _filteredEntities = filteredEntities;
        _allEntities = allEntities ?? filteredEntities;
        _customExportProperties = preSelectedProperties ?? [];

        Text = "Export Configuration";
        ClientSize = new Size(900, 580);
        MinimumSize = new Size(700, 450);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;

        SuspendLayout();

        // ── Search row ─────────────────────────────────────────────────────
        _searchTextBox = new ThemedTextBox { Dock = DockStyle.Fill, PlaceholderText = "Search properties..." };
        _searchTextBox.TextChanged += (_, _) => FilterPropertiesTree(_searchTextBox.Text);

        var expandAllButton = new ThemedButton { Text = "Expand All", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
        expandAllButton.Click += (_, _) => _availableTreeView?.ExpandAll();

        var collapseAllButton = new ThemedButton { Text = "Collapse All", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
        collapseAllButton.Click += (_, _) => _availableTreeView?.CollapseAll();

        var searchRow = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            ColumnCount = 4,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 4),
        };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // "Search:" label
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // TextBox stretches
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // Expand All
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // Collapse All
        searchRow.Controls.Add(new Label { Text = "Search:", AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 4, 0) }, 0, 0);
        searchRow.Controls.Add(_searchTextBox, 1, 0);
        searchRow.Controls.Add(expandAllButton, 2, 0);
        searchRow.Controls.Add(collapseAllButton, 3, 0);

        // ── Left panel (TreeView) ──────────────────────────────────────────
        _availableTreeView = new TreeView
        {
            Dock = DockStyle.Fill,
            CheckBoxes = true,
            Font = new Font("Consolas", 9),
        };
        _availableTreeView.NodeMouseClick += OnTreeViewNodeMouseClick;

        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        leftPanel.Controls.Add(new Label { Text = "Available Properties (by Entity Type):", AutoSize = true, Margin = new Padding(0, 0, 0, 2) }, 0, 0);
        leftPanel.Controls.Add(_availableTreeView, 0, 1);

        // ── Middle button column ───────────────────────────────────────────
        // Anchor=None centers the FlowLayoutPanel in its cell (both H and V).
        // AutoSize=true + GrowAndShrink lets the column width adapt to button text.
        var addButton = new ThemedButton { Text = "\u2192 Add", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        addButton.Click += (_, _) => AddSelectedProperties();

        var removeButton = new ThemedButton { Text = "\u2190 Remove", AutoSize = true, Margin = new Padding(0, 4, 0, 16) };
        removeButton.Click += (_, _) => RemoveSelectedProperties();

        var addAllButton = new ThemedButton { Text = "\u21D2 All", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        addAllButton.Click += (_, _) => AddAllAvailableProperties();

        var addSmartButton = new ThemedButton { Text = "\u21D2 Smart", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        addSmartButton.Click += (_, _) => AddSmartDefaultProperties();

        var buttonColumn = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.None,
            Padding = new Padding(8, 0, 8, 0),
        };
        buttonColumn.Controls.Add(addButton);
        buttonColumn.Controls.Add(removeButton);
        buttonColumn.Controls.Add(addAllButton);
        buttonColumn.Controls.Add(addSmartButton);

        // ── Right panel (ListBox) ──────────────────────────────────────────
        _selectedListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9),
            SelectionMode = SelectionMode.MultiExtended,
        };

        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rightPanel.Controls.Add(new Label { Text = "Selected Properties for Export:", AutoSize = true, Margin = new Padding(0, 0, 0, 2) }, 0, 0);
        rightPanel.Controls.Add(_selectedListBox, 0, 1);

        // ── Content row (3 columns: left | buttons | right) ───────────────
        var contentRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        contentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        contentRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        contentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        contentRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        contentRow.Controls.Add(leftPanel, 0, 0);
        contentRow.Controls.Add(buttonColumn, 1, 0);
        contentRow.Controls.Add(rightPanel, 2, 0);

        // ── Bottom row (checkboxes + dialog buttons) ───────────────────────
        _includePropertyRelatedCheckBox = new CheckBox
        {
            Text = "Include related entities (properties)",
            AutoSize = true,
        };
        _includeConnectionRelatedCheckBox = new CheckBox
        {
            Text = "Include related entities (connections)",
            AutoSize = true,
        };

        var checkBoxPanel = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
        };
        checkBoxPanel.Controls.Add(_includePropertyRelatedCheckBox);
        checkBoxPanel.Controls.Add(_includeConnectionRelatedCheckBox);

        var okButton = new ThemedButton { Text = "Export", AutoSize = true, MinimumSize = new Size(75, 28), DialogResult = DialogResult.OK, Margin = new Padding(4, 0, 0, 0) };
        okButton.Click += OnOkClick;

        var cancelButton = new ThemedButton { Text = "Cancel", AutoSize = true, MinimumSize = new Size(75, 28), DialogResult = DialogResult.Cancel, Margin = new Padding(4, 0, 0, 0) };

        var dialogButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        dialogButtons.Controls.Add(cancelButton);
        dialogButtons.Controls.Add(okButton);

        var bottomRow = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 4, 0, 0),
        };
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottomRow.Controls.Add(checkBoxPanel, 0, 0);
        bottomRow.Controls.Add(dialogButtons, 1, 0);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        // ── Main layout (3 rows) ───────────────────────────────────────────
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8),
            Margin = Padding.Empty,
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // search row
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // content
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // bottom
        mainLayout.Controls.Add(searchRow, 0, 0);
        mainLayout.Controls.Add(contentRow, 0, 1);
        mainLayout.Controls.Add(bottomRow, 0, 2);

        Controls.Add(mainLayout);

        ResumeLayout(true);

        PopulateAvailablePropertiesTree();
        InitializeSelectedProperties();
    }
#pragma warning restore CA2000

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchTextBox.Dispose();
            _availableTreeView.Dispose();
            _selectedListBox.Dispose();
            _includePropertyRelatedCheckBox.Dispose();
            _includeConnectionRelatedCheckBox.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (_selectedListBox.Items.Count == 0)
        {
            _ = AppMessageDialogs.ShowMessageAsync(
                "Please select at least one property for export.",
                "No Properties Selected",
                MessageIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        SelectedProperties.Clear();
        foreach (string property in _selectedListBox.Items)
        {
            SelectedProperties.Add(property);
        }

        var includePropertyRelated = _includePropertyRelatedCheckBox.Checked;
        var includeConnectionRelated = _includeConnectionRelatedCheckBox.Checked;

        FinalEntityList = (includePropertyRelated || includeConnectionRelated)
            ? GetEntitiesWithRelated(_filteredEntities, _allEntities, includePropertyRelated, includeConnectionRelated)
            : new List<EntityLump.Entity>(_filteredEntities);

        var totalAvailableProperties = GetAllAvailableProperties(_filteredEntities).Count;
        ExportedJson = SelectedProperties.Count == totalAvailableProperties
            ? SerializeEntitiesFull(FinalEntityList)
            : SerializeEntitiesWithSelectedProperties(FinalEntityList, SelectedProperties);

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnTreeViewNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Node == null)
        {
            return;
        }

        if (e.Node.Parent != null && e.Node.Tag is string)
        {
            e.Node.Checked = !e.Node.Checked;
        }
        else if (e.Node.Parent == null)
        {
            var newState = !e.Node.Checked;
            e.Node.Checked = newState;
            foreach (TreeNode childNode in e.Node.Nodes)
            {
                childNode.Checked = newState;
            }
        }
    }

    private void PopulateAvailablePropertiesTree()
    {
        _availableTreeView.Nodes.Clear();
        var propertiesByClassname = new Dictionary<string, HashSet<string>>();

        foreach (var entity in _filteredEntities)
        {
            var classname = entity.GetStringProperty("classname", "unknown");
            if (!propertiesByClassname.TryGetValue(classname, out var properties))
            {
                properties = [];
                propertiesByClassname[classname] = properties;
            }

            foreach (var prop in entity.Children)
            {
                properties.Add(prop.Key);
            }
        }

        foreach (var kvp in propertiesByClassname.OrderBy(x => x.Key))
        {
            var classnameNode = new TreeNode($"{kvp.Key} ({kvp.Value.Count} properties)") { Tag = kvp.Key };

            foreach (var property in kvp.Value.OrderBy(x => x))
            {
                classnameNode.Nodes.Add(new TreeNode(property) { Tag = property });
            }

            _availableTreeView.Nodes.Add(classnameNode);
        }

        _availableTreeView.ExpandAll();
    }

    private void InitializeSelectedProperties()
    {
        _selectedListBox.Items.Clear();

        var properties = _customExportProperties.Count > 0
            ? _customExportProperties
            : EntityPropertyManager.GetSmartProperties(_filteredEntities);

        foreach (var property in properties.OrderBy(x => x))
        {
            _selectedListBox.Items.Add(property);
        }
    }

    private void FilterPropertiesTree(string searchText)
    {
        _availableTreeView.BeginUpdate();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            ResetTreeNodeColors(_availableTreeView.Nodes);
            _availableTreeView.EndUpdate();
            return;
        }

        FilterTreeNodes(_availableTreeView.Nodes, searchText);
        _availableTreeView.EndUpdate();
    }

    private static void ResetTreeNodeColors(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            node.BackColor = Color.White;
            node.ForeColor = Color.Black;
            if (node.Nodes.Count > 0)
            {
                ResetTreeNodeColors(node.Nodes);
            }
        }
    }

    private static bool FilterTreeNodes(TreeNodeCollection nodes, string searchText)
    {
        var hasMatchingChild = false;

        foreach (TreeNode node in nodes)
        {
            var nodeMatches = node.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase);
            var childrenMatch = node.Nodes.Count > 0 && FilterTreeNodes(node.Nodes, searchText);

            if (nodeMatches || childrenMatch)
            {
                node.BackColor = nodeMatches ? Color.LightYellow : Color.White;
                node.ForeColor = Color.Black;
                node.Expand();
                hasMatchingChild = true;
            }
            else
            {
                node.BackColor = Color.White;
                node.ForeColor = Color.Gray;
                if (node.Level < 2)
                {
                    node.Collapse();
                }
            }
        }

        return hasMatchingChild;
    }

    private void AddSelectedProperties()
    {
        var selected = new List<string>();

        foreach (TreeNode classnameNode in _availableTreeView.Nodes)
        {
            foreach (TreeNode propertyNode in classnameNode.Nodes)
            {
                if (propertyNode.Checked && propertyNode.Tag is string property)
                {
                    selected.Add(property);
                    propertyNode.Checked = false;
                }
            }
        }

        UpdateListBoxProperties(selected);
    }

    private void RemoveSelectedProperties()
    {
        var selectedItems = _selectedListBox.SelectedItems.Cast<string>().ToList();
        foreach (var item in selectedItems)
        {
            _selectedListBox.Items.Remove(item);
        }
    }

    private void AddAllAvailableProperties()
    {
        UpdateListBoxProperties(GetAllAvailableProperties(_filteredEntities), clearExisting: true);
    }

    private void AddSmartDefaultProperties()
    {
        UpdateListBoxProperties(EntityPropertyManager.GetSmartProperties(_filteredEntities), clearExisting: true);
    }

    private void UpdateListBoxProperties(IEnumerable<string> properties, bool clearExisting = false)
    {
        var existingItems = clearExisting ? new HashSet<string>() : _selectedListBox.Items.Cast<string>().ToHashSet();

        if (clearExisting)
        {
            _selectedListBox.Items.Clear();
        }

        foreach (var property in properties)
        {
            existingItems.Add(property);
        }

        var sorted = existingItems.OrderBy(x => x).ToArray();
        _selectedListBox.Items.Clear();
        _selectedListBox.Items.AddRange(sorted);
    }

    private static HashSet<string> GetAllAvailableProperties(List<EntityLump.Entity> entities)
    {
        var allProperties = new HashSet<string>();
        foreach (var entity in entities)
        {
            foreach (var prop in entity.Children)
            {
                allProperties.Add(prop.Key);
            }
        }
        return allProperties;
    }

    private static List<EntityLump.Entity> GetEntitiesWithRelated(
        List<EntityLump.Entity> filteredEntities,
        List<EntityLump.Entity> allEntities,
        bool includePropertyRelated,
        bool includeConnectionRelated)
    {
        var namedEntitiesLookup = allEntities
            .Where(e => !string.IsNullOrEmpty(e.GetStringProperty("targetname")))
            .ToLookup(e => e.GetStringProperty("targetname"));

        var targetResolver = includeConnectionRelated ? new EntityIOTargetResolver(allEntities) : null;

        var processed = new HashSet<EntityLump.Entity>();
        var result = new List<EntityLump.Entity>();
        var queue = new Queue<EntityLump.Entity>(filteredEntities);

        while (queue.Count > 0)
        {
            var entity = queue.Dequeue();
            if (!processed.Add(entity))
            {
                continue;
            }

            result.Add(entity);

            if (includePropertyRelated)
            {
                ProcessPropertyRelated(entity, namedEntitiesLookup, processed, queue);
            }

            if (targetResolver != null)
            {
                ProcessConnectionRelated(entity, targetResolver, processed, queue);
            }
        }

        return result;
    }

    private static void ProcessPropertyRelated(
        EntityLump.Entity entity,
        ILookup<string, EntityLump.Entity> namedEntitiesLookup,
        HashSet<EntityLump.Entity> processed,
        Queue<EntityLump.Entity> queue)
    {
        foreach (var (key, value) in entity.Children)
        {
            if (value.IsNull || !EntityPropertyManager.ReferenceProperties.Contains(key))
            {
                continue;
            }

            var referencedName = value.ToString().Trim();
            if (string.IsNullOrEmpty(referencedName) ||
                referencedName.StartsWith('!') ||
                referencedName.Contains(' ') ||
                referencedName.Contains(',') ||
                referencedName.Contains(';'))
            {
                continue;
            }

            foreach (var referenced in FindEntitiesByName(namedEntitiesLookup, referencedName))
            {
                if (!processed.Contains(referenced))
                {
                    queue.Enqueue(referenced);
                }
            }
        }
    }

    private static void ProcessConnectionRelated(
        EntityLump.Entity entity,
        EntityIOTargetResolver targetResolver,
        HashSet<EntityLump.Entity> processed,
        Queue<EntityLump.Entity> queue)
    {
        if (entity.Connections != null)
        {
            foreach (var connection in entity.Connections)
            {
                var targets = new List<EntityLump.Entity>();
                targetResolver.Resolve(connection, targets);
                foreach (var target in targets)
                {
                    if (!processed.Contains(target))
                    {
                        queue.Enqueue(target);
                    }
                }
            }
        }

        foreach (var connection in targetResolver.GetInputConnections(entity))
        {
            if (!processed.Contains(connection.SourceEntity))
            {
                queue.Enqueue(connection.SourceEntity);
            }
        }
    }

    private static IEnumerable<EntityLump.Entity> FindEntitiesByName(
        ILookup<string, EntityLump.Entity> namedEntitiesLookup,
        string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
        {
            return [];
        }

        var exactMatches = namedEntitiesLookup[targetName];
        if (exactMatches.Any())
        {
            return exactMatches;
        }

        // Fuzzy matching for names with suffixes like "&0000"
        var fuzzyMatches = new List<EntityLump.Entity>();
        foreach (var group in namedEntitiesLookup)
        {
            var entityName = group.Key;
            if (entityName.Length < targetName.Length ||
                !entityName.StartsWith(targetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entityName.Length == targetName.Length)
            {
                fuzzyMatches.AddRange(group);
            }
            else
            {
                var nextChar = entityName[targetName.Length];
                if (nextChar is '&' or '_' or '.' or '#' || char.IsDigit(nextChar))
                {
                    fuzzyMatches.AddRange(group);
                }
            }
        }

        return fuzzyMatches;
    }

    private static string SerializeEntitiesFull(List<EntityLump.Entity> entities)
    {
        return MapExtract.SerializeEntities(entities);
    }

    private static string SerializeEntitiesWithSelectedProperties(
        List<EntityLump.Entity> entities,
        HashSet<string> selectedProperties)
    {
        var propertiesByType = new Dictionary<string, HashSet<string>>();

        foreach (var group in entities.GroupBy(e => e.GetStringProperty("classname", "unknown")))
        {
            var prefixProperties = EntityPropertyManager.GetPropertiesForClassname(group.Key);
            var finalProperties = new HashSet<string>(prefixProperties, StringComparer.OrdinalIgnoreCase);
            finalProperties.UnionWith(selectedProperties);
            propertiesByType[group.Key] = finalProperties;
        }

        var filteredEntities = entities.Select(entity =>
        {
            var classname = entity.GetStringProperty("classname", "unknown");
            var relevantProperties = propertiesByType.GetValueOrDefault(classname, selectedProperties);
            var entityDict = new Dictionary<string, object>();

            foreach (var (key, value) in entity.Children)
            {
                if (relevantProperties.Contains(key) && !value.IsNull)
                {
                    entityDict[key] = KVJsonSerializer.ConvertToJsonValue(value)!;
                }
            }

            if (entity.Connections?.Count > 0)
            {
                var connections = entity.Connections
                    .Select(FilterConnection)
                    .Where(c => c.Count > 0)
                    .Cast<object>()
                    .ToList();

                if (connections.Count > 0)
                {
                    entityDict["connections"] = connections;
                }
            }

            return entityDict;
        }).Where(dict => dict.Count > 0).ToList();

        return JsonSerializer.Serialize(filteredEntities, KVJsonContext.Options);
    }

    private static Dictionary<string, object> FilterConnection(EntityLump.Connection connection)
    {
        return new Dictionary<string, object>
        {
            ["m_outputName"] = connection.OutputName,
            ["m_targetName"] = connection.TargetName,
            ["m_inputName"] = connection.InputName,
            ["m_overrideParam"] = connection.OverrideParam,
            ["m_flDelay"] = connection.Delay,
            ["m_nTimesToFire"] = connection.TimesToFire,
        };
    }
}
