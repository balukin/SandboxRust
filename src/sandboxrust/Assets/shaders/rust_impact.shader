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
    // Output texture needs to use the g_t prefix and Attribute syntax
    RWTexture3D<float3> g_tDataTexture < Attribute("DataTexture"); >;
    float3 g_vImpactPosition < Attribute("ImpactPosition"); >;
    float g_flImpactRadius < Attribute("ImpactRadius"); >;
    float g_flImpactStrength < Attribute("ImpactStrength"); >;
    static const uint TextureSize = 64;

    [numthreads(8, 8, 8)]
    void MainCs(uint uGroupIndex : SV_GroupIndex, uint3 vThreadId : SV_DispatchThreadID)
    {
        // Convert thread ID to position in 3D texture space
        float3 pos = float3(vThreadId) / TextureSize;
        
        // Calculate distance from impact
        float dist = length(pos - g_vImpactPosition);
        
        if (dist <= g_flImpactRadius)
        {
            float factor = 1.0 - (dist / g_flImpactRadius);
            float3 current = g_tDataTexture[vThreadId];
            
            // Modify moisture (G channel) based on impact
            current.g = min(1.0, current.g + factor * g_flImpactStrength);
            
            g_tDataTexture[vThreadId] = current;
        }
    } 
}
