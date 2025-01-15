using System.Runtime.InteropServices;

/// <summary>
/// GPU-friendly struct for storing impact data.
/// </summary>
public struct ImpactDataStruct
{
	// Aligned to 3*16 total; probably compiler would align it, too but it's a lovely puzzle game
	public Vector3 Position;      // 12 bytes
	public float Radius;          // 4 

	public Vector3 Direction;     // 12 bytes
	public float Strength;        // 4 

	public float ConeAngle;       // 4
	public float MaxPenetration;  // 4
	public float Enabled;         // 4
	public float Padding;         // 4

	public static ImpactDataStruct None => new ImpactDataStruct { Enabled = 0 };

	public override string ToString()
	{
		return $"Position: {Position}, Radius: {Radius}, Direction: {Direction}, Strength: {Strength}, ConeAngle: {ConeAngle}, MaxPenetration: {MaxPenetration}, Enabled: {Enabled}, Padding: {Padding}";
	}
}
