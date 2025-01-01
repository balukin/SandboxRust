using Sandbox;

public sealed class WeaponSwitcher : Component
{
	
	[Property]
	public WeaponType CurrentWeapon { get; set; }

	[Property]
	public GameObject SprayGo { get; set; }

	[Property]
	public GameObject GunGo { get; set; }

	private PlayerController playerController;


	protected override void OnStart()
	{
		base.OnStart();
		playerController = GetComponent<PlayerController>();
	}

	protected override void OnUpdate()
	{
		HandleInput();
		UpdateVisibility();
	}

	private void HandleInput()
	{
		if(Input.Pressed("Slot1"))
		{
			Log.Info("Switching to Spray");
			CurrentWeapon = WeaponType.Spray;
		}
		else if(Input.Pressed("Slot2"))
		{
			Log.Info("Switching to Gun");
			CurrentWeapon = WeaponType.Gun;
		}
	}

	private void UpdateVisibility()
	{
		// Select which VM to render, probably could use change handlers
		// but it seems that GameObject.Enabled property is smart enough to not do any changes if the value is the same
		if ( playerController.ThirdPerson )
		{
			SprayGo.Enabled = false;
			GunGo.Enabled = false;
		}
		else if ( CurrentWeapon == WeaponType.Spray )
		{
			SprayGo.Enabled = true;
			GunGo.Enabled = false;
		}
		else if ( CurrentWeapon == WeaponType.Gun )
		{
			SprayGo.Enabled = false;
			GunGo.Enabled = true;
		}
	}
}
