using System.Globalization;
using System.Linq;
using GUI.Types.GLViewers;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GameplayFlow;

internal sealed class GameplayFlowStateAdapter : IDisposable
{
    private const float OriginTolerance = 0.1f;

    private readonly GLWorldViewer viewer;
    private readonly string resourcePath;
    private readonly GameplayFlowAnnotationSession annotationSession;
    private bool disposed;

    public GameplayFlowStateAdapter(GLWorldViewer viewer, string resourcePath)
    {
        this.viewer = viewer;
        this.resourcePath = NormalizeResourcePath(resourcePath);

        var entities = viewer.LoadedWorld?.Entities;
        var parentLump = entities is { Count: > 0 }
            ? entities[0].ParentLump
            : new EntityLump { Resource = new ValveResourceFormat.Resource() };
        annotationSession = new(viewer.Scene, parentLump);
    }

    public bool CanRestore(GameplayFlowMap map)
        => string.Equals(resourcePath, NormalizeResourcePath(map.ResourcePath), StringComparison.OrdinalIgnoreCase);

    public GameplayFlowCapturedState Capture(Guid mapId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var lockedGl = viewer.MakeCurrent();

        var camera = viewer.Renderer.Camera;
        var selectedNodes = viewer.GetGameplayFlowSelectedNodes();
        var selectedNodeSet = new HashSet<SceneNode>(selectedNodes, ReferenceEqualityComparer.Instance);
        var selectedEntities = new HashSet<EntityLump.Entity>(ReferenceEqualityComparer.Instance);
        var entityLocators = new List<GameplayFlowEntityLocator>();
        var issues = new List<GameplayFlowIssue>();
        var omittedSelectionCount = 0;
        var sceneGroups = viewer.Renderer.Scenes.GroupBy(scene => (Scene: GetScene(scene), Key: GetSceneKey(scene))).ToList();
        var recordableScenes = sceneGroups.Where(static group => group.Count() == 1)
            .Select(static group => group.First()).ToHashSet();
        if (sceneGroups.Any(static group => group.Count() > 1))
        {
            issues.Add(new(GameplayFlowIssueSeverity.Notice, "Ambiguous scene instances could not be recorded."));
        }

        foreach (var node in selectedNodes)
        {
            if (annotationSession.TryGetAnnotation(node, out _))
            {
                continue;
            }

            if (node.EntityData is not { } entity)
            {
                omittedSelectionCount++;
                continue;
            }

            if (recordableScenes.Contains(node.Scene) && selectedEntities.Add(entity))
            {
                entityLocators.Add(CreateLocator(entity, GetScene(node.Scene), mapId, GetSceneKey(node.Scene)));
            }
        }

        if (omittedSelectionCount > 0)
        {
            issues.Add(new(
                GameplayFlowIssueSeverity.Notice,
                $"{omittedSelectionCount} selected non-entity node(s) could not be recorded."));
        }

        var annotations = annotationSession.Annotations
            .Select(annotation => CopyAnnotation(annotation, selectedNodeSet))
            .ToList();
        var visibility = viewer.Renderer.Scenes.Where(recordableScenes.Contains)
            .Select(scene => CaptureVisibility(scene, GetScene(scene), mapId))
            .ToList();

        return new(
            new()
            {
                Position = ToFlowVector(camera.Location),
                QAngle = ToFlowVector(camera.GetQAngle()),
                FieldOfView = camera.FieldOfView,
            },
            entityLocators,
            annotations,
            visibility,
            issues);
    }

    public List<GameplayFlowIssue> Restore(GameplayFlowNode node, GameplayFlowMap map)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!CanRestore(map) || node.MapId != map.Id)
        {
            return
            [
                new(
                    GameplayFlowIssueSeverity.Notice,
                    $"This node belongs to map '{map.DisplayName}'.",
                    NodeId: node.Id),
            ];
        }

        using var lockedGl = viewer.MakeCurrent();
        var issues = new List<GameplayFlowIssue>();
        viewer.Input.SaveCameraForTransition(exitWalkMode: false);
        viewer.Input.Camera.SetLocation(ToVector3(node.Camera.Position));
        viewer.Input.Camera.SetFromQAngle(ToVector3(node.Camera.QAngle));
        viewer.Input.Camera.FieldOfView = node.Camera.FieldOfView;

        issues.AddRange(annotationSession.Show(node.Annotations));

        var selectedNodes = new List<SceneNode>();
        foreach (var locator in node.SelectedEntities)
        {
            var resolution = ResolveLocator(locator);
            if (resolution.Node != null)
            {
                selectedNodes.Add(resolution.Node);
            }
            else if (resolution.Issue != null)
            {
                issues.Add(resolution.Issue with { NodeId = node.Id });
            }
        }

        foreach (var annotationNode in annotationSession.Nodes)
        {
            if (annotationSession.TryGetAnnotation(annotationNode, out var annotation) && annotation.IsSelected)
            {
                selectedNodes.Add(annotationNode);
            }
        }

        RestoreVisibility(node.Visibility, issues);
        viewer.SelectGameplayFlowNodes(selectedNodes.Distinct().ToList());
        viewer.ActivateRenderLoop();
        return issues;
    }

    public GameplayFlowAnnotation? ConvertSelectedCoordinateMarker()
        => viewer.CreateGameplayFlowAnnotationFromSelectedMarker();

    public GameplayFlowAnnotation CreateAnnotationAtCamera(string name)
    {
        var position = viewer.Renderer.Camera.Location;
        return new()
        {
            Name = name,
            Position = ToFlowVector(position),
        };
    }

    public GameplayFlowResolvedEntity ResolveLocator(GameplayFlowEntityLocator locator)
    {
        var scene = ResolveScene(locator.Scene, locator.SceneKey);
        if (scene == null)
        {
            return Missing(locator, "The recorded scene is not loaded or is ambiguous.");
        }

        var candidates = scene.AllNodes
            .Where(static node => node.EntityData != null)
            .DistinctBy(static node => node.EntityData, ReferenceEqualityComparer.Instance)
            .Select(node => scene.Find(node.EntityData!) ?? node)
            .ToList();
        if (!string.IsNullOrEmpty(locator.HammerUniqueId))
        {
            var idMatches = candidates
                .Where(node => string.Equals(
                    node.EntityData!.GetStringProperty("hammeruniqueid"),
                    locator.HammerUniqueId,
                    StringComparison.Ordinal))
                .ToList();
            if (idMatches.Count == 1 && MatchesDetails(idMatches[0].EntityData!, locator))
            {
                return new(idMatches[0], null);
            }

            if (idMatches.Count > 1)
            {
                return Ambiguous(locator);
            }
        }

        if (string.IsNullOrEmpty(locator.Classname)
            || string.IsNullOrEmpty(locator.TargetName) && locator.Origin == null)
        {
            return Missing(locator, "The entity locator does not contain enough fallback information.");
        }

        var fallbackMatches = candidates
            .Where(node => MatchesDetails(node.EntityData!, locator))
            .ToList();
        return fallbackMatches.Count switch
        {
            1 => new(fallbackMatches[0], null),
            > 1 => Ambiguous(locator),
            _ => Missing(locator, "The recorded entity is not renderable in this map."),
        };
    }

    public void ClearAnnotations()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var lockedGl = viewer.MakeCurrent();
        var selectedNodes = viewer.GetGameplayFlowSelectedNodes();
        var remainingNodes = selectedNodes.Where(node => !annotationSession.TryGetAnnotation(node, out _)).ToList();

        annotationSession.Clear();
        if (remainingNodes.Count != selectedNodes.Count)
        {
            viewer.SelectGameplayFlowNodes(remainingNodes);
        }

        viewer.ActivateRenderLoop();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        using (viewer.MakeCurrent())
        {
            annotationSession.Dispose();
        }
    }

    private GameplayFlowSceneVisibilitySnapshot CaptureVisibility(
        Scene scene,
        GameplayFlowScene flowScene,
        Guid mapId)
    {
        var state = scene.CaptureViewerVisibility();
        var hiddenEntities = new HashSet<EntityLump.Entity>(state.HiddenEntities, ReferenceEqualityComparer.Instance);
        var hiddenNodes = new HashSet<SceneNode>(state.HiddenNodes, ReferenceEqualityComparer.Instance);
        var sourceEntities = state.IsIsolating
            ? state.IsolatedEntities.Where(entity => !hiddenEntities.Contains(entity)).ToList()
            : state.HiddenEntities;
        var sourceNodes = state.IsIsolating
            ? state.IsolatedNodes.Where(node => node.EntityData is { } entity
                ? !hiddenEntities.Contains(entity) : !hiddenNodes.Contains(node)).ToList()
            : state.HiddenNodes;
        var sceneEntities = scene.AllNodes.Select(static node => node.EntityData)
            .OfType<EntityLump.Entity>().ToHashSet(ReferenceEqualityComparer.Instance);
        var locators = sourceEntities
            .Where(sceneEntities.Contains)
            .Select(entity => CreateLocator(entity, flowScene, mapId, GetSceneKey(scene)))
            .ToList();
        return new()
        {
            Scene = flowScene,
            SceneKey = GetSceneKey(scene),
            Mode = state.IsIsolating
                ? GameplayFlowVisibilityMode.Isolated
                : locators.Count > 0 || sourceNodes.Count > 0 ? GameplayFlowVisibilityMode.Hidden : GameplayFlowVisibilityMode.None,
            Entities = locators,
            Nodes = sourceNodes.Where(node => node.Scene == scene).Select(CreateNodeLocator).ToList(),
        };
    }

    private void RestoreVisibility(
        IReadOnlyList<GameplayFlowSceneVisibilitySnapshot> visibility,
        List<GameplayFlowIssue> issues)
    {
        foreach (var snapshot in visibility)
        {
            if (ResolveScene(snapshot.Scene, snapshot.SceneKey) == null)
            {
                issues.Add(new(GameplayFlowIssueSeverity.Notice, "A recorded visibility scene is not loaded or is ambiguous."));
            }
        }

        foreach (var scene in viewer.Renderer.Scenes)
        {
            if (ResolveScene(GetScene(scene), GetSceneKey(scene)) == scene)
            {
                RestoreSceneVisibility(scene, GetScene(scene), visibility, issues);
            }
        }
    }

    private void RestoreSceneVisibility(
        Scene scene,
        GameplayFlowScene flowScene,
        IReadOnlyList<GameplayFlowSceneVisibilitySnapshot> visibility,
        List<GameplayFlowIssue> issues)
    {
        var snapshot = visibility.FirstOrDefault(state => ResolveScene(state.Scene, state.SceneKey) == scene);
        if (snapshot == null || snapshot.Mode == GameplayFlowVisibilityMode.None)
        {
            scene.ShowAllNodes();
            return;
        }

        var entities = new List<EntityLump.Entity>();
        foreach (var locator in snapshot.Entities)
        {
            var resolution = ResolveLocator(locator);
            if (resolution.Node?.EntityData is { } entity)
            {
                entities.Add(entity);
            }
            else if (resolution.Issue != null)
            {
                issues.Add(resolution.Issue);
            }
        }

        var nodes = new List<SceneNode>();
        foreach (var locator in snapshot.Nodes)
        {
            var candidates = scene.AllNodes.Where(candidate => MatchesNodeLocator(candidate, locator)).ToList();
            var hinted = candidates.Find(candidate => candidate.Id == locator.NodeId);
            if (hinted != null)
            {
                nodes.Add(hinted);
            }
            else if (candidates.Count == 1)
            {
                nodes.Add(candidates[0]);
            }
            else
            {
                issues.Add(new(GameplayFlowIssueSeverity.Notice,
                    $"Scene node '{locator.Name}' is {(candidates.Count == 0 ? "missing or changed" : "ambiguous")} in the {flowScene} scene."));
            }
        }

        scene.RestoreViewerVisibility(snapshot.Mode == GameplayFlowVisibilityMode.Isolated
            ? new ViewerVisibilityState { IsIsolating = true, IsolatedEntities = entities, IsolatedNodes = nodes }
            : new ViewerVisibilityState { HiddenEntities = entities, HiddenNodes = nodes });
    }

    private static GameplayFlowSceneNodeLocator CreateNodeLocator(SceneNode node)
    {
        var transform = node.Transform;
        return new()
        {
            NodeId = node.Id,
            NodeType = node.GetType().FullName!,
            Name = node.Name ?? string.Empty,
            LayerName = node.LayerName ?? string.Empty,
            BoundsMin = ToFlowVector(node.LocalBoundingBox.Min),
            BoundsMax = ToFlowVector(node.LocalBoundingBox.Max),
            Transform = GetTransformValues(transform),
        };
    }

    private static bool MatchesNodeLocator(SceneNode node, GameplayFlowSceneNodeLocator locator)
    {
        if (!string.Equals(node.GetType().FullName, locator.NodeType, StringComparison.Ordinal)
            || !string.Equals(node.Name ?? string.Empty, locator.Name, StringComparison.Ordinal)
            || !string.Equals(node.LayerName ?? string.Empty, locator.LayerName, StringComparison.Ordinal)
            || node.LocalBoundingBox.Min != ToVector3(locator.BoundsMin)
            || node.LocalBoundingBox.Max != ToVector3(locator.BoundsMax))
        {
            return false;
        }

        var values = locator.Transform;
        return node.Transform == new Matrix4x4(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11],
            values[12], values[13], values[14], values[15]);
    }

    private static GameplayFlowEntityLocator CreateLocator(
        EntityLump.Entity entity,
        GameplayFlowScene scene,
        Guid mapId,
        string? sceneKey)
    {
        GameplayFlowVector3? origin = null;
        if (entity.TryGetValue("origin", out _))
        {
            origin = ToFlowVector(entity.GetVector3Property("origin"));
        }

        return new()
        {
            MapId = mapId,
            Scene = scene,
            SceneKey = sceneKey,
            HammerUniqueId = entity.GetStringProperty("hammeruniqueid"),
            Classname = entity.GetStringProperty("classname") ?? string.Empty,
            TargetName = entity.GetStringProperty("targetname") ?? string.Empty,
            Origin = origin,
        };
    }

    private static bool MatchesDetails(EntityLump.Entity entity, GameplayFlowEntityLocator locator)
    {
        if (!string.Equals(entity.GetStringProperty("classname"), locator.Classname, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(locator.TargetName)
            && !string.Equals(entity.GetStringProperty("targetname"), locator.TargetName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return locator.Origin is not { } origin
            || Vector3.Distance(entity.GetVector3Property("origin"), ToVector3(origin)) <= OriginTolerance;
    }

    private GameplayFlowAnnotation CopyAnnotation(
        GameplayFlowAnnotation annotation,
        HashSet<SceneNode> selectedNodes)
    {
        var isSelected = annotationSession.Nodes.Any(node =>
            selectedNodes.Contains(node)
            && annotationSession.TryGetAnnotation(node, out var candidate)
            && candidate.Id == annotation.Id);
        return new()
        {
            Type = annotation.Type,
            Name = annotation.Name,
            Description = annotation.Description,
            Position = annotation.Position,
            IsSelected = isSelected,
        };
    }

    private GameplayFlowScene GetScene(Scene scene)
        => ReferenceEquals(scene, viewer.Scene) ? GameplayFlowScene.Main : GameplayFlowScene.Skybox;

    private string? GetSceneKey(Scene scene)
    {
        if (scene == viewer.Scene)
        {
            return null;
        }

        var group = viewer.OwnSceneSpawnGroups.FirstOrDefault(group => group.Scene == scene);
        var placement = group?.Transform ?? Matrix4x4.Identity;
        var transform = string.Join(',', GetTransformValues(placement).Select(static value => value.ToString("R", CultureInfo.InvariantCulture)));
        return $"{scene.WorldGroup}|{group?.MapName}|{transform}";
    }

    private Scene? ResolveScene(GameplayFlowScene scene, string? sceneKey)
    {
        var matches = viewer.Renderer.Scenes
            .Where(candidate => GetScene(candidate) == scene
                && (sceneKey != null || scene != GameplayFlowScene.Skybox || candidate.WorldGroup != null)
                && (sceneKey == null || string.Equals(GetSceneKey(candidate), sceneKey, StringComparison.Ordinal)))
            .Take(2)
            .ToArray();
        return matches is [var match] ? match : null;
    }

    private static float[] GetTransformValues(Matrix4x4 transform)
        =>
        [
            transform.M11, transform.M12, transform.M13, transform.M14,
            transform.M21, transform.M22, transform.M23, transform.M24,
            transform.M31, transform.M32, transform.M33, transform.M34,
            transform.M41, transform.M42, transform.M43, transform.M44,
        ];

    private static GameplayFlowResolvedEntity Missing(GameplayFlowEntityLocator locator, string message)
        => new(null, new(GameplayFlowIssueSeverity.Notice, $"{Describe(locator)}: {message}"));

    private static GameplayFlowResolvedEntity Ambiguous(GameplayFlowEntityLocator locator)
        => new(null, new(GameplayFlowIssueSeverity.Notice, $"{Describe(locator)}: more than one entity matches."));

    private static string Describe(GameplayFlowEntityLocator locator)
        => !string.IsNullOrEmpty(locator.TargetName)
            ? locator.TargetName
            : !string.IsNullOrEmpty(locator.HammerUniqueId) ? locator.HammerUniqueId : locator.Classname;

    private static GameplayFlowVector3 ToFlowVector(Vector3 vector)
        => new(vector.X, vector.Y, vector.Z);

    private static Vector3 ToVector3(GameplayFlowVector3 vector)
        => new(vector.X, vector.Y, vector.Z);

    private static string NormalizeResourcePath(string path)
        => path.Replace('\\', '/').Trim();
}

internal sealed record GameplayFlowCapturedState(
    GameplayFlowCameraSnapshot Camera,
    List<GameplayFlowEntityLocator> SelectedEntities,
    List<GameplayFlowAnnotation> Annotations,
    List<GameplayFlowSceneVisibilitySnapshot> Visibility,
    IReadOnlyList<GameplayFlowIssue> Issues);

internal sealed record GameplayFlowResolvedEntity(
    SceneNode? Node,
    GameplayFlowIssue? Issue);
