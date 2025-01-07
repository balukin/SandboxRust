using Sandbox;
using Sandbox.Rendering;

public sealed class CameraHud : Component
{
	private CameraComponent cam;
	public static CameraHud Current { get; private set; }
	public string AtmosphereText { get; set; }
	private int topLeftCurrentLineOffset = 0;
	private const int lineHeight = 20;

	protected override void OnStart()
	{
		cam = GetComponent<CameraComponent>();
		Current = this;
	}
	protected override void OnUpdate()
	{
		var hud = cam.Hud;
		WriteExplainers( hud );
	}



	private void WriteExplainers( HudPainter hud )
	{
		topLeftCurrentLineOffset = 0;
		WriteLine( hud, "Hello!" );
		WriteLine( hud, AtmosphereText );
	}

	private void WriteLine( HudPainter hud, string text )
	{
		hud.DrawText( new TextRendering.Scope( text, Color.Red, lineHeight ), new Vector2( 10, 10 + topLeftCurrentLineOffset ), TextFlag.LeftTop );
		topLeftCurrentLineOffset += lineHeight;
	}
}
