using System;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Rendering;
using Sandbox.UI;
using System.Linq;
using System.Diagnostics;

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

	private ModelRenderer modelRenderer;
	private MeshDensifier meshDensifier;

	private Texture RustData { get; set; }
	private Texture RustDataReadBuffer { get; set; }

	private ComputeShader clone3dTexShader;
	private ComputeShader getSprayedShader;
	private ComputeShader getHitShader;
	private ComputeShader simulationShader;
	private ComputeShader meshErosionShader;
	private Vector3 meshCenter;

	[Property]
	public float ErosionStrength = 0.3f;

	private SurfaceImpactHandler impactHandler;

	private const int TextureSize = 64;

	private Atmosphere atmosphere;
	private RustSystem rustSystem;

	private ImpactData storedImpactData;

	private bool applyErosionRequested = false;

	// TODO: Coordinate spread across all rustable objects to avoid frame spikes
	[Property]
	public int SimulationFrameInterval = 15;

	[Property]
	public int ErosionFrameInterval = 60;

	private Material rustableDebugMaterial;
	private Material rustableProperMaterial;
	private Vertex[] vertices;
	private ushort[] indices;

	private GpuBuffer<VertexData> inputBuffer;
	private GpuBuffer<VertexData> outputBuffer;

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

	protected override void OnAwake()
	{
		base.OnAwake();

		modelRenderer = GetComponent<ModelRenderer>();
		meshDensifier = GameObject.GetOrAddComponent<MeshDensifier>();
	}

	protected override void OnStart()
	{
		base.OnStart();
		
		// We need a mesh with vertex density of certain threshold to avoid artifacts when displacing vertices due to rust progress
		DensifyObjectMesh();

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
			meshCenter = objectBounds.Center;

			// Initialize the GPU buffers
			inputBuffer = new GpuBuffer<VertexData>( vertices.Length, GpuBuffer.UsageFlags.Structured );
			outputBuffer = new GpuBuffer<VertexData>( vertices.Length, GpuBuffer.UsageFlags.Structured );
		}
		else
		{
			Log.Error( $"Model {modelRenderer.Model.Name} not found. Will not render." );
		}

		getSprayedShader = new ComputeShader( "shaders/getsprayed" );
		getHitShader = new ComputeShader( "shaders/gethit" );
		simulationShader = new ComputeShader( "shaders/rust_simulation" );
		clone3dTexShader = new ComputeShader( "shaders/clone3dtex" );
		meshErosionShader = new ComputeShader( "shaders/mesh_erosion" );

		impactHandler = GameObject.GetOrAddComponent<SurfaceImpactHandler>();
		impactHandler.OnImpact += StoreImpact;
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		impactHandler.OnImpact -= StoreImpact;
		
		inputBuffer?.Dispose();
		outputBuffer?.Dispose();
	}


	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( simTicks++ % ErosionFrameInterval == 0 )
		{
			ApplyErosion();
		}

		if ( applyErosionRequested )
		{
			ApplyErosion();
			applyErosionRequested = false;
		}
	}

	protected override void OnPreRender()
	{
		base.OnPreRender();
		sceneCustomObject.Transform = Transform.World;
	}

	private void DensifyObjectMesh()
	{
		// TODO: Make it depend on rust data resolution and object size (or mod Densify to work in world space)
		// No more than 10 in case other conditions are invalid
		for ( var densificationTurn = 0; densificationTurn < 10; densificationTurn++ )
		{
			var result = meshDensifier.Densify( 5f );

			if ( result.newTriangleCount > 100_000 )
			{
				// that's too many triangles
				// Log.Warning("Bailing out of densification due to too many triangles");
				break;
			}

			if ( result.maxRemainingEdgeLength < 6f )
			{
				// that's good enough
				// Log.Info("Bailing out of densification due to max edge length");
				break;
			}

			if ( result.avgRemainingEdgeLength < 3f )
			{
				// that's good enough, mostly...
				// Log.Info("Bailing out of densification due to avg edge length");
				break;
			}
		}
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

			// For some reason Graphics.CopyTexture( RustData, RustDataReadBuffer ); does NOT work
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

	[Button( "Update erosion" )]
	public void StoreErosionRequest()
	{
		applyErosionRequested = true;
	}

	public void ApplyErosion()
	{
		var sw = Stopwatch.StartNew();

		var oldVertices = modelRenderer.Model.GetVertices().ToArray();
		var oldIndices = modelRenderer.Model.GetIndices().Select(i => (ushort)i).ToArray();

		// Use the existing buffers instead of creating new ones
		inputBuffer.SetData<VertexData>(oldVertices.Select(v => new VertexData(v)).ToArray());
		meshErosionShader.Attributes.Set("InputVertices", inputBuffer);
		meshErosionShader.Attributes.Set("OutputVertices", outputBuffer);
		meshErosionShader.Attributes.Set("RustData", RustData);
		meshErosionShader.Attributes.Set("MeshCenter", meshCenter);
		meshErosionShader.Attributes.Set("BoundsMin", boundsMin);
		meshErosionShader.Attributes.Set("BoundsScale", boundsScale);
		meshErosionShader.Attributes.Set("ErosionStrength", ErosionStrength);
		meshErosionShader.Attributes.Set("VertexCount", oldVertices.Length);

		meshErosionShader.Dispatch(oldVertices.Length, 1, 1);

		// Get results and update mesh
		var newVertices = new VertexData[oldVertices.Length];
		outputBuffer.GetData<VertexData>(newVertices);

		// TODO: Refactor too many allocations
		ReplaceMeshVertices(oldVertices.ToList(), oldIndices.ToList(), newVertices.Select(v => v.ToVector3()).ToList());

		Log.Info($"Erosion took {sw.ElapsedMilliseconds}ms, num vertices: {newVertices.Length}, old vertices: {oldVertices.Length}");
	}

	private void ReplaceMeshVertices( List<Vertex> oldVertices, List<ushort> oldIndices, List<Vector3> newVertices )
	{
		var bounds = new BBox();
		var vb = new VertexBuffer();
		vb.Init( true );

		// Update proxy mesh, too
		vertices = new Vertex[newVertices.Count];
		for ( int i = 0; i < newVertices.Count; i++ )
		{
			var oldVertex = oldVertices[i];
			var newVertex = newVertices[i];
			vb.Add( oldVertex with { Position = newVertex } );
			bounds.AddPoint( newVertex );
			vertices[i] = new Vertex( newVertex );
		}

		// Reuse original indices
		foreach ( var index in oldIndices )
		{
			vb.AddRawIndex( index );
		}

		var mesh = new Mesh();
		mesh.CreateBuffers( vb, false );

		mesh.Material = modelRenderer.Model.Materials.First();
		mesh.Bounds = bounds;

		// Create hull from updated vertices
		var hull = new MeshHull( newVertices );

		// Create new model with updated mesh but same collision data
		var newModel = Model.Builder
			.AddMesh( mesh )
			.AddCollisionHull( hull.Vertices )
			.Create();

		// Update the model renderer
		modelRenderer.Model = newModel;

		// Update the model collider if present
		var modelCollider = GetComponent<ModelCollider>();
		if ( modelCollider != null )
		{
			modelCollider.Model = newModel;
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

		var impactData = storedImpactData;
		storedImpactData = null;

		var positionOs = Transform.World.PointToLocal( impactData.Position );
		var impactDirOs = Transform.World.NormalToLocal( impactData.ImpactDirection ).Normal;

		// Convert to 0-1 space for texture sampling
		var texPos = (positionOs - boundsMin) * boundsScale;
		var shader = impactData.WeaponType == WeaponType.Spray ? getSprayedShader : getHitShader;

		// Set common properties
		shader.Attributes.Set( "DataTexture", RustData );
		shader.Attributes.Set( "ImpactPosition", texPos );
		shader.Attributes.Set( "ImpactRadius", impactData.ImpactRadius );
		shader.Attributes.Set( "ImpactStrength", impactData.ImpactStrength );
		shader.Attributes.Set( "ImpactDirection", impactDirOs );
		shader.Attributes.Set( "ConeAngleRad", impactData.ImpactPenetrationConeDeg * MathF.PI / 180.0f );
		shader.Attributes.Set( "MaxPenetration", impactData.ImpactPenetrationStrength );

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
