using System;
using Sandbox;

public record struct ImpactData(Vector3 position, Vector3 surfaceNormal, Vector3 impactDirection, WeaponType weaponType);
public sealed class SurfaceImpactHandler : Component
{
	public delegate void ImpactHandler(ImpactData impactData);

	/// <summary>
	/// Raised whenever an impact is detected.
	/// </summary>
	public event ImpactHandler OnImpact;

	public void HandleImpact(ImpactData impactData)
	{
		OnImpact?.Invoke(impactData);
	}
}
