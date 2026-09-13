using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.IO;

public static partial class EntityMapImporter
{
    private static void ValidateUniqueKeys(JsonElement value, string context)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                {
                    throw new InvalidDataException($"{context}: duplicate property '{property.Name}'.");
                }

                ValidateUniqueKeys(property.Value, $"{context}.{property.Name}");
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ValidateUniqueKeys(item, string.Create(CultureInfo.InvariantCulture, $"{context}[{index++}]"));
            }
        }
    }

    private static void RequireObject(JsonElement value, string context)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{context} must be an object.");
        }
    }

    private static void RequireMembers(JsonElement value, string context, params string[] names)
    {
        RequireObject(value, context);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"{context}: unknown field '{property.Name}'.");
            }
        }
    }

    private static KVObject ReadValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return KVObject.Null();
            case JsonValueKind.String:
                return new KVObject(value.GetString()!);
            case JsonValueKind.True:
            case JsonValueKind.False:
                return new KVObject(value.GetBoolean());
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var signed))
                {
                    return new KVObject(signed);
                }

                if (value.TryGetUInt64(out var unsigned))
                {
                    return new KVObject(unsigned);
                }

                if (value.TryGetDouble(out var floating) && double.IsFinite(floating))
                {
                    return new KVObject(floating);
                }

                throw new InvalidDataException("Property numbers must be finite.");
            case JsonValueKind.Array:
                return KVObject.Array(value.EnumerateArray().Select(ReadValue));
            case JsonValueKind.Object:
                var collection = KVObject.Collection();
                foreach (var property in value.EnumerateObject())
                {
                    collection.Add(property.Name, ReadValue(property.Value));
                }

                return collection;
            default:
                throw new InvalidDataException("Unsupported JSON property value.");
        }
    }

    private static Vector3 ReadVector(JsonElement value, string context)
    {
        if (value.ValueKind == JsonValueKind.String && EntityTransformHelper.TryParseCoordinates(value.GetString()!, out var vector))
        {
            return vector;
        }

        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 3)
        {
            Span<float> coordinates = stackalloc float[3];
            var index = 0;
            foreach (var coordinate in value.EnumerateArray())
            {
                if (coordinate.ValueKind != JsonValueKind.Number || !coordinate.TryGetSingle(out var number) || !float.IsFinite(number))
                {
                    throw new InvalidDataException($"{context} requires three finite coordinates.");
                }

                coordinates[index++] = number;
            }

            return new Vector3(coordinates[0], coordinates[1], coordinates[2]);
        }

        throw new InvalidDataException($"{context} requires three finite coordinates as an array or coordinate string.");
    }

    private static List<Connection> ReadConnections(JsonElement connections, Entity source, string context)
    {
        if (connections.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{context}.connections must be an array.");
        }

        var result = new List<Connection>();
        foreach (var record in connections.EnumerateArray())
        {
            var connectionContext = string.Create(CultureInfo.InvariantCulture, $"{context}.connections[{result.Count}]");
            RequireMembers(record, connectionContext, "output", "target", "input", "parameter", "delay", "timesToFire", "targetType", "paramMap",
                "m_outputName", "m_targetName", "m_inputName", "m_overrideParam", "m_flDelay", "m_nTimesToFire", "m_targetType", "m_paramMap");

            JsonElement Field(string friendly, string legacy)
            {
                var hasFriendly = record.TryGetProperty(friendly, out var first);
                var hasLegacy = record.TryGetProperty(legacy, out var second);
                if (hasFriendly && hasLegacy)
                {
                    throw new InvalidDataException($"{connectionContext}: use either '{friendly}' or '{legacy}', not both.");
                }

                return hasFriendly ? first : second;
            }

            var output = ReadString(Field("output", "m_outputName"), $"{connectionContext}.output");
            var target = ReadString(Field("target", "m_targetName"), $"{connectionContext}.target");
            var input = ReadString(Field("input", "m_inputName"), $"{connectionContext}.input");
            var parameterValue = Field("parameter", "m_overrideParam");
            var parameter = parameterValue.ValueKind == JsonValueKind.Undefined ? string.Empty
                : parameterValue.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                    ? parameterValue.ValueKind == JsonValueKind.String ? parameterValue.GetString()! : parameterValue.GetRawText()
                    : throw new InvalidDataException($"{connectionContext}.parameter must be a scalar value.");
            var delayValue = Field("delay", "m_flDelay");
            var delay = 0f;
            if (delayValue.ValueKind != JsonValueKind.Undefined && (delayValue.ValueKind != JsonValueKind.Number
                || !delayValue.TryGetSingle(out delay) || !float.IsFinite(delay) || delay < 0))
            {
                throw new InvalidDataException($"{connectionContext}.delay must be finite and non-negative.");
            }

            var timesToFire = ReadInteger(Field("timesToFire", "m_nTimesToFire"), -1, $"{connectionContext}.timesToFire");
            if (timesToFire < -1)
            {
                throw new InvalidDataException($"{connectionContext}.timesToFire must be -1 or non-negative.");
            }

            var targetType = ReadInteger(Field("targetType", "m_targetType"), 2, $"{connectionContext}.targetType");
            if (!Enum.IsDefined((EntityIOTargetType)targetType))
            {
                throw new InvalidDataException($"{connectionContext}.targetType is not a supported target type.");
            }

            var paramMap = Field("paramMap", "m_paramMap");
            if (paramMap.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            {
                RequireObject(paramMap, $"{connectionContext}.paramMap");
                ValidateParameterNumbers(paramMap, $"{connectionContext}.paramMap");
            }

            result.Add(new Connection
            {
                SourceEntity = source,
                OutputName = output,
                TargetName = target,
                InputName = input,
                OverrideParam = parameter,
                Delay = delay,
                TimesToFire = timesToFire,
                TargetType = (EntityIOTargetType)targetType,
                ParamMap = paramMap.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : ReadValue(paramMap),
            });
        }

        return result;
    }

    private static void ValidateParameterNumbers(JsonElement value, string context)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                ValidateParameterNumbers(property.Value, $"{context}.{property.Name}");
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                ValidateParameterNumbers(item, context);
            }
        }
        else if (value.ValueKind == JsonValueKind.Number)
        {
            // The shared VMAP conversion narrows signed integers to int32 and floating-point values to float32.
            var valid = value.TryGetInt64(out var integer)
                ? integer is >= int.MinValue and <= int.MaxValue
                : value.TryGetUInt64(out _) || (value.TryGetSingle(out var number) && float.IsFinite(number));
            if (!valid)
            {
                throw new InvalidDataException($"{context}: number is outside the supported VMAP parameter range.");
            }
        }
    }

    private static string ReadString(JsonElement value, string context)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"{context} must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static int ReadInteger(JsonElement value, int defaultValue, string context)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            return defaultValue;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new InvalidDataException($"{context} must be a 32-bit integer.");
        }

        return number;
    }
}
