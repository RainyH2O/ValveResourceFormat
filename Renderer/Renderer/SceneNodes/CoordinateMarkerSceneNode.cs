namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>
/// Renders a session-only coordinate marker with a solid pickable center and a minimum on-screen size.
/// </summary>
public sealed class CoordinateMarkerSceneNode : ShapeSceneNode
{
    private const float BaseRadius = 16f;
    private const float DiamondRadius = 6f;
    private const float MinimumPixelRadius = 12f;

    private readonly LineSceneNode axes;
    private readonly Vector3 position;

    /// <inheritdoc/>
    public override bool IsTranslucent => false;

    /// <inheritdoc/>
    protected override bool Shaded => false;

    /// <summary>
    /// Initializes a coordinate marker at the specified world position.
    /// </summary>
    public CoordinateMarkerSceneNode(Scene scene, Vector3 position)
        : base(scene, CreateDiamondVertices(), CreateDiamondIndices())
    {
        this.position = position;
        axes = new LineSceneNode(scene, CreateAxisVertices());
        LocalBoundingBox = new AABB(-Vector3.One * BaseRadius, Vector3.One * BaseRadius);
        SetTransform(Matrix4x4.CreateTranslation(position));
    }

    /// <inheritdoc/>
    public override void Update(Scene.UpdateContext context)
    {
        var distance = Vector3.Distance(position, context.Camera.Location);
        var pixelsPerWorldUnit = distance > 0f
            ? context.Camera.WindowSize.Y * context.Camera.ProjectionMatrix.M22 / distance
            : float.MaxValue;
        var scale = pixelsPerWorldUnit > 0f
            ? MathF.Max(1f, MinimumPixelRadius / (BaseRadius * pixelsPerWorldUnit))
            : 1f;

        SetTransform(Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position));
    }

    /// <inheritdoc/>
    public override void Render(Scene.RenderContext context)
    {
        base.Render(context);
        axes.Id = Id;
        axes.Render(context);
    }

    /// <inheritdoc/>
    public override void Delete()
    {
        axes.Delete();
        base.Delete();
    }

    private void SetTransform(Matrix4x4 transform)
    {
        Transform = transform;
        axes.Transform = transform;
    }

    private static List<SimpleVertexNormal> CreateDiamondVertices()
    {
        var color = new Color32(0f, 1f, 1f, 1f);
        return
        [
            new(Vector3.UnitX * DiamondRadius, color, Vector3.UnitX),
            new(Vector3.UnitY * DiamondRadius, color, Vector3.UnitY),
            new(-Vector3.UnitX * DiamondRadius, color, -Vector3.UnitX),
            new(-Vector3.UnitY * DiamondRadius, color, -Vector3.UnitY),
            new(Vector3.UnitZ * DiamondRadius, color, Vector3.UnitZ),
            new(-Vector3.UnitZ * DiamondRadius, color, -Vector3.UnitZ),
        ];
    }

    private static List<int> CreateDiamondIndices()
        =>
        [
            4, 0, 1,
            4, 1, 2,
            4, 2, 3,
            4, 3, 0,
            5, 1, 0,
            5, 2, 1,
            5, 3, 2,
            5, 0, 3,
        ];

    private static SimpleVertex[] CreateAxisVertices()
    {
        var vertices = new List<SimpleVertex>();
        var color = new Color32(0f, 1f, 1f, 1f);
        ShapeSceneNode.AddLine(vertices, -Vector3.UnitX * BaseRadius, Vector3.UnitX * BaseRadius, color);
        ShapeSceneNode.AddLine(vertices, -Vector3.UnitY * BaseRadius, Vector3.UnitY * BaseRadius, color);
        ShapeSceneNode.AddLine(vertices, -Vector3.UnitZ * BaseRadius, Vector3.UnitZ * BaseRadius, color);
        return [.. vertices];
    }
}
