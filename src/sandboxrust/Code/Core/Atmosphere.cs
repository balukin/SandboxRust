using Sandbox;

/// <summary>
/// Controls atmospheric conditions that affect rust formation.
/// Should be placed on a scene-level object.
/// </summary>
public sealed class Atmosphere : Component
{
    [Property, Range( 0, 1 )]
    public float OxygenLevel { get; set; } = 0.2f;

    [Property, Range( 0, 1 )]
    public float WaterVapor { get; set; } = 0.5f;

    protected override void OnUpdate()
    {
        HandleInput();
        CameraHud.Current.UI.OxygenLevel = OxygenLevel;
    }

    private void HandleInput()
    {        
        var majorStep = 0.1f;


        if ( Input.Pressed( "o2_major_step_up" ) )
        {
            OxygenLevel += majorStep;
            CameraHud.Current.UI.ExplainerText = "Rust will form faster on wet surfaces.";
        }
        if ( Input.Pressed( "o2_major_step_down" ) )
        {
            OxygenLevel -= majorStep;
            CameraHud.Current.UI.ExplainerText = "Rust will form slower on wet surfaces.";
        }

        if ( OxygenLevel < 0 )
        {
            OxygenLevel = 0;
        }
    }
}