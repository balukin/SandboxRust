MODES
{
    Default();
}

COMMON
{
	#include "common/shared.hlsl"
}

CS
{
    RWTexture3D<float3> g_tDataTexture < Attribute("DataTexture"); >;
    float3 g_vImpactPosition < Attribute("ImpactPosition"); >;
    float g_flImpactRadius < Attribute("ImpactRadius"); >;
    float g_flImpactStrength < Attribute("ImpactStrength"); >;
    int g_iVolumeResolution < Attribute("VolumeResolution"); Default(64); >;
    
    float3 g_vSprayDirection < Attribute("SprayDirection"); >;
    float g_flMaxPenetration < Attribute("MaxPenetration"); Default(0.1); >;
    
    [numthreads(8, 8, 8)]
    void MainCs(uint uGroupIndex : SV_GroupIndex, uint3 vThreadId : SV_DispatchThreadID)
    {
        // Convert thread ID to position in 3D texture space
        float3 pos = float3(vThreadId) / g_iVolumeResolution;
        
        // Calculate spherical spray effect
        float dist = length(pos - g_vImpactPosition);
        float sphericalFactor = 0;
        
        if (dist <= g_flImpactRadius)
        {
            sphericalFactor = 1.0 - (dist / g_flImpactRadius);
        }
        
        // Calculate cylindrical penetration
        float cylinderFactor = 0;
        float3 toPoint = pos - g_vImpactPosition;
        float depth = dot(toPoint, g_vSprayDirection);
        
        // Allow for negative penetration up to 25% of max penetration
        float negativeAllowance = g_flMaxPenetration * 0.25;
        if (depth >= -negativeAllowance && depth <= g_flMaxPenetration)
        {
            float3 projection = g_vImpactPosition + g_vSprayDirection * depth;
            float perpDist = length(pos - projection);
            
            if (perpDist <= g_flImpactRadius)
            {
                float depthFactor = 1.0 - ((depth + negativeAllowance) / (g_flMaxPenetration + negativeAllowance));
                cylinderFactor = depthFactor;
            }
        }
        
        // Combine both effects
        float factor = max(sphericalFactor, cylinderFactor);
        
        if (factor > 0)
        {
            float3 current = g_tDataTexture[vThreadId];
            current.g = min(1.0, current.g + factor * g_flImpactStrength);
            g_tDataTexture[vThreadId] = current;
        }
    } 
}
