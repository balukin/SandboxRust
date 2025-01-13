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

	private Atmosphere atmosphere;
	private RustSystem rustSystem;

	private ImpactData storedImpactData;

	private Material rustableDebugMaterial;
	private Material rustableProperMaterial;
	private GpuBuffer<Vertex> proxyVertices;

	/// <summary>
	/// Unique vertex count in the model. NOT equal to proxy vertices count (those may contain repeated vertices).
	/// Used for erosion simulation.
	/// </summary>
	private int uniqueVertexCount;

	/// <summary>
	/// Vertex count in the proxy mesh. Not used anywhere but here just to remember that uniqueVertexCount is something different.
	/// </summary>
	private int proxyVertexCount;

	private GpuBuffer<VertexData> erosionInputBuffer;
	private GpuBuffer<VertexData> erosionOutputBuffer;

	private QualitySystem qualitySystem;

	private int currentVolumeResolution;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		sceneCustomObject = new SceneCustomObject( Scene.SceneWorld ) { RenderOverride = RenderHook };

		atmosphere = GameObject.GetComponentInParent<Atmosphere>();
		rustSystem = GameObject.GetComponentInParent<RustSystem>();
		qualitySystem = Scene.GetSystem<QualitySystem>();

		if ( atmosphere == null )
		{
			Log.Error( $"Atmosphere component not found in {GameObject.Name} parent hierarchy. Will use defaults." );
		}

		if ( qualitySystem == null )
		{
			Log.Error( $"QualitySystem component not found in {GameObject.Name} parent hierarchy. Will use defaults." );
		}

		EnsureResourceResolutionIsValid();

		impactHandler = GameObject.GetOrAddComponent<SurfaceImpactHandler>();
		impactHandler.OnImpact += StoreImpact;
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		sceneCustomObject.RenderOverride = null;
		impactHandler.OnImpact -= StoreImpact;

		if ( sceneCustomObject.IsValid() )
		{
			sceneCustomObject.Delete();
		}

		sceneCustomObject = null;

		proxyVertices?.Dispose();
		proxyVertices = null;
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

		rustSystem?.RegisterRustableObject( this );

		// We need a mesh with vertex density of certain threshold to avoid artifacts when displacing vertices due to rust progress
		DensifyObjectMesh();

		ProcessMeshVertices( modelRenderer.Model );

		// Create per-instance material - TODO: maybe it loads already as an instance or is it shared?
		rustableDebugMaterial = Material.Load( "materials/rustable_debug.vmat" ).CreateCopy();
		rustableProperMaterial = Material.Load( "materials/rustable_proper.vmat" ).CreateCopy();

		// Cache the model's mesh for overlay rendering
		if ( modelRenderer.Model != null )
		{
			// Store aabb so we can map the volume properly for non-unit-sized objects
			objectBounds = modelRenderer.Model.Bounds;
			boundsSize = objectBounds.Size;
			boundsMin = objectBounds.Mins;
			boundsScale = Vector3.One / boundsSize;
			// Log.Info( $"Object {GameObject.Name} BoundsMin: {boundsMin}, BoundsSize: {boundsSize}, BoundsScale: {boundsScale}" );

			meshCenter = objectBounds.Center;

			// Initialize the GPU buffers
			erosionInputBuffer = new GpuBuffer<VertexData>( uniqueVertexCount, GpuBuffer.UsageFlags.Structured );
			erosionOutputBuffer = new GpuBuffer<VertexData>( uniqueVertexCount, GpuBuffer.UsageFlags.Structured );
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
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		rustSystem?.UnregisterRustableObject( this );
		erosionInputBuffer?.Dispose();
		erosionOutputBuffer?.Dispose();
	}

	protected override void OnPreRender()
	{
		base.OnPreRender();
		sceneCustomObject.Transform = Transform.World;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		EnsureResourceResolutionIsValid();

		if ( rustSystem.ShouldRunErosion( this ) )
		{
			// This needs to happen in Update because mesh flickering happens otherwise
			// TODO: Figure out pipeline order of S&Box - when do custom objects get their hook called, etc.
			RunErosionSimulation();
		}
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

	/// <summary>
	/// Creates a proxy mesh with all sequentially laid triangle vertices.
	/// </summary>
	/// <param name="model">Model to create proxy mesh for.</param>
	/// <remarks>
	/// We need to re-format vertex data to always have 3 vertices per triangle (with no vertex sharing) because Graphics.Draw has only
	/// an overload taking GpuBuffer for VertexBuffer but no overload taking GpuBuffer for IndexBuffer
	/// Previously I tried simply using Graphics.Draw(vertices+len, indices+len, material, attributes, primitive) overload but it seems to cause a memory leak of some sort
	/// Therefore we go with GpuBuffer`Vertex and create a new buffer with potentially repeated vertices sequentially for each triangle.
	/// To be fair, it's also possible that it's not a leak and maybe it's some internal caching with LRU eviction once VRAM pressure is high
	/// but I don't want to risk OOMs. Maybe later (TODO) I'll investigate with RenderDoc or look how to enable leak detection in debug layers or something.
	/// </remarks>
	private void ProcessMeshVertices( Model model )
	{
		var vertices = model.GetVertices().ToList();
		var indices = model.GetIndices().ToList();

		uniqueVertexCount = vertices.Count;
		proxyVertexCount = indices.Count;

		proxyVertices?.Dispose();
		proxyVertices = new GpuBuffer<Vertex>( indices.Count, GpuBuffer.UsageFlags.Vertex );

		var vertexData = new List<Vertex>();
		for ( int i = 0; i < indices.Count; i += 3 )
		{
			vertexData.Add( vertices[(int)indices[i]] );
			vertexData.Add( vertices[(int)indices[i + 1]] );
			vertexData.Add( vertices[(int)indices[i + 2]] );
		}

		proxyVertices.SetData( vertexData.ToArray() );

		Log.Info( $"Created proxy mesh with {proxyVertices.ElementCount} vertices, valid: {proxyVertices.IsValid}" );
	}

	private void EnsureResourceResolutionIsValid()
	{
		if ( currentVolumeResolution != qualitySystem.VolumeResolution )
		{
			Log.Info( $"RustableObject {GameObject.Name} resolution changed from {currentVolumeResolution} to {qualitySystem.VolumeResolution}. Recreating resources." );
			RustData?.Dispose();
			RustDataReadBuffer?.Dispose();
			RustData = CreateVolumeTexture( qualitySystem.VolumeResolution );
			RustDataReadBuffer = CreateVolumeTexture( qualitySystem.VolumeResolution );

			// Update material bindings
			if ( rustableDebugMaterial != null )
			{
				rustableDebugMaterial.Set( "RustDataRead", RustData );
			}

			if ( rustableProperMaterial != null )
			{
				rustableProperMaterial.Set( "RustDataRead", RustData );
			}

			currentVolumeResolution = qualitySystem.VolumeResolution;
		}
	}

	public void RenderHook( SceneObject o )
	{
		if ( rustSystem.ShouldRunSimulation( this ) )
		{
			RunRustSimulation();
		}

		RunImpactSimulation();
		RenderOverlayRust();
	}

	public void RunErosionSimulation()
	{
		if ( currentVolumeResolution == 0 )
		{
			// Skip, resources were not created yet
			return;
		}

		var sw = Stopwatch.StartNew();

		var oldVertices = modelRenderer.Model.GetVertices().ToArray();
		var oldIndices = modelRenderer.Model.GetIndices().Select( i => (ushort)i ).ToArray();

		// TODO: We can probably use only one RW buffer, each thread operates on its own [vertex] in isolation
		erosionInputBuffer.SetData<VertexData>( oldVertices.Select( v => new VertexData( v ) ).ToArray() );
		meshErosionShader.Attributes.Set( "InputVertices", erosionInputBuffer );
		meshErosionShader.Attributes.Set( "OutputVertices", erosionOutputBuffer );
		meshErosionShader.Attributes.Set( "RustData", RustData );
		meshErosionShader.Attributes.Set( "MeshCenter", meshCenter );
		meshErosionShader.Attributes.Set( "BoundsMin", boundsMin );
		meshErosionShader.Attributes.Set( "BoundsScale", boundsScale );
		meshErosionShader.Attributes.Set( "ErosionStrength", ErosionStrength );
		meshErosionShader.Attributes.Set( "VertexCount", oldVertices.Length );

		meshErosionShader.Dispatch( oldVertices.Length, 1, 1 );

		// Get results and update mesh
		var newVertices = new VertexData[oldVertices.Length];
		erosionOutputBuffer.GetData<VertexData>( newVertices );

		// TODO: Refactor too many allocations with all the ToLists - pass spans or something
		ReplaceMeshVertices( oldVertices.ToList(), oldIndices.ToList(), newVertices.Select( v => v.ToVector3() ).ToList() );

		Log.Trace( $"Erosion took {sw.ElapsedMilliseconds}ms, num vertices: {newVertices.Length}, old vertices: {oldVertices.Length}" );
	}

	private void RunRustSimulation()
	{
		if ( currentVolumeResolution == 0 )
		{
			// Skip, resources were not created yet
			return;
		}
		// Optimization opportunities in the rust simulation:
		// - use ping-pong swap to avoid resource barrier mess (do I even need them? better safe than crash)
		// - generate mipmaps and sample from lower-resolution resource to reduce total computation with some nice downsampling filter
		// - design smarter algorithm that doesn't have r/w hazards (possible?)

		Matrix worldToObject = Matrix.CreateRotation( Transform.World.Rotation.Inverse ) * Matrix.CreateTranslation( -Transform.World.Position );

		// Copy the data to the read-only buffer to avoid race condition on R/W in the same resource

		// For some reason Graphics.CopyTexture( RustData, RustDataReadBuffer ); does NOT work
		// Only one slice of 3D texture was being copied and changing slice indices had no effect
		// We're gonna do it the crude way
		Graphics.ResourceBarrierTransition( RustData, ResourceState.CopySource );
		Graphics.ResourceBarrierTransition( RustDataReadBuffer, ResourceState.CopyDestination );
		clone3dTexShader.Attributes.Set( "SourceTexture", RustData );
		clone3dTexShader.Attributes.Set( "TargetTexture", RustDataReadBuffer );
		clone3dTexShader.Dispatch( currentVolumeResolution, currentVolumeResolution, currentVolumeResolution );

		Graphics.ResourceBarrierTransition( RustDataReadBuffer, ResourceState.UnorderedAccess );
		Graphics.ResourceBarrierTransition( RustData, ResourceState.UnorderedAccess );
		simulationShader.Attributes.Set( "SourceTexture", RustDataReadBuffer );
		simulationShader.Attributes.Set( "TargetTexture", RustData );
		simulationShader.Attributes.Set( "WorldToObject", worldToObject );
		simulationShader.Attributes.Set( "VolumeResolution", currentVolumeResolution );

		if ( atmosphere != null )
		{
			simulationShader.Attributes.Set( "OxygenLevel", atmosphere.OxygenLevel );
			simulationShader.Attributes.Set( "WaterVapor", atmosphere.WaterVapor );
		}
		else
		{
			// Use some default reasonable values if no Atmosphere component is present (probably something else breaks anyway)
			simulationShader.Attributes.Set( "OxygenLevel", 0.2f );
			simulationShader.Attributes.Set( "WaterVapor", 0.5f );
		}

		simulationShader.Dispatch( currentVolumeResolution, currentVolumeResolution, currentVolumeResolution );
	}

	public void RunImpactSimulation()
	{
		if ( storedImpactData == null )
		{
			return;
		}

		if ( currentVolumeResolution == 0 )
		{
			// Skip, resources were not created yet
			return;
		}

		Graphics.ResourceBarrierTransition( RustData, ResourceState.UnorderedAccess );

		var impactData = storedImpactData;
		storedImpactData = null;

		var positionOs = Transform.World.PointToLocal( impactData.Position );
		var impactDirOs = Transform.World.NormalToLocal( impactData.ImpactDirection ).Normal;

		// Convert to 0-1 space for texture sampling
		var texPos = (positionOs - boundsMin) * boundsScale;
		var shader = impactData.WeaponType == WeaponType.Spray ? getSprayedShader : getHitShader;

		shader.Attributes.Set( "DataTexture", RustData );
		shader.Attributes.Set( "ImpactPosition", texPos );
		shader.Attributes.Set( "ImpactRadius", impactData.ImpactRadius );
		shader.Attributes.Set( "ImpactStrength", impactData.ImpactStrength );
		shader.Attributes.Set( "ImpactDirection", impactDirOs );
		shader.Attributes.Set( "ConeAngleRad", impactData.ImpactPenetrationConeDeg * MathF.PI / 180.0f );
		shader.Attributes.Set( "MaxPenetration", impactData.ImpactPenetrationStrength );
		shader.Attributes.Set( "VolumeResolution", currentVolumeResolution );

		shader.Dispatch( currentVolumeResolution, currentVolumeResolution, currentVolumeResolution );
	}

	public void RenderOverlayRust()
	{
		Graphics.ResourceBarrierTransition( RustData, ResourceState.PixelShaderResource );
		var attributes = new RenderAttributes();

		attributes.Set( "RustDataRead", RustData );
		attributes.Set( "BoundsScale", boundsScale );
		attributes.Set( "BoundsMin", boundsMin );
		attributes.Set( "FlashlightPosition", rustSystem.Flashlight.Transform.World.Position );
		attributes.Set( "FlashlightDirection", rustSystem.Flashlight.Transform.World.Rotation.Forward );
		attributes.Set( "FlashlightIntensity", rustSystem.Flashlight.IsEnabled ? 1.0f : 0.0f );
		attributes.Set( "FlashlightAngle", rustSystem.Flashlight.Angle );
		attributes.Set( "SoftRustEnabled", qualitySystem.SoftRustEnabled );
		attributes.Set( "VolumeResolution", currentVolumeResolution );

		var mode = rustSystem.RenderingMode;
		sceneCustomObject.RenderLayer = SceneRenderLayer.OverlayWithDepth;

		var material = mode == RustRenderingMode.Debug ? rustableDebugMaterial : rustableProperMaterial;
		material.Set( "VolumeResolution", currentVolumeResolution );
		material.Set( "RustDataRead", RustData );

		Graphics.Draw(
			proxyVertices,
			material,
			attributes: attributes,
			primitiveType: Graphics.PrimitiveType.Triangles
		);

		attributes.Clear();
	}

	private void ReplaceMeshVertices( List<Vertex> oldVertices, List<ushort> oldIndices, List<Vector3> newVertices )
	{
		var bounds = new BBox();
		var vb = new VertexBuffer();
		vb.Init( true );

		Vertex[] proxyVertexTriplets = new Vertex[oldIndices.Count];
		for ( int i = 0; i < newVertices.Count; i++ )
		{
			var oldVertex = oldVertices[i];
			var newVertex = newVertices[i];
			vb.Add( oldVertex with { Position = newVertex } );
			bounds.AddPoint( newVertex );
		}

		for ( int i = 0; i < oldIndices.Count; i++ )
		{
			ushort index = oldIndices[i];
			vb.AddRawIndex( index );

			// See ProcessMeshVertices for explanation why we copy vertices to a new array
			// We could reuse the above method when we're done here but we're iterating over indices anyway...
			proxyVertexTriplets[i] = oldVertices[index] with { Position = newVertices[index] };
		}

		proxyVertices?.Dispose();
		proxyVertices = new GpuBuffer<Vertex>( proxyVertexTriplets.Length, GpuBuffer.UsageFlags.Vertex );
		proxyVertices.SetData( proxyVertexTriplets );

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

		// Update the model 
		// var oldModel = modelRenderer.Model;
		modelRenderer.Model = newModel;

		// How to delete old model? Does C# finalizer do it?
		// oldModel.Dispose();

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

	private Texture CreateVolumeTexture( int size )
	{
		var data = new byte[size * size * size * 3];

		// Random garbage to check if it's even getting to the shader
		// FillInitialData( size, data );

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
