using System;

public class CrowbarWeapon : BaseWeapon
{
    [Property]
    public SoundEvent ClangSound { get; set; }

    public override float ShootDelay => 0.5f;
    public override float WeaponRange => 50f;
    public override float AnimStartToImpactDelay => 0.1f;

    protected override void OnUpdate()
    {
        if (Input.Down("attack2"))
        {
            Attack();
        }
    }

    protected override void HandleImpact(SceneTraceResult tr)
    {
        var impactData = new ImpactData()
        {
            Position = tr.HitPosition,
            SurfaceNormal = tr.Normal,
            ImpactDirection = Player.EyeTransform.Forward.Normal,
            ImpactRadius = 0.3f,
            ImpactStrength = 4.0f,
            ImpactPenetrationStrength = 0.3f, // tex space, todo: unify it with other weapons
            ImpactPenetrationConeDeg = 20.0f,
            WeaponType = WeaponType.Crowbar
        };

        tr.GameObject.GetComponent<SurfaceImpactHandler>()?.HandleImpact(impactData);
        Sound.Play(ClangSound, tr.HitPosition);
    }
}