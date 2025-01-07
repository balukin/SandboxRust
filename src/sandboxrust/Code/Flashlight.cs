public class Flashlight : Component
{
    [Property]
    public SoundEvent FlashlightSoundOn;
    [Property]
    public SoundEvent FlashlightSoundOff;
    private SpotLight spotLight;

    protected override void OnStart()
    {
        spotLight = GetComponent<SpotLight>();
    }

    protected override void OnUpdate()
    {
        if ( Input.Pressed( "flashlight" ) )
        {
            spotLight.Enabled = !spotLight.Enabled;

            
            var soundOrigin = Transform.World.Position;
            Sound.Play(spotLight.Enabled ? FlashlightSoundOn : FlashlightSoundOff, soundOrigin);
        }

        CameraHud.Current.FlashlightText = spotLight.Enabled ? "[F]lashlight: On" : "[F]lashlight: Off";
    }
}