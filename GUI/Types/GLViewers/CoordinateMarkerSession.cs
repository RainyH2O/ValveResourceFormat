using System.Globalization;
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

            var normalizedLine = string.Concat(line.Select(static character =>
                (char.IsWhiteSpace(character) || char.IsPunctuation(character) || char.IsSymbol(character))
                    && character is not '+' and not '-' and not '.'
                    ? ' '
                    : character));
            var parts = normalizedLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
                || !float.IsFinite(x)
                || !float.IsFinite(y)
                || !float.IsFinite(z))
            {
                error = $"Line {i + 1} must contain exactly three finite numbers: X Y Z.";
                coordinates.Clear();
                return false;
            }

            coordinates.Add(new Vector3(x, y, z));
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
