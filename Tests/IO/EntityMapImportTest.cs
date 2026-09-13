using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;
using DMElement = Datamodel.Element;

namespace Tests.IO;

public class EntityMapImportTest
{
    [Test]
    public async Task WritesEditableEntitiesWithConnectionsAndLocalBoxGeometry()
    {
        var result = EntityMapImporter.Convert("""
            {"formatVersion":1,"entities":[
                {"properties":{"classname":"trigger_once","targetname":"start","origin":[100,200,300]},
                 "box":{"size":[32,64,128],"center":[0,0,64]},
                 "connections":[{"output":"OnStartTouch","target":"relay","input":"Trigger","delay":0.5}]},
                {"properties":{"classname":"logic_relay","targetname":"relay","origin":"(1, 2, 3)"}}
            ]}
            """);
        using var map = Load(result.Data);
        var world = (DMElement)map.Root!["world"]!;
        var entities = (Datamodel.ElementArray)world["children"]!;
        var trigger = entities[0]!;
        var connection = ((Datamodel.ElementArray)trigger["connectionsData"]!)[0]!;
        var mesh = ((Datamodel.ElementArray)trigger["children"]!)[0]!;
        var meshData = (DMElement)mesh["meshData"]!;
        var vertexData = (DMElement)meshData["vertexData"]!;
        var positionStream = ((Datamodel.ElementArray)vertexData["streams"]!).Single(stream => stream!.Name == "position:0")!;
        var positions = (Datamodel.Vector3Array)positionStream["data"]!;

        await Assert.That(result.EntityCount).IsEqualTo(2);
        await Assert.That((string)((DMElement)trigger["entity_properties"]!)["classname"]!).IsEqualTo("trigger_once");
        await Assert.That((Vector3)trigger["origin"]!).IsEqualTo(new Vector3(100, 200, 300));
        await Assert.That((Vector3)entities[1]!["origin"]!).IsEqualTo(new Vector3(1, 2, 3));
        await Assert.That((string)connection["targetName"]!).IsEqualTo("relay");
        await Assert.That((float)connection["delay"]!).IsEqualTo(0.5f);
        await Assert.That((int)connection["timesToFire"]!).IsEqualTo(-1);
        await Assert.That((int)connection["targetType"]!).IsEqualTo(2);
        await Assert.That(positions.Count).IsEqualTo(8);
        await Assert.That(positions.Aggregate(Vector3.Min)).IsEqualTo(new Vector3(-16, -32, 0));
        await Assert.That(positions.Aggregate(Vector3.Max)).IsEqualTo(new Vector3(16, 32, 128));
        await Assert.That(((Datamodel.IntArray)meshData["faceEdgeIndices"]!).Count).IsEqualTo(6);
        await Assert.That(((Datamodel.IntArray)meshData["edgeFaceIndices"]!).All(face => face >= 0)).IsTrue();
        await Assert.That((int)trigger["nodeID"]! != (int)mesh["nodeID"]!).IsTrue();
    }

    [Test]
    public async Task AcceptsLegacyExportsAndReplacesCompiledBrushModels()
    {
        var result = EntityMapImporter.Convert("""
            [{"classname":"worldspawn","skyname":"test"},
             {"classname":"func_door","hammeruniqueid":"10:20","model":"maps/old/entities/door.vmdl",
              "connections":[{"m_outputName":"OnOpen","m_targetName":"!activator","m_inputName":"Kill",
                              "m_overrideParam":"","m_flDelay":0,"m_nTimesToFire":1,"m_targetType":4}]},
             {"classname":"prop_dynamic","model":"models/props/crate.vmdl","origin":[1,2,3]}]
            """);
        using var map = Load(result.Data);
        var entities = ((Datamodel.ElementArray)((DMElement)map.Root!["world"]!)["children"]!);
        var doorProperties = (DMElement)entities[0]!["entity_properties"]!;
        var propProperties = (DMElement)entities[1]!["entity_properties"]!;

        await Assert.That(result.EntityCount).IsEqualTo(2);
        await Assert.That(doorProperties.ContainsKey("model")).IsFalse();
        await Assert.That(doorProperties.ContainsKey("hammeruniqueid")).IsFalse();
        await Assert.That(((Datamodel.ElementArray)entities[0]!["children"]!).Count).IsEqualTo(1);
        await Assert.That((string)propProperties["model"]!).IsEqualTo("models/props/crate.vmdl");
        await Assert.That(result.Warnings.Any(warning => warning.Contains("placeholder", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task KeepsParameterMapsDuplicateNamesAndCyclicConnections()
    {
        var result = EntityMapImporter.Convert("""
            [{"classname":"logic_relay","targetname":"relay","connections":[
                {"output":"OnTrigger","target":"relay","input":"Trigger","paramMap":{"enabled":{"value":true}}},
                {"output":"OnTrigger","target":"external","input":"Enable"}]},
             {"classname":"logic_relay","targetname":"relay"}]
            """);
        using var map = Load(result.Data);
        var connection = map.AllElements.First(element => element.ClassName == "DmeConnectionData");
        var parameterMap = (DMElement)connection["paramMap"]!;
        await Assert.That((bool)((DMElement)parameterMap["enabled"]!)["value"]!).IsTrue();
        await Assert.That(result.Warnings.Any(warning => warning.Contains("external", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.EntityCount).IsEqualTo(2);
    }

    [Test]
    public async Task ExplicitBoxesKeepModelBackedEntityResources()
    {
        var result = EntityMapImporter.Convert("""
            [{"classname":"prop_dynamic","model":"models/props/crate.vmdl","box":{"size":[16,16,16]}}]
            """);
        using var map = Load(result.Data);
        var prop = map.AllElements.Single(element => element.ClassName == "CMapEntity");
        await Assert.That((string)((DMElement)prop["entity_properties"]!)["model"]!).IsEqualTo("models/props/crate.vmdl");
        var mesh = (DMElement)((Datamodel.ElementArray)prop["children"]!)[0]!["meshData"]!;
        await Assert.That(((Datamodel.StringArray)mesh["materials"]!)[0]).IsEqualTo("materials/dev/graygrid.vmat");
    }

    [Test]
    [Arguments("{\"formatVersion\":2,\"entities\":[]}")]
    [Arguments("{\"formatVersion\":\"1\",\"entities\":[]}")]
    [Arguments("[]")]
    [Arguments("[{\"targetname\":\"missing class\"}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"targetname\":123}]")]
    [Arguments("[{\"classname\":\"prop_dynamic\",\"model\":123}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"Connections\":[]}]")]
    [Arguments("[{\"properties\":{\"classname\":\"logic_relay\",\"connections\":[]}}]")]
    [Arguments("[{\"properties\":{\"classname\":\"func_door\",\"box\":{\"size\":[16,16,16]}}}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"connections\":[{\"output\":\"OnTrigger\",\"target\":\"!caller\",\"input\":\"Trigger\",\"paramMap\":{\"pValue\":{\"value\":2147483648}}}]}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"connections\":[{\"output\":\"OnTrigger\",\"target\":\"!caller\",\"input\":\"Trigger\",\"paramMap\":{\"pValue\":{\"value\":1e100}}}]}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"origin\":[1,2]}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"origin\":[1e99,2,3]}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"origin\":\"NaN 0 0\"}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"classname\":\"logic_timer\"}]")]
    [Arguments("[{\"classname\":\"func_door\",\"box\":{\"size\":[0,64,64]}}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"connections\":[{\"output\":\"OnTrigger\",\"target\":\"x\"}]}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"connections\":[{\"output\":\"OnTrigger\",\"target\":\"x\",\"input\":\"Trigger\",\"delay\":-1}]}]")]
    [Arguments("[{\"classname\":\"logic_relay\",\"connections\":[{\"output\":\"OnTrigger\",\"m_outputName\":\"OnSpawn\",\"target\":\"x\",\"input\":\"Trigger\"}]}]")]
    public async Task RejectsMalformedModulesBeforeWriting(string json)
    {
        var exception = Assert.Throws<InvalidDataException>(() => EntityMapImporter.Convert(json));
        await Assert.That(exception).IsNotNull();
    }

    private static Datamodel.Datamodel Load(byte[] data)
        => Datamodel.Datamodel.Load(new MemoryStream(data), Datamodel.Codecs.DeferredMode.Disabled);
}
