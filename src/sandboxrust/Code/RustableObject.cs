using System;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Rendering;
using Sandbox.UI;

/// <summary>
/// Component for objects that can have rust applied to them.
/// </summary>
public sealed class RustableObject : Component
{
	// We could create an optimized variant that 
	// - uses precomputed data maps projected onto the object, similarly to UVs
	// - for a cube, uses 6-sheet texture at higher resolution instead of 3D texture
	// but let's do it stupidly simple for now - with a 3D texture representing data stretched across the object bbox (const 50 for now)
	public const float MaxSize = 50.0f;

	// Counter for simulation ticks
	private long simTicks = 0;

	private SceneCustomObject sceneCustomObject;

	ModelRenderer modelRenderer;

	private Texture RustData { get; set; }
	private Texture RustDataReadBuffer { get; set; }

	private ComputeShader clone3dTexShader;
	private ComputeShader getSprayedShader;
	private ComputeShader getHitShader;
	private ComputeShader simulationShader;

	private Material instanceMaterial;

	private SurfaceImpactHandler impactHandler;

	private const int TextureSize = 64;

	private ImpactData? storedImpactData;

	// TODO: Coordinate spread across all rustable objects to avoid frame spikes
	[Property]
	public int SimulationFrameInterval = 15;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		// Using SceneCustomObject with custom render hook
		// seems to be correct way to access CopyResource
		sceneCustomObject = new SceneCustomObject( Scene.SceneWorld );
		sceneCustomObject.RenderOverride = RunSimulation;
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		sceneCustomObject.RenderOverride = null;
		if ( sceneCustomObject.IsValid() )
		{
			sceneCustomObject.Delete();
		}
		sceneCustomObject = null;
	}

	protected override void OnStart()
	{
		base.OnStart();

		// Create two 3D textures for ping-pong simulation
		RustData = CreateVolumeTexture( TextureSize );
		RustDataReadBuffer = CreateVolumeTexture( TextureSize );

		modelRenderer = GetComponent<ModelRenderer>();

		// Create per-instance material - TODO: maybe it loads already as an instance or is it shared?
		instanceMaterial = Material.Load( "materials/rustable_untextured.vmat" ).CreateCopy();
		modelRenderer.MaterialOverride = instanceMaterial;
		instanceMaterial.Set( "RustDataRead", RustData );

		getSprayedShader = new ComputeShader( "shaders/getsprayed" );
		getHitShader = new ComputeShader( "shaders/gethit" );
		simulationShader = new ComputeShader( "shaders/rust_simulation" );
		clone3dTexShader = new ComputeShader( "shaders/clone3dtex" );

		impactHandler = GameObject.GetOrAddComponent<SurfaceImpactHandler>();
		impactHandler.OnImpact += StoreImpact;
	}

	private Texture CreateVolumeTexture( int size )
	{
		// Random garbage to check if it's even getting to the shader
		var data = new byte[size * size * size * 3];
		// FillInitialData( size, data );

		// https://wiki.facepunch.com/sbox/Compute_Shaders
		return Texture.CreateVolume( size, size, size, ImageFormat.RGB888 )
			.WithDynamicUsage()
			.WithUAVBinding()
			.WithData( data )
			.Finish();
	}

	private static void FillInitialData( int size, byte[] data )
	{
		const int globalMultiplier = 1;
		const int checkerboardSizeR = 16 * globalMultiplier;
		const int checkerboardSizeG = 4 * globalMultiplier;
		const int checkerboardSizeB = 1 * globalMultiplier;

		const int dark = 10;
		const int light = 60;

		for ( int z = 0; z < size; z++ )
		{
			for ( int y = 0; y < size; y++ )
			{
				for ( int x = 0; x < size; x++ )
				{
					int index = (z * size * size + y * size + x) * 3;

					// Red channel: large checkerboard
					bool darkOrLightR = ((x / checkerboardSizeR) + (y / checkerboardSizeR) + (z / checkerboardSizeR)) % 2 == 0;
					data[index] = darkOrLightR ? (byte)dark : (byte)light;

					// Green channel: medium checkerboard
					bool darkOrLightG = ((x / checkerboardSizeG) + (y / checkerboardSizeG) + (z / checkerboardSizeG)) % 2 == 0;
					data[index + 1] = darkOrLightG ? (byte)dark : (byte)light;

					// Blue channel: small checkerboard
					bool darkOrLightB = ((x / checkerboardSizeB) + (y / checkerboardSizeB) + (z / checkerboardSizeB)) % 2 == 0;
					data[index + 2] = darkOrLightB ? (byte)dark : (byte)light;
				}
			}
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
	}

	private void RunSimulation( SceneObject o )
	{
		// Optimization opportunities:
		// - use ping-pong swap to avoid resource barrier mess (do I even need it? better safe than crash)
		// - generate mipmaps and sample from lower-resolution resource to reduce total computation with some nice downsampling filter

		// First, apply impact if it happened this frame
		Graphics.ResourceBarrierTransition( RustData, ResourceState.UnorderedAccess );
		ApplyImpact();

		if ( simTicks++ % SimulationFrameInterval == 0 )
		{
			// Copy the data to the read-only buffer to avoid race condition on R/W in the same resource

			// For some reason Graphics.CopyTexture( RustData, RustDataReadBuffer ); 
			// Only one slice was being copied and changing slice indices had no effect
			// We're gonna do it the stupid way
			Graphics.ResourceBarrierTransition( RustData, ResourceState.CopySource );
			Graphics.ResourceBarrierTransition( RustDataReadBuffer, ResourceState.CopyDestination );
			clone3dTexShader.Attributes.Set( "SourceTexture", RustData );
			clone3dTexShader.Attributes.Set( "TargetTexture", RustDataReadBuffer );
			clone3dTexShader.Dispatch( TextureSize, TextureSize, TextureSize );

			Graphics.ResourceBarrierTransition( RustDataReadBuffer, ResourceState.UnorderedAccess );
			Graphics.ResourceBarrierTransition( RustData, ResourceState.UnorderedAccess );
			simulationShader.Attributes.Set( "SourceTexture", RustDataReadBuffer );
			simulationShader.Attributes.Set( "TargetTexture", RustData );
			simulationShader.Dispatch( TextureSize, TextureSize, TextureSize );
		}
	}

	private void StoreImpact( ImpactData impactData )
	{
		storedImpactData = impactData;
	}

	private void ApplyImpact()
	{
		if ( storedImpactData == null )
		{
			return;
		}

		var impactData = storedImpactData.Value;
		storedImpactData = null;

		var positionOs = Transform.World.PointToLocal( impactData.position );
		var impactDirOs = Transform.World.NormalToLocal( impactData.impactDirection ).Normal;

		// Convert to 0-1 space for texture sampling
		var texPos = (positionOs / MaxSize) + Vector3.One * 0.5f;

		var shader = impactData.weaponType == WeaponType.Spray ? getSprayedShader : getHitShader;

		var impactRadius = impactData.weaponType == WeaponType.Spray ? 0.15f : 0.1f;
		var impactStrength = impactData.weaponType == WeaponType.Spray ? 0.2f : 0.4f;

		// Set common properties
		shader.Attributes.Set( "DataTexture", RustData );
		shader.Attributes.Set( "ImpactPosition", texPos );
		shader.Attributes.Set( "ImpactRadius", impactRadius );
		shader.Attributes.Set( "ImpactStrength", impactStrength );

		if ( impactData.weaponType == WeaponType.Gun )
		{
			// Those probably could be weapon or ammo properties
			const float coneAngleDeg = 20;
			const float coneAngleRadians = coneAngleDeg * MathF.PI / 180.0f;
			const float maxPenOs = 2f; // fully through the object and then some more

			shader.Attributes.Set( "ImpactDirection", impactDirOs );
			shader.Attributes.Set( "ConeAngleRad", coneAngleRadians );
			shader.Attributes.Set( "MaxPenetration", maxPenOs );
		}

		shader.Dispatch( TextureSize, TextureSize, TextureSize );
	}
}