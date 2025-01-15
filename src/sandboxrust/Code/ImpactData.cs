using System;

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

	public ImpactDataStruct ToLocalStruct( GameTransform transform, Vector3 boundsMin, Vector3 boundsScale )
	{
		var localPos = transform.World.PointToLocal( Position );
		var localDir = transform.World.NormalToLocal( ImpactDirection ).Normal;
		var texPos = (localPos - boundsMin) * boundsScale;

		return new ImpactDataStruct
		{
			Position = texPos,
			Radius = ImpactRadius,
			Direction = localDir,
			Strength = ImpactStrength,
			ConeAngle = ImpactPenetrationConeDeg * MathF.PI / 180.0f,
			MaxPenetration = ImpactPenetrationStrength,
			Enabled = 1.0f,
			Padding = 0
		};
	}
}
