using System;
using System.Collections.Generic;
using System.Linq;
using MIConvexHull;

public class MeshHull
{
    public Vector3[] Vertices { get; }

	public MeshHull( VertexData[] vertices, BBox bb, float vertexBias = 0.0f )
    {
        // Maybe base this on mesh size or world scale or something
        // But since I'm handpicking the vertex offset, there's no point
        var xScale = 1.0f;
        var yScale = 1.0f;
        var zScale = 1.0f;

        // Convert Vector3 to MIConvexHull's DefaultVertex and apply scaling/offset
        var points = vertices.Select( v => new DefaultVertex
        {
            Position = [
                v.X + (vertexBias * xScale * Math.Sign(v.X)),
                v.Y + (vertexBias * yScale * Math.Sign(v.Y)),
                v.Z + (vertexBias * zScale * Math.Sign(v.Z))
            ]
        } ).ToList();

        var planeDistanceTolerance = CalculatePlaneDistanceTolerance( bb );
        // Create the convex hull
        var hullResult = ConvexHull.Create( points, planeDistanceTolerance );

        if ( hullResult.Outcome != ConvexHullCreationResultOutcome.Success )
        {
            throw new Exception( $"Failed to create convex hull: {hullResult.ErrorMessage}" );
        }

        // Convert back to Vector3 array
        Vertices = hullResult.Result.Points
            .Select( p => new Vector3( (float)p.Position[0], (float)p.Position[1], (float)p.Position[2] ) )
            .ToArray();

        // Vertices:
        // Log.Info( "Hull vertices: " + string.Join( ", ", Vertices.Select( v => v.ToString() ) ) );
    }

    private static double CalculatePlaneDistanceTolerance( BBox bb )
    {
        // Pulled the number out my... hat... to make the hull not break for meshes of varying sizes
        return Math.Max( bb.Size.Length / 25, 1.5 );
    }

	public MeshHull( List<Vertex> vertices, BBox bb, float vertexOffset = 0.0f )
        : this( vertices.Select( v => new VertexData( v ) ).ToArray(), bb, vertexOffset )
	{
	}
}
