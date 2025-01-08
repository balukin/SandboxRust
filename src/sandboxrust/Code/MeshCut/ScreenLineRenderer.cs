using System.Collections;
using System.Collections.Generic;
using Sandbox.Rendering;

public class ScreenLineRenderer : Component
{

    // Line Drawn event handler
    public delegate void LineDrawnHandler( Vector3 begin, Vector3 end, Vector3 depth );
    public event LineDrawnHandler OnLineDrawn;

    bool dragging;
    Vector3 start;
    Vector3 end;
    CameraComponent cam;

    public Material lineMaterial;

    // Use this for initialization
    protected override void OnStart()
    {
        cam = Scene.Camera;
        dragging = false;

    }

    public bool IsRmbDown()
    {
        return Input.Down( "attack2" );
    }

    public bool IsRmbReleased()
    {
        return Input.Released( "attack2" );
    }

    // Update is called once per frame
    protected override void OnUpdate()
    {   
        var mousePos = Mouse.Position;
        if ( !dragging && IsRmbDown() )
        {
            start = mousePos;
            dragging = true;
        }

        if ( dragging )
        {
            end = mousePos;
        }

        if ( dragging && IsRmbReleased() )
        {
            // Finished dragging. We draw the line segment
            end = mousePos;
            dragging = false;

            var startRay = cam.ScreenPixelToRay( start );
            var endRay = cam.ScreenPixelToRay( end );

            // Raise OnLineDrawnEvent
            OnLineDrawn?.Invoke(
                startRay.Project( cam.ZNear ),
                endRay.Project( cam.ZNear ),
                endRay.Forward );
        }

        if ( IsRmbDown() )
        {
            Draw( cam.Hud );
        }
    }


    /// <summary>
    /// Draws the line in viewport space using start and end variables
    /// </summary>
    private void Draw( HudPainter painter )
    {
        painter.DrawLine( start, end, 3, Color.White );
    }
}
