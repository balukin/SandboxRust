using Sandbox;

/// <summary>
/// Controls atmospheric conditions that affect rust formation.
/// Should be placed on a scene-level object.
/// </summary>
/// <remarks>
/// This is remnant of the previous implementation that used water vapor and some other made up stuff.
/// This should be mostly merged into RustSystem and OxygenLevel should be simply called RustingSpeed.
/// </remarks>
public sealed class Atmosphere : Component
{
    [Property, Range( 0, 1 )]
    public float OxygenLevel { get; set; } = 0.2f;

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