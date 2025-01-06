using Sandbox;

/// <summary>
/// Controls atmospheric conditions that affect rust formation.
/// Should be placed on a scene-level object.
/// </summary>
public sealed class Atmosphere : Component
{
    [Property, Range(0, 1)]
    public float OxygenLevel { get; set; } = 0.2f;

    [Property, Range(0, 1)]
    public float WaterVapor { get; set; } = 0.5f;
} 