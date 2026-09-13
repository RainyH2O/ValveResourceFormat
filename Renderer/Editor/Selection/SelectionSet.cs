using System.Linq;
using System.Threading;

namespace ValveResourceFormat.Renderer.Editor.Selection;

/// <summary>
/// The scene nodes selected in a viewport. It is changed from both the UI thread and the render
/// thread, so changes take a lock and readers iterate <see cref="Nodes"/>, an immutable snapshot.
/// </summary>
public sealed class SelectionSet
{
    private SceneNode[] nodes = [];

    /// <summary>Occurs after the selected nodes change.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the selected nodes, in the order they were selected.</summary>
    public IReadOnlyList<SceneNode> Nodes => Volatile.Read(ref nodes);

    /// <summary>Gets whether nothing is selected.</summary>
    public bool IsEmpty => Volatile.Read(ref nodes).Length == 0;

    /// <summary>Replaces the selection with a single node.</summary>
    /// <param name="node">The node to select, or <see langword="null"/> to clear the selection.</param>
    public void Select(SceneNode? node)
    {
        using var synchronization = Scene.SelectionSynchronization.EnterScope();

        foreach (var selected in nodes)
        {
            selected.IsSelected = false;
        }

        if (node == null)
        {
            Volatile.Write(ref nodes, []);
        }
        else
        {
            node.IsSelected = true;
            Volatile.Write(ref nodes, [node]);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears the selection.</summary>
    public void Clear() => Select(null);

    /// <summary>Adds a node to the selection, or removes it when it is already selected.</summary>
    /// <param name="node">The node to toggle.</param>
    public void Toggle(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        using var synchronization = Scene.SelectionSynchronization.EnterScope();

        var index = Array.IndexOf(nodes, node);

        if (index >= 0)
        {
            node.IsSelected = false;
            Volatile.Write(ref nodes, [.. nodes.AsSpan(0, index), .. nodes.AsSpan(index + 1)]);
        }
        else
        {
            node.IsSelected = true;
            Volatile.Write(ref nodes, [.. nodes, node]);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the selection with the given nodes, preserving their order.</summary>
    /// <param name="newNodes">The nodes to select.</param>
    public void Replace(IEnumerable<SceneNode> newNodes)
    {
        ArgumentNullException.ThrowIfNull(newNodes);

        var replacement = newNodes
            .Distinct<SceneNode>(ReferenceEqualityComparer.Instance)
            .ToArray();

        using var synchronization = Scene.SelectionSynchronization.EnterScope();

        if (nodes.SequenceEqual(replacement))
        {
            return;
        }

        foreach (var selected in nodes)
        {
            selected.IsSelected = false;
        }

        foreach (var node in replacement)
        {
            node.IsSelected = true;
        }

        Volatile.Write(ref nodes, replacement);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
