public class ImpactData
{
	public required Vector3 Position {get; init;}
	public required Vector3 SurfaceNormal {get; init;}
	public required Vector3 ImpactDirection {get; init;}
	public required float ImpactRadius {get; init;}
	public required float ImpactStrength {get; init;}
	public required float ImpactPenetrationStrength {get; init;}
	public required float ImpactPenetrationConeDeg {get; init;}
	public required WeaponType WeaponType {get; init;}
}
