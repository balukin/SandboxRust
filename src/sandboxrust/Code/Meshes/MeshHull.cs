using System;
using System.Collections.Generic;
using System.Linq;
using MIConvexHull;

public class MeshHull
{
    public Vector3[] Vertices { get; }


    private const float Epsilon = 0.001f;

	public MeshHull( VertexData[] vertices )
    {

        // Convert Vector3 to MIConvexHull's DefaultVertex
        var points = vertices.Select( v => new DefaultVertex
        {
            Position = [v.X, v.Y, v.Z]
        } ).ToList();

        // Create the convex hull
        var hullResult = ConvexHull.Create( points, 3 );

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

	public MeshHull( List<Vertex> vertices )
        : this( vertices.Select( v => new VertexData( v ) ).ToArray() )
	{
	}
}
