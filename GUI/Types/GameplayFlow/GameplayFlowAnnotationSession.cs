using System.Globalization;
using System.Linq;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.GameplayFlow;

internal sealed class GameplayFlowAnnotationSession : IDisposable
{
    private sealed record LiveAnnotation(
        GameplayFlowAnnotation Annotation,
        CoordinateMarkerSceneNode Node);

    private readonly Scene scene;
    private readonly EntityLump parentLump;
    private readonly List<LiveAnnotation> annotations = [];
    private readonly Dictionary<SceneNode, GameplayFlowAnnotation> annotationsByNode = new(ReferenceEqualityComparer.Instance);
    private bool disposed;

    public IReadOnlyList<SceneNode> Nodes => [.. annotations.Select(static annotation => annotation.Node)];

    public IReadOnlyList<GameplayFlowAnnotation> Annotations => [.. annotations.Select(static annotation => annotation.Annotation)];

    public GameplayFlowAnnotationSession(Scene scene, EntityLump parentLump)
    {
        this.scene = scene;
        this.parentLump = parentLump;
    }

    public List<GameplayFlowIssue> Show(IReadOnlyList<GameplayFlowAnnotation> nodeAnnotations)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Clear();

        var issues = new List<GameplayFlowIssue>();
        foreach (var annotation in nodeAnnotations)
        {
            if (!string.Equals(annotation.Type, GameplayFlowAnnotation.PointType, StringComparison.Ordinal))
            {
                issues.Add(new(
                    GameplayFlowIssueSeverity.Notice,
                    $"Annotation type '{annotation.Type}' is not available in this version."));
                continue;
            }

            var position = new Vector3(annotation.Position.X, annotation.Position.Y, annotation.Position.Z);
            var entity = new EntityLump.Entity { ParentLump = parentLump };
            entity.Add("classname", "s2v_gameplay_flow_annotation");
            entity.Add("targetname", annotation.Name.Trim());
            entity.Add("s2v_description", annotation.Description);
            entity.Add("s2v_gameplay_flow_annotation_id", annotation.Id.ToString("D", CultureInfo.InvariantCulture));
            entity.Add("origin", FormattableString.Invariant($"{position.X:R} {position.Y:R} {position.Z:R}"));

            var node = new CoordinateMarkerSceneNode(scene, position)
            {
                EntityData = entity,
                Name = annotation.Name.Trim(),
            };
            scene.Add(node, dynamic: true);

            annotations.Add(new(annotation, node));
            annotationsByNode.Add(node, annotation);
        }

        return issues;
    }

    public void Clear()
    {
        foreach (var annotation in annotations)
        {
            scene.Remove(annotation.Node, dynamic: true);
            annotation.Node.Delete();
        }

        annotations.Clear();
        annotationsByNode.Clear();
    }

    public bool TryGetAnnotation(SceneNode node, out GameplayFlowAnnotation annotation)
        => annotationsByNode.TryGetValue(node, out annotation!);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Clear();
    }
}
