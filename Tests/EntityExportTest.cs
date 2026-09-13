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
}
