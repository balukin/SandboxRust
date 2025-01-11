using System;
using Sandbox;

public sealed class SurfaceImpactHandler : Component
{
	public delegate void ImpactHandler( ImpactData impactData );

	/// <summary>
	/// Raised whenever an impact is detected.
	/// </summary>
	public event ImpactHandler OnImpact;

	public void HandleImpact( ImpactData impactData )
	{
		OnImpact?.Invoke( impactData );
	}
}
