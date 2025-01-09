using System;
using System.Collections.Generic;
using System.Linq;
using MIConvexHull;

public class MeshHull
{
    public Vector3[] Vertices { get; }


    private const float Epsilon = 0.001f;

    public MeshHull( List<Vector3> vertices )
    {

        // Convert Vector3 to MIConvexHull's DefaultVertex
        var points = vertices.Select( v => new DefaultVertex
        {
            Position = new double[] { v.x, v.y, v.z }
        } ).ToList();

        // Create the convex hull
        var hullResult = ConvexHull.Create( points );

        if ( hullResult.Outcome != ConvexHullCreationResultOutcome.Success )
        {
            throw new Exception( $"Failed to create convex hull: {hullResult.ErrorMessage}" );
        }

        // Convert back to Vector3 array
        Vertices = hullResult.Result.Points
            .Select( p => new Vector3( (float)p.Position[0], (float)p.Position[1], (float)p.Position[2] ) )
            .ToArray();

        // Vertices:
        // Log.Info( string.Join( ", ", Vertices.Select( v => v.ToString() ) ) );
    }
}
