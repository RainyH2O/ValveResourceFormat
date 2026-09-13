namespace ValveResourceFormat.IO;

/// <summary>The editable map and notices produced by entity JSON conversion.</summary>
public sealed class EntityMapImportResult
{
    /// <summary>Gets the serialized VMAP source file.</summary>
    public byte[] Data { get; }

    /// <summary>Gets the number of placed entities, excluding worldspawn.</summary>
    public int EntityCount { get; }

    /// <summary>Gets notices about placeholders and dependencies requiring review in Hammer.</summary>
    public IReadOnlyList<string> Warnings { get; }

    internal EntityMapImportResult(byte[] data, int entityCount, IReadOnlyList<string> warnings)
    {
        Data = data;
        EntityCount = entityCount;
        Warnings = warnings;
    }
}
