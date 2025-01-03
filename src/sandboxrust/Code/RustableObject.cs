using System;
using Sandbox;
using Sandbox.Diagnostics;

/// <summary>
/// Component for objects that can have rust applied to them.
/// </summary>
public sealed class RustableObject : Component
{

	ModelRenderer modelRenderer;

	/// <summary>
	/// Texture that holds all the data related to rusting in three channels.
	/// R represents rusting factor - how much rust is on surface in this spot.
	/// G represents moisture factor - how wet the surface is in this spot.
	/// B represents structural integrity - starts at 1, decreases when rusty surface is hit by some projectile.
	/// </summary>
	public Texture RustData { get; set; }

	/// <summary>
	/// If set to true, the object will move around a bit to verify coord mappings. Don't use with actual physics.
	/// </summary>
	[Property]
	public bool TestWiggle { get; set; }

	private SurfaceImpactHandler impactHandler;

	protected override void OnStart()
	{
		base.OnStart();
		RustData = CreateDebugTexture( 1024, 1024 );

		modelRenderer = GetComponent<ModelRenderer>();

		// Create per-instance material - TODO: does it load already as an instance or is it shared?
		var instanceMaterial = Material.Load( "materials/rustable_untextured.vmat" ).CreateCopy();
		modelRenderer.MaterialOverride = instanceMaterial;
		instanceMaterial.Set( "RustData", RustData );

		impactHandler = GameObject.GetOrAddComponent<SurfaceImpactHandler>();
	}

	/// <summary>
	/// Create a texture that will hold rusting data.
	/// </summary>
	private Texture CreateDebugTexture( int width, int height )
	{
		// Random garbage to check if it's even getting to the shader
		var data = new byte[width * height * 3];
		const int globalMultiplier = 4;
		const int checkerboardSizeR = 32 * globalMultiplier;
		const int checkerboardSizeG = 16 * globalMultiplier;
		const int checkerboardSizeB = 8 * globalMultiplier;
		
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				
				int index = (y * width + x) * 3;

				// Red channel: checkerboard size of 32
				bool darkOrLightR = ((x / checkerboardSizeR) + (y / checkerboardSizeR)) % 2 == 0;
				data[index] = darkOrLightR ? (byte)100 : (byte)200;

				// Green channel: checkerboard size of 16
				bool darkOrLightG = ((x / checkerboardSizeG) + (y / checkerboardSizeG)) % 2 == 0;
				data[index + 1] = darkOrLightG ? (byte)100 : (byte)200;

				// Blue channel: checkerboard size of 8
				bool darkOrLightB = ((x / checkerboardSizeB) + (y / checkerboardSizeB)) % 2 == 0;
				data[index + 2] = darkOrLightB ? (byte)100 : (byte)200;

				
			}
		}

		return Texture.Create(width, height, ImageFormat.RGB888 )
			.WithDynamicUsage()
			.WithData( data, data.Length )
			.Finish();
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
}