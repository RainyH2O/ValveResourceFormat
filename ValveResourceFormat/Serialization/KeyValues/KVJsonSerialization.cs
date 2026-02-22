using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Serialization.KeyValues;

/// <summary>
/// JSON converter for KVObject that flattens Properties into direct key-value pairs.
/// </summary>
public sealed class KVObjectJsonConverter : JsonConverter<KVObject>
{
    /// <inheritdoc/>
    public override KVObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException("KVObject deserialization is not implemented.");
    }

    /// <inheritdoc/>
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
                if (nestedKvObject.IsArray)
                    WriteArray(writer, nestedKvObject, options);
                else
                    WriteObject(writer, nestedKvObject, options);
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

/// <summary>
/// Source-generated JSON serializer context for KV types with AOT support.
/// </summary>
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
    /// <summary>
    /// Gets the configured <see cref="JsonSerializerOptions"/> with the KV custom converter and source-generated type resolver.
    /// </summary>
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
    /// <summary>
    /// Serializes a list of entities to a JSON string.
    /// </summary>
    public static string SerializeEntities(List<Entity> entities)
    {
        var typeInfo = (JsonTypeInfo<List<Entity>>)KVJsonContext.Options.GetTypeInfo(typeof(List<Entity>));
        return JsonSerializer.Serialize(entities, typeInfo);
    }

    /// <summary>
    /// Serializes the specified value to a JSON string using the KV serializer context.
    /// </summary>
    public static string Serialize<T>(T value) where T : class
    {
        var typeInfo = (JsonTypeInfo<T>)KVJsonContext.Options.GetTypeInfo(typeof(T));
        return JsonSerializer.Serialize(value, typeInfo);
    }
}
