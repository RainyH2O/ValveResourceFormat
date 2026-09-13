using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer;

/// <summary>Captures visibility overrides applied only by viewer shortcuts.</summary>
public sealed record ViewerVisibilityState
{
    /// <summary>Gets whether the scene is isolating a selection.</summary>
    public bool IsIsolating { get; init; }

    /// <summary>Gets entities hidden by viewer shortcuts.</summary>
    public IReadOnlyList<EntityLump.Entity> HiddenEntities { get; init; } = [];

    /// <summary>Gets exact non-entity nodes hidden by viewer shortcuts.</summary>
    public IReadOnlyList<SceneNode> HiddenNodes { get; init; } = [];

    /// <summary>Gets entities retained by the isolation override.</summary>
    public IReadOnlyList<EntityLump.Entity> IsolatedEntities { get; init; } = [];

    /// <summary>Gets exact nodes retained by the isolation override.</summary>
    public IReadOnlyList<SceneNode> IsolatedNodes { get; init; } = [];
}
