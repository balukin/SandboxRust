using Sandbox;

public sealed class CameraHud : Component
{
	public static CameraHud Current { get; private set; }
	private RustUI ui;

	public RustUI UI => ui;

	protected override void OnStart()
	{
		Current = this;

		var screenPanel = Scene.GetOrAddComponent<ScreenPanel>();

		ui = new RustUI();
		ui.Parent = screenPanel.GetPanel();
	}

	protected override void OnUpdate()
	{
		if ( Input.Pressed( "Menu" ) )
		{
			ui.OptionsOpen = !ui.OptionsOpen;			
		}

		if(Input.Pressed("Forward") || Input.Pressed("Back") || Input.Pressed("Left") || Input.Pressed("Right"))
		{
			ui.ExplainerText = RustUI.ExplainerDefault;
		}

		if(Input.Pressed("hud_toggle"))
		{
			ui.HudVisible = !ui.HudVisible;
		}
	}

	protected override void OnDestroy()
	{
		if ( Current == this )
		{
			Current = null;
		}

		ui?.Delete();
	}
}
