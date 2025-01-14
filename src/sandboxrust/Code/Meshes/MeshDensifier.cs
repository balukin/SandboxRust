using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Densifies mesh by subdividing all edges longer than certain length.
/// </summary>
public class MeshDensifier : Component
{
	// TODO: Use world-space edge length instead of object-space.
	// Then use the same edge distance as 3d texture resolution to keep vertex density roughly the same as voxel density

	public record DensificationResult( bool success, float maxRemainingEdgeLength, float avgRemainingEdgeLength, int newTriangleCount );

	private class Edge
	{
		public int V1 { get; }
		public int V2 { get; }

		public Edge( int v1, int v2 )
		{
			// Store vertices in sorted order for consistent hashing
			V1 = Math.Min( v1, v2 );
			V2 = Math.Max( v1, v2 );
		}

		public override int GetHashCode() => HashCode.Combine( V1, V2 );

		public override bool Equals( object obj ) =>
			obj is Edge other && V1 == other.V1 && V2 == other.V2;
	}

	[Property]
	public float MaxEdgeLength { get; set; } = 1.0f;

	private ModelRenderer modelRenderer;
	private Model original;

	protected override void OnAwake()
	{
		base.OnAwake();
		modelRenderer = GetComponent<ModelRenderer>();
		original = modelRenderer.Model;

		ArgumentNullException.ThrowIfNull( modelRenderer );
		Log.Info( $"MeshDensifier: Initialized with model {original.Name}" );
	}

	[Button( "Densify" )]
	private void DensifyEditorButton()
	{
		original = modelRenderer.Model;

		if ( modelRenderer == null )
		{
			Log.Error( "MeshDensifierTester: ModelRenderer component not found" );
			return;
		}

		Densify( MaxEdgeLength );
	}

	/// <summary>
	/// Densifies given model's mesh by subdividing all edges longer than certain length.
	/// </summary>
	/// <param name="maxEdgeLength">Maximum length of edge to be subdivided.</param>
	/// <returns>New model with the densified mesh.</returns>
	/// <remarks>
	/// This will only run a one pass of subdivision. Use result to decide if more passes are needed.
	/// </remarks>
	public DensificationResult Densify(float maxEdgeLength )
	{
		var sw = Stopwatch.StartNew();
		
		// TODO: Let's just assume that the model has only one mesh+material
		if ( original.MeshCount != 1 )
		{
			Log.Error( "MeshDensifier: Model has incorrect more than one mesh. This is not supported yet." );
			return new DensificationResult( false, 0, 0, 0 );
		}

		var vertices = original.GetVertices().ToList();
		var indices = original.GetIndices().ToList();
		var materials = original.Materials;

		if ( materials.Count() != 1 )
		{
			Log.Error( "MeshDensifier: Model has incorrect material count" );
			return new DensificationResult( false, 0, 0, 0 );
		}

		var maxLengthSqr = maxEdgeLength * maxEdgeLength;

		var splitEdges = new Dictionary<Edge, int>();
		var newIndices = new List<uint>();
		
		// Track edge length statistics
		float maxRemainingLength = 0f;
		float totalLength = 0f;
		int edgeCount = 0;

		float[] edgeLengths = new float[3];
		bool[] edgeTooLongFlags = new bool[3];

		// Process each triangle
		for ( int i = 0; i < indices.Count; i += 3 )
		{
			var i0 = (int)indices[i];
			var i1 = (int)indices[i + 1];
			var i2 = (int)indices[i + 2];

			var v0 = vertices[i0];
			var v1 = vertices[i1];
			var v2 = vertices[i2];

			// Check each edge length and track statistics
			edgeLengths[0] = (v1.Position - v0.Position).Length;
			edgeLengths[1] = (v2.Position - v1.Position).Length;
			edgeLengths[2] = (v0.Position - v2.Position).Length;

			// Extra copy in an intermediate array cell to reduce the if/else count in the next loop
			bool e01Long = edgeTooLongFlags[0] = edgeLengths[0] * edgeLengths[0] > maxLengthSqr;
			bool e12Long = edgeTooLongFlags[1] = edgeLengths[1] * edgeLengths[1] > maxLengthSqr;
			bool e20Long = edgeTooLongFlags[2] = edgeLengths[2] * edgeLengths[2] > maxLengthSqr;

			// Track statistics for edges for debug purposes
			for ( int j = 0; j < 3; j++ )
			{
				if ( !edgeTooLongFlags[j] )
				{
					maxRemainingLength = Math.Max( maxRemainingLength, edgeLengths[j] );
					totalLength += edgeLengths[j];
				}
				else
				{
					totalLength += edgeLengths[j] / 2;
					maxRemainingLength = Math.Max( maxRemainingLength, edgeLengths[j] / 2 );
				}
				edgeCount++;
			}

			// Debug: test if we can successfully re-write original mesh
			const bool debugNoSubdivision = false;

			if ( debugNoSubdivision || (!e01Long && !e12Long && !e20Long) )
			{
				// No subdivision needed
				newIndices.AddRange( [(uint)i0, (uint)i1, (uint)i2] );
				continue;
			}

			// TODO: Refactor numeric types here
			// Sometimes the API treats indices as ushorts, sometimes uints, then we use ints for indexing - mess

			// Get or create midpoints
			int m01 = e01Long ? GetOrCreateMidpoint( i0, i1, vertices, splitEdges ) : -1;
			int m12 = e12Long ? GetOrCreateMidpoint( i1, i2, vertices, splitEdges ) : -1;
			int m20 = e20Long ? GetOrCreateMidpoint( i2, i0, vertices, splitEdges ) : -1;

			// Add new triangles based on which edges were split
			// 4 new triangles when all edges are split, 3 when 2 are split, 2 when 1 is split
			if ( e01Long && e12Long && e20Long )
			{
				newIndices.AddRange( [(uint)i0, (uint)m01, (uint)m20] );
				newIndices.AddRange( [(uint)m01, (uint)i1, (uint)m12] );
				newIndices.AddRange( [(uint)m20, (uint)m12, (uint)i2] );
				newIndices.AddRange( [(uint)m01, (uint)m12, (uint)m20] );
			}
			else if ( e01Long && e12Long )
			{
				newIndices.AddRange( [(uint)i0, (uint)m01, (uint)i2] );
				newIndices.AddRange( [(uint)m01, (uint)i1, (uint)m12] );
				newIndices.AddRange( [(uint)m01, (uint)m12, (uint)i2] );
			}
			else if ( e12Long && e20Long )
			{
				newIndices.AddRange( [(uint)i0, (uint)i1, (uint)m12] );
				newIndices.AddRange( [(uint)i0, (uint)m12, (uint)m20] );
				newIndices.AddRange( [(uint)m12, (uint)i2, (uint)m20] );
			}
			else if ( e20Long && e01Long )
			{
				newIndices.AddRange( [(uint)m01, (uint)i1, (uint)i2] );
				newIndices.AddRange( [(uint)i0, (uint)m01, (uint)m20] );
				newIndices.AddRange( [(uint)m01, (uint)i2, (uint)m20] );
			}
			else if ( e01Long )
			{
				newIndices.AddRange( [(uint)i0, (uint)m01, (uint)i2] );
				newIndices.AddRange( [(uint)m01, (uint)i1, (uint)i2] );
			}
			else if ( e12Long )
			{
				newIndices.AddRange( [(uint)i0, (uint)i1, (uint)m12] );
				newIndices.AddRange( [(uint)i0, (uint)m12, (uint)i2] );
			}
			else if ( e20Long )
			{
				newIndices.AddRange( [(uint)i0, (uint)i1, (uint)m20] );
				newIndices.AddRange( [(uint)i1, (uint)i2, (uint)m20] );
			}
		}

		var avgRemainingLength = totalLength / edgeCount;

		var vb = new VertexBuffer();
		vb.Init( true );
		foreach ( var vertex in vertices )
		{
			vb.Add( vertex );
		}

		foreach ( var index in newIndices )
		{
			vb.AddRawIndex( (int)index );
		}

		var mesh = new Mesh();
		mesh.CreateBuffers( vb );

		var bounds = new BBox();
		foreach ( var vertex in vertices )
		{
			bounds = bounds.AddPoint( vertex.Position );
		}

		mesh.Bounds = bounds;
		mesh.Material = materials.First();

		// Log.Info( $"Mesh vertex count: {mesh.VertexCount}" );
		// Log.Info( $"Mesh index count: {mesh.IndexCount}" );
		// Log.Info( $"Mesh bounds: {bounds}" );
		// Log.Info( "Mesh is valid: " + mesh.IsValid() );

		var hull = new MeshHull( vertices );

		var newModel = Model.Builder
			.AddMesh( mesh )
			.AddCollisionHull( hull.Vertices )
			.Create();

		// Hull vertices:
		// Log.Info( $"Hull vertices: {string.Join( ", ", hull.Vertices )}" );
		Log.Info( $"MeshDensifier: Densified {original.Name} from {indices.Count / 3} triangles "
				  + $"to {newIndices.Count / 3} triangles with a hull of {hull.Vertices.Length} vertices in "
				  + $"({avgRemainingLength:F2} avg length, {maxRemainingLength:F2} max length) in {sw.ElapsedMilliseconds}ms" );

		modelRenderer.Model = newModel;
		var modelCollider = GetComponent<ModelCollider>();

		if ( modelCollider != null )
		{
			modelCollider.Model = newModel;
		}

		original = newModel;
		return new DensificationResult( true, maxRemainingLength, avgRemainingLength, newIndices.Count / 3 );
	}

	private int GetOrCreateMidpoint( int v1, int v2, List<Vertex> vertices, Dictionary<Edge, int> splitEdges )
	{
		var edge = new Edge( v1, v2 );

		// Check if we already created this midpoint
		if ( splitEdges.TryGetValue( edge, out int existingIndex ) )
		{
			return existingIndex;
		}

		// Create new midpoint vertex by interpolating
		var vert1 = vertices[v1];
		var vert2 = vertices[v2];

		var newVertex = new Vertex(
			position: (vert1.Position + vert2.Position) * 0.5f,
			normal: (vert1.Normal + vert2.Normal).Normal,
			tangent: new Vector3( vert1.Tangent.x + vert2.Tangent.x,
				vert1.Tangent.y + vert2.Tangent.y,
				vert1.Tangent.z + vert2.Tangent.z ).Normal,
			texCoord0: (vert1.TexCoord0 + vert2.TexCoord0) * 0.5f
		);

		// Add new vertex and track the split edge
		int newIndex = vertices.Count;
		vertices.Add( newVertex );
		splitEdges.Add( edge, newIndex );

		return newIndex;
	}
}
