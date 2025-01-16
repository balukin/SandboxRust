using System;

public class SprayWeapon : BaseWeapon
{
    [Property]
    public SprayParticleSystem SprayParticleSystem { get; set; }

    public override float ShootDelay => 0.016f;
    public override float WeaponRange => 100f;
    public override float AnimStartToImpactDelay => 0.0f;

    protected override void OnUpdate()
    {
        if (Input.Down("attack1"))
        {
            SprayParticleSystem.Rate = 100;
            Attack();
        }
        else
        {
            SprayParticleSystem.Rate = 0;
        }
    }

    protected override void HandleImpact(SceneTraceResult tr)
    {
        var impactData = new ImpactData()
        {
            Position = tr.HitPosition,
            SurfaceNormal = tr.Normal,
            ImpactDirection = Player.EyeTransform.Forward.Normal,
            ImpactRadius = 0.15f,
            ImpactStrength = 0.03f,
            ImpactPenetrationStrength = 5f, // world space this time, todo: unify it with other weapons
            ImpactPenetrationConeDeg = 0.0f, // doesn't work for spray shader anyway
            WeaponType = WeaponType.Spray
        };

        tr.GameObject.GetComponent<SurfaceImpactHandler>()?.HandleImpact(impactData);
    }
} 