using System;
using Sandbox;
using Sandbox.Diagnostics;

/// <summary>
/// Component for objects that can have rust applied to them.
/// </summary>
public sealed class RustableObject : Component
{
	public const float MaxSize = 100.0f;

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
		impactHandler.OnImpact += ProcessImpact;
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

		const int dark = 10;
		const int light = 60;
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{

				int index = (y * width + x) * 3;

				// Red channel: checkerboard size of 32
				bool darkOrLightR = ((x / checkerboardSizeR) + (y / checkerboardSizeR)) % 2 == 0;
				data[index] = darkOrLightR ? (byte)dark : (byte)light;

				// Green channel: checkerboard size of 16
				bool darkOrLightG = ((x / checkerboardSizeG) + (y / checkerboardSizeG)) % 2 == 0;
				data[index + 1] = darkOrLightG ? (byte)dark : (byte)light;

				// Blue channel: checkerboard size of 8
				bool darkOrLightB = ((x / checkerboardSizeB) + (y / checkerboardSizeB)) % 2 == 0;
				data[index + 2] = darkOrLightB ? (byte)dark : (byte)light;


			}
		}

		return Texture.Create( width, height, ImageFormat.RGB888 )
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

	private void ProcessImpact( Vector3 positionWs, Vector3 normalWs, WeaponType weaponType )
	{
		var positionOs = Transform.World.PointToLocal( positionWs );
		var normalOs = Transform.World.NormalToLocal( normalWs );

		if ( weaponType == WeaponType.Spray )
		{
			SprayWater( positionOs, normalOs, 1f );
		}
	}

	/// <summary>
	/// Writes a value to texture data using triplanar projection
	/// </summary>
	private void WriteTriplanarValue( Span<Color3> textureData, int width, int height,
		Vector3 objectPos, Vector3 objectNormal, Color3 valueToBlend )
	{
		// Get the triplanar mapping in the same fashion as we do it in the rustable.shader
		var (yzCoord, xzCoord, xyCoord, weights) = GetTriplanarMapping( objectPos, objectNormal );

		// Convert UV to pixel coordinates for each projection
		var yzPixel = new Vector2(
			(int)(yzCoord.x * width).Clamp( 0, width - 1 ),
			(int)(yzCoord.y * height).Clamp( 0, height - 1 )
		);
		var xzPixel = new Vector2(
			(int)(xzCoord.x * width).Clamp( 0, width - 1 ),
			(int)(xzCoord.y * height).Clamp( 0, height - 1 )
		);
		var xyPixel = new Vector2(
			(int)(xyCoord.x * width).Clamp( 0, width - 1 ),
			(int)(xyCoord.y * height).Clamp( 0, height - 1 )
		);

		// Write weighted values to each projection
		if ( weights.x > 0.01f )
		{
			int yzIndex = (int)(yzPixel.y * width + yzPixel.x);
			BlendColorAtIndex( textureData, yzIndex, valueToBlend, weights.x );
		}
		if ( weights.y > 0.01f )
		{
			int xzIndex = (int)(xzPixel.y * width + xzPixel.x);
			BlendColorAtIndex( textureData, xzIndex, valueToBlend, weights.y );
		}
		if ( weights.z > 0.01f )
		{
			int xyIndex = (int)(xyPixel.y * width + xyPixel.x);
			BlendColorAtIndex( textureData, xyIndex, valueToBlend, weights.z );
		}
	}

	/// <summary>
	/// Blends a color into the texture at a specific index.
	/// </summary>
	private void BlendColorAtIndex( Span<Color3> textureData, int index, Color3 valueToBlend, float weight )
	{
		var current = textureData[index];
		textureData[index] = new Color3(
			(byte)((current.r + valueToBlend.r * weight) / 2),
			(byte)((current.g + valueToBlend.g * weight) / 2),
			(byte)((current.b + valueToBlend.b * weight) / 2)
		);
	}

	private void SprayWater( Vector3 impactOs, Vector3 impactNormalOs, float sprayRadius )
	{
		// TODO: Terribly inefficient - this should be done in a compute shader
		// but let's keep it here for now so we can debug and see if this idea has potential

		// First, copy the entire texture to a local buffer
		Span<Color3> data = new Color3[RustData.Width * RustData.Height];
		var srcRect = (0, 0, RustData.Width, RustData.Height);
		RustData.GetPixels( srcRect, 0, 0, data, ImageFormat.RGB888 );

		// Then, execute the spray logic - for now only a boundaries of the spray radius
		// to see if we are even hitting the correct texels
		// TODO: Fill in the circle or better yet, do something smarter in the shader
		const int samples = 64;
		for ( int n = 0; n < samples; n++ )
		{
			// Map circle to offsets
			float angle = 2f * MathF.PI * n / samples;
			float xOff = MathF.Cos( angle ) * sprayRadius;
			float yOff = MathF.Sin( angle ) * sprayRadius;
			var offset = new Vector3( xOff, 0, yOff );

			// Write to the texture
			WriteTriplanarValue( data, RustData.Width, RustData.Height,
				impactOs + offset,
				impactNormalOs,
				new Color3( 0, 255, 0 )
			);
		}

		RustData.Update<Color3>( data );
	}

	/// <summary>
	/// Converts object-space position and normal to triplanar texture coordinates and blend weights in the same way as the shader
	/// uses them.
	/// </summary>
	private static (Vector2 YzCoord, Vector2 XzCoord, Vector2 XyCoord, Vector3 BlendWeights) GetTriplanarMapping( Vector3 objectPos, Vector3 objectNormal )
	{
		// Normalize position same as shader
		var uvw = objectPos / MaxSize + new Vector3( 0.5f );

		// Calculate blend weights matching shader exactly
		var blendWeights = new Vector3(
			MathF.Abs( objectNormal.x ),
			MathF.Abs( objectNormal.y ),
			MathF.Abs( objectNormal.z )
		);

		// Square the weights
		blendWeights *= blendWeights;

		// Normalize weights with small extra to avoid div/0 in some edge cases (literally...)
		float sum = blendWeights.x + blendWeights.y + blendWeights.z + 1e-5f;
		blendWeights /= sum;

		return (
			new Vector2( uvw.y, uvw.z ),  // YZ plane
			new Vector2( uvw.x, uvw.z ),  // XZ plane
			new Vector2( uvw.x, uvw.y ),  // XY plane
			blendWeights
		);
	}
}