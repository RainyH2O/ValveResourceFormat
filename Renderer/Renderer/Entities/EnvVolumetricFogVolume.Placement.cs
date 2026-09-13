namespace ValveResourceFormat.Renderer.Entities;

public sealed partial class EnvVolumetricFogVolume
{
    internal override void RefreshPlacement()
    {
        base.RefreshPlacement();

        if (fogVolume is not { } volume || Scene.FogInfo.Volumetric is not { } volumetric)
        {
            return;
        }

        var updated = volume with { LocalToWorld = RigidTransform };
        volumetric.UpdateVolume(volume, updated);
        fogVolume = updated;
    }
}
