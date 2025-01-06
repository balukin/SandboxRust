using System;
using Sandbox;

/// <summary>
/// Test component that makes objects move in a simple pattern.
/// Useful for verifying coordinate mappings and shader behaviors.
/// </summary>
public sealed class TestMovement : Component
{
    /// <summary>
    /// If true, the object will rotate around the up axis.
    /// </summary>
    [Property]
    public bool IsRotating { get; set; }

    [Property]
    public float RotationSpeedDegPerSec { get; set; } = 30f;

    protected override void OnUpdate()
    {
        base.OnUpdate();

        if ( IsRotating )
        {
            var rotation = Rotation.FromAxis( Vector3.Up, Time.Delta * RotationSpeedDegPerSec );
            Transform.Local = Transform.Local.RotateAround( Transform.Local.Position, rotation );
        }
    }
} 