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

    protected override void OnUpdate()
    {
        HandleInput();
        CameraHud.Current.AtmosphereText = $"Oxygen Level: {OxygenLevel:P0} (affects rusting speed, adjust with Arrow keys)";
    }

    private void HandleInput()
    {
        var minorStep = 0.01f;
        var majorStep = 0.1f;

        if (Input.Pressed("o2_minor_step_down"))
        {
            OxygenLevel -= minorStep;
        }
        if (Input.Pressed("o2_minor_step_up"))
        {
            OxygenLevel += minorStep;
        }
        if (Input.Pressed("o2_major_step_up"))
        {
            OxygenLevel += majorStep;
        }
        if (Input.Pressed("o2_major_step_down"))
        {
            OxygenLevel -= majorStep;
        }

        if (OxygenLevel < 0)
        {
            OxygenLevel = 0;
        }
    }
} 