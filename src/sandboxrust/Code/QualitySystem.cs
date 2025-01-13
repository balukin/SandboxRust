using Sandbox;

/// <summary>
/// Holder for whatever quality settings we want to be available in other components.
/// </summary>
public class QualitySystem( Scene scene ) : GameObjectSystem( scene )
{
    public int ObjectUpdatesPerFrame { get; set; } = 1;
    public int VolumeResolution { get; set; } = 64;
    public bool SoftRustEnabled { get; set; } = true;
}