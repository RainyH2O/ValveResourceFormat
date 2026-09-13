using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Graphs;

internal sealed class WindowedEntityIOGraphViewer : EntityIOGraphViewer
{
    private readonly CancellationTokenSource renderCancellation = new();
    private readonly Action<IReadOnlyList<EntityLump.Entity>> selectInMap;
    private Thread? renderThread;
    private bool synchronizingSelection;

    public WindowedEntityIOGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext,
        List<EntityLump.Entity> entities, Action<IReadOnlyList<EntityLump.Entity>> showInMap,
        Action<IReadOnlyList<EntityLump.Entity>> selectInMap)
        : base(vrfGuiContext, rendererContext, entities, showInMap)
    {
        this.selectInMap = selectInMap;
        View.MultipleSelectionEnabled = true;
        View.SelectionChanged += OnSelectionChanged;
    }

    protected override void OnKeyDown(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.A))
        {
            View.SelectAllVisibleNodes();
            return;
        }

        base.OnKeyDown(keyData);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (synchronizingSelection)
        {
            return;
        }

        var entities = View.Selection.SelectedNodes
            .SelectMany(node => NodeMembers.TryGetValue(node, out var members)
                ? members
                : node.Tag is EntityLump.Entity entity ? [entity] : [])
            .Distinct()
            .ToList();

        selectInMap(entities);
    }

    public void SelectEntities(IReadOnlyCollection<EntityLump.Entity> entities)
    {
        var selectedEntities = entities.ToHashSet();
        var selectedNodes = View.Nodes.Where(node => !node.Hidden &&
            (NodeMembers.TryGetValue(node, out var members)
                ? members.Any(selectedEntities.Contains)
                : node.Tag is EntityLump.Entity entity && selectedEntities.Contains(entity)));

        synchronizingSelection = true;
        try
        {
            View.SynchronizeSelectedNodes(selectedNodes);
        }
        finally
        {
            synchronizingSelection = false;
        }
    }

    protected override bool UsesSharedRenderLoop => false;

    protected override void InitializePrivateRenderLoop(bool renderImmediately)
    {
        renderThread = new Thread(RenderLoop)
        {
            Name = nameof(WindowedEntityIOGraphViewer),
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        renderThread.Start();
    }

    protected override void DisposePrivateRenderLoop()
    {
        renderCancellation.Cancel();

        if (renderThread != null && renderThread != Thread.CurrentThread)
        {
            renderThread.Join();
        }

        renderThread = null;
    }

    private void RenderLoop()
    {
        while (!renderCancellation.IsCancellationRequested)
        {
            if (GLControl is { IsDisposed: false, Visible: true } && Form.ActiveForm != null)
            {
                Draw(isPaused: false);
                continue;
            }

            renderCancellation.Token.WaitHandle.WaitOne(50);
        }
    }

    public override void Dispose()
    {
        View.SelectionChanged -= OnSelectionChanged;
        base.Dispose();
        renderCancellation.Dispose();
    }
}
