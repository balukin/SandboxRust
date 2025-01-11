using System;

/// <summary>
/// Handles shooting and scanning for hits.
/// </summary>
public class Weapon : Component
{

    PlayerController player;
    WeaponSwitcher switcher;

    [Property]
    public float ShootDelay { get; set; } = 0.1f;

    [Property]
    public bool VisualizeHits { get; set; } = false;

    TimeSince timeSincePrimaryAttack;

    protected override void OnStart()
    {
        base.OnStart();
        switcher = GetComponent<WeaponSwitcher>();
        player = GetComponent<PlayerController>();
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
            return;

        TryAnimating();

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
        var tr = Scene.Trace.Ray( ray, 4096 )
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
        impactHandler.HandleImpact( new ImpactData( tr.HitPosition, tr.Normal, player.EyeTransform.Forward.Normal, switcher.CurrentWeapon ) );

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

	private void TryAnimating()
	{
        var skinnedModelRenderer = switcher.CurrentWeaponGo?.GetComponent<SkinnedModelRenderer>();
		
        if(skinnedModelRenderer != null)
        {
            Log.Info( "Setting Attack" );
            //skinnedModelRenderer.Set("Attack", true);
            skinnedModelRenderer.Set("b_attack", true);
        
        }
	}
}