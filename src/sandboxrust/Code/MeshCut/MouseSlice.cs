using System;
using System.Collections.Generic;

public class MouseSlice : Component
{

    public GameObject plane;
    public Transform ObjectContainer;

    // How far away from the slice do we separate resulting objects
    public float separation;

    // Do we draw a plane object associated with the slice
    private Plane slicePlane = new Plane();
    public bool drawPlane;

    // Reference to the line renderer
    [Property]
    public ScreenLineRenderer lineRenderer;

    [Property]
    public GameObject debugPlane;

    private MeshCutter meshCutter;
    private TempMesh biggerMesh, smallerMesh;

    #region Utility Functions

    void DrawPlane( Vector3 start, Vector3 end, Vector3 normalVec )
    {
        Rotation rotate = Rotation.FromToRotation( Vector3.Up, normalVec );

        plane.Transform.Local = plane.Transform.Local
            .WithRotation( rotate )
            .WithPosition( (end + start) / 2 );
        plane.Enabled = true;
    }

    #endregion

    // Use this for initialization
    protected override void OnStart()
    {
        // Initialize a somewhat big array so that it doesn't resize
        meshCutter = new MeshCutter( 256 );
    }

    protected override void OnEnabled()
    {
        lineRenderer.OnLineDrawn += OnLineDrawn;
    }

    protected override void OnDisabled()
    {
        lineRenderer.OnLineDrawn -= OnLineDrawn;
    }

    private void OnLineDrawn( Vector3 start, Vector3 end, Vector3 depth )
    {
        var planeTangent = (end - start).Normal;

        // if we didn't drag, we set tangent to be on x
        if ( planeTangent == Vector3.Zero )
            planeTangent = Vector3.Right;

        var normalVec = Vector3.Cross( depth, planeTangent );

        if ( drawPlane ) DrawPlane( start, end, normalVec );

        try
        {
            SliceObjects( start, normalVec );
        }
        catch ( MeshCutException e )
        {
            // TODO: Remove this exception flow control
            Log.Warning( e.Message );
        }
    }


    void SliceObjects( Vector3 point, Vector3 normal )
    {
        // TODO: Create lookup table for tags
        var toSlice = Scene.GetAllObjects( true ).Where( obj => obj.Tags.Has( "slice_debuggable" ) ).ToArray();

        // Put results in positive and negative array so that we separate all meshes if there was a cut made
        List<GameTransform> positive = new List<GameTransform>(),
            negative = new List<GameTransform>();

        debugPlane.Transform.World = new Transform( point, Rotation.FromToRotation( Vector3.Up, normal ) );

        GameObject obj;
        bool slicedAny = false;
        for ( int i = 0; i < toSlice.Length; ++i )
        {
            obj = toSlice[i];
            // We multiply by the inverse transpose of the worldToLocal Matrix, a.k.a the transpose of the localToWorld Matrix
            // Since this is how normal are transformed
            var transformedPosition = obj.Transform.World.PointToLocal( point );
            var transformedNormal = obj.Transform.World.NormalToLocal( normal );

            //Convert plane in object's local frame
            slicePlane = new Plane( transformedPosition, transformedNormal );

            Log.Info( $"Slice plane position: {transformedPosition}, normal: {transformedNormal}" );

            slicedAny = SliceObject( ref slicePlane, obj, positive, negative ) || slicedAny;
        }

        Log.Info( $"Sliced any: {slicedAny}" );
        // Separate meshes if a slice was made
        if ( slicedAny )
        {
            // SeparateMeshes( positive, negative, normal );
        }
    }

    bool SliceObject( ref Plane slicePlane, GameObject obj, List<GameTransform> positiveObjects, List<GameTransform> negativeObjects )
    {
        var model = obj.GetComponent<ModelRenderer>().Model;

        if ( !meshCutter.SliceMesh( model, ref slicePlane ) )
        {
            // Put object in the respective list
            if ( slicePlane.GetDistance( meshCutter.GetFirstVertex() ) >= 0 )
                positiveObjects.Add( obj.Transform );
            else
                negativeObjects.Add( obj.Transform );

            return false;
        }

        // TODO: Update center of mass

        // Silly condition that labels which mesh is bigger to keep the bigger mesh in the original gameobject
        bool posBigger = meshCutter.PositiveMesh.surfacearea > meshCutter.NegativeMesh.surfacearea;
        if ( posBigger )
        {
            biggerMesh = meshCutter.PositiveMesh;
            smallerMesh = meshCutter.NegativeMesh;
        }
        else
        {
            biggerMesh = meshCutter.NegativeMesh;
            smallerMesh = meshCutter.PositiveMesh;
        }

        // Create new Sliced object with the other mesh
        // GameObject newObject = Instantiate( obj, ObjectContainer );
        // newObject.transform.SetPositionAndRotation( obj.transform.position, obj.transform.rotation );
        // var newObjMesh = newObject.GetComponent<MeshFilter>().mesh;

        // // Put the bigger mesh in the original object
        // // TODO: Enable collider generation (either the exact mesh or compute smallest enclosing sphere)
        // ReplaceMesh( mesh, biggerMesh );
        // ReplaceMesh( newObjMesh, smallerMesh );

        // (posBigger ? positiveObjects : negativeObjects).Add( obj.transform );
        // (posBigger ? negativeObjects : positiveObjects).Add( newObject.transform );

        return true;
    }


    // /// <summary>
    // /// Replace the mesh with tempMesh.
    // /// </summary>
    // void ReplaceMesh( Mesh mesh, TempMesh tempMesh, MeshCollider collider = null )
    // {
    //     mesh.Clear();
    //     mesh.SetVertices( tempMesh.vertices );
    //     mesh.SetTriangles( tempMesh.triangles, 0 );
    //     mesh.SetNormals( tempMesh.normals );
    //     mesh.SetUVs( 0, tempMesh.uvs );

    //     //mesh.RecalculateNormals();
    //     mesh.RecalculateTangents();

    //     if ( collider != null && collider.enabled )
    //     {
    //         collider.sharedMesh = mesh;
    //         collider.convex = true;
    //     }
    // }

    // void SeparateMeshes( Transform posTransform, Transform negTransform, Vector3 localPlaneNormal )
    // {
    //     // Bring back normal in world space
    //     Vector3 worldNormal = ((Vector3)(posTransform.worldToLocalMatrix.transpose * localPlaneNormal)).normalized;

    //     Vector3 separationVec = worldNormal * separation;
    //     // Transform direction in world coordinates
    //     posTransform.position += separationVec;
    //     negTransform.position -= separationVec;
    // }

    // void SeparateMeshes( List<Transform> positives, List<Transform> negatives, Vector3 worldPlaneNormal )
    // {
    //     int i;
    //     var separationVector = worldPlaneNormal * separation;

    //     for ( i = 0; i < positives.Count; ++i )
    //         positives[i].transform.position += separationVector;

    //     for ( i = 0; i < negatives.Count; ++i )
    //         negatives[i].transform.position -= separationVector;
    // }
}
