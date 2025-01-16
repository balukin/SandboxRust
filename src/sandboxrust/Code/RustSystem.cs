using System;

public class RustSystem : Component
{
    [Property]
    public RustRenderingMode RenderingMode = RustRenderingMode.Colored;

    [Property]
    public Flashlight Flashlight { get; set; }

    [Property]
    [Range( 0, 10 )]
	public float ErosionStrength { get; set; } = 1.5f;

	private long frameCount = 0;

    // Those are intervals, not frequency - that's the opposite of the frequency meaning...

    /// <summary>
    /// Minimum frames between simulation updates for each object
    /// </summary>
    [Property]
    [Range( 1, 300 )]
    public int SimulationFrequency = 7;

    /// <summary>
    /// Minimum frames between erosion updates for each object
    /// </summary>
    [Property]
    [Range( 1, 500 )]
    public int ErosionFrequency = 241;

    /// <summary>
    /// Collection of all known active rustable objects
    /// </summary>
    private List<RustableObject> activeObjects = new();

    /// <summary>
    /// Offset erosion from simulation by this many frames
    /// </summary>
    private const int ErosionOffset = 5;

    // Track last updates separately for simulation and erosion
    private Dictionary<RustableObject, long> lastSimulationFrames = new();
    private Dictionary<RustableObject, long> lastErosionFrames = new();
	private QualitySystem qualitySystem;

	protected override void OnStart()
    {
        base.OnStart();
        qualitySystem = Scene.GetSystem<QualitySystem>();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        frameCount++;

        HandleInput();
        CameraHud.Current.UI.ErosionFrequency = ErosionFrequency;
        CameraHud.Current.UI.RenderingMode = RenderingMode;
        CameraHud.Current.UI.ErosionStrength = ErosionStrength;
    }
    
    private void HandleInput()
    {
        if ( Input.Pressed( "reload" ) )
        {
            RenderingMode = RenderingMode == RustRenderingMode.Debug ? RustRenderingMode.Colored : RustRenderingMode.Debug;
            Log.Info( "Rendering Mode switched to: " + RenderingMode );

            if(RenderingMode == RustRenderingMode.Debug)
            {
                CameraHud.Current.UI.ExplainerText = "Channels: R - Rust, G - Wetness, B - Structure health";
            }
        }

        if ( Input.Pressed( "erosion_interval_up" ) )
        {
            ErosionFrequency = (int)(ErosionFrequency * 1.1f);
            CameraHud.Current.UI.ExplainerText = "Mesh will deform more often.";
        }

        if ( Input.Pressed( "erosion_interval_down" ) )
        {
            ErosionFrequency = (int)(ErosionFrequency * 0.9f);
            CameraHud.Current.UI.ExplainerText = "Mesh will deform less often.";
        }

        if( Input.Pressed( "erosion_strength_up" ) )
        {
            ErosionStrength += 0.1f;
            CameraHud.Current.UI.ExplainerText = "Mesh deformation will be stronger.";
        }

        if( Input.Pressed( "erosion_strength_down" ) )
        {
            ErosionStrength -= 0.1f;
            CameraHud.Current.UI.ExplainerText = "Mesh deformation will be weaker.";
        }

        if ( ErosionFrequency < 10 )
        {
            ErosionFrequency = 10;
        }
    }


    private bool IsThisObjectTurnThisFrame( RustableObject obj, int offset = 0 )
    {
        if ( !activeObjects.Contains( obj ) )
        {
            return false;
        }

        if ( activeObjects.Count == 0 )
        {
            return false;
        }

        var index = activeObjects.IndexOf( obj );
        var currentFrame = (frameCount + offset) % Math.Max( 1, activeObjects.Count / qualitySystem.ObjectUpdatesPerFrame );

        // Check if it's this object's turn based on the current frame
        for ( int i = 0; i < qualitySystem.ObjectUpdatesPerFrame; i++ )
        {
            var targetIndex = ((currentFrame * qualitySystem.ObjectUpdatesPerFrame) + i) % activeObjects.Count;
            if ( index == targetIndex )
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldRun( RustableObject obj, Dictionary<RustableObject, long> previousUpdates, int frequency, int offset = 0 )
    {
        if ( previousUpdates.TryGetValue( obj, out long lastUpdate ) )
        {
            if ( (frameCount - lastUpdate) < frequency )
            {
                // Cooldown not yet over for this simulation type and object
                return false;
            }
        }

        if ( IsThisObjectTurnThisFrame( obj, offset ) )
        {
            previousUpdates[obj] = frameCount;
            return true;
        }
        
        return false;
    }

    public bool ShouldRunSimulation( RustableObject obj )
    {
        return ShouldRun( obj, lastSimulationFrames, SimulationFrequency );
    }

    public bool ShouldRunErosion( RustableObject obj )
    {
        return ShouldRun( obj, lastErosionFrames, ErosionFrequency, ErosionOffset );
    }

    public void RegisterRustableObject( RustableObject obj )
    {
        if ( !activeObjects.Contains( obj ) )
        {
            activeObjects.Add( obj );
            lastSimulationFrames[obj] = frameCount;
            lastErosionFrames[obj] = frameCount;
        }
    }

    public void UnregisterRustableObject( RustableObject obj )
    {
        activeObjects.Remove( obj );
        lastSimulationFrames.Remove( obj );
        lastErosionFrames.Remove( obj );
    }


    /// <summary>
    /// Call this to run erosion simulation for a given object during next turn immediately.
    /// </summary>
	public void RunErosionNextFrame( RustableObject rustableObject )
	{
		lastErosionFrames[rustableObject] = 0;
	}
}

public enum RustRenderingMode
{
    Debug,
    Colored,
}
