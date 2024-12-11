using System.Text.Json;
using System.Text.Json.Serialization;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Serialization.KeyValues.KVConverts
{
    public class EntityConverter : JsonConverter<Entity>
    {
        public override Entity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException("Read operation is not implemented.");
        }

        public override void Write(Utf8JsonWriter writer, Entity value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("Properties");
            JsonSerializer.Serialize(writer, value.Properties, options);

            writer.WritePropertyName("Connections");
            JsonSerializer.Serialize(writer, value.Connections, options);

            writer.WriteEndObject();
        }
    }

    public class KVObjectConverter : JsonConverter<KVObject>
    {
        public override KVObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException("Read operation is not implemented.");
        }

        public override void Write(Utf8JsonWriter writer, KVObject value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value.Properties, options);
        }
    }

    public class KVObjectListConverter : JsonConverter<List<KVObject>>
    {
        public override List<KVObject> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException("Read operation is not implemented.");
        }

        public override void Write(Utf8JsonWriter writer, List<KVObject> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();

            foreach (var kvObject in value)
            {
                JsonSerializer.Serialize(writer, kvObject.Properties, options);
            }

            writer.WriteEndArray();
        }
    }

    public class KVValueConverter : JsonConverter<KVValue>
    {
        public override KVValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException("Read operation is not implemented.");
        }

        public override void Write(Utf8JsonWriter writer, KVValue value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
