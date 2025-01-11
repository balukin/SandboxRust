using System;

// TODO: Merge this and WeaponSwitcher into one component and create weapon definition objects instead of switching over weapon types

/// <summary>
/// Handles shooting and scanning for hits.
/// </summary>
public class Weapon : Component
{

    PlayerController player;
    WeaponSwitcher switcher;

    public float ShootDelay => switcher.CurrentWeapon == WeaponType.Spray ? 0.05f : 0.5f;
    public float WeaponRange => switcher.CurrentWeapon == WeaponType.Spray ? 100f : 50f;
    public float AnimStartToImpactDelay => switcher.CurrentWeapon == WeaponType.Spray ? 0.00f : 0.25f;

    [Property]
    public bool VisualizeHits { get; set; } = false;

    TimeSince timeSincePrimaryAttack;

    protected override void OnStart()
    {
        base.OnStart();
        switcher = GetComponent<WeaponSwitcher>();
        player = GetComponent<PlayerController>();
        switcher.OnWeaponChanged += OnWeaponChanged;
    }

	protected override void OnDestroy()
	{
		base.OnDestroy();
		switcher.OnWeaponChanged -= OnWeaponChanged;
	}

	private void OnWeaponChanged( GameObject previousWeapon, GameObject newWeapon )
	{
		StopAnimatingAttack( previousWeapon );
	}

	protected override void OnFixedUpdate()
    {
        if ( Input.Down( "attack1" ) )
        {
            Attack();
        }
    }

    protected void Attack()
    {
        // Check if we can shoot again based on delay
        if ( timeSincePrimaryAttack < ShootDelay )
        {
            return;
        }

        timeSincePrimaryAttack = 0;

        TryAnimatingAttack();

        if ( AnimStartToImpactDelay > 0 )
        {
            Task.Delay( (int)(AnimStartToImpactDelay * 1000) ).ContinueWith( _ => DoImpact() );
        }
        else
        {
            DoImpact();
        }

        void DoImpact()
        {
            if ( VisualizeHits )
            {
                // Spawn temporary shooting ray effect
                DebugObject.Create()
                    .WithName( "Ray" )
                    .WithPosition( player.EyeTransform.Position )
                    .WithDirection( player.EyeTransform.Forward )
                    .WithTimeout( 2f );
            }

            // Create a ray from camera position forward
            var ray = player.EyeTransform.ForwardRay;

            // Perform raycast
            var tr = Scene.Trace.Ray( ray, WeaponRange )
                .IgnoreGameObjectHierarchy( GameObject )
                .Run();

            if ( !tr.Hit )
            {
                return;
            }

            var impactHandler = tr.GameObject.GetComponent<SurfaceImpactHandler>();
            if ( impactHandler == null )
            {
                // Something we don't care about was hit
                return;
            }

            // Assume no other actors can cause impact - use eye forward
            var weapon = switcher.CurrentWeapon;
            impactHandler.HandleImpact( GetImpactData( weapon, tr, player.EyeTransform.Forward.Normal ) );

            if ( VisualizeHits )
            {
                // Spawn temporary hit effect
                DebugObject.Create()
                    .WithName( "Hit" )
                    .WithPosition( tr.HitPosition )
                    .WithDirection( tr.Normal )
                    .WithTimeout( 1f );
            }
        }
    }

    private void StopAnimatingAttack( GameObject previousWeapon )
    {
        var skinnedModelRenderer = previousWeapon?.GetComponent<SkinnedModelRenderer>();
        if(skinnedModelRenderer != null)
        {
            Log.Info( "Stopping Attack" );
            skinnedModelRenderer.Set("b_attack", false);
        }
    }

	private void TryAnimatingAttack()
	{
        var skinnedModelRenderer = switcher.CurrentWeaponGo?.GetComponent<SkinnedModelRenderer>();
		
        if(skinnedModelRenderer != null)
        {
            Log.Info( "Setting Attack" );
            skinnedModelRenderer.Set("b_attack", true);
        
        }
	}

    private ImpactData GetImpactData(WeaponType weapon, SceneTraceResult tr, Vector3 direction)
    {
        if(weapon == WeaponType.Spray)
        {
            return new ImpactData()
            {
                Position = tr.HitPosition,
                SurfaceNormal = tr.Normal,
                ImpactDirection = direction,
                ImpactRadius = 0.15f,
                ImpactStrength = 0.2f,
                ImpactPenetrationStrength = 0.0f,
                ImpactPenetrationConeDeg = 0.0f,
                WeaponType = weapon
            };
        }
        else if(weapon == WeaponType.Crowbar)
        {
            return new ImpactData()
            {
                Position = tr.HitPosition,
                SurfaceNormal = tr.Normal,
                ImpactDirection = direction,
                ImpactRadius = 0.1f,
                ImpactStrength = 0.4f,
                ImpactPenetrationStrength = 0.2f,
                ImpactPenetrationConeDeg = 20.0f,
                WeaponType = weapon
            };
        }

        // Gun not implemented yet
        return null;
    }
}