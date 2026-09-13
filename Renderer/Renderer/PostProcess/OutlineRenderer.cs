using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.PostProcess;

/// <summary>
/// Fullscreen pass that draws an outline by running edge detection over the outline coverage mask.
/// </summary>
public class OutlineRenderer(RendererContext rendererContext)
{
    private Shader? outlineEdge;

    /// <summary>Gets or sets the visual settings used for selection outlines.</summary>
    public SelectionHighlightSettings Settings { get; set; } = SelectionHighlightSettings.Default;

    /// <summary>Loads the outline edge detection shader.</summary>
    public void Load()
    {
        outlineEdge = rendererContext.ShaderLoader.LoadShader("outline_post");
    }

    /// <summary>
    /// Execute the outline post-pass. Caller must ensure the destination framebuffer is bound.
    /// </summary>
    public void Render(RenderTexture outlineMask, int numSamples, bool flipY)
    {
        Debug.Assert(outlineEdge != null);

        outlineEdge.Use();

        outlineEdge.SetUniform("g_bFlipY", flipY);
        outlineEdge.SetUniform("g_nNumSamplesMSAA", numSamples);

        var settings = Settings;
        outlineEdge.SetUniform("g_flOutlineSize", settings.OutlineWidth / 2.5f);
        outlineEdge.SetUniform("g_flOutlineSoftness", settings.OutlineSoftness);
        outlineEdge.SetUniform("g_flOutlineIntensity", settings.OutlineIntensity);
        outlineEdge.SetUniform("g_flOutlineFillAlpha", settings.FillAlpha);
        outlineEdge.SetUniform("g_vOutlineColor", new Vector3(settings.Color.R, settings.Color.G, settings.Color.B) / 255f);

        outlineEdge.SetTexture(0, "g_tOutlineMask", outlineMask);

        using var _ = GraphicsContext.RenderState.Scope(blend: true);

        GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }
}
