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

        // Visualize cutting plane at player camera position
        // debugPlane.Transform.World = new Transform( point, Rotation.FromToRotation( Vector3.Up, normal ) );

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

    bool SliceObject( ref Plane slicePlane, GameObject originalObj, List<GameTransform> positiveObjects, List<GameTransform> negativeObjects )
    {
        var originalModelRenderer = originalObj.GetComponent<ModelRenderer>();
        var originalModel = originalModelRenderer.Model;

        if ( !meshCutter.SliceMesh( originalModel, ref slicePlane ) )
        {
            // Put object in the respective list
            if ( slicePlane.GetDistance( meshCutter.GetFirstVertex() ) >= 0 )
                positiveObjects.Add( originalObj.Transform );
            else
                negativeObjects.Add( originalObj.Transform );

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

        // Create new Sliced object with the other mesh and move both of them to a common container
        var container = new GameObject( true, "Container for " + originalObj.Name );
        container.Transform.World = originalObj.Transform.World;
        container.SetParent( originalObj.Parent );
        originalObj.SetParent( container );
        originalObj.Name = "Original slice of " + originalObj.Name;
        var cloneConfig = new CloneConfig( container.WorldTransform, parent: container, name: "New slice of " + originalObj.Name );

        GameObject newObject = originalObj.Clone( cloneConfig );
        
        newObject.Transform.World = originalObj.Transform.World;


        var newObjModelRenderer = newObject.GetComponent<ModelRenderer>();

        // Put the bigger mesh in the original object
        // TODO: Enable collider generation
        ReplaceModel( originalModelRenderer, biggerMesh );
        ReplaceModel( newObjModelRenderer, smallerMesh );

        // TODO: Dispose of the old mesh data

        (posBigger ? positiveObjects : negativeObjects).Add( originalObj.Transform );
        (posBigger ? negativeObjects : positiveObjects).Add( newObject.Transform );

        return true;
    }

    void ReplaceModel( ModelRenderer modelRenderer, TempMesh tempMesh )
    {
        var sandboxMesh = new Mesh();

        // Assume default Vertex is the used vertex layout, will probably crash if not
        // TODO: Add guard clause

        VertexBuffer vb = new VertexBuffer();
        vb.Init( true );
        for ( int i = 0; i < tempMesh.triangles.Count; i += 3 )
        {
            vb.AddTriangle( 
                 tempMesh.vertices[tempMesh.triangles[i]],
                 tempMesh.vertices[tempMesh.triangles[i + 1]],
                 tempMesh.vertices[tempMesh.triangles[i + 2]] );
        }

        // Create and set mesh buffers
        sandboxMesh.CreateBuffers( vb, true );

        sandboxMesh.Material = tempMesh.material;
        
        Log.Info( $"Mesh valid: {sandboxMesh.IsValid}, og material: {modelRenderer.GetMaterial()?.Name}" );

        // Calculate and set bounds
        // var bounds = new BBox();
        // foreach ( var vertex in tempMesh.vertices )
        // {
        //     bounds = bounds.AddPoint( vertex.Position );
        // }
        // sandboxMesh.Bounds = bounds;

        // Create a new Model using ModelBuilder
        var modelBuilder = new ModelBuilder();
        modelBuilder.AddMesh( sandboxMesh );

        // modelBuilder.AddCollisionBox( new Vector3( 1, 1, 1 ) );

        var model = modelBuilder.Create();
        Log.Info( $"Model valid: {model.IsValid} with tri count: {tempMesh.triangles.Count}" );
        modelRenderer.Model = model;

        // Maybe this way?
        // var go = modelRenderer.GameObject;
        // modelRenderer.Destroy();
        // go.AddComponent<ModelRenderer>().Model = model;
    }
}
