using System.Text.Json;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests;

public class EntityExportTest
{
    private sealed class TestLump(KVObject data) : EntityLump
    {
        public void Initialize() => Data = data;
    }

    [Test]
    public async Task LegacyExportKeepsConnectionsAndTargetTypes()
    {
        var properties = KVObject.Collection();
        properties.Add("classname", "logic_relay");
        properties.Add("hammeruniqueid", "duplicate");
        properties.Add("nullable", KVObject.Null());
        var entityData = KVObject.Collection();
        entityData.Add("version", 1L);
        entityData.Add("values", properties);
        entityData.Add("attributes", KVObject.Collection());
        var connection = KVObject.Collection();
        connection.Add("m_outputName", "OnTrigger");
        connection.Add("m_targetName", "target");
        connection.Add("m_inputName", "Enable");
        connection.Add("m_overrideParam", "");
        connection.Add("m_flDelay", 0.0f);
        connection.Add("m_nTimesToFire", -1L);
        connection.Add("m_targetType", 2L);
        var record = KVObject.Collection();
        record.Add("keyValues3Data", entityData);
        record.Add("m_connections", KVObject.Array([connection]));
        var data = KVObject.Collection();
        data.Add("m_entityKeyValues", KVObject.Array([record]));
        var lump = new TestLump(data) { Resource = new Resource() };
        lump.Initialize();

        using var document = JsonDocument.Parse(KVJsonSerializer.SerializeEntities(lump.GetEntities()));
        var entity = document.RootElement[0];
        await Assert.That(entity.GetProperty("connections").GetArrayLength()).IsEqualTo(1);
        await Assert.That(entity.GetProperty("connections")[0].GetProperty("m_targetType").GetInt32()).IsEqualTo(2);
        await Assert.That(entity.TryGetProperty("nullable", out _)).IsTrue();
    }

    [Test]
    public async Task SelectedPropertiesKeepConnectionsWithoutNullValues()
    {
        var entity = new EntityLump.Entity { ParentLump = new EntityLump { Resource = new Resource() } };
        entity.Add("classname", "logic_relay");
        entity.Add("nullable", KVObject.Null());
        entity.Connections =
        [
            new EntityLump.Connection
            {
                SourceEntity = entity,
                OutputName = "OnTrigger",
                TargetName = "target",
                InputName = "Enable",
                OverrideParam = "",
                Delay = 0,
                TimesToFire = -1,
                TargetType = EntityIOTargetType.EntityName,
            },
        ];

        using var document = JsonDocument.Parse(KVJsonSerializer.Serialize(EntityExportSerializer.ToLegacyEntity(entity, new HashSet<string> { "nullable" })));
        await Assert.That(document.RootElement.TryGetProperty("nullable", out _)).IsFalse();
        await Assert.That(document.RootElement.GetProperty("connections")[0].GetProperty("m_targetType").GetInt32()).IsEqualTo(2);
    }

    [Test]
    public async Task NativeValuesKeepTheirJsonTypes()
    {
        var values = KVObject.Collection();
        values.Add("signed", long.MinValue);
        values.Add("unsigned", ulong.MaxValue);
        values.Add("boolean", true);
        values.Add("floating", 1.25f);
        values.Add("binary", KVObject.Blob([0, 1, 255]));
        values.Add("packed", KVObject.PackedArray(new long[] { 1, 2, 3 }));

        using var document = JsonDocument.Parse(KVJsonSerializer.Serialize(values));
        var root = document.RootElement;
        await Assert.That(root.GetProperty("signed").GetInt64()).IsEqualTo(long.MinValue);
        await Assert.That(root.GetProperty("unsigned").GetUInt64()).IsEqualTo(ulong.MaxValue);
        await Assert.That(root.GetProperty("boolean").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("floating").GetSingle()).IsEqualTo(1.25f);
        await Assert.That(root.GetProperty("binary").GetString()).IsEqualTo("AAH/");
        await Assert.That(root.GetProperty("packed").GetArrayLength()).IsEqualTo(3);
        await Assert.That(root.GetProperty("packed")[2].GetInt64()).IsEqualTo(3L);
        await Assert.That(values["packed"].IsPackedArray).IsTrue();

        await Assert.That(KVJsonSerializer.Serialize(new KVObject(42L))).IsEqualTo("42");
        await Assert.That(KVJsonSerializer.Serialize(KVObject.Null())).IsEqualTo("null");
        await Assert.That(KVJsonSerializer.Serialize(KVObject.Blob([0, 1, 255]))).IsEqualTo("\"AAH/\"");
    }
}
