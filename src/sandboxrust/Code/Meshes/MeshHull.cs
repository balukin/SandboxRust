using System;
using System.Collections.Generic;
using System.Linq;
using MIConvexHull;

public class MeshHull
{
    public Vector3[] Vertices { get; }


    private const float Epsilon = 0.001f;

	public MeshHull( VertexData[] vertices, BBox bb )
    {

        // Convert Vector3 to MIConvexHull's DefaultVertex
        var points = vertices.Select( v => new DefaultVertex
        {
            Position = [v.X, v.Y, v.Z]
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
        return Math.Max( bb.Size.Length / 20, 1.5 );
    }

	public MeshHull( List<Vertex> vertices, BBox bb )
        : this( vertices.Select( v => new VertexData( v ) ).ToArray(), bb )
	{
	}
}
