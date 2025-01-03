/// <summary>
/// Handles shooting and scanning for hits.
/// </summary>
public class Weapon : Component
{
    PlayerController player;
    WeaponSwitcher switcher;
    
    [Property]
    public float ShootDelay { get; set; } = 0.1f;
    
    TimeSince timeSincePrimaryAttack;

    protected override void OnStart()
    {
        base.OnStart();
        switcher = GetComponent<WeaponSwitcher>();
        player = GetComponent<PlayerController>();
    }

    protected override void OnFixedUpdate()
    {
        if (Input.Pressed("attack1"))
        {
            TryShoot();
        }
    }

    protected void TryShoot()
    {
        // Check if we can shoot again based on delay
        if (timeSincePrimaryAttack < ShootDelay)
            return;

        timeSincePrimaryAttack = 0;

        DebugObject.Create()
            .WithName("Ray")
            .WithPosition(player.EyeTransform.Position)
            .WithDirection(player.EyeTransform.Forward)
            .WithTimeout(2f);

        // Create a ray from camera position forward
        var ray = player.EyeTransform.ForwardRay;
        
        // Perform raycast
        var tr = Scene.Trace.Ray(ray, 4096)
            .IgnoreGameObjectHierarchy(GameObject.Parent)
            .Run();

        if (!tr.Hit)
        {
            Log.Info("No hit");
            return;
        }

        // Spawn temporary hit effect
        DebugObject.Create()
            .WithName("Hit")
            .WithPosition(tr.HitPosition)
            .WithTimeout(1f);

        // We have hit something - you can use tr.HitPosition and tr.Normal
        // for effects or further processing
        Log.Info($"Hit at position: {tr.HitPosition}, Normal: {tr.Normal}");
    }
}