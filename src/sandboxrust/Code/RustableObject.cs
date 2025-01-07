using System;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Rendering;
using Sandbox.UI;
using System.Linq;

/// <summary>
/// Component for objects that can have rust applied to them.
/// </summary>
public sealed class RustableObject : Component
{
	private BBox objectBounds;
	private Vector3 boundsSize;
	private Vector3 boundsMin;
	private Vector3 boundsScale;

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

	private SurfaceImpactHandler impactHandler;

	private const int TextureSize = 64;

	private Atmosphere atmosphere;
	private RustSystem rustSystem;

	private ImpactData? storedImpactData;

	// TODO: Coordinate spread across all rustable objects to avoid frame spikes
	[Property]
	public int SimulationFrameInterval = 15;

	private Material rustableDebugMaterial;
	private Material rustableProperMaterial;
	private Vertex[] vertices;
	private ushort[] indices;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		// Using SceneCustomObject with custom render hook
		// seems to be correct way to access CopyResource
		sceneCustomObject = new SceneCustomObject( Scene.SceneWorld );
		sceneCustomObject.RenderOverride = RunSimulation;

		atmosphere = GameObject.GetComponentInParent<Atmosphere>();
		rustSystem = GameObject.GetComponentInParent<RustSystem>();

		if ( atmosphere == null )
		{
			Log.Error( $"Atmosphere component not found in {GameObject.Name} parent hierarchy. Will use defaults." );
		}

		if ( RustData != null )
		{
			Log.Error( "Re-creating texture before previous was disposed, why?" );
		}

		RustData = CreateVolumeTexture( TextureSize );
		RustDataReadBuffer = CreateVolumeTexture( TextureSize );
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

		RustData.Dispose();
		RustDataReadBuffer.Dispose();
		RustData = null;
		RustDataReadBuffer = null;
	}

	protected override void OnStart()
	{
		base.OnStart();




		modelRenderer = GetComponent<ModelRenderer>();

		// Create per-instance material - TODO: maybe it loads already as an instance or is it shared?
		rustableDebugMaterial = Material.Load( "materials/rustable_debug.vmat" ).CreateCopy();
		rustableProperMaterial = Material.Load( "materials/rustable_proper.vmat" ).CreateCopy();
		rustableDebugMaterial.Set( "RustDataRead", RustData );
		rustableProperMaterial.Set( "RustDataRead", RustData );

		// Cache the model's mesh for overlay rendering
		if ( modelRenderer.Model != null )
		{
			// Store aabb so we can map the volume properly for non-unit-sized objects
			objectBounds = modelRenderer.Model.Bounds;
			boundsSize = objectBounds.Size;
			boundsMin = objectBounds.Mins;
			boundsScale = Vector3.One / boundsSize;
			// Log.Info( $"Object {GameObject.Name} BoundsMin: {boundsMin}, BoundsSize: {boundsSize}, BoundsScale: {boundsScale}" );


			// Why does GetIndices return uint[] but Graphics.Draw expects ushort[]?
			vertices = modelRenderer.Model.GetVertices().ToArray();

			// Unknown, let's just check if a model will be too big
			if ( vertices.Length > ushort.MaxValue )
			{
				throw new Exception( $"Model {modelRenderer.Model.Name} has more than {ushort.MaxValue} vertices. This is not supported." );
			}

			indices = modelRenderer.Model.GetIndices().Select( i => (ushort)i ).ToArray();
		}
		else
		{
			Log.Error( $"Model {modelRenderer.Model.Name} not found. Will not render." );
		}

		getSprayedShader = new ComputeShader( "shaders/getsprayed" );
		getHitShader = new ComputeShader( "shaders/gethit" );
		simulationShader = new ComputeShader( "shaders/rust_simulation" );
		clone3dTexShader = new ComputeShader( "shaders/clone3dtex" );

		impactHandler = GameObject.GetOrAddComponent<SurfaceImpactHandler>();
		impactHandler.OnImpact += StoreImpact;
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		impactHandler.OnImpact -= StoreImpact;
	}



	protected override void OnUpdate()
	{
		base.OnUpdate();
	}

	protected override void OnPreRender()
	{
		base.OnPreRender();
		sceneCustomObject.Transform = Transform.World;
	}

	private void RunSimulation( SceneObject o )
	{

		// Optimization opportunities:
		// - use ping-pong swap to avoid resource barrier mess (do I even need it? better safe than crash)
		// - generate mipmaps and sample from lower-resolution resource to reduce total computation with some nice downsampling filter
		Matrix worldToObject = Matrix.CreateRotation( Transform.World.Rotation.Inverse ) * Matrix.CreateTranslation( -Transform.World.Position );

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
			simulationShader.Attributes.Set( "WorldToObject", worldToObject );

			if ( atmosphere != null )
			{
				simulationShader.Attributes.Set( "OxygenLevel", atmosphere.OxygenLevel );
				simulationShader.Attributes.Set( "WaterVapor", atmosphere.WaterVapor );
			}
			else
			{
				// Use default values if no Atmosphere component is present
				simulationShader.Attributes.Set( "OxygenLevel", 0.2f );
				simulationShader.Attributes.Set( "WaterVapor", 0.5f );
			}

			simulationShader.Dispatch( TextureSize, TextureSize, TextureSize );
		}

		// After simulation, render the rust overlay
		if ( vertices != null )
		{
			var attributes = new RenderAttributes();

			// Do I set these in the material or in the RenderAttributes?
			attributes.Set( "RustDataRead", RustData );
			attributes.Set( "BoundsScale", boundsScale );
			attributes.Set( "BoundsMin", boundsMin );
			attributes.Set( "FlashlightPosition", rustSystem.Flashlight.Transform.World.Position );
			attributes.Set( "FlashlightDirection", rustSystem.Flashlight.Transform.World.Rotation.Forward );
			attributes.Set( "FlashlightIntensity", rustSystem.Flashlight.IsEnabled ? 1.0f : 0.0f );
			attributes.Set( "FlashlightAngle", rustSystem.Flashlight.Angle );

			var mode = rustSystem.RenderingMode;
			sceneCustomObject.RenderLayer = SceneRenderLayer.OverlayWithDepth;

			Graphics.Draw(
				vertices,
				vertices.Length,
				indices,
				indices.Length,
				mode == RustRenderingMode.Debug ? rustableDebugMaterial : rustableProperMaterial,
				attributes,
				Graphics.PrimitiveType.Triangles
			);
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
		var texPos = (positionOs - boundsMin) * boundsScale;
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
}
