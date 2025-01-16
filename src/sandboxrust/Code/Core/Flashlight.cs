public class Flashlight : Component
{
    [Property]
    public SoundEvent FlashlightSoundOn;
    [Property]
    public SoundEvent FlashlightSoundOff;
    private SpotLight spotLight;

    public float Angle => spotLight.ConeInner;

    public bool IsEnabled => spotLight.Enabled;

    protected override void OnStart()
    {        
        spotLight = GetComponent<SpotLight>();
    }

    protected override void OnUpdate()
    {
        if ( Input.Pressed( "flashlight" ) )
        {
            spotLight.Enabled = !spotLight.Enabled;
            Log.Info( "Flashlight toggled: " + spotLight.Enabled );

            var soundOrigin = Transform.World.Position;
            Sound.Play( spotLight.Enabled ? FlashlightSoundOn : FlashlightSoundOff, soundOrigin );
        }

        CameraHud.Current.UI.FlashlightEnabled = spotLight.Enabled;
    }
}