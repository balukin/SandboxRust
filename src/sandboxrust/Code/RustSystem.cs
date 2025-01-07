public class RustSystem : Component // Probably could be a GameObjectSystem but not sure what the benefit would be
{
    [Property]
    public RustRenderingMode RenderingMode = RustRenderingMode.Debug;

    protected override void OnUpdate()
    {
        if ( Input.Pressed( "reload" ) )
        {
            RenderingMode = RenderingMode == RustRenderingMode.Debug ? RustRenderingMode.Pretty : RustRenderingMode.Debug;
            Log.Info( "Rendering Mode switched to: " + RenderingMode );
        }
        else if ( Input.Pressed( "reload" ) )
        {
            RenderingMode = RenderingMode == RustRenderingMode.Pretty ? RustRenderingMode.Debug : RustRenderingMode.Pretty;
            Log.Info( "Rendering Mode switched to: " + RenderingMode );
        }

        CameraHud.Current.RustModeText = "[R]endering Mode: " + RenderingMode;
    }
}


public enum RustRenderingMode
{
    Debug,
    Pretty,
}