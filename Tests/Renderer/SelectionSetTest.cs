using System.Reflection;
using System.Threading;
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
    public async Task SelectionChangesShareTheRenderSynchronizationBoundary()
    {
        var synchronization = (Lock)typeof(Scene)
            .GetField("SelectionSynchronization", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var selection = new SelectionSet();
        var synchronized = false;
        selection.Changed += (_, _) => synchronized = synchronization.IsHeldByCurrentThread;

        selection.Clear();

        await Assert.That(synchronized).IsTrue();
    }

    [Test]
    public async Task PinnedHighlightsSurviveOrdinarySelectionChanges()
    {
        using var loader = new GameFileLoader(null, null);
        using var context = new RendererContext(loader, NullLogger.Instance);
        using var scene = new Scene(context);
        var selection = new SelectionSet();
        var highlights = new SelectedNodeRenderer(context, selection);
        var pinned = new TestNode(scene);
        var ordinary = new TestNode(scene);
        var changes = 0;
        highlights.SelectionChanged += (_, _) => changes++;

        highlights.AddPinnedNodes([pinned, pinned]);
        await Assert.That(changes).IsEqualTo(0);
        await Assert.That(highlights.PinnedNodes.Count).IsEqualTo(1);
        selection.Select(pinned);
        selection.Clear();
        await Assert.That(pinned.IsSelected).IsTrue();
        await Assert.That(highlights.SelectedNodes.Count).IsEqualTo(0);
        await Assert.That(highlights.HasSelectedNodes).IsTrue();

        selection.Select(ordinary);
        highlights.AddPinnedNodes([ordinary]);
        highlights.ClearPinnedNodes();
        await Assert.That(pinned.IsSelected).IsFalse();
        await Assert.That(ordinary.IsSelected).IsTrue();
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
        var highlights = new SelectedNodeRenderer(context, selection);
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
