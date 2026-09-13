using System.Linq;
using ValveResourceFormat.Renderer.Entities;

namespace ValveResourceFormat.Renderer.Editor.Entities;

/// <summary>Edits live entity positions in a paused world, keeping a session-only undo and redo history.</summary>
/// <remarks>Callers synchronize access with the renderer and hold its graphics context current.</remarks>
public sealed class EntityMoveSession(EntitySystem entitySystem)
{
    private readonly List<MoveRecord> Records = [];

    /// <summary>Gets how many moves are currently applied.</summary>
    public int Position { get; private set; }

    /// <summary>Gets the number of recorded moves, including moves available to redo.</summary>
    public int Count => Records.Count;

    /// <summary>Gets whether an applied move can be undone.</summary>
    public bool CanUndo => Position > 0;

    /// <summary>Gets whether an undone move can be reapplied.</summary>
    public bool CanRedo => Position < Count;

    /// <summary>Gets an entity's current position in the same world space as the viewer camera.</summary>
    public static Vector3 GetViewerOrigin(BaseEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Vector3.Transform(entity.WorldOrigin, entity.Scene.ToViewerWorld);
    }

    /// <summary>Gets how many living entities move with this entity, including descendants of its children.</summary>
    public int GetDescendantCount(BaseEntity entity)
    {
        ValidateEntity(entity);
        return CollectHierarchy(entity).Count - 1;
    }

    /// <summary>Moves one entity to a viewer-world origin and records the change.</summary>
    /// <returns>Whether the position changed.</returns>
    public bool Move(BaseEntity entity, Vector3 viewerOrigin, bool moveDescendants = true)
    {
        EnsurePaused();
        ValidateEntity(entity);

        if (!float.IsFinite(viewerOrigin.X) || !float.IsFinite(viewerOrigin.Y) || !float.IsFinite(viewerOrigin.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(viewerOrigin), "The origin must contain three finite numbers.");
        }

        if (!Matrix4x4.Invert(entity.Scene.ToViewerWorld, out var viewerToScene))
        {
            throw new InvalidOperationException("The entity's scene cannot be mapped from viewer coordinates.");
        }

        var origin = Vector3.Transform(viewerOrigin, viewerToScene);
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(viewerOrigin), "The origin is outside the scene's coordinate range.");
        }

        if (GetViewerOrigin(entity) == viewerOrigin || entity.WorldOrigin == origin)
        {
            return false;
        }

        var hierarchy = CollectHierarchy(entity);
        var before = Capture(hierarchy);
        (BaseEntity Entity, Matrix4x4 Transform)[] stationaryChildren = moveDescendants
            ? []
            : hierarchy.Skip(1)
                .Where(child => ReferenceEquals(child.MoveParent, entity))
                .Select(static child => (Entity: child, Transform: child.RigidTransform))
                .ToArray();
        var localAngles = entity.Angles;
        entity.WorldOrigin = origin;
        entity.Angles = localAngles;

        if (!moveDescendants)
        {
            entity.RefreshPlacement();
            foreach (var (child, transform) in stationaryChildren)
            {
                child.SetWorldPose(transform);
            }
        }

        RefreshPlacements(hierarchy);

        Records.RemoveRange(Position, Count - Position);
        Records.Add(new(before, Capture(hierarchy)));
        Position++;
        return true;
    }

    /// <summary>Restores the state before the most recently applied move.</summary>
    /// <returns>The move's root entity, or null when there is nothing to undo.</returns>
    public BaseEntity? Undo()
    {
        EnsurePaused();
        if (!CanUndo)
        {
            return null;
        }

        var poses = Records[Position - 1].Before;
        Restore(poses);
        Position--;
        return poses[0].Entity;
    }

    /// <summary>Restores the state after the next undone move.</summary>
    /// <returns>The move's root entity, or null when there is nothing to redo.</returns>
    public BaseEntity? Redo()
    {
        EnsurePaused();
        if (!CanRedo)
        {
            return null;
        }

        var poses = Records[Position].After;
        Restore(poses);
        Position++;
        return poses[0].Entity;
    }

    /// <summary>Forgets the history without changing any live entity poses.</summary>
    public void Clear()
    {
        Records.Clear();
        Position = 0;
    }

    private void Restore(Pose[] poses)
    {
        // Validate the entire change before touching a pose, so an unloaded child cannot cause a partial restore.
        if (poses.Any(pose => pose.Entity.IsRemoved || pose.Entity.MoveParent != pose.Parent)
            || CollectHierarchy(poses[0].Entity).Count != poses.Length)
        {
            Clear();
            throw new InvalidOperationException("An entity in this move is no longer available. The move history was cleared.");
        }

        foreach (var pose in poses)
        {
            pose.Entity.Origin = pose.Origin;
            pose.Entity.Angles = pose.Angles;
        }

        RefreshPlacements([.. poses.Select(static pose => pose.Entity)]);
    }

    private static void RefreshPlacements(List<BaseEntity> hierarchy)
    {
        HashSet<Scene> relightScenes = [];
        foreach (var entity in hierarchy)
        {
            entity.RefreshPlacement();

            if (entity is PostProcessingVolume { Volume: { } volume })
            {
                volume.Transform = entity.Transform;
                if (volume.Collider != null)
                {
                    volume.Collider.Transform = entity.Transform;
                }
            }

            if (entity is EnvCubemapFog { Fog: { } fog })
            {
                // The fog can use its orientation from a separate env_sky entity.
                fog.Transform = fog.Transform with { Translation = entity.Transform.Translation };
            }

            var lightingInfo = entity.Scene.LightingInfo;
            var hasLightingVolumes = false;
            foreach (var envMap in lightingInfo.EnvMaps.Where(envMap => ReferenceEquals(envMap.EntityInstance, entity)))
            {
                envMap.Transform = entity.RigidTransform;
                hasLightingVolumes = true;
            }

            foreach (var lightProbe in lightingInfo.LightProbes.Where(lightProbe => ReferenceEquals(lightProbe.EntityInstance, entity)))
            {
                lightProbe.Transform = entity.RigidTransform;
                hasLightingVolumes = true;
            }

            if (hasLightingVolumes)
            {
                relightScenes.Add(entity.Scene);
            }
        }

        foreach (var scene in relightScenes)
        {
            scene.LightingDebug.Clear();
            scene.RebindLighting();
        }
    }

    private List<BaseEntity> CollectHierarchy(BaseEntity entity)
    {
        var children = entitySystem.Entities.Where(static candidate => !candidate.IsRemoved && candidate.MoveParent != null)
            .ToLookup(static candidate => candidate.MoveParent!);
        var hierarchy = new List<BaseEntity> { entity };
        for (var i = 0; i < hierarchy.Count; i++)
        {
            hierarchy.AddRange(children[hierarchy[i]]);
        }

        return hierarchy;
    }

    private static Pose[] Capture(List<BaseEntity> hierarchy)
        => [.. hierarchy.Select(static entity => new Pose(entity, entity.MoveParent, entity.Origin, entity.Angles))];

    private void EnsurePaused()
    {
        if (entitySystem.Enabled)
        {
            throw new InvalidOperationException("Pause entity simulation before editing positions.");
        }
    }

    private void ValidateEntity(BaseEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.EntitySystem != entitySystem || entity.IsRemoved || entity is WorldEntity or PlayerEntity)
        {
            throw new InvalidOperationException("Select a living map entity in this viewer.");
        }
    }

    private sealed record Pose(BaseEntity Entity, BaseEntity? Parent, Vector3 Origin, Vector3 Angles);
    private sealed record MoveRecord(Pose[] Before, Pose[] After);
}
