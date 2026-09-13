using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GUI.Types.GameplayFlow;

internal static class GameplayFlowJsonSerializer
{
    public static GameplayFlowLoadResult Deserialize(string json)
    {
        GameplayFlowDocument document;
        try
        {
            var root = UpgradeToCurrentVersion(json);
            document = root.Deserialize(GameplayFlowJsonContext.Default.GameplayFlowDocument)
                ?? throw new GameplayFlowDocumentException("The JSON document is empty.");
        }
        catch (JsonException exception)
        {
            throw new GameplayFlowDocumentException("The JSON document is invalid.", exception);
        }

        EnsureCollections(document);
        var issues = GameplayFlowSession.Validate(document);
        var errors = issues.Where(static issue => issue.Severity == GameplayFlowIssueSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new GameplayFlowDocumentException(string.Join(Environment.NewLine, errors.Select(static issue => issue.Message)));
        }

        AddUnsupportedAnnotationIssues(document, issues);
        return new(document, issues);
    }

    private static JsonObject UpgradeToCurrentVersion(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root)
        {
            throw new GameplayFlowDocumentException("The JSON document must contain an object.");
        }

        if (!root.TryGetPropertyValue("formatVersion", out var versionNode)
            || versionNode is not JsonValue versionValue
            || !versionValue.TryGetValue<int>(out var version))
        {
            throw new GameplayFlowDocumentException("The document has no valid format version.");
        }

        if (version != GameplayFlowDocument.CurrentFormatVersion)
        {
            throw new GameplayFlowDocumentException($"Unsupported format version {version}.");
        }

        return root;
    }

    public static string Serialize(GameplayFlowDocument document)
    {
        var errors = GameplayFlowSession.Validate(document)
            .Where(static issue => issue.Severity == GameplayFlowIssueSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            throw new GameplayFlowDocumentException(string.Join(Environment.NewLine, errors.Select(static issue => issue.Message)));
        }

        return JsonSerializer.Serialize(document, GameplayFlowJsonContext.Default.GameplayFlowDocument);
    }

    public static GameplayFlowLoadResult Load(string path)
        => Deserialize(File.ReadAllText(path, Encoding.UTF8));

    public static void Save(string path, GameplayFlowDocument document)
    {
        var json = Serialize(document);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new GameplayFlowDocumentException("The destination directory is invalid.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureCollections(GameplayFlowDocument document)
    {
        if (document.Maps == null
            || document.Flows == null
            || document.Maps.Any(static map => map == null)
            || document.Flows.Any(static flow => flow == null))
        {
            throw new GameplayFlowDocumentException("The document is missing its map or flow collection.");
        }

        if (document.Maps.Any(static map => map.ResourcePath == null || map.DisplayName == null))
        {
            throw new GameplayFlowDocumentException("A map is missing required text data.");
        }

        foreach (var flow in document.Flows)
        {
            if (flow.Title == null
                || flow.Description == null
                || flow.Groups == null
                || flow.Nodes == null
                || flow.Groups.Any(static group => group == null)
                || flow.Nodes.Any(static node => node == null))
            {
                throw new GameplayFlowDocumentException("A flow is missing its group or node collection.");
            }

            if (flow.Groups.Any(static group => group.Title == null || group.Description == null))
            {
                throw new GameplayFlowDocumentException("A group is missing required text data.");
            }

            foreach (var node in flow.Nodes)
            {
                if (node.Title == null
                    || node.Description == null
                    || node.Camera == null
                    || node.Tags == null
                    || node.SelectedEntities == null
                    || node.Annotations == null
                    || node.Visibility == null
                    || node.NextNodeIds == null
                    || node.Tags.Any(static tag => tag == null)
                    || node.SelectedEntities.Any(static locator => locator == null)
                    || node.Annotations.Any(static annotation => annotation == null)
                    || node.Visibility.Any(static state => state == null)
                    || node.Visibility.Any(static state => state.Entities == null || state.Entities.Any(locator => locator == null)))
                {
                    throw new GameplayFlowDocumentException("A node is missing required snapshot data.");
                }

                if (node.SelectedEntities.Any(static locator => locator.Classname == null || locator.TargetName == null)
                    || node.Visibility.SelectMany(static state => state.Entities).Any(static locator => locator.Classname == null || locator.TargetName == null)
                    || node.Annotations.Any(static annotation => annotation.Type == null || annotation.Name == null || annotation.Description == null))
                {
                    throw new GameplayFlowDocumentException("A node snapshot is missing required text data.");
                }
            }
        }
    }

    private static void AddUnsupportedAnnotationIssues(GameplayFlowDocument document, List<GameplayFlowIssue> issues)
    {
        foreach (var (flow, node) in document.Flows.SelectMany(flow => flow.Nodes.Select(node => (flow, node))))
        {
            foreach (var annotation in node.Annotations)
            {
                if (!string.Equals(annotation.Type, GameplayFlowAnnotation.PointType, StringComparison.Ordinal))
                {
                    issues.Add(new(
                        GameplayFlowIssueSeverity.Notice,
                        $"Annotation type '{annotation.Type}' is not available in this version.",
                        flow.Id,
                        node.Id));
                }
            }
        }
    }
}

internal sealed record GameplayFlowLoadResult(
    GameplayFlowDocument Document,
    IReadOnlyList<GameplayFlowIssue> Issues);

internal sealed class GameplayFlowDocumentException : Exception
{
    public GameplayFlowDocumentException()
    {
    }

    public GameplayFlowDocumentException(string message)
        : base(message)
    {
    }

    public GameplayFlowDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
