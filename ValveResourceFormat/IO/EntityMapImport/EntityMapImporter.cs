using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ValveKeyValue;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.IO;

/// <summary>Converts authored or exported entity JSON into an editable Hammer VMAP.</summary>
public static partial class EntityMapImporter
{
    /// <summary>Validates entity JSON and creates a VMAP without writing any files.</summary>
    /// <param name="json">An entity array or a version-1 document containing an entities array.</param>
    /// <returns>The serialized map, entity count, and conversion notices.</returns>
    /// <exception cref="InvalidDataException">The JSON does not describe a supported entity document.</exception>
    public static EntityMapImportResult Convert(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            ValidateUniqueKeys(document.RootElement, "$");
            return ConvertDocument(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid entity JSON: {exception.Message}", exception);
        }
    }

    private static EntityMapImportResult ConvertDocument(JsonElement root)
    {
        var records = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            RequireMembers(root, "document", "formatVersion", "entities");
            if (!root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var formatVersion) || formatVersion != 1)
            {
                throw new InvalidDataException("The entity document requires formatVersion 1.");
            }

            records = root.TryGetProperty("entities", out var array) ? array : default;
        }

        if (records.ValueKind != JsonValueKind.Array || records.GetArrayLength() == 0)
        {
            throw new InvalidDataException("Expected a non-empty entities array.");
        }

        var map = new CMapRootElement();
        var nextId = 1;
        map.World.NodeID = nextId++;
        map.World.ReferenceID = (ulong)map.World.NodeID;
        var warnings = new List<string>();
        var entities = new List<Entity>();
        using var sourceResource = new Resource();
        var parentLump = new EntityLump { Resource = sourceResource };
        var hasWorld = false;
        var recordIndex = 0;

        foreach (var record in records.EnumerateArray())
        {
            var context = string.Create(CultureInfo.InvariantCulture, $"entities[{recordIndex++}]");
            RequireObject(record, context);
            var properties = record;
            var hasNestedProperties = record.TryGetProperty("properties", out var nested);
            if (hasNestedProperties)
            {
                RequireMembers(record, context, "properties", "box", "connections");
                RequireObject(nested, $"{context}.properties");
                properties = nested;
            }

            var entity = new Entity { ParentLump = parentLump };
            foreach (var property in properties.EnumerateObject())
            {
                var key = property.Name.ToLowerInvariant();
                if (key is "box" or "connections" or "properties")
                {
                    if (hasNestedProperties)
                    {
                        throw new InvalidDataException($"{context}.properties: place '{property.Name}' outside the properties object.");
                    }

                    if (!property.Name.Equals(key, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"{context}: use the field name '{key}' with this exact casing.");
                    }

                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Null && key != "hammeruniqueid")
                {
                    entity.Add(key, ReadValue(property.Value));
                }
            }

            if (!entity.TryGetValue("classname", out var classValue) || classValue.ValueType != KVValueType.String
                || string.IsNullOrWhiteSpace((string)classValue) || ((string)classValue).Any(char.IsWhiteSpace))
            {
                throw new InvalidDataException($"{context}: classname must be a non-empty entity class name.");
            }

            var className = (string)classValue;
            foreach (var stringKey in new[] { "targetname", "model" })
            {
                if (entity.TryGetValue(stringKey, out var stringValue) && stringValue.ValueType != KVValueType.String)
                {
                    throw new InvalidDataException($"{context}.{stringKey} must be a string.");
                }
            }

            foreach (var transformKey in new[] { "origin", "angles", "scales" })
            {
                if (entity.ContainsKey(transformKey))
                {
                    var transform = properties.EnumerateObject().First(property => property.Name.Equals(transformKey, StringComparison.OrdinalIgnoreCase));
                    var vector = ReadVector(transform.Value, $"{context}.{transformKey}");
                    if (transformKey == "scales" && (vector.X == 0 || vector.Y == 0 || vector.Z == 0))
                    {
                        throw new InvalidDataException($"{context}.scales must have nonzero components.");
                    }

                    entity[transformKey] = KVObject.Array([new KVObject(vector.X), new KVObject(vector.Y), new KVObject(vector.Z)]);
                }
            }

            if (record.TryGetProperty("connections", out var connections))
            {
                entity.Connections = ReadConnections(connections, entity, context);
            }

            if (className == "worldspawn")
            {
                if (hasWorld || record.TryGetProperty("box", out _))
                {
                    throw new InvalidDataException($"{context}: only one worldspawn is allowed, without box geometry.");
                }

                hasWorld = true;
                MapExtract.AddProperties(className, entity, map.World);
                continue;
            }

            var mapEntity = new CMapEntity
            {
                NodeID = nextId++,
                Name = entity.GetStringProperty("targetname", className),
            };
            mapEntity.ReferenceID = (ulong)mapEntity.NodeID;
            var hasBox = record.TryGetProperty("box", out var box);
            if (hasBox || NeedsBox(className))
            {
                if (!hasBox)
                {
                    warnings.Add($"{context} ({className}): generated a 64-unit placeholder box; resize it in Hammer.");
                }

                if (NeedsBox(className) && entity.Remove("model"))
                {
                    warnings.Add($"{context} ({className}): replaced the model reference with editable box geometry.");
                }

                var mesh = CreateBox(box, className, context);
                mesh.NodeID = nextId++;
                mesh.ReferenceID = (ulong)mesh.NodeID;
                mapEntity.Children.Add(mesh);
            }

            MapExtract.AddProperties(className, entity, mapEntity);
            map.World.Children.Add(mapEntity);
            entities.Add(entity);

            if (className.StartsWith("prop_", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(entity.GetStringProperty("model")))
                {
                    warnings.Add($"{context} ({className}): this model-backed entity needs a valid model resource in Hammer.");
                }
                else if (hasBox)
                {
                    warnings.Add($"{context} ({className}): the box does not replace the model required by this entity; verify the geometry in Hammer.");
                }
            }

            if (entity.ContainsKey("entitylumpname"))
            {
                mapEntity.EntityProperties.Remove("entitylumpname");
                warnings.Add($"{context} ({className}): removed compiled entity-lump reference; configure template membership in Hammer.");
            }
        }

        if (entities.Count == 0)
        {
            throw new InvalidDataException("The document must contain at least one entity other than worldspawn.");
        }

        var resolver = new EntityIOTargetResolver(entities);
        foreach (var entity in entities)
        {
            foreach (var connection in entity.Connections ?? [])
            {
                var outcome = resolver.Resolve(connection, []);
                if (outcome is EntityIOTargetOutcome.NotFound or EntityIOTargetOutcome.Unsupported)
                {
                    warnings.Add($"{entity.TargetName ?? entity.GetStringProperty("classname")}: target '{connection.TargetName}' needs an external or runtime entity; verify it in Hammer.");
                }
            }
        }

        using var datamodel = new Datamodel.Datamodel("vmap", 29) { Root = map };
        using var stream = new MemoryStream();
        datamodel.Save(stream, "binary", 9);
        return new EntityMapImportResult(stream.ToArray(), entities.Count, warnings.AsReadOnly());
    }
}
