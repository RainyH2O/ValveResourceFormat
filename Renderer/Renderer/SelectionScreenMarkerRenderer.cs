using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Draws fixed-size screen-space corner markers around distant selected nodes.
/// </summary>
internal static class SelectionScreenMarkerRenderer
{
    public static void Draw(ToolGeometry geometry, Camera camera, IReadOnlyList<SceneNode> selectedNodes, SelectionHighlightSettings settings)
    {
        if (!Matrix4x4.Invert(camera.ViewProjectionMatrix, out var projectionToWorld))
        {
            return;
        }

        var context = new ToolRenderContext(geometry) { LineWidth = settings.MarkerLineWidth };
        context.PushRenderMode(ToolRenderMode.WireframeNoZ);

        foreach (var node in selectedNodes)
        {
            AddMarker(context, camera, projectionToWorld, node.BoundingBox.Transform(node.Scene.ToViewerWorld), settings);
        }
    }

    private static void AddMarker(
        ToolRenderContext context,
        Camera camera,
        in Matrix4x4 projectionToWorld,
        in AABB bounds,
        SelectionHighlightSettings settings)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        var depth = 0f;
        var projectedCorners = 0;

        ReadOnlySpan<Vector3> corners =
        [
            new(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Max.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Max.Z),
        ];

        foreach (var corner in corners)
        {
            var clip = Vector4.Transform(new Vector4(corner, 1f), camera.ViewProjectionMatrix);

            if (clip.W <= 0f)
            {
                continue;
            }

            var normalized = clip / clip.W;
            if (!float.IsFinite(normalized.X)
                || !float.IsFinite(normalized.Y)
                || normalized.Z is < 0f or > 1f)
            {
                continue;
            }

            var screen = new Vector2(
                (normalized.X + 1f) * 0.5f * camera.WindowSize.X,
                (1f - normalized.Y) * 0.5f * camera.WindowSize.Y);

            min = Vector2.Min(min, screen);
            max = Vector2.Max(max, screen);
            depth += normalized.Z;
            projectedCorners++;
        }

        if (projectedCorners == 0
            || max.X < 0f || max.Y < 0f
            || min.X > camera.WindowSize.X || min.Y > camera.WindowSize.Y)
        {
            return;
        }

        var projectedSize = max - min;
        if (projectedSize.X >= settings.MarkerThreshold || projectedSize.Y >= settings.MarkerThreshold)
        {
            return;
        }

        var center = Vector2.Clamp((min + max) * 0.5f, Vector2.Zero, camera.WindowSize);
        var halfSize = settings.MarkerSize * 0.5f;
        var left = center.X - halfSize;
        var right = center.X + halfSize;
        var top = center.Y - halfSize;
        var bottom = center.Y + halfSize;
        var normalizedDepth = depth / projectedCorners;
        var color = settings.Color;

        AddScreenLine(context, camera, projectionToWorld, new(left, top), new(left + settings.MarkerCornerLength, top), normalizedDepth, color);
        AddScreenLine(context, camera, projectionToWorld, new(left, top), new(left, top + settings.MarkerCornerLength), normalizedDepth, color);
        AddScreenLine(context, camera, projectionToWorld, new(right, top), new(right - settings.MarkerCornerLength, top), normalizedDepth, color);
        AddScreenLine(context, camera, projectionToWorld, new(right, top), new(right, top + settings.MarkerCornerLength), normalizedDepth, color);
        AddScreenLine(context, camera, projectionToWorld, new(left, bottom), new(left + settings.MarkerCornerLength, bottom), normalizedDepth, color);
        AddScreenLine(context, camera, projectionToWorld, new(left, bottom), new(left, bottom - settings.MarkerCornerLength), normalizedDepth, color);
        AddScreenLine(context, camera, projectionToWorld, new(right, bottom), new(right - settings.MarkerCornerLength, bottom), normalizedDepth, color);
        AddScreenLine(context, camera, projectionToWorld, new(right, bottom), new(right, bottom - settings.MarkerCornerLength), normalizedDepth, color);
    }

    private static void AddScreenLine(
        ToolRenderContext context,
        Camera camera,
        in Matrix4x4 projectionToWorld,
        Vector2 start,
        Vector2 end,
        float depth,
        Color32 color)
    {
        context.DrawLine(
            ScreenToWorld(camera, projectionToWorld, start, depth),
            ScreenToWorld(camera, projectionToWorld, end, depth),
            color);
    }

    private static Vector3 ScreenToWorld(Camera camera, in Matrix4x4 projectionToWorld, Vector2 screen, float depth)
    {
        var normalized = new Vector4(
            screen.X / camera.WindowSize.X * 2f - 1f,
            1f - screen.Y / camera.WindowSize.Y * 2f,
            depth,
            1f);
        var world = Vector4.Transform(normalized, projectionToWorld);
        return new Vector3(world.X, world.Y, world.Z) / world.W;
    }
}
