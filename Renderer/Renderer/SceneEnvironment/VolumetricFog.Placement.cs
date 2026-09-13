namespace ValveResourceFormat.Renderer.SceneEnvironment;

public sealed partial class VolumetricFog
{
    public readonly partial record struct FogVolume
    {
        internal object? Owner { get; init; }
    }

    internal void UpdateVolume(FogVolume previous, FogVolume updated)
    {
        var index = volumes.IndexOf(previous);
        if (index < 0)
        {
            return;
        }

        volumes[index] = updated;
        volumesChanged = true;
    }
}
