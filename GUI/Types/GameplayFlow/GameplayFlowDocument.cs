using System.Text.Json.Serialization;

namespace GUI.Types.GameplayFlow;

internal sealed class GameplayFlowDocument
{
    public const int CurrentFormatVersion = 1;

    [JsonRequired]
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    [JsonRequired]
    [JsonPropertyName("documentId")]
    public Guid DocumentId { get; set; } = Guid.NewGuid();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("maps")]
    public List<GameplayFlowMap> Maps { get; set; } = [];

    [JsonRequired]
    [JsonPropertyName("flows")]
    public List<GameplayFlow> Flows { get; set; } = [];
}

internal sealed class GameplayFlowMap
{
    [JsonRequired]
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonRequired]
    [JsonPropertyName("resourcePath")]
    public string ResourcePath { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("contentFingerprint")]
    public string? ContentFingerprint { get; set; }
}

internal sealed class GameplayFlow
{
    [JsonRequired]
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("groups")]
    public List<GameplayFlowGroup> Groups { get; set; } = [];

    [JsonRequired]
    [JsonPropertyName("nodes")]
    public List<GameplayFlowNode> Nodes { get; set; } = [];
}

internal sealed class GameplayFlowGroup
{
    [JsonRequired]
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("parentGroupId")]
    public Guid? ParentGroupId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

internal sealed class GameplayFlowNode
{
    [JsonRequired]
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonRequired]
    [JsonPropertyName("mapId")]
    public Guid MapId { get; set; }

    [JsonPropertyName("groupId")]
    public Guid? GroupId { get; set; }

    [JsonPropertyName("childFlowId")]
    public Guid? ChildFlowId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonRequired]
    [JsonPropertyName("camera")]
    public GameplayFlowCameraSnapshot Camera { get; set; } = new();

    [JsonPropertyName("selectedEntities")]
    public List<GameplayFlowEntityLocator> SelectedEntities { get; set; } = [];

    [JsonPropertyName("annotations")]
    public List<GameplayFlowAnnotation> Annotations { get; set; } = [];

    [JsonPropertyName("visibility")]
    public List<GameplayFlowSceneVisibilitySnapshot> Visibility { get; set; } = [];

    [JsonPropertyName("nextNodeIds")]
    public List<Guid> NextNodeIds { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("modifiedAt")]
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class GameplayFlowCameraSnapshot
{
    [JsonRequired]
    [JsonPropertyName("position")]
    public GameplayFlowVector3 Position { get; set; }

    [JsonRequired]
    [JsonPropertyName("qAngle")]
    public GameplayFlowVector3 QAngle { get; set; }

    [JsonPropertyName("fieldOfView")]
    public float FieldOfView { get; set; } = 90f;
}

internal sealed class GameplayFlowEntityLocator
{
    [JsonRequired]
    [JsonPropertyName("mapId")]
    public Guid MapId { get; set; }

    [JsonPropertyName("scene")]
    public GameplayFlowScene Scene { get; set; }

    [JsonPropertyName("hammerUniqueId")]
    public string? HammerUniqueId { get; set; }

    [JsonPropertyName("classname")]
    public string Classname { get; set; } = string.Empty;

    [JsonPropertyName("targetName")]
    public string TargetName { get; set; } = string.Empty;

    [JsonPropertyName("origin")]
    public GameplayFlowVector3? Origin { get; set; }
}

internal sealed class GameplayFlowAnnotation
{
    public const string PointType = "point";

    [JsonRequired]
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonRequired]
    [JsonPropertyName("type")]
    public string Type { get; set; } = PointType;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("position")]
    public GameplayFlowVector3 Position { get; set; }

    [JsonPropertyName("isSelected")]
    public bool IsSelected { get; set; }
}

internal sealed class GameplayFlowSceneVisibilitySnapshot
{
    [JsonPropertyName("scene")]
    public GameplayFlowScene Scene { get; set; }

    [JsonPropertyName("mode")]
    public GameplayFlowVisibilityMode Mode { get; set; }

    [JsonPropertyName("entities")]
    public List<GameplayFlowEntityLocator> Entities { get; set; } = [];
}

internal readonly record struct GameplayFlowVector3(
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("z")] float Z)
{
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
}

internal enum GameplayFlowScene
{
    Main,
    Skybox,
}

internal enum GameplayFlowVisibilityMode
{
    None,
    Hidden,
    Isolated,
}

internal enum GameplayFlowIssueSeverity
{
    Notice,
    Error,
}

internal sealed record GameplayFlowIssue(
    GameplayFlowIssueSeverity Severity,
    string Message,
    Guid? FlowId = null,
    Guid? NodeId = null);
