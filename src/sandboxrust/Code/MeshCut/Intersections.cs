using System;
using System.Collections.Generic;

public class Intersections
{
    #region Static functions

    /// <summary>
    /// Based on https://gdbooks.gitbooks.io/3dcollisions/content/Chapter2/static_aabb_plane.html
    /// </summary>
    public static bool BoundPlaneIntersect(Model mesh, ref Plane plane)
    {
        // Compute projection interval radius
        float r = mesh.Bounds.Extents.x * MathF.Abs(plane.Normal.x) +
            mesh.Bounds.Extents.y * MathF.Abs(plane.Normal.y) +
            mesh.Bounds.Extents.z * MathF.Abs(plane.Normal.z);

        // Compute distance of box center from plane
        float s = Vector3.Dot(plane.Normal, mesh.Bounds.Center) - (-plane.Distance);

        // Intersection occurs when distance s falls within [-r,+r] interval
        return MathF.Abs(s) <= r;
    }

    #endregion

    // Initialize fixed arrays so that we don't initialize them every time we call TrianglePlaneIntersect
    private readonly Vector3[] v;
    private readonly Vector2[] u;
    private readonly int[] t;
    private readonly bool[] positive;

    // Used in intersect method
    private Ray edgeRay;

    public Intersections()
    {
        v = new Vector3[3];
        u = new Vector2[3];
        t = new int[3];
        positive = new bool[3];
    }

    /// <summary>
    /// Find intersection between a plane and a line segment defined by vectors first and second.
    /// </summary>
    public ValueTuple<Vector3, Vector2> Intersect(Plane plane, Vector3 first, Vector3 second, Vector2 uv1, Vector2 uv2)
    {
        // Note-Migration: Changing ray cast behavior and general vector calculations
        // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Plane.Raycast.html:
        // This function sets enter to the distance along the ray, where it intersects the plane. 
        // If the ray is parallel to the plane, function returns false and sets enter to zero.If the ray is pointing in the opposite direction than the plane, function returns false and sets enter to the distance along the ray( negative value ).

        // Log.Info( $"Intersecting ray from {first} to {second}" );
        edgeRay.Position = first;
        edgeRay.Forward = (second - first).Normal;
        Vector3 hitPoint;
        float maxDist = (first - second).Length;
        
        bool result = plane.TryTrace( edgeRay, out hitPoint, twosided: true );
        
        float dist = (first - hitPoint).Length;
        if (result == false)
            // Intersect in wrong direction...
            throw new MeshCutException("Line-Plane intersect in wrong direction");
        else if (dist > maxDist)
            // Intersect outside of line segment
            throw new MeshCutException("Intersect outside of line");

        var returnVal = new ValueTuple<Vector3, Vector2>
        {
            // Note-Migration: Unity.Ray.GetPoint Returns a point at distance units along the ray.
            // but we already have a intersection point
            Item1 = hitPoint
        };

        var relativeDist = dist / maxDist;
        // Compute new uv by doing Linear interpolation between uv1 and uv2
        returnVal.Item2.x = MathX.Lerp(uv1.x, uv2.x, relativeDist);
        returnVal.Item2.y = MathX.Lerp(uv1.y, uv2.y, relativeDist);
        return returnVal;
    }

    /*
     * Small diagram for reference :)
     *       |      |  /|
     *       |      | / |P1       
     *       |      |/  |         
     *       |    I1|   |
     *       |     /|   |
     *      y|    / |   |
     *       | P0/__|___|P2
     *       |      |I2
     *       |      |
     *       |___________________
     */

    public bool TrianglePlaneIntersect(List<Vertex> vertices, List<ushort> triangles, int startIdx, ref Plane plane, TempMesh posMesh, TempMesh negMesh, Vector3[] intersectVectors)
    {
        int i;

        // Store triangle, vertex and uv from indices
        for(i = 0; i < 3; ++i)
        {
            // TODO-Migration: vector4 to vector2 (again)
            t[i] = triangles[startIdx + i];
            v[i] = vertices[t[i]].Position;
            u[i] = new Vector2(vertices[t[i]].TexCoord0.x, vertices[t[i]].TexCoord0.y);
        }

        // Store wether the vertex is on positive mesh
        posMesh.ContainsKeys(triangles, startIdx, positive);

        // If they're all on the same side, don't do intersection
        if (positive[0] == positive[1] && positive[1] == positive[2])
        {
            // All points are on the same side. No intersection
            // Add them to either positive or negative mesh
            (positive[0] ? posMesh : negMesh).AddOgTriangle(t);
            return false;
        }

        // Find lonely point
        int lonelyPoint = 0;
        if (positive[0] != positive[1])
            lonelyPoint = positive[0] != positive[2] ? 0 : 1;
        else
            lonelyPoint = 2;

        // Set previous point in relation to front face order
        int prevPoint = lonelyPoint - 1;
        if (prevPoint == -1) prevPoint = 2;
        // Set next point in relation to front face order
        int nextPoint = lonelyPoint + 1;
        if (nextPoint == 3) nextPoint = 0;

        // Get the 2 intersection points
        ValueTuple<Vector3, Vector2> newPointPrev = Intersect(plane, v[lonelyPoint], v[prevPoint], u[lonelyPoint], u[prevPoint]);
        ValueTuple<Vector3, Vector2> newPointNext = Intersect(plane, v[lonelyPoint], v[nextPoint], u[lonelyPoint], u[nextPoint]);

        //Set the new triangles and store them in respective tempmeshes
        (positive[lonelyPoint] ? posMesh : negMesh).AddSlicedTriangle(t[lonelyPoint], newPointNext.Item1, newPointPrev.Item1, newPointNext.Item2, newPointPrev.Item2);

        (positive[prevPoint] ? posMesh : negMesh).AddSlicedTriangle(t[prevPoint], newPointPrev.Item1, newPointPrev.Item2, t[nextPoint]);

        (positive[prevPoint] ? posMesh : negMesh).AddSlicedTriangle(t[nextPoint], newPointPrev.Item1, newPointNext.Item1, newPointPrev.Item2, newPointNext.Item2);

        // We return the edge that will be in the correct orientation for the positive side mesh
        if (positive[lonelyPoint])
        {
            intersectVectors[0] = newPointPrev.Item1;
            intersectVectors[1] = newPointNext.Item1;
        } else
        {
            intersectVectors[0] = newPointNext.Item1;
            intersectVectors[1] = newPointPrev.Item1;
        }
        return true;
    }



}
