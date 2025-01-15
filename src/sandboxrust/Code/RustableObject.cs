using System;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Rendering;
using Sandbox.UI;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;

/// <summary>
/// Component for objects that can have rust applied to them.
/// </summary>
public sealed class RustableObject : Component
{
	/// <summary>
	/// Setting it to true will make frame pacing much smoother but is very unstable with unexplainable errors due to
	/// thread-related issues. It's possible to get it working but the documentation on what can be called off main thread
	/// is scarce, error reporting is not-so-clear and it requires way more debugging to guarantee 100% stability.
	/// 
	/// Setting it to false will make the simulation run on the main thread and will block the game during costly mesh 
	/// re-calculation which sucks but at least it doesn't kill the app randomly.
	/// </summary>
	private const bool UseBgErosion = false;

	private int frameNo = 0;

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

	[Property]
	public Vector3 ErosionTarget { get; set; }

	/// <summary>
	/// If set, it will override the volume resolution for this object and will ignore the quality system settings.
	/// </summary>
	/// <remarks>
	/// There's no point in using high resolutions for very small objects.
	/// </remarks>
	[Property]
	public int OverrideVolumeResolution = 0;

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

	/// <summary>
	/// Whenever we update the mesh we store a copy of buffers here so we don't have to re-fetch them from GPU.
	/// </summary>
	/// <remarks>
	/// Currently there is no way to use async read/write to GpuBuffer so we have to do a lot of copies here and there to 
	/// avoid unnecessary blocking round trips.
	/// Issue to track: https://github.com/Facepunch/sbox-issues/issues/7270
	/// </remarks>
	public Vertex[] meshVertices;
	public ushort[] meshIndices;

	private GpuBuffer<VertexData> erosionInputBuffer;
	private GpuBuffer<VertexData> erosionOutputBuffer;

	private QualitySystem qualitySystem;

	private int currentVolumeResolution;

	private bool meshUpdatePending;
	private Vertex[] pendingProxyVertices;
	private Model pendingModel;

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

		if ( modelRenderer.Model.MeshCount > 1)
		{
			Log.Error( "Only meshes with one material and one mesh are currently supported" );
			Enabled = false;
			Destroy();
			sceneCustomObject.Delete();
			return;
		}
		
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
		frameNo++;

		EnsureResourceResolutionIsValid();

		if ( rustSystem.ShouldRunErosion( this ) )
		{
			if ( UseBgErosion )
			{
				_ = GameTask.RunInThreadAsync( RunErosionSimulation );
			}
			else
			{
				_ = RunErosionSimulation();
			}
		}

		SwapMeshIfPending();
		if ( rustSystem.RenderingMode == RustRenderingMode.Debug && ErosionTarget != Vector3.Zero )
		{
			var erosionTargetWorld = Transform.World.PointToWorld( ErosionTarget );

			using ( var instance = Gizmo.Scope( "ErosionTarget", erosionTargetWorld ) )
			{
				Gizmo.Draw.Color = Color.Red;
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.SolidSphere( Vector3.Zero, 0.5f );				
			}

			var worldPos = Transform.World.PointToWorld( ErosionTarget );
			Gizmo.Draw.Text( "Deform target", new Transform( worldPos, Transform.World.Rotation, Transform.World.Scale ) );
		}
	}

	private void DensifyObjectMesh()
	{
		// TODO: Make it depend on rust data resolution and object size (or mod Densify to work in world space)
		// No more than 10 in case other conditions are invalid
		for ( var densificationTurn = 0; densificationTurn < 10; densificationTurn++ )
		{
			var result = meshDensifier.Densify( 5f );

			if ( result.newTriangleCount > 50_000 )
			{
				// that's too many triangles
				// Log.Warning("Bailing out of densification due to too many triangles");
				break;
			}

			if ( result.maxRemainingEdgeLength < 10f )
			{
				// that's good enough
				// Log.Info("Bailing out of densification due to max edge length");
				break;
			}

			if ( result.avgRemainingEdgeLength < 6f )
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
	/// To be fair, it's also possible that it's not a leak and maybe it's some internal buffer caching with LRU eviction once VRAM pressure is high
	/// but I don't want to risk OOMs. Maybe later (TODO) I'll investigate with RenderDoc or look how to enable leak detection in debug layers or something.
	/// </remarks>
	private void ProcessMeshVertices( Model model )
	{
		meshVertices = meshDensifier.LastVertices;
		meshIndices = meshDensifier.LastIndices;

		uniqueVertexCount = meshVertices.Length;
		proxyVertexCount = meshIndices.Length;

		proxyVertices = new GpuBuffer<Vertex>( meshIndices.Length, GpuBuffer.UsageFlags.Vertex );

		var vertexData = new List<Vertex>();
		for ( int i = 0; i < meshIndices.Length; i += 3 )
		{
			vertexData.Add( meshVertices[meshIndices[i]] );
			vertexData.Add( meshVertices[meshIndices[i + 1]] );
			vertexData.Add( meshVertices[meshIndices[i + 2]] );
		}

		proxyVertices.SetData( vertexData.ToArray() );

		Log.Info( $"Created proxy mesh with {proxyVertices.ElementCount} vertices, valid: {proxyVertices.IsValid}" );
	}

	private void EnsureResourceResolutionIsValid()
	{
		int resolutionToUse;
		string reasonForChange;
		if ( OverrideVolumeResolution > 0 )
		{
			resolutionToUse = OverrideVolumeResolution;
			reasonForChange = "override";
		}
		else
		{
			resolutionToUse = qualitySystem.VolumeResolution;
			reasonForChange = "quality";
		}

		if ( currentVolumeResolution != resolutionToUse )
		{
			Log.Info( $"RustableObject {GameObject.Name} resolution changed from {currentVolumeResolution} to {resolutionToUse} ({reasonForChange}). Recreating resources." );
			RustData?.Dispose();
			RustDataReadBuffer?.Dispose();
			RustData = CreateVolumeTexture( resolutionToUse );
			RustDataReadBuffer = CreateVolumeTexture( resolutionToUse );

			// Update material bindings
			if ( rustableDebugMaterial != null )
			{
				rustableDebugMaterial.Set( "RustDataRead", RustData );
			}

			if ( rustableProperMaterial != null )
			{
				rustableProperMaterial.Set( "RustDataRead", RustData );
			}

			currentVolumeResolution = resolutionToUse;
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

	private void RenderOverlayRust()
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

	private async Task RunErosionSimulation()
	{
		//Log.Info( "Running erosion simulation" );
		if ( currentVolumeResolution == 0 || meshUpdatePending )
		{
			// Skip if resources not created or update already pending
			return;
		}

		var oldVertices = meshVertices;
		var oldIndices = meshIndices;


		var currentErosionTarget = meshCenter;
		if ( ErosionTarget != Vector3.Zero )
		{
			currentErosionTarget = ErosionTarget;
		}

		// Step 1 - erosion simulation in compute shader
		// TODO: We can probably use only one RW buffer, each thread operates on its own [vertex] in isolation
		erosionInputBuffer.SetData<VertexData>( oldVertices.Select( v => new VertexData( v ) ).ToArray() );
		meshErosionShader.Attributes.Set( "InputVertices", erosionInputBuffer );
		meshErosionShader.Attributes.Set( "OutputVertices", erosionOutputBuffer );
		meshErosionShader.Attributes.Set( "RustData", RustData );
		meshErosionShader.Attributes.Set( "ErosionTarget", currentErosionTarget );
		meshErosionShader.Attributes.Set( "BoundsMin", boundsMin );
		meshErosionShader.Attributes.Set( "BoundsScale", boundsScale );
		meshErosionShader.Attributes.Set( "ErosionStrength", ErosionStrength );
		meshErosionShader.Attributes.Set( "VertexCount", oldVertices.Length );
		meshErosionShader.Dispatch( oldVertices.Length, 1, 1 );

		// Step 2 - get results and calculate new mesh - still on worker thread
		var newVertices = new VertexData[oldVertices.Length];

		if ( UseBgErosion == false )
		{
			// If we cannot do proper threading, let's at least split the work across multiple frames
			await GameTask.Delay( 1 );
		}

		// This one takes long and nas no async version, yet
		erosionOutputBuffer.GetData<VertexData>( newVertices );

		var vb = new VertexBuffer();
		vb.Init( true );

		var bb = new BBox();

		Vertex[] proxyVertexTriplets = new Vertex[oldIndices.Length];
		for ( int i = 0; i < newVertices.Length; i++ )
		{
			var newPosition = newVertices[i].ToVector3();
			var oldVertex = oldVertices[i];
			var newVertex = oldVertex with { Position = newPosition };
			vb.Add( newVertex );
			bb = bb.AddPoint( newPosition );
			meshVertices[i] = newVertex;
		}

		for ( int i = 0; i < oldIndices.Length; i++ )
		{
			ushort index = oldIndices[i];
			vb.AddRawIndex( index );
			meshIndices[i] = index;

			// See ProcessMeshVertices for explanation why we copy vertices to a new array
			// We could reuse the above method when we're done here but we're iterating over indices anyway...
			proxyVertexTriplets[i] = oldVertices[index] with { Position = newVertices[index].ToVector3() };
		}


		// Create hull from updated vertices

		// This is also costly and we're 99.(9)% safe to do it off main thread because we're not touching any engine code
		await GameTask.WorkerThread();
		var hull = new MeshHull( newVertices, bb );
		await GameTask.MainThread();

		var mesh = new Mesh();
		mesh.CreateBuffers( vb, false );
		mesh.Bounds = bb;
		mesh.Material = modelRenderer.Model.Materials.First();


		// Step 3 - Prepare pending state for main thread to update the mesh
		// Note: Don't use await GameTask.MainThread() because we want to drop back exactly to PreRender
		// to avoid some weird visual flickering

		// Technically we could simply swap it here if UseBgErosion is false but being one frame behind
		// isn't much of a deal and it keeps the spaghetti code al dente

		// Create new model with updated mesh
		pendingModel = Model.Builder
			.AddMesh( mesh )
			.AddCollisionHull( hull.Vertices )
			.Create();

		pendingProxyVertices = proxyVertexTriplets;
		meshUpdatePending = true;
	}

	/// <summary>
	/// Swaps a new mesh (that was calculated in RunErosion) with the old one.
	/// Should be called from main thread.
	/// </summary>
	private void SwapMeshIfPending()
	{
		if ( !meshUpdatePending )
			return;

		// Update proxy vertices
		proxyVertices?.Dispose();
		proxyVertices = new GpuBuffer<Vertex>( pendingProxyVertices.Length, GpuBuffer.UsageFlags.Vertex );
		proxyVertices.SetData( pendingProxyVertices );

		// Update model
		modelRenderer.Model = pendingModel;

		// Update the model collider if present
		var modelCollider = GetComponent<ModelCollider>();
		if ( modelCollider != null )
		{
			modelCollider.Model = pendingModel;
		}

		// Clear pending state
		meshUpdatePending = false;
		pendingProxyVertices = null;
		pendingModel = null;
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
