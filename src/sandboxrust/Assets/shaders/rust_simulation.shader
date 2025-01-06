// RGB Reminder: R for rust, G for moisture, B for structural strength
// Simulation rules:
// - ignore the fact that object may be concave, simulation can run across the gaps of the mesh as if it was a box
// - treat everyting as water-permeable 
// MOISURE
// - Water drips from the top of the object down (z axis) in world space
// - 
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
    // This could probably benefit from a groupshared cache of some sorts
    // but it's a PITA to handle boundary conditions

    #include "common/shared.hlsl"

    // TODO: make this dynamic?
    static const uint TextureSize = 64;


    RWTexture3D<float3> g_tSource < Attribute("SourceTexture"); >;
    RWTexture3D<float3> g_tTarget < Attribute("TargetTexture"); >;
    
    // Gonna need that to find out where the gravational up is for the water to drip down
    float4x4 g_matWorldToObject < Attribute("WorldToObject"); >;

    // Get texture-space direction that corresponds to world-space up
    float3 GetTextureSpaceUp()
    {
        // Transform world up vector (0,0,1) to object space
        float3 worldUp = float3(0, 0, 1);
        float3 localUp = mul(g_matWorldToObject, float4(worldUp, 0.0)).xyz;
        return normalize(localUp);
    }

    // Simulate how the water drips down from the top of the object
    float SimulateWaterDrip(uint3 globalId)
    {
        float3 texCoord = float3(globalId) / float(TextureSize);
        float3 upDir = GetTextureSpaceUp();
        
        // Sample step size in texture space (1 texel)
        float stepSize = 2.0 / TextureSize;
        
        float totalMoisture = 0.0;
        float3 sampleBase = texCoord + upDir * stepSize;
        
        // Sample in a pointing-down pyramid pattern from the upper 4 corners of the pyramid base
        // Previously I was sampling 3 points but it can line up unfortunately and cause the water not to drip down on one surface
        // That's still not 100% correct but my brain is now fried in all 3 dimensions so it's good enough
        static const float2 offsets[4] = {
            float2(-0.5, -0.5),
            float2(-0.5,  0.5),
            float2( 0.5, -0.5),
            float2( 0.5,  0.5)
        };
        
        // Create basis vectors for sampling plane
        float3 rightDir = normalize(float3(upDir.z, 0, -upDir.x));
        float3 forwardDir = cross(upDir, rightDir);
                
        for (int i = 0; i < 4; i++)
        {    
            float3 samplePos = sampleBase + 
                rightDir * (offsets[i].x * stepSize) +
                forwardDir * (offsets[i].y * stepSize);
            
            // Ensure we don't sample outside the texture bounds on the sides
            if (all(samplePos >= 0.0) && all(samplePos <= 1.0))
            {
                uint3 sampleTexel = uint3(samplePos * TextureSize);
                totalMoisture += g_tSource[sampleTexel].g;
            }
        }

        // Calculate the average moisture     
        float averageMoisture = totalMoisture / 4.0;

        // Attenuate the drip to simulate evaporation or something
        return averageMoisture * 0.90;
    }

    [numthreads(8, 8, 8)]
    void MainCs(uint3 localId : SV_GroupThreadID, uint3 groupId : SV_GroupID, uint3 vThreadId : SV_DispatchThreadID)
    {       
        if (any(vThreadId >= TextureSize)) return;

        float3 center = g_tSource[vThreadId];

        // Simulate water dripping and update moisture (G channel)
        float dripMoisture = SimulateWaterDrip(vThreadId);
        center.g = dripMoisture > center.g ? dripMoisture : center.g;

        // Simulate rust growth and update rust (R channel)
        // float rustGrowth = SimulateRustGrowth(vThreadId);
        // center.r = rustGrowth > center.r ? rustGrowth : center.r;

        g_tTarget[vThreadId] = center;
    }
}
