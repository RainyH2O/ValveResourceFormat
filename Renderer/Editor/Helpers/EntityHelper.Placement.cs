namespace ValveResourceFormat.Renderer.Editor.Helpers;

public abstract partial class EntityHelper
{
    internal virtual void RefreshPlacement()
    {
        foreach (var node in shapeNodes)
        {
            node.RefreshShapes();
        }
    }
}
