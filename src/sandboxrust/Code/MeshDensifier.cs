using System;

public class MeshDensifier
{
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

    /// <summary>
    /// Densifies given model's mesh by subdividing all edges longer than certain length.
    /// </summary>
    /// <param name="original">Model which mesh will be densified.</param>
    /// <param name="maxEdgeLength">Maximum length of edge to be subdivided.</param>
    /// <returns>New model with the densified mesh.</returns>
    public static Model Densify( Model original, float maxEdgeLength )
    {
        // TODO: Let's just assume that the model has only one mesh+material
        if ( original.MeshCount != 1 )
        {
            Log.Error( "MeshDensifier: Model has incorrect more than one mesh. This is not supported yet." );
            return original;
        }

        var vertices = original.GetVertices().ToList();
        var indices = original.GetIndices().ToList();
        var materials = original.Materials;

        if ( materials.Count() != 1 )
        {
            Log.Error( "MeshDensifier: Model has incorrect material count" );
            return original;
        }

        var maxLengthSqr = maxEdgeLength * maxEdgeLength;

        var splitEdges = new Dictionary<Edge, int>();
        var newIndices = new List<uint>();

        // Process each triangle
        for ( int i = 0; i < indices.Count; i += 3 )
        {
            var i0 = (int)indices[i];
            var i1 = (int)indices[i + 1];
            var i2 = (int)indices[i + 2];

            var v0 = vertices[i0];
            var v1 = vertices[i1];
            var v2 = vertices[i2];

            // Check each edge length
            bool e01Long = (v1.Position - v0.Position).LengthSquared > maxLengthSqr;
            bool e12Long = (v2.Position - v1.Position).LengthSquared > maxLengthSqr;
            bool e20Long = (v0.Position - v2.Position).LengthSquared > maxLengthSqr;

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

        var newModel = Model.Builder
            .AddMesh( mesh )
            .Create();

        return newModel;
    }

    private static int GetOrCreateMidpoint( int v1, int v2, List<Vertex> vertices, Dictionary<Edge, int> splitEdges )
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

public class MeshDensifierTester : Component
{
    [Property]
    public float MaxEdgeLength = 1.0f;

    private ModelRenderer modelRenderer;
    private Model originalModel;

    protected override void OnStart()
    {
        base.OnStart();
        modelRenderer = GetComponent<ModelRenderer>();
    }

    [Button( "Densify" )]
    public void Densify()
    {
        originalModel = modelRenderer.Model;

        if ( modelRenderer == null )
        {
            Log.Error( "MeshDensifierTester: ModelRenderer component not found" );
            return;
        }

        var prevTris = originalModel.GetIndices().Length / 3;
        var densified = MeshDensifier.Densify( originalModel, MaxEdgeLength );
        var newTris = densified.GetIndices().Length / 3;
        Log.Info( $"MeshDensifierTester: Densified {originalModel.Name} from {prevTris} triangles to {newTris} triangles" );
        modelRenderer.Model = densified;
    }
}