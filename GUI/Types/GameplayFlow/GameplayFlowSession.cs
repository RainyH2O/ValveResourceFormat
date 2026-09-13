using System.Linq;

namespace GUI.Types.GameplayFlow;

internal sealed class GameplayFlowSession
{
    private readonly Stack<(Guid FlowId, Guid NodeId)> parentFlows = [];

    public GameplayFlowDocument Document { get; private set; }

    public GameplayFlow? CurrentFlow { get; private set; }

    public GameplayFlowNode? CurrentNode { get; private set; }

    public string? FilePath { get; private set; }

    public bool IsDirty { get; private set; }

    public event EventHandler? Changed;

    public GameplayFlowSession(GameplayFlowDocument document, string? filePath = null, bool isDirty = false)
    {
        Document = document;
        FilePath = filePath;
        IsDirty = isDirty;
        CurrentFlow = GetFirstOrDefault(GetRootFlows()) ?? GetFirstOrDefault(document.Flows);
        CurrentNode = CurrentFlow == null ? null : GetFirstOrDefault(CurrentFlow.Nodes);
    }

    public IReadOnlyList<GameplayFlow> GetRootFlows()
    {
        var childFlowIds = Document.Flows
            .SelectMany(static flow => flow.Nodes)
            .Select(static node => node.ChildFlowId)
            .OfType<Guid>()
            .ToHashSet();
        return Document.Flows.Where(flow => !childFlowIds.Contains(flow.Id)).ToList();
    }

    public GameplayFlow CreateFlow(string title)
    {
        var flow = new GameplayFlow { Title = title.Trim() };
        Document.Flows.Add(flow);
        SetCurrent(flow, null);
        MarkDirty();
        return flow;
    }

    public GameplayFlow CreateChildFlow(GameplayFlowNode ownerNode, string title)
    {
        if (ownerNode.ChildFlowId != null || FindFlowContaining(ownerNode) is not { } parentFlow)
        {
            throw new InvalidOperationException("The node cannot own another child flow.");
        }

        var childFlow = new GameplayFlow { Title = title.Trim() };
        Document.Flows.Add(childFlow);
        ownerNode.ChildFlowId = childFlow.Id;
        ownerNode.ModifiedAt = DateTimeOffset.UtcNow;
        parentFlows.Push((parentFlow.Id, ownerNode.Id));
        SetCurrent(childFlow, null);
        MarkDirty();
        return childFlow;
    }

    public bool EnterChildFlow(GameplayFlowNode ownerNode)
    {
        var parentFlow = FindFlowContaining(ownerNode);
        if (parentFlow == null
            || ownerNode.ChildFlowId is not { } childFlowId
            || FindFlow(childFlowId) is not { } childFlow)
        {
            return false;
        }

        parentFlows.Push((parentFlow.Id, ownerNode.Id));
        SetCurrent(childFlow, GetFirstOrDefault(childFlow.Nodes));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool ReturnToParentFlow()
    {
        if (!parentFlows.TryPop(out var parent)
            || FindFlow(parent.FlowId) is not { } flow)
        {
            return false;
        }

        SetCurrent(flow, flow.Nodes.FirstOrDefault(node => node.Id == parent.NodeId));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SelectFlow(GameplayFlow flow)
    {
        if (!Document.Flows.Contains(flow))
        {
            throw new ArgumentException("The flow does not belong to this document.", nameof(flow));
        }

        parentFlows.Clear();
        SetCurrent(flow, GetFirstOrDefault(flow.Nodes));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectNode(GameplayFlowNode? node)
    {
        if (node != null && (CurrentFlow == null || !CurrentFlow.Nodes.Contains(node)))
        {
            throw new ArgumentException("The node does not belong to the current flow.", nameof(node));
        }

        CurrentNode = node;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddNode(GameplayFlowNode node, GameplayFlowNode? afterNode = null)
    {
        var flow = CurrentFlow ?? throw new InvalidOperationException("No flow is selected.");
        if (Document.Flows.SelectMany(static item => item.Nodes).Any(item => item.Id == node.Id))
        {
            throw new InvalidOperationException("A node with the same ID already exists.");
        }

        var index = flow.Nodes.Count;
        if (afterNode != null)
        {
            index = flow.Nodes.IndexOf(afterNode);
            if (index < 0)
            {
                throw new ArgumentException("The insertion node does not belong to the current flow.", nameof(afterNode));
            }

            index++;
        }

        flow.Nodes.Insert(index, node);
        CurrentNode = node;
        MarkDirty(node);
    }

    public void MoveNode(GameplayFlowNode node, int destinationIndex)
    {
        var flow = FindFlowContaining(node)
            ?? throw new ArgumentException("The node does not belong to this document.", nameof(node));
        var sourceIndex = flow.Nodes.IndexOf(node);
        destinationIndex = Math.Clamp(destinationIndex, 0, flow.Nodes.Count - 1);
        if (sourceIndex == destinationIndex)
        {
            return;
        }

        flow.Nodes.RemoveAt(sourceIndex);
        flow.Nodes.Insert(destinationIndex, node);
        MarkDirty(node);
    }

    public void SetNodeGroup(GameplayFlowNode node, Guid? groupId)
    {
        var flow = FindFlowContaining(node)
            ?? throw new ArgumentException("The node does not belong to this document.", nameof(node));
        if (groupId != null && !flow.Groups.Any(group => group.Id == groupId))
        {
            throw new ArgumentException("The group does not belong to the node's flow.", nameof(groupId));
        }

        node.GroupId = groupId;
        MarkDirty(node);
    }

    public bool ConnectNodes(GameplayFlowNode source, GameplayFlowNode target, out string error)
    {
        var flow = FindFlowContaining(source);
        if (flow == null || !flow.Nodes.Contains(target))
        {
            error = "Connections must remain inside one flow.";
            return false;
        }

        if (ReferenceEquals(source, target))
        {
            error = "A node cannot connect to itself.";
            return false;
        }

        if (source.NextNodeIds.Contains(target.Id))
        {
            error = "The connection already exists.";
            return false;
        }

        if (CanReach(flow, target.Id, source.Id))
        {
            error = "The connection would create a cycle.";
            return false;
        }

        source.NextNodeIds.Add(target.Id);
        MarkDirty(source);
        error = string.Empty;
        return true;
    }

    public void DisconnectNodes(GameplayFlowNode source, GameplayFlowNode target)
    {
        if (source.NextNodeIds.Remove(target.Id))
        {
            MarkDirty(source);
        }
    }

    public void RemoveNode(GameplayFlowNode node, bool removeChildFlow)
    {
        var flow = FindFlowContaining(node)
            ?? throw new ArgumentException("The node does not belong to this document.", nameof(node));

        foreach (var otherNode in flow.Nodes)
        {
            otherNode.NextNodeIds.Remove(node.Id);
        }

        if (node.ChildFlowId is { } childFlowId && removeChildFlow)
        {
            RemoveFlowHierarchy(childFlowId);
        }

        flow.Nodes.Remove(node);
        CurrentNode = ReferenceEquals(CurrentNode, node) ? GetFirstOrDefault(flow.Nodes) : CurrentNode;
        MarkDirty();
    }

    public void RemoveFlow(GameplayFlow flow, bool removeChildFlows)
    {
        if (!Document.Flows.Contains(flow))
        {
            return;
        }

        if (removeChildFlows)
        {
            RemoveFlowHierarchy(flow.Id);
        }
        else
        {
            Document.Flows.Remove(flow);
        }

        foreach (var node in Document.Flows.SelectMany(static item => item.Nodes))
        {
            if (node.ChildFlowId == flow.Id)
            {
                node.ChildFlowId = null;
            }
        }

        parentFlows.Clear();
        CurrentFlow = GetFirstOrDefault(GetRootFlows()) ?? GetFirstOrDefault(Document.Flows);
        CurrentNode = CurrentFlow == null ? null : GetFirstOrDefault(CurrentFlow.Nodes);
        MarkDirty();
    }

    public void MarkDirty(GameplayFlowNode? node = null)
    {
        if (node != null)
        {
            node.ModifiedAt = DateTimeOffset.UtcNow;
        }

        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved(string path)
    {
        FilePath = path;
        IsDirty = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceDocument(GameplayFlowDocument document, string? filePath, bool isDirty)
    {
        Document = document;
        FilePath = filePath;
        IsDirty = isDirty;
        parentFlows.Clear();
        CurrentFlow = GetFirstOrDefault(GetRootFlows()) ?? GetFirstOrDefault(document.Flows);
        CurrentNode = CurrentFlow == null ? null : GetFirstOrDefault(CurrentFlow.Nodes);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static List<GameplayFlowIssue> Validate(GameplayFlowDocument document)
    {
        var issues = new List<GameplayFlowIssue>();
        if (document.FormatVersion != GameplayFlowDocument.CurrentFormatVersion)
        {
            issues.Add(Error($"Unsupported format version {document.FormatVersion}."));
        }

        ValidateIds(document, issues);
        ValidateReferences(document, issues);
        ValidateGroupHierarchy(document, issues);
        ValidateChildFlows(document, issues);

        foreach (var flow in document.Flows)
        {
            ValidateNodeGraph(flow, issues);
        }

        return issues;
    }

    private static void ValidateIds(GameplayFlowDocument document, List<GameplayFlowIssue> issues)
    {
        var ids = new HashSet<Guid>();
        void Add(Guid id, string kind, Guid? flowId = null, Guid? nodeId = null)
        {
            if (id == Guid.Empty || !ids.Add(id))
            {
                issues.Add(Error($"{kind} has an empty or duplicate ID.", flowId, nodeId));
            }
        }

        Add(document.DocumentId, "Document");
        foreach (var map in document.Maps)
        {
            Add(map.Id, "Map");
        }

        foreach (var flow in document.Flows)
        {
            Add(flow.Id, "Flow", flow.Id);
            foreach (var group in flow.Groups)
            {
                Add(group.Id, "Group", flow.Id);
            }

            foreach (var node in flow.Nodes)
            {
                Add(node.Id, "Node", flow.Id, node.Id);
                foreach (var annotation in node.Annotations)
                {
                    Add(annotation.Id, "Annotation", flow.Id, node.Id);
                }
            }
        }
    }

    private static void ValidateReferences(GameplayFlowDocument document, List<GameplayFlowIssue> issues)
    {
        var mapIds = document.Maps.Select(static map => map.Id).ToHashSet();
        var flowIds = document.Flows.Select(static flow => flow.Id).ToHashSet();
        foreach (var flow in document.Flows)
        {
            var groupIds = flow.Groups.Select(static group => group.Id).ToHashSet();
            var nodeIds = flow.Nodes.Select(static node => node.Id).ToHashSet();
            foreach (var group in flow.Groups)
            {
                if (group.ParentGroupId is { } parentGroupId && !groupIds.Contains(parentGroupId))
                {
                    issues.Add(Error("Group references a parent outside its flow.", flow.Id));
                }
            }

            foreach (var node in flow.Nodes)
            {
                if (!mapIds.Contains(node.MapId))
                {
                    issues.Add(Error("Node references a missing map.", flow.Id, node.Id));
                }

                if (node.GroupId is { } groupId && !groupIds.Contains(groupId))
                {
                    issues.Add(Error("Node references a group outside its flow.", flow.Id, node.Id));
                }

                if (node.ChildFlowId is { } childFlowId && !flowIds.Contains(childFlowId))
                {
                    issues.Add(Error("Node references a missing child flow.", flow.Id, node.Id));
                }

                if (!node.Camera.Position.IsFinite
                    || !node.Camera.QAngle.IsFinite
                    || !float.IsFinite(node.Camera.FieldOfView)
                    || node.Camera.FieldOfView <= 0f
                    || node.Camera.FieldOfView >= 180f)
                {
                    issues.Add(Error("Node camera contains an invalid value.", flow.Id, node.Id));
                }

                var locators = node.SelectedEntities
                    .Concat(node.Visibility.SelectMany(static state => state.Entities))
                    .ToList();
                if (locators.Any(locator => locator.MapId != node.MapId || !mapIds.Contains(locator.MapId)))
                {
                    issues.Add(Error("Node state contains an entity locator for another map.", flow.Id, node.Id));
                }

                if (node.Visibility.GroupBy(static state => state.Scene).Any(static states => states.Count() > 1))
                {
                    issues.Add(Error("Node contains duplicate visibility snapshots for one scene.", flow.Id, node.Id));
                }

                if (node.SelectedEntities.Any(static locator => locator.Origin is { IsFinite: false })
                    || node.Annotations.Any(static annotation => !annotation.Position.IsFinite)
                    || node.Visibility.SelectMany(static state => state.Entities).Any(static locator => locator.Origin is { IsFinite: false }))
                {
                    issues.Add(Error("Node state contains a non-finite position.", flow.Id, node.Id));
                }

                if (node.NextNodeIds.Count != node.NextNodeIds.Distinct().Count())
                {
                    issues.Add(Error("Node has duplicate outgoing connections.", flow.Id, node.Id));
                }

                foreach (var nextId in node.NextNodeIds)
                {
                    if (!nodeIds.Contains(nextId))
                    {
                        issues.Add(Error("Node connection leaves its owning flow.", flow.Id, node.Id));
                    }
                    else if (nextId == node.Id)
                    {
                        issues.Add(Error("Node connects to itself.", flow.Id, node.Id));
                    }
                }
            }
        }
    }

    private static void ValidateGroupHierarchy(GameplayFlowDocument document, List<GameplayFlowIssue> issues)
    {
        foreach (var flow in document.Flows)
        {
            var groups = new Dictionary<Guid, GameplayFlowGroup>();
            foreach (var group in flow.Groups)
            {
                groups.TryAdd(group.Id, group);
            }

            var visiting = new HashSet<Guid>();
            var visited = new HashSet<Guid>();
            bool Visit(GameplayFlowGroup group)
            {
                if (visited.Contains(group.Id))
                {
                    return true;
                }

                if (!visiting.Add(group.Id))
                {
                    return false;
                }

                if (group.ParentGroupId is { } parentGroupId
                    && groups.TryGetValue(parentGroupId, out var parent)
                    && !Visit(parent))
                {
                    return false;
                }

                visiting.Remove(group.Id);
                visited.Add(group.Id);
                return true;
            }

            foreach (var group in flow.Groups)
            {
                if (!Visit(group))
                {
                    issues.Add(Error("Group hierarchy contains a cycle.", flow.Id));
                    break;
                }
            }
        }
    }

    private static void ValidateChildFlows(GameplayFlowDocument document, List<GameplayFlowIssue> issues)
    {
        var owners = document.Flows
            .SelectMany(flow => flow.Nodes.Select(node => (ParentFlow: flow, Node: node)))
            .Where(static item => item.Node.ChildFlowId != null)
            .GroupBy(static item => item.Node.ChildFlowId!.Value);

        foreach (var ownersForFlow in owners)
        {
            if (ownersForFlow.Count() > 1)
            {
                var owner = ownersForFlow.First();
                issues.Add(Error("Child flow has more than one owning node.", owner.ParentFlow.Id, owner.Node.Id));
            }
        }

        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        bool Visit(GameplayFlow flow)
        {
            if (!visiting.Add(flow.Id))
            {
                return false;
            }

            foreach (var childId in flow.Nodes.Select(static node => node.ChildFlowId).OfType<Guid>())
            {
                if (!visited.Contains(childId)
                    && FindFlow(document, childId) is { } child
                    && !Visit(child))
                {
                    return false;
                }
            }

            visiting.Remove(flow.Id);
            visited.Add(flow.Id);
            return true;
        }

        foreach (var flow in document.Flows)
        {
            if (!visited.Contains(flow.Id) && !Visit(flow))
            {
                issues.Add(Error("Child flow references are recursive.", flow.Id));
                break;
            }
        }
    }

    private static void ValidateNodeGraph(GameplayFlow flow, List<GameplayFlowIssue> issues)
    {
        var nodeIds = flow.Nodes.Select(static node => node.Id).ToHashSet();
        var incoming = flow.Nodes.ToDictionary(static node => node.Id, static _ => 0);
        foreach (var node in flow.Nodes)
        {
            foreach (var nextId in node.NextNodeIds.Where(nodeIds.Contains))
            {
                incoming[nextId]++;
            }
        }

        var explicitEdges = flow.Nodes.Sum(static node => node.NextNodeIds.Count);
        if (explicitEdges > 0 && incoming.Count(static pair => pair.Value == 0) != 1)
        {
            issues.Add(Notice("Flow has zero or multiple entry nodes.", flow.Id));
        }

        foreach (var node in flow.Nodes.Where(node => incoming[node.Id] == 0 && node.NextNodeIds.Count == 0))
        {
            issues.Add(Notice("Flow contains an isolated node.", flow.Id, node.Id));
        }

        var visited = new HashSet<Guid>();
        var visiting = new HashSet<Guid>();
        bool Visit(Guid nodeId)
        {
            if (!visiting.Add(nodeId))
            {
                return false;
            }

            if (flow.Nodes.FirstOrDefault(node => node.Id == nodeId) is { } node)
            {
                foreach (var nextId in node.NextNodeIds.Where(nodeIds.Contains))
                {
                    if (!visited.Contains(nextId) && !Visit(nextId))
                    {
                        return false;
                    }
                }
            }

            visiting.Remove(nodeId);
            visited.Add(nodeId);
            return true;
        }

        foreach (var node in flow.Nodes)
        {
            if (!visited.Contains(node.Id) && !Visit(node.Id))
            {
                issues.Add(Error("Flow node connections contain a cycle.", flow.Id, node.Id));
                break;
            }
        }

        if (explicitEdges > 0 && flow.Nodes.Count > 1)
        {
            var connected = CollectUndirected(flow, flow.Nodes[0].Id);
            if (connected.Count != flow.Nodes.Count)
            {
                issues.Add(Notice("Flow contains disconnected node paths.", flow.Id));
            }
        }
    }

    private static HashSet<Guid> CollectUndirected(GameplayFlow flow, Guid startId)
    {
        var pending = new Stack<Guid>();
        var connected = new HashSet<Guid>();
        pending.Push(startId);
        while (pending.TryPop(out var nodeId) && connected.Add(nodeId))
        {
            var node = flow.Nodes.First(item => item.Id == nodeId);
            foreach (var adjacentId in node.NextNodeIds.Concat(
                flow.Nodes.Where(item => item.NextNodeIds.Contains(nodeId)).Select(static item => item.Id)))
            {
                pending.Push(adjacentId);
            }
        }

        return connected;
    }

    private static bool CanReach(GameplayFlow flow, Guid startId, Guid targetId)
    {
        var nodes = flow.Nodes.ToDictionary(static node => node.Id);
        var pending = new Stack<Guid>();
        var visited = new HashSet<Guid>();
        pending.Push(startId);
        while (pending.TryPop(out var id))
        {
            if (id == targetId)
            {
                return true;
            }

            if (visited.Add(id) && nodes.TryGetValue(id, out var node))
            {
                foreach (var nextId in node.NextNodeIds)
                {
                    pending.Push(nextId);
                }
            }
        }

        return false;
    }

    private void RemoveFlowHierarchy(Guid flowId)
    {
        if (FindFlow(flowId) is not { } flow)
        {
            return;
        }

        foreach (var childFlowId in flow.Nodes.Select(static node => node.ChildFlowId).OfType<Guid>().ToList())
        {
            RemoveFlowHierarchy(childFlowId);
        }

        Document.Flows.Remove(flow);
    }

    private GameplayFlow? FindFlow(Guid id) => FindFlow(Document, id);

    private static GameplayFlow? FindFlow(GameplayFlowDocument document, Guid id)
        => document.Flows.FirstOrDefault(flow => flow.Id == id);

    private GameplayFlow? FindFlowContaining(GameplayFlowNode node)
        => Document.Flows.FirstOrDefault(flow => flow.Nodes.Contains(node));

    private void SetCurrent(GameplayFlow? flow, GameplayFlowNode? node)
    {
        CurrentFlow = flow;
        CurrentNode = node;
    }

    private static T? GetFirstOrDefault<T>(IReadOnlyList<T> items)
        where T : class
        => items.Count > 0 ? items[0] : null;

    private static GameplayFlowIssue Error(string message, Guid? flowId = null, Guid? nodeId = null)
        => new(GameplayFlowIssueSeverity.Error, message, flowId, nodeId);

    private static GameplayFlowIssue Notice(string message, Guid? flowId = null, Guid? nodeId = null)
        => new(GameplayFlowIssueSeverity.Notice, message, flowId, nodeId);
}
