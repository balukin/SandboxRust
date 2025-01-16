using System;

public abstract class BaseWeapon : Component
{
    [Property]
    public bool VisualizeHits { get; set; } = false;

    [Property]
    public PlayerController Player { get; set; }

    public abstract float ShootDelay { get; }
    public abstract float WeaponRange { get; }
    public abstract float AnimStartToImpactDelay { get; }

    protected TimeSince timeSincePrimaryAttack;

    protected override void OnStart()
    {
        base.OnStart();
    }

    protected virtual void Attack()
    {
        if (timeSincePrimaryAttack < ShootDelay)
        {
            return;
        }

        timeSincePrimaryAttack = 0;

        TryAnimatingAttack();

        if (AnimStartToImpactDelay > 0)
        {
            Task.Delay((int)(AnimStartToImpactDelay * 1000)).ContinueWith(_ => DoImpact());
        }
        else
        {
            DoImpact();
        }
    }

    protected virtual void DoImpact()
    {
        if (VisualizeHits)
        {
            DebugObject.Create()
                .WithName("Ray")
                .WithPosition(Player.EyeTransform.Position)
                .WithDirection(Player.EyeTransform.Forward)
                .WithTimeout(2f);
        }

        var ray = Player.EyeTransform.ForwardRay;
        var tr = Scene.Trace.Ray(ray, WeaponRange)
            .IgnoreGameObjectHierarchy(GameObject)
            .Run();

        if (!tr.Hit)
        {
            return;
        }

        var impactHandler = tr.GameObject.GetComponent<SurfaceImpactHandler>();
        if (impactHandler == null)
        {
            return;
        }

        HandleImpact(tr);

        if (VisualizeHits)
        {
            DebugObject.Create()
                .WithName("Hit")
                .WithPosition(tr.HitPosition)
                .WithDirection(tr.Normal)
                .WithTimeout(1f);
        }
    }

    protected abstract void HandleImpact(SceneTraceResult tr);

    protected virtual void TryAnimatingAttack()
    {
        var skinnedModelRenderer = GameObject.GetComponent<SkinnedModelRenderer>();
        if (skinnedModelRenderer != null)
        {
            skinnedModelRenderer.Set("b_attack", true);
        }
    }

    protected virtual void StopAnimatingAttack()
    {
        var skinnedModelRenderer = GameObject.GetComponent<SkinnedModelRenderer>();
        if (skinnedModelRenderer != null)
        {
            skinnedModelRenderer.Set("b_attack", false);
        }
    }
} 