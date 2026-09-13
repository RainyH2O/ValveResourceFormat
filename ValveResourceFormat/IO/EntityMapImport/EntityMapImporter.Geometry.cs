using System.IO;
using System.Text.Json;
using ValveResourceFormat.IO.ContentFormats.ValveMap;

namespace ValveResourceFormat.IO;

public static partial class EntityMapImporter
{
    private static bool NeedsBox(string className)
        => IsToolVolume(className)
            || className is "func_door" or "func_door_rotating" or "func_button" or "func_rot_button"
                or "func_brush" or "func_breakable" or "func_breakable_surf" or "func_physbox"
                or "func_movelinear" or "func_rotating" or "func_tracktrain" or "func_tanktrain"
                or "func_water";

    private static bool IsToolVolume(string className)
        => className.StartsWith("trigger_", StringComparison.Ordinal)
            || className is "func_bomb_target" or "func_buyzone" or "func_hostage_rescue" or "func_clip_vphysics"
                or "func_nav_blocker" or "func_nav_markup" or "func_precipitation"
                or "post_processing_volume" or "env_cs_place";

    private static CMapMesh CreateBox(JsonElement box, string className, string context)
    {
        var size = new Vector3(64);
        var center = Vector3.Zero;
        var material = IsToolVolume(className)
            ? ToolsMaterials.GetForEntity(className, new NullFileLoader())!
            : "materials/dev/graygrid.vmat";

        if (box.ValueKind != JsonValueKind.Undefined)
        {
            RequireMembers(box, $"{context}.box", "size", "center", "material");
            if (!box.TryGetProperty("size", out var sizeValue))
            {
                throw new InvalidDataException($"{context}.box requires size.");
            }

            size = ReadVector(sizeValue, $"{context}.box.size");
            if (box.TryGetProperty("center", out var centerValue))
            {
                center = ReadVector(centerValue, $"{context}.box.center");
            }

            if (box.TryGetProperty("material", out var materialValue))
            {
                material = ReadString(materialValue, $"{context}.box.material");
            }
        }

        var min = center - size * 0.5f;
        var max = center + size * 0.5f;
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0 || !float.IsFinite(min.X) || !float.IsFinite(min.Y) || !float.IsFinite(min.Z)
            || !float.IsFinite(max.X) || !float.IsFinite(max.Y) || !float.IsFinite(max.Z)
            || min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
        {
            throw new InvalidDataException($"{context}.box must have positive, finite, non-degenerate dimensions.");
        }

        var builder = new HammerMeshBuilder();
        builder.AddVertices([
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        ]);
        builder.AddFace([0, 3, 2, 1], material);
        builder.AddFace([4, 5, 6, 7], material);
        builder.AddFace([0, 1, 5, 4], material);
        builder.AddFace([1, 2, 6, 5], material);
        builder.AddFace([2, 3, 7, 6], material);
        builder.AddFace([3, 0, 4, 7], material);
        return new CMapMesh { Name = "placeholder", MeshData = builder.GenerateMesh() };
    }
}
