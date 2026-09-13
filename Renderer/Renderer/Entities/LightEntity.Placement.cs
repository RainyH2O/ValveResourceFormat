namespace ValveResourceFormat.Renderer.Entities;

public sealed partial class LightEntity
{
    internal override void RefreshPlacement()
    {
        base.RefreshPlacement();

        if (light != null)
        {
            light.PlaceAt(Transform);
            Changed();
        }
    }
}
