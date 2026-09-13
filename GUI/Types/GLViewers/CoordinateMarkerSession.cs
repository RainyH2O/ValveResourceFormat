using System.Linq;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.GLViewers;

internal sealed class CoordinateMarkerSession
{
    internal sealed record Marker(EntityLump.Entity Entity, CoordinateMarkerSceneNode Node, Vector3 Position);

    private readonly Scene scene;
    private readonly EntityLump parentLump;
    private readonly List<Marker> markers = [];

    public IReadOnlyList<Marker> Markers => markers;

    public CoordinateMarkerSession(Scene scene, EntityLump parentLump)
    {
        this.scene = scene;
        this.parentLump = parentLump;
    }

    public static bool TryParseCoordinates(string text, out List<Vector3> coordinates, out string error)
    {
        coordinates = [];
        error = string.Empty;

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!EntityTransformHelper.TryParseVector3(line, out var position))
            {
                error = $"Line {i + 1} must contain exactly three finite numbers: X Y Z.";
                coordinates.Clear();
                return false;
            }

            coordinates.Add(position);
        }

        if (coordinates.Count == 0)
        {
            error = "Enter at least one coordinate.";
            return false;
        }

        return true;
    }

    public void Add(string name, IReadOnlyList<Vector3> coordinates)
    {
        foreach (var position in coordinates)
        {
            var entity = new EntityLump.Entity { ParentLump = parentLump };
            entity.Add("classname", "s2v_coordinate_marker");
            entity.Add("targetname", name.Trim());
            entity.Add("origin", FormattableString.Invariant($"{position.X:R} {position.Y:R} {position.Z:R}"));

            var node = new CoordinateMarkerSceneNode(scene, position)
            {
                EntityData = entity,
                Name = name.Trim(),
            };
            scene.Add(node, dynamic: true);
            markers.Add(new Marker(entity, node, position));
        }
    }

    public void Rename(Marker marker, string name)
    {
        if (!markers.Contains(marker))
        {
            return;
        }

        marker.Entity.Remove("targetname");
        marker.Entity.Add("targetname", name.Trim());
    }

    public void Remove(IReadOnlyCollection<Marker> markersToRemove)
    {
        foreach (var marker in markersToRemove)
        {
            if (!markers.Remove(marker))
            {
                continue;
            }

            scene.Remove(marker.Node, dynamic: true);
            marker.Node.Delete();
        }
    }

    public bool Contains(EntityLump.Entity entity)
        => markers.Any(marker => ReferenceEquals(marker.Entity, entity));

    public List<EntityLump.Entity> CombineWith(IReadOnlyCollection<EntityLump.Entity> entities)
        => [.. entities, .. markers.Select(static marker => marker.Entity)];
}
