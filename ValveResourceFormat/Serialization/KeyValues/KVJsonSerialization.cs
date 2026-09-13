using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ValveKeyValue;
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
            WriteKVObject(writer, kvArray[i], options);
        }

        writer.WriteEndArray();
    }

    private static void WriteObject(Utf8JsonWriter writer, KVObject kvObject, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (key, kvValue) in kvObject.Children)
        {
            writer.WritePropertyName(key);
            WriteKVObject(writer, kvValue, options);
        }

        writer.WriteEndObject();
    }

    private static void WriteKVObject(Utf8JsonWriter writer, KVObject kvObject, JsonSerializerOptions options)
    {
        switch (kvObject.ValueType)
        {
            case KVValueType.Null:
                writer.WriteNullValue();
                break;
            case KVValueType.Collection:
                WriteObject(writer, kvObject, options);
                break;
            case KVValueType.Array:
                WriteArray(writer, kvObject, options);
                break;
            case KVValueType.BinaryBlob:
                writer.WriteBase64StringValue(kvObject.AsBlob());
                break;
            case KVValueType.Boolean:
                writer.WriteBooleanValue((bool)kvObject);
                break;
            case KVValueType.String:
                writer.WriteStringValue((string)kvObject);
                break;
            case KVValueType.Int16:
                writer.WriteNumberValue((short)kvObject);
                break;
            case KVValueType.Int32:
                writer.WriteNumberValue((int)kvObject);
                break;
            case KVValueType.Int64:
                writer.WriteNumberValue((long)kvObject);
                break;
            case KVValueType.UInt16:
                writer.WriteNumberValue((ushort)kvObject);
                break;
            case KVValueType.UInt32:
                writer.WriteNumberValue((uint)kvObject);
                break;
            case KVValueType.UInt64:
                writer.WriteNumberValue((ulong)kvObject);
                break;
            case KVValueType.FloatingPoint:
                writer.WriteNumberValue((float)kvObject);
                break;
            case KVValueType.FloatingPoint64:
                writer.WriteNumberValue((double)kvObject);
                break;
            default:
                writer.WriteStringValue(kvObject.ToString());
                break;
        }
    }
}

/// <summary>
/// Source-generated JSON serializer context for KV types with AOT support.
/// </summary>
[JsonSerializable(typeof(KVObject), TypeInfoPropertyName = "KVObjectTypeInfo")]
[JsonSerializable(typeof(List<KVObject>))]
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
        TypeInfoResolver = new KVJsonContext(),
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
        var typeInfo = (JsonTypeInfo<List<KVObject>>)KVJsonContext.Options.GetTypeInfo(typeof(List<KVObject>));
        return JsonSerializer.Serialize(entities.ConvertAll(entity => EntityExportSerializer.ToLegacyEntity(entity)), typeInfo);
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
