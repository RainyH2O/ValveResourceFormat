using System.Linq;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Entities;

public sealed partial class HammerEntityVisuals
{
    internal void RefreshPlacements()
    {
        foreach (var (entity, owner) in gameEntities)
        {
            if (entity.IsRemoved)
            {
                continue;
            }

            foreach (var helper in owner.Helpers)
            {
                helper.RefreshPlacement();
            }
        }

        foreach (var scene in gameEntities.Select(static pair => pair.Key.Scene).Distinct())
        {
            foreach (var node in scene.AllNodes.OfType<ToolSceneNode>())
            {
                if (node.LayerName == ConnectionsLayerName)
                {
                    node.RefreshShapes();
                }
            }
        }
    }
}
