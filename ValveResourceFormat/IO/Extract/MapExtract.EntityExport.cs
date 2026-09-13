using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.IO;

public sealed partial class MapExtract
{
    /// <summary>
    /// Extracts all entities from this map's entity lumps.
    /// </summary>
    public List<Entity> ToEntities()
    {
        var allEntities = new List<Entity>();
        var seen = new HashSet<Entity>();

        foreach (var entityLumpName in EntityLumpNames)
        {
            if (entityLumpName == null)
            {
                continue;
            }

            using var resource = FileLoader.LoadFileCompiled(entityLumpName);
            if (resource == null)
            {
                continue;
            }

            var entityLump = resource.DataBlock as EntityLump;
            if (entityLump == null)
            {
                continue;
            }

            foreach (var traversed in EntityLumpTraversal.EnumerateEntities(entityLump, FileLoader, Matrix4x4.Identity, includeUnreferencedChildLumps: true))
            {
                if (seen.Add(traversed.Entity))
                {
                    allEntities.Add(traversed.Entity);
                }
            }
        }

        return allEntities;
    }

    /// <summary>
    /// Serializes entities to JSON.
    /// </summary>
    public static string SerializeEntities(List<Entity> entities)
    {
        return KVJsonSerializer.SerializeEntities(entities);
    }
}
