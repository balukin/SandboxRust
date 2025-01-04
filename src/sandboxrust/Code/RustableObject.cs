using System;
using Sandbox;
using Sandbox.Diagnostics;

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

	ModelRenderer modelRenderer;

	private Texture RustData { get; set; }
	private Texture RustDataSimBuffer { get; set; }

	private ComputeShader getSprayedShader;
	private ComputeShader getHitShader;
	private ComputeShader simulationShader;

	/// <summary>
	/// If set to true, the object will move around a bit to verify coord mappings. Don't use with actual physics.
	/// </summary>
	[Property]
	public bool TestWiggle { get; set; }

	private Material instanceMaterial;

	private SurfaceImpactHandler impactHandler;

	private const int TextureSize = 64;
	protected override void OnStart()
	{
		base.OnStart();

		// Create two 3D textures for ping-pong simulation
		RustData = CreateVolumeTexture( 64 );
		RustDataSimBuffer = CreateVolumeTexture( 64 );

		modelRenderer = GetComponent<ModelRenderer>();

		// Create per-instance material - TODO: does it load already as an instance or is it shared?
		instanceMaterial = Material.Load( "materials/rustable_untextured.vmat" ).CreateCopy();
		modelRenderer.MaterialOverride = instanceMaterial;
		instanceMaterial.Set( "RustDataRead", RustData );

		getSprayedShader = new ComputeShader( "shaders/getsprayed" );
		getHitShader = new ComputeShader( "shaders/gethit" );
		simulationShader = new ComputeShader( "shaders/rust_simulation" );

		impactHandler = GameObject.GetOrAddComponent<SurfaceImpactHandler>();
		impactHandler.OnImpact += ProcessImpact;
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

		if ( TestWiggle )
		{
			Transform.Local = Transform.Local
				.WithRotation( Transform.Local.Rotation * Rotation.FromAxis( Vector3.Up, Time.Now / 15f ) );
		}


	}

	private void RunSimulation()
	{
		// TODO: fix ping-pong swaps
		// Run simulation step
		// simulationShader.Attributes.Set("Source", usingTextureA ? RustDataA : RustDataB);
		// simulationShader.Attributes.Set("Target", usingTextureA ? RustDataB : RustDataA);

		// simulationShader.Dispatch(TextureSize, TextureSize, TextureSize);

		// Swap textures
		// readingTextureA = !readingTextureA;
	}

	private void ProcessImpact( Vector3 positionWs, Vector3 normalWs, WeaponType weaponType )
	{
		var positionOs = Transform.World.PointToLocal( positionWs );
		var normalOs = Transform.World.NormalToLocal( normalWs ).Normal;

		// Convert to 0-1 space for texture sampling
		var texPos = (positionOs / MaxSize) + Vector3.One * 0.5f;

		var shader = weaponType == WeaponType.Spray ? getSprayedShader : getHitShader;

		var impactRadius = weaponType == WeaponType.Spray ? 0.3f : 0.1f;
		var impactStrength = weaponType == WeaponType.Spray ? 0.2f : 0.4f;

		// Set common properties
		shader.Attributes.Set( "DataTexture", RustData );
		shader.Attributes.Set( "ImpactPosition", texPos );
		shader.Attributes.Set( "ImpactRadius", impactRadius );
		shader.Attributes.Set( "ImpactStrength", impactStrength );

		if ( weaponType == WeaponType.Gun )
		{
			// Those probably could be weapon or ammo properties
			const float coneAngleDeg = 20;
			const float coneAngleRadians = coneAngleDeg * MathF.PI / 180.0f;	
			const float maxPenOs = 2f; // fully through the object and then some more

			shader.Attributes.Set( "ImpactDirection", -normalOs );
			shader.Attributes.Set( "ConeAngleRad", coneAngleRadians );
			shader.Attributes.Set( "MaxPenetration", maxPenOs );
		}

		shader.Dispatch( TextureSize, TextureSize, TextureSize );
	}
}