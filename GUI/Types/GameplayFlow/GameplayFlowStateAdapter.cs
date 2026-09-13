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

        var camera = viewer.Renderer.Camera;
        var selectedNodes = viewer.GetGameplayFlowSelectedNodes();
        var selectedNodeSet = new HashSet<SceneNode>(selectedNodes, ReferenceEqualityComparer.Instance);
        var selectedEntities = new HashSet<EntityLump.Entity>(ReferenceEqualityComparer.Instance);
        var entityLocators = new List<GameplayFlowEntityLocator>();
        var issues = new List<GameplayFlowIssue>();
        var omittedSelectionCount = 0;

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

            if (selectedEntities.Add(entity))
            {
                entityLocators.Add(CreateLocator(entity, GetScene(node.Scene), mapId));
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
        var visibility = new List<GameplayFlowSceneVisibilitySnapshot>
        {
            CaptureVisibility(viewer.Scene, GameplayFlowScene.Main, mapId, issues),
        };
        if (viewer.SkyboxScene is { } skyboxScene)
        {
            visibility.Add(CaptureVisibility(skyboxScene, GameplayFlowScene.Skybox, mapId, issues));
        }

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

        var issues = new List<GameplayFlowIssue>();
        viewer.Input.SaveCameraForTransition(exitWalkMode: false);
        viewer.Input.Camera.SetLocation(ToVector3(node.Camera.Position));
        viewer.Input.Camera.SetFromQAngle(ToVector3(node.Camera.QAngle));
        viewer.Input.Camera.FieldOfView = node.Camera.FieldOfView;

        using (viewer.MakeCurrent())
        {
            issues.AddRange(annotationSession.Show(node.Annotations));
        }

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
        var scene = locator.Scene == GameplayFlowScene.Skybox ? viewer.SkyboxScene : viewer.Scene;
        if (scene == null)
        {
            return Missing(locator, "The recorded scene is not loaded.");
        }

        var candidates = scene.AllNodes
            .Where(static node => node.EntityData != null)
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

    private static GameplayFlowSceneVisibilitySnapshot CaptureVisibility(
        Scene scene,
        GameplayFlowScene flowScene,
        Guid mapId,
        List<GameplayFlowIssue> issues)
    {
        var state = scene.CaptureViewerVisibility();
        var sourceEntities = state.IsIsolating ? state.IsolatedEntities : state.HiddenEntities;
        var sourceNodes = state.IsIsolating ? state.IsolatedNodes : state.HiddenNodes;
        var locators = sourceEntities
            .Select(entity => CreateLocator(entity, flowScene, mapId))
            .ToList();
        var nonEntityNodeCount = sourceNodes.Count(static node => node.EntityData == null);
        if (nonEntityNodeCount > 0)
        {
            issues.Add(new(
                GameplayFlowIssueSeverity.Notice,
                $"{nonEntityNodeCount} non-entity visibility override(s) in the {flowScene} scene could not be recorded."));
        }

        return new()
        {
            Scene = flowScene,
            Mode = state.IsIsolating
                ? GameplayFlowVisibilityMode.Isolated
                : locators.Count > 0 ? GameplayFlowVisibilityMode.Hidden : GameplayFlowVisibilityMode.None,
            Entities = locators,
        };
    }

    private void RestoreVisibility(
        IReadOnlyList<GameplayFlowSceneVisibilitySnapshot> visibility,
        List<GameplayFlowIssue> issues)
    {
        RestoreSceneVisibility(viewer.Scene, GameplayFlowScene.Main, visibility, issues);
        if (viewer.SkyboxScene is { } skyboxScene)
        {
            RestoreSceneVisibility(skyboxScene, GameplayFlowScene.Skybox, visibility, issues);
        }
    }

    private void RestoreSceneVisibility(
        Scene scene,
        GameplayFlowScene flowScene,
        IReadOnlyList<GameplayFlowSceneVisibilitySnapshot> visibility,
        List<GameplayFlowIssue> issues)
    {
        var snapshot = visibility.FirstOrDefault(state => state.Scene == flowScene);
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

        scene.RestoreViewerVisibility(snapshot.Mode == GameplayFlowVisibilityMode.Isolated
            ? new ViewerVisibilityState { IsIsolating = true, IsolatedEntities = entities }
            : new ViewerVisibilityState { HiddenEntities = entities });
    }

    private static GameplayFlowEntityLocator CreateLocator(
        EntityLump.Entity entity,
        GameplayFlowScene scene,
        Guid mapId)
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
            HammerUniqueId = entity.GetStringProperty("hammeruniqueid"),
            Classname = entity.GetStringProperty("classname"),
            TargetName = entity.GetStringProperty("targetname"),
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
            Id = annotation.Id,
            Type = annotation.Type,
            Name = annotation.Name,
            Description = annotation.Description,
            Position = annotation.Position,
            IsSelected = isSelected,
        };
    }

    private GameplayFlowScene GetScene(Scene scene)
        => ReferenceEquals(scene, viewer.SkyboxScene) ? GameplayFlowScene.Skybox : GameplayFlowScene.Main;

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
