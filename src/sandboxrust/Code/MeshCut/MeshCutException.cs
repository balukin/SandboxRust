using System;

internal class MeshCutException : Exception
{
	public MeshCutException()
	{
	}

	public MeshCutException( string message ) : base( message )
	{
	}

	public MeshCutException( string message, Exception innerException ) : base( message, innerException )
	{
	}
}
