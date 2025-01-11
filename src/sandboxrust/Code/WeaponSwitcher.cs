using System;
using Sandbox;

public sealed class WeaponSwitcher : Component
{
	public delegate void WeaponChangedHandler(GameObject previousWeapon, GameObject newWeapon);
	public event WeaponChangedHandler OnWeaponChanged;

	[Property]
	public WeaponType CurrentWeapon { get; set; }

	[Property]
	public GameObject SprayGo { get; set; }

	[Property]
	public GameObject GunGo { get; set; }

	[Property]
	public GameObject CrowbarGo { get; set; }

	public GameObject CurrentWeaponGo { get; set; }

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
		var previousWeapon = CurrentWeaponGo;
		if ( Input.Pressed( "Slot1" ) )
		{
			Log.Info( "Switching to Spray" );
			CurrentWeapon = WeaponType.Spray;
			CurrentWeaponGo = SprayGo;
		}
		else if ( Input.Pressed( "Slot2" ) )
		{
			Log.Info( "Switching to Crowbar" );
			CurrentWeapon = WeaponType.Crowbar;
			CurrentWeaponGo = CrowbarGo;
		}
		// else if ( Input.Pressed( "Slot3" ) ) // TODO: Maybe later
		// {
		// 	Log.Info( "Switching to Gun" );
		// 	CurrentWeapon = WeaponType.Gun;
		// 	CurrentWeaponGo = GunGo;
		// }

		if ( previousWeapon != CurrentWeaponGo )
		{
			OnWeaponChanged?.Invoke( previousWeapon, CurrentWeaponGo );
		}
	}

	private void UpdateVisibility()
	{
		bool isThirdPerson = playerController.ThirdPerson;

		SprayGo.Enabled = !isThirdPerson && CurrentWeapon == WeaponType.Spray;
		GunGo.Enabled = !isThirdPerson && CurrentWeapon == WeaponType.Gun;
		CrowbarGo.Enabled = !isThirdPerson && CurrentWeapon == WeaponType.Crowbar;
	}
}
