using System.Text.Json.Serialization;

namespace GUI.Types.GameplayFlow;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GameplayFlowDocument))]
internal partial class GameplayFlowJsonContext : JsonSerializerContext;
