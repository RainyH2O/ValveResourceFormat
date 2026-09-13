namespace ValveResourceFormat.Renderer;

/// <summary>
/// Session-only visual settings for selected scene nodes.
/// </summary>
public sealed record SelectionHighlightSettings
{
    /// <summary>Gets the standard selection-highlight settings.</summary>
    public static SelectionHighlightSettings Default { get; } = new();

    /// <summary>Gets the outline kernel width in pixels.</summary>
    public float OutlineWidth { get; init; } = 2.5f;

    /// <summary>Gets the softness of the outline falloff.</summary>
    public float OutlineSoftness { get; init; } = 0.8f;

    /// <summary>Gets the outline brightness multiplier.</summary>
    public float OutlineIntensity { get; init; } = 2.25f;

    /// <summary>Gets the opacity of the selected-object fill.</summary>
    public float FillAlpha { get; init; }

    /// <summary>Gets the shared outline, fill, and marker color.</summary>
    public Color32 Color { get; init; } = new(1f, 1f, 0.2f, 1f);

    /// <summary>Gets whether selected-node dimensions are displayed.</summary>
    public bool ShowDimensions { get; init; } = true;

    /// <summary>Gets whether fixed-size markers are displayed for small projected bounds.</summary>
    public bool ShowDistantMarkers { get; init; }

    /// <summary>Gets the projected-size threshold for displaying a distant marker.</summary>
    public float MarkerThreshold { get; init; } = 48f;

    /// <summary>Gets the marker frame size in pixels.</summary>
    public float MarkerSize { get; init; } = 52f;

    /// <summary>Gets the length of each marker corner segment in pixels.</summary>
    public float MarkerCornerLength { get; init; } = 14f;

    /// <summary>Gets the marker line width in pixels.</summary>
    public int MarkerLineWidth { get; init; } = 3;
}
