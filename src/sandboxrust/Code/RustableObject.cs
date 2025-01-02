using System;
using Sandbox;

/// <summary>
/// Component for objects that can have rust applied to them.
/// </summary>
public sealed class RustableObject : Component
{

	ModelRenderer modelRenderer;

	[Property]
	public Texture RustData { get; set; }

	protected override void OnStart()
	{
		base.OnStart();
		RustData = CreateDebugTexture( 1024, 1024 );

		modelRenderer = GetComponent<ModelRenderer>();

		// Create per-instance material?
		var instanceMaterial = modelRenderer.GetMaterial( 0 ).CreateCopy();
		instanceMaterial.Attributes.Set( "RustData", RustData );
		modelRenderer.SetMaterial( instanceMaterial );		
	}

	/// <summary>
	/// Create a texture that will hold rusting data.
	/// </summary>
	private Texture CreateDebugTexture( int width, int height )
	{
		// Random garbage to check if it's even getting to the shader
		var data = new byte[width * height * 3];
		const int checkerboardSizeR = 32;
		const int checkerboardSizeG = 16;
		const int checkerboardSizeB = 8;
		
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

		return new Texture2DBuilder()
			.WithSize( width, height )
			.WithFormat( ImageFormat.RGB888 )
			.WithData( data )
			.Finish();
	}
}