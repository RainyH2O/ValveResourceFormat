using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Serialization.KeyValues;

/// <summary>
/// JSON converter for KVObject that flattens Properties into direct key-value pairs.
/// </summary>
public sealed class KVObjectJsonConverter : JsonConverter<KVObject>
{
    public override KVObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException("KVObject deserialization is not implemented.");
    }

    public override void Write(Utf8JsonWriter writer, KVObject value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.IsArray)
        {
            WriteArray(writer, value, options);
        }
        else
        {
            WriteObject(writer, value, options);
        }
    }

    private static void WriteArray(Utf8JsonWriter writer, KVObject kvArray, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        for (var i = 0; i < kvArray.Count; i++)
        {
            var indexKey = i.ToString(CultureInfo.InvariantCulture);
            if (kvArray.Properties.TryGetValue(indexKey, out var kvValue))
            {
                WriteKVValue(writer, kvValue, options);
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteObject(Utf8JsonWriter writer, KVObject kvObject, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (key, kvValue) in kvObject.Properties)
        {
            writer.WritePropertyName(key);
            WriteKVValue(writer, kvValue, options);
        }

        writer.WriteEndObject();
    }

    private static void WriteKVValue(Utf8JsonWriter writer, KVValue kvValue, JsonSerializerOptions options)
    {
        switch (kvValue.Value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case KVObject nestedKvObject:
                JsonSerializer.Serialize(writer, nestedKvObject, options);
                break;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                break;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                break;
            case byte byteValue:
                writer.WriteNumberValue(byteValue);
                break;
            case short shortValue:
                writer.WriteNumberValue(shortValue);
                break;
            case int intValue:
                writer.WriteNumberValue(intValue);
                break;
            case uint uintValue:
                writer.WriteNumberValue(uintValue);
                break;
            case long longValue:
                writer.WriteNumberValue(longValue);
                break;
            case ulong ulongValue:
                writer.WriteNumberValue(ulongValue);
                break;
            case float floatValue:
                writer.WriteNumberValue(floatValue);
                break;
            case double doubleValue:
                writer.WriteNumberValue(doubleValue);
                break;
            case byte[] byteArray:
                writer.WriteBase64StringValue(byteArray);
                break;
            default:
                writer.WriteStringValue(kvValue.Value.ToString() ?? string.Empty);
                break;
        }
    }
}

[JsonSerializable(typeof(Entity))]
[JsonSerializable(typeof(KVObject))]
[JsonSerializable(typeof(List<KVObject>))]
[JsonSerializable(typeof(KVValue))]
[JsonSerializable(typeof(List<Entity>))]
[JsonSerializable(typeof(Vector3))]
[JsonSerializable(typeof(Vector2))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
public partial class KVJsonContext : JsonSerializerContext
{
    public new static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = Default,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new KVObjectJsonConverter() }
    };
}

/// <summary>
/// JSON serializer for KV objects using source generation.
/// </summary>
public static class KVJsonSerializer
{
    public static string SerializeEntities(List<Entity> entities)
    {
        return JsonSerializer.Serialize(entities, KVJsonContext.Options);
    }

    public static string Serialize<T>(T value) where T : class
    {
        return JsonSerializer.Serialize(value, KVJsonContext.Options);
    }
}
