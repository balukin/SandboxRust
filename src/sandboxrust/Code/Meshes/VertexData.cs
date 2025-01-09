using System.Runtime.InteropServices;

[StructLayout( LayoutKind.Sequential )]
public readonly struct VertexData
{
    public readonly float X;
    public readonly float Y;
    public readonly float Z;
    public readonly float Padding;

    public VertexData( Vector3 position, float padding )
    {
        X = position.x;
        Y = position.y;
        Z = position.z;
        Padding = padding;
    }

    public VertexData( Vector3 position )
        : this( position, 0 )
    {
    }

    public VertexData( float x, float y, float z )
        : this( new Vector3( x, y, z ), 0 )
    {
    }

    public VertexData( Vertex vertex )
        : this( vertex.Position, 0 )
    {
    }

    public Vector3 ToVector3()
    {
        return new Vector3( X, Y, Z );
    }

    public override string ToString()
    {
        return $"VertexData( {X}, {Y}, {Z} )";
    }
}