using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Editor.Selection;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Utils;

namespace Tests.Renderer;

public class SelectionSetTest
{
    private sealed class TestNode(Scene scene) : SceneNode(scene);

    [Test]
    public async Task ReplacingSelectionPublishesOneStableSnapshot()
    {
        using var loader = new GameFileLoader(null, null);
        using var context = new RendererContext(loader, NullLogger.Instance);
        using var scene = new Scene(context);
        var selection = new SelectionSet();
        var first = new TestNode(scene);
        var second = new TestNode(scene);
        selection.Select(first);
        var previousSnapshot = selection.Nodes;
        var changes = 0;
        selection.Changed += (_, _) => changes++;

        selection.Replace([second, first, second]);

        await Assert.That(changes).IsEqualTo(1);
        await Assert.That(previousSnapshot.Count).IsEqualTo(1);
        await Assert.That(previousSnapshot[0]).IsEqualTo(first);
        await Assert.That(selection.Nodes.Count).IsEqualTo(2);
        await Assert.That(selection.Nodes[0]).IsEqualTo(second);
        await Assert.That(selection.Nodes[1]).IsEqualTo(first);
    }

    [Test]
    public async Task PinnedHighlightsSurviveOrdinarySelectionChanges()
    {
        using var loader = new GameFileLoader(null, null);
        using var context = new RendererContext(loader, NullLogger.Instance);
        using var scene = new Scene(context);
        var selection = new SelectionSet();
        var highlights = new SelectedNodeRenderer(context, selection, new(loader, NullLogger.Instance), new());
        var pinned = new TestNode(scene);
        var ordinary = new TestNode(scene);
        var changes = 0;
        highlights.SelectionChanged += (_, _) => changes++;

        highlights.AddPinnedNodes([pinned, pinned]);
        await Assert.That(changes).IsEqualTo(0);
        await Assert.That(highlights.PinnedNodes.Count).IsEqualTo(1);
        selection.Select(pinned);
        selection.Clear();
        await Assert.That(highlights.PinnedNodes.Contains(pinned)).IsTrue();
        await Assert.That(highlights.SelectedNodes.Count).IsEqualTo(0);
        await Assert.That(highlights.HasSelectedNodes).IsTrue();

        selection.Select(ordinary);
        highlights.AddPinnedNodes([ordinary]);
        highlights.ClearPinnedNodes();
        await Assert.That(highlights.PinnedNodes.Contains(pinned)).IsFalse();
        await Assert.That(selection.Nodes.Contains(ordinary)).IsTrue();
        await Assert.That(highlights.SelectedNode).IsEqualTo(ordinary);
        highlights.Delete();
    }

    [Test]
    public async Task HighlightedProbeInputsFollowPinningAndRenderMode()
    {
        using var loader = new GameFileLoader(null, null);
        using var context = new RendererContext(loader, NullLogger.Instance);
        using var mainScene = new Scene(context);
        using var skyScene = new Scene(context);
        var selection = new SelectionSet();
        var highlights = new SelectedNodeRenderer(context, selection, new(loader, NullLogger.Instance), new());
        var mainProbe = new SceneLightProbe(mainScene, new AABB(-1, -1, -1, 1, 1, 1));
        var skyProbe = new SceneLightProbe(skyScene, new AABB(-1, -1, -1, 1, 1, 1));
        var mainNode = new TestNode(mainScene) { LightProbeBinding = mainProbe };
        var skyNode = new TestNode(skyScene) { LightProbeBinding = skyProbe };

        highlights.SetRenderMode("Irradiance");
        highlights.AddPinnedNodes([skyNode]);
        selection.Select(mainNode);
        highlights.UpdateLightingHighlights();
        await Assert.That(mainScene.LightingDebug.HighlightedLightProbes.Contains(mainProbe)).IsTrue();
        await Assert.That(skyScene.LightingDebug.HighlightedLightProbes.Contains(skyProbe)).IsTrue();

        selection.Clear();
        highlights.UpdateLightingHighlights();
        await Assert.That(mainScene.LightingDebug.HighlightedLightProbes.Count).IsEqualTo(0);
        await Assert.That(skyScene.LightingDebug.HighlightedLightProbes.Contains(skyProbe)).IsTrue();

        highlights.RemovePinnedNodes([skyNode]);
        highlights.UpdateLightingHighlights();
        await Assert.That(skyScene.LightingDebug.HighlightedLightProbes.Count).IsEqualTo(0);

        highlights.AddPinnedNodes([skyNode]);
        highlights.UpdateLightingHighlights();
        highlights.SetRenderMode("Default");
        highlights.UpdateLightingHighlights();
        await Assert.That(skyScene.LightingDebug.HighlightedLightProbes.Count).IsEqualTo(0);
        highlights.Delete();
    }
}
