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
    float3 g_vImpactDirection < Attribute("ImpactDirection"); >;
    float g_flImpactRadius < Attribute("ImpactRadius"); >;
    float g_flImpactStrength < Attribute("ImpactStrength"); >;
    float g_flConeAngle < Attribute("ConeAngleRad"); >;
    float g_flMaxPenetration < Attribute("MaxPenetration"); >;
    static const uint TextureSize = 64;

    [numthreads(8, 8, 8)]
    void MainCs(uint uGroupIndex : SV_GroupIndex, uint3 vThreadId : SV_DispatchThreadID)
    {
        float3 pos = float3(vThreadId) / TextureSize;
        float3 toPoint = pos - g_vImpactPosition;
        
        // Calculate spherical impact
        float distanceFromImpact = length(toPoint);
        float sphericalFactor = 0;
        if(distanceFromImpact <= g_flImpactRadius)
        {
            sphericalFactor = 1.0 - (distanceFromImpact / g_flImpactRadius);
        }
        
        // Calculate conical penetration
        float depth = dot(toPoint, g_vImpactDirection);
        float coneFactor = 0;
        
        if (depth > 0 && depth <= g_flMaxPenetration)
        {
            float3 projection = g_vImpactPosition + g_vImpactDirection * depth;
            float perpDist = length(pos - projection);
            float coneRadius = depth * tan(g_flConeAngle);
            
            if (perpDist <= coneRadius)
            {
                float depthFactor = 1.0 - (depth / g_flMaxPenetration);
                float radialFactor = 1.0 - (perpDist / coneRadius);
                coneFactor = depthFactor * radialFactor;
            }
        }
        
        // Combine both effects
        float factor = max(sphericalFactor, coneFactor) * 0.5;

        // TODO: make the areas with higher rust (higher R value) more susceptible to losing structural strength (B value)
        
        if(factor > 0)
        {
            float3 current = g_tDataTexture[vThreadId];
            // B channel holds structural strength            
            current.b = min(1.0, current.b + factor * g_flImpactStrength);
            g_tDataTexture[vThreadId] = current;
        }
    }
}