using System;
using Sandbox;

public sealed class SurfaceImpactHandler : Component
{
	public delegate void ImpactHandler(Vector3 position, Vector3 normal, WeaponType weaponType);

	/// <summary>
	/// Raised whenever an impact is detected.
	/// </summary>
	public event ImpactHandler OnImpact;

	public void HandleImpact(Vector3 position, Vector3 normal, WeaponType weaponType)
	{
		OnImpact?.Invoke(position, normal, weaponType);
	}
}
