using Sandbox;

/// <summary>
/// Holder for whatever quality settings we want to be available in other components.
/// </summary>
public class QualitySystem( Scene scene ) : GameObjectSystem( scene )
{
    public int UpdatesPerSecond { get; set; }
    public int VolumeResolution { get; set; }
    public bool SoftRustEnabled { get; set; }
}