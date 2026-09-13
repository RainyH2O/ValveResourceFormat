using System.Linq;
using ValveKeyValue;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Serialization.KeyValues;

/// <summary>Serializes legacy entity exports with their authored I/O connections.</summary>
public static class EntityExportSerializer
{
    /// <summary>Builds a legacy entity object from the selected property values and its I/O connections.</summary>
    /// <param name="entity">The source entity.</param>
    /// <param name="properties">Optional property selection; null includes every property.</param>
    /// <returns>A JSON-compatible entity object.</returns>
    public static KVObject ToLegacyEntity(Entity entity, ISet<string>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var result = KVObject.Collection();
        foreach (var (key, value) in entity.Children)
        {
            if ((properties == null || properties.Contains(key)) && (properties == null || !value.IsNull))
            {
                result.Add(key, value);
            }
        }

        if (entity.Connections?.Count > 0)
        {
            result.Add("connections", KVObject.Array(entity.Connections.Select(ConnectionData)));
        }

        return result;
    }

    private static KVObject ConnectionData(Connection connection)
    {
        var result = KVObject.Collection();
        result.Add("m_outputName", connection.OutputName);
        result.Add("m_targetName", connection.TargetName);
        result.Add("m_inputName", connection.InputName);
        result.Add("m_overrideParam", connection.OverrideParam);
        result.Add("m_flDelay", connection.Delay);
        result.Add("m_nTimesToFire", (long)connection.TimesToFire);
        result.Add("m_targetType", (long)connection.TargetType);
        return result;
    }
}
