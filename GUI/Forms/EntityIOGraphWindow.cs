using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Types.Graphs;
using GUI.Utils;

namespace GUI.Forms;

internal sealed class EntityIOGraphWindow : IDisposable
{
    private readonly GLWorldViewer worldViewer;
    private readonly WindowedEntityIOGraphViewer graphViewer;
    private readonly ThemedForm form;
    private bool disposed;

    public event EventHandler? Closed;

#pragma warning disable CA2000 // The fields own the form, viewer, and renderer context after successful construction
    public EntityIOGraphWindow(VrfGuiContext vrfGuiContext, GLWorldViewer worldViewer)
    {
        this.worldViewer = worldViewer;

        var rendererContext = vrfGuiContext.CreateRendererContext();
        WindowedEntityIOGraphViewer? viewer = null;
        ThemedForm? createdForm = null;

        try
        {
            viewer = new WindowedEntityIOGraphViewer(vrfGuiContext, rendererContext, worldViewer.LoadedWorld!.Entities,
                worldViewer.SelectAndFocusEntities, worldViewer.SelectEntitiesFromGraph);
            viewer.InitializeLoad();

            createdForm = new ThemedForm
            {
                Text = $"{Path.GetFileName(vrfGuiContext.FileName)} - ENTITY I/O GRAPH",
                ClientSize = new Size(1000, 700),
                MinimumSize = new Size(640, 480),
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
            };
            createdForm.Controls.Add(viewer.InitializeUiControls(isPreview: false));
            createdForm.FormClosing += OnFormClosing;

            graphViewer = viewer;
            form = createdForm;
            worldViewer.SelectEntitiesInGraph = graphViewer.SelectEntities;
        }
        catch
        {
            createdForm?.Dispose();
            viewer?.Dispose();

            if (viewer == null)
            {
                rendererContext.Dispose();
            }

            throw;
        }
    }
#pragma warning restore CA2000

    public void Show()
    {
        form.Show(Program.MainForm);
        graphViewer.InitializeRenderLoop(renderImmediately: true);
    }

    public void Activate() => form.Activate();

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        form.FormClosing -= OnFormClosing;
        DisposeViewer();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeViewer()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        worldViewer.SelectEntitiesInGraph = null;
        graphViewer.Dispose();
    }

    public void Dispose()
    {
        if (disposed)
        {
            form.Dispose();
            return;
        }

        form.FormClosing -= OnFormClosing;
        DisposeViewer();
        form.Close();
        form.Dispose();
        GC.SuppressFinalize(this);
    }
}
