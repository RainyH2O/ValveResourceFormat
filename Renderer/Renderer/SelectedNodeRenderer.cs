using System.Linq;
using System.Threading;
using ValveResourceFormat.Renderer.Editor;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Selection;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer
{
    /// <summary>Manages ordinary and pinned highlights using the editor selection helpers.</summary>
    public sealed class SelectedNodeRenderer
    {
        private readonly RendererContext RendererContext;
        private readonly SelectionSet Selection;
        private SceneNode[] PinnedNodeSnapshot = [];
        private bool ForceDisableDepth;
        private LightingBindingDisplay LightingBindings;
        private readonly HashSet<Scene> ProbeScenes = [];
        private readonly HammerEntityVisuals EntityVisuals;
        private readonly ToolsVisibility Tools;
        private HighlightVisuals? Visuals;
        private SelectionScreenMarkerRenderer? ScreenMarkerRenderer;
        private SceneNode[] FrameNodes = [];
        private HashSet<SceneNode> FramePinnedNodes = [];
        private bool FrameIgnoreDepth;
        private SelectionHighlightSettings FrameSettings = SelectionHighlightSettings.Default;

        private sealed class HighlightVisuals(SelectedNodeRenderer owner, RendererContext context, SelectionSet selection, HammerEntityVisuals entityVisuals, ToolsVisibility tools)
            : SelectionVisuals(selection, entityVisuals, tools, context)
        {
            protected override IReadOnlyList<SceneNode> HighlightedNodes => owner.FrameNodes;
            protected override bool ShowDimensions => owner.FrameSettings.ShowDimensions;
            protected override bool IgnoreDepth => owner.FrameIgnoreDepth;
            protected override SelectionHighlightSettings? OutlineSettings => owner.FrameSettings;

            protected override void AddNodeLabel(Camera camera, TextRenderer textRenderer, SceneNode node, string nodeName, AABB bounds)
            {
                base.AddNodeLabel(camera, textRenderer, node, nodeName, bounds);

                if (!owner.FramePinnedNodes.Contains(node)
                    || node.EntityData?.TargetName is not { } targetName
                    || string.IsNullOrWhiteSpace(targetName))
                {
                    return;
                }

                var position = bounds.Center;
                position.Z = bounds.Max.Z;
                var gap = NodeLabelScale * 1.32f + 8f;

                textRenderer.AddTextBillboard(position, new TextRenderer.TextRenderRequest
                {
                    Scale = NodeLabelScale,
                    Text = targetName,
                    CenterHorizontal = true,
                    TextOffset = new(0, NodeNameOffset.Y - gap),
                }, camera);
            }
        }

        /// <summary>Initializes the highlights for a shared editor selection.</summary>
        /// <param name="rendererContext">The context used to create drawing resources when needed.</param>
        /// <param name="selection">The ordinary selection shared with the viewer.</param>
        /// <param name="entityVisuals">The editor entities whose helpers draw selected shapes.</param>
        /// <param name="tools">The tools visibility shared with the viewer.</param>
        public SelectedNodeRenderer(RendererContext rendererContext, SelectionSet selection, HammerEntityVisuals entityVisuals, ToolsVisibility tools)
        {
            RendererContext = rendererContext;
            Selection = selection;
            EntityVisuals = entityVisuals;
            Tools = tools;
            Selection.Changed += OnSelectionChanged;
        }

        /// <summary>Gets whether any ordinary or pinned highlight needs drawing.</summary>
        public bool HasSelectedNodes => !Selection.IsEmpty || Volatile.Read(ref PinnedNodeSnapshot).Length > 0;

        /// <summary>Gets the primary selected node, or null when nothing is selected.</summary>
        public SceneNode? SelectedNode => Selection.Nodes is [var node, ..] ? node : null;

        /// <summary>Gets an immutable snapshot of the ordinary selection.</summary>
        public IReadOnlyList<SceneNode> SelectedNodes => Selection.Nodes;

        /// <summary>Gets an immutable snapshot of the independently pinned nodes.</summary>
        public IReadOnlyList<SceneNode> PinnedNodes => Volatile.Read(ref PinnedNodeSnapshot);

        /// <summary>Gets the selection shapes drawn by the viewer's tools pass.</summary>
        public SelectionVisuals SelectionVisuals => Visuals ??= new(this, RendererContext, Selection, EntityVisuals, Tools);

        /// <summary>Gets or sets the session highlight style.</summary>
        public SelectionHighlightSettings HighlightSettings { get; set; } = SelectionHighlightSettings.Default;

        /// <summary>Occurs when the ordinary selection changes.</summary>
        public event EventHandler? SelectionChanged;

        /// <summary>Replaces ordinary selection without changing pinned highlights.</summary>
        /// <param name="nodes">The nodes in primary-selection order.</param>
        /// <param name="forceDisableDepth">Whether their helpers remain visible through the scene.</param>
        public void SetSelectedNodes(IEnumerable<SceneNode> nodes, bool forceDisableDepth = false)
        {
            using var synchronization = Selection.Synchronization.EnterScope();
            Selection.Replace(nodes);
            ForceDisableDepth = forceDisableDepth && !Selection.IsEmpty;
        }

        /// <summary>Replaces ordinary selection with one node, or clears it.</summary>
        /// <param name="node">The node to select, or null to clear selection.</param>
        /// <param name="forceDisableDepth">Whether its helpers remain visible through the scene.</param>
        public void SelectNode(SceneNode? node, bool forceDisableDepth = false)
        {
            SetSelectedNodes(node == null ? [] : [node], forceDisableDepth);
        }

        private void OnSelectionChanged(object? sender, EventArgs e)
        {
            using var synchronization = Selection.Synchronization.EnterScope();
            ForceDisableDepth = false;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Adds nodes to the independent pinned-highlight collection as one batch.</summary>
        /// <param name="nodes">The nodes to pin.</param>
        public void AddPinnedNodes(IEnumerable<SceneNode> nodes)
        {
            using var synchronization = Selection.Synchronization.EnterScope();
            var replacement = PinnedNodeSnapshot.Concat(nodes).Distinct<SceneNode>(ReferenceEqualityComparer.Instance).ToArray();
            Volatile.Write(ref PinnedNodeSnapshot, replacement);
        }

        /// <summary>Removes nodes from pinned highlights without clearing ordinary selection.</summary>
        /// <param name="nodes">The nodes to unpin.</param>
        public void RemovePinnedNodes(IEnumerable<SceneNode> nodes)
        {
            using var synchronization = Selection.Synchronization.EnterScope();
            var removed = new HashSet<SceneNode>(nodes, ReferenceEqualityComparer.Instance);
            Volatile.Write(ref PinnedNodeSnapshot, PinnedNodeSnapshot.Where(node => !removed.Contains(node)).ToArray());
        }

        /// <summary>Clears pinned highlights while keeping ordinary selection.</summary>
        public void ClearPinnedNodes()
        {
            using var synchronization = Selection.Synchronization.EnterScope();
            Volatile.Write(ref PinnedNodeSnapshot, []);
        }

        private SceneNode[] GetHighlightedNodes()
        {
            return Selection.Nodes.Concat(PinnedNodeSnapshot).Distinct<SceneNode>(ReferenceEqualityComparer.Instance).ToArray();
        }

        /// <summary>Updates the highlighted probe input before the scene updates its debug grids.</summary>
        public void UpdateLightingHighlights()
        {
            using var synchronization = Selection.Synchronization.EnterScope();
            var probes = LightingBindings == LightingBindingDisplay.LightProbe
                ? GetHighlightedNodes().Select(static node => node.LightProbeBinding ?? node as SceneLightProbe).OfType<SceneLightProbe>().ToArray()
                : [];

            foreach (var scene in ProbeScenes)
            {
                scene.LightingDebug.HighlightedLightProbes = new HashSet<SceneLightProbe>();
            }

            ProbeScenes.Clear();
            foreach (var group in probes.GroupBy(static probe => probe.Scene))
            {
                group.Key.LightingDebug.HighlightedLightProbes = group.ToHashSet();
                ProbeScenes.Add(group.Key);
            }
        }

        /// <summary>Rebuilds the editor helpers, labels, and optional screen markers.</summary>
        /// <param name="renderContext">The context providing the main camera.</param>
        /// <param name="updateContext">The context receiving text labels.</param>
        public void Update(Scene.RenderContext renderContext, Scene.UpdateContext updateContext)
        {
            using (Selection.Synchronization.EnterScope())
            {
                FrameNodes = GetHighlightedNodes();
                FramePinnedNodes = PinnedNodeSnapshot.ToHashSet();
                FrameIgnoreDepth = ForceDisableDepth || FrameNodes.Length > 1;
                FrameSettings = HighlightSettings;
            }

            SelectionVisuals.LightingBindings = LightingBindings;
            SelectionVisuals.Update(renderContext, updateContext);

            if (FrameSettings.ShowDistantMarkers)
            {
                ScreenMarkerRenderer ??= new(RendererContext);
                ScreenMarkerRenderer.Update(renderContext.Camera, FrameNodes, FrameSettings);
            }
            else
            {
                ScreenMarkerRenderer?.Update(renderContext.Camera, [], FrameSettings);
            }
        }

        /// <summary>Draws distant screen markers over the final image.</summary>
        public void RenderScreenMarkers()
        {
            ScreenMarkerRenderer?.Render();
        }

        /// <summary>Releases subscriptions, probe highlights, and owned drawing resources.</summary>
        public void Delete()
        {
            Selection.Changed -= OnSelectionChanged;
            ClearPinnedNodes();
            LightingBindings = LightingBindingDisplay.None;
            UpdateLightingHighlights();
            Visuals?.Delete();
            ScreenMarkerRenderer?.Delete();
        }

        /// <summary>Chooses the lighting bindings shown by the current render mode.</summary>
        /// <param name="mode">The viewer's render mode.</param>
        public void SetRenderMode(string mode)
        {
            LightingBindings = mode switch
            {
                "Cubemaps" => LightingBindingDisplay.EnvMaps,
                "Irradiance" or "Illumination" => LightingBindingDisplay.LightProbe,
                _ => LightingBindingDisplay.None,
            };
        }
    }
}
