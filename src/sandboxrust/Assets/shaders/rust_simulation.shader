// RGB Reminder: R for rust, G for moisture, B for structural strength
// Simulation rules:
// - ignore the fact that object may be concave, simulation can run across the gaps of the mesh as if it was a box
// - treat everything as water-permeable 
// MOISURE
// - Water drips from the top of the object down (z axis) in world space
// CORROSION
// - Rust begins to grow on the surface of the object that is wet
// - Simplified rust growth is a function of
//     - surface moisture
//     - atmospheric oxygen 
//     - amount of water vapor in the air (sort of a global rustability factor)
//     - neighboring rust (made-up physics: rust-damaged surface exposes more iron to the air)
// - We ignore time factor, oxygen amount is linearly related to rusting speed so adding more oxygen will speed up the rusting process without
//   having to implement time control (which would have to affect physics simulation speed, too)
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

    RWTexture3D<float3> g_tSource < Attribute("SourceTexture"); >;
    RWTexture3D<float3> g_tTarget < Attribute("TargetTexture"); >;
    
    // Gonna need that to find out where the gravational up is for the water to drip down
    float4x4 g_matWorldToObject < Attribute("WorldToObject"); >;

    // Global parameters for rust simulation
    float g_flOxygenLevel < Attribute("OxygenLevel"); Range(0.0, 1.0); Default(0.2); >;
    float g_flWaterVapor < Attribute("WaterVapor"); Range(0.0, 1.0); Default(0.5); >;
    int g_iVolumeResolution < Attribute("VolumeResolution"); Default(64); >;

    // Rust growth simulation constants
    // TODO: Expose those with some UI sliders or something
    static const float WATER_DRIP_EVAPORATION_RATE = 0.12;
    static const float WATER_GLOBAL_EVAPORATION_RATE = 0.01; // Maybe this should be based on the amount of water vapor in the air?
    static const float RUST_SPREAD_RADIUS = 1.5;             // How far rust spreads in texels
    static const float MIN_MOISTURE = 0.1;                   // Minimum moisture needed for rust to form
    static const float RUST_GROWTH_RATE = 0.05;              // Base rate of rust formation
    static const float NEIGHBOR_RUST_INFLUENCE = 0.3;        // How much nearby rust accelerates growth
    static const int SAMPLE_COUNT = 6;                       // Number of neighboring points to check

    // Get texture-space direction that corresponds to world-space up
    float3 GetTextureSpaceUp()
    {
        // Transform world up vector (0,0,1) to object space
        float3 worldUp = float3(0, 0, 1);
        float3 localUp = mul(g_matWorldToObject, float4(worldUp, 0.0)).xyz;
        return normalize(localUp);
    }

    // Attenuate the drip to simulate evaporation or something
    float GetWaterDripEvaporationRate()
    {
        return 0.12 * (64.0 / float(g_iVolumeResolution));
    }

    // Simulate how the water drips down from the top of the object
    float SimulateWaterDrip(uint3 globalId)
    {
        float3 texCoord = float3(globalId) / float(g_iVolumeResolution);
        float3 upDir = GetTextureSpaceUp();
        
        // Scale the sampling pattern based on resolution
        float baseStepSize = 2.0 / 64.0;
        float stepSize = baseStepSize * (64.0 / float(g_iVolumeResolution));
        
        // Scale the offset pattern based on resolution
        float offsetScale = min(1.0, float(g_iVolumeResolution) / 32.0); // Starts reducing scale below 32^3
        
        // Sample in a pointing-down pyramid pattern from the upper 4 corners of the pyramid base
        // Previously I was sampling 3 points but it can line up unfortunately and cause the water not to drip down on one surface
        // That's still not 100% correct but my brain is now fried in all 3 dimensions so it's good enough
        static const float2 offsets[4] = {
            float2(-0.5, -0.5),
            float2(-0.5,  0.5),
            float2( 0.5, -0.5),
            float2( 0.5,  0.5)
        };
        
        float totalMoisture = 0.0;
        float validSamples = 0.0;
        float3 sampleBase = texCoord + upDir * stepSize;
        
        // Create basis vectors for sampling plane
        float3 rightDir = normalize(float3(upDir.z, 0, -upDir.x));
        float3 forwardDir = cross(upDir, rightDir);
                
        for (int i = 0; i < 4; i++)
        {    
            float3 samplePos = sampleBase + 
                rightDir * (offsets[i].x * stepSize * offsetScale) +  // Apply scale to offset
                forwardDir * (offsets[i].y * stepSize * offsetScale);
            
            // Widen the valid sampling range for lower resolutions
            float boundary = lerp(0.01, 0.1, 1.0 - offsetScale);
            if (all(samplePos >= boundary) && all(samplePos <= (1.0 - boundary)))
            {
                uint3 sampleTexel = uint3(samplePos * g_iVolumeResolution);
                totalMoisture += g_tSource[sampleTexel].g;
                validSamples++;
            }
        }

        // Ensure we always get at least one valid sample
        validSamples = max(validSamples, 1.0);
        float averageMoisture = totalMoisture / validSamples;
        
        return averageMoisture * (1.0 - GetWaterDripEvaporationRate());
    }

    // Simulates rust growth based on moisture, oxygen, and neighboring rust
    float SimulateRustGrowth(uint3 vThreadId)
    {
        float3 currentState = g_tSource[vThreadId];
        float currentRust = currentState.r;
        float currentMoisture = currentState.g;

        // No rust growth if moisture is below threshold
        if (currentMoisture < MIN_MOISTURE)
            return currentRust;

        // Sample neighboring points in a cube pattern
        float neighboringRust = 0.0;
        static const int3 offsets[SAMPLE_COUNT] = {
            int3(-1,  0,  0),
            int3( 1,  0,  0),
            int3( 0, -1,  0),
            int3( 0,  1,  0),
            int3( 0,  0, -1),
            int3( 0,  0,  1)
        };

        // Accumulate rust values from neighbors
        for (int i = 0; i < SAMPLE_COUNT; i++)
        {
            int3 samplePos = int3(vThreadId) + offsets[i];
            
            // Check texture bounds
            if (all(samplePos >= 0) && all(samplePos < int3(g_iVolumeResolution, g_iVolumeResolution, g_iVolumeResolution)))
            {
                neighboringRust += g_tSource[samplePos].r;
            }
        }
        
        // Average the neighboring rust
        neighboringRust /= float(SAMPLE_COUNT);

        // Calculate rust growth based on all factors:
        // - Base growth rate
        // - Moisture level (direct multiplier)
        // - Oxygen level (controls reaction speed)
        // - Water vapor (environmental factor)
        // - Neighboring rust (... some physical explanation here)
        float rustGrowth = currentRust + (
            RUST_GROWTH_RATE *  
            currentMoisture * 
            g_flOxygenLevel * 
            g_flWaterVapor * 
            (1.0 + neighboringRust * NEIGHBOR_RUST_INFLUENCE)
        );

        // Clamp the result to valid range
        return saturate(rustGrowth);
    }

    float SimulateStructuralDetoration(uint3 vThreadId, float rust, float moisture, float structuralStrength)
    {
        // Once rusting reaches 0.5, start to lower structural strength
        return lerp(structuralStrength, saturate(1 - rust + 0.5f), step(0.5f, rust));
    }

    [numthreads(8, 8, 8)]
    void MainCs(uint3 localId : SV_GroupThreadID, uint3 groupId : SV_GroupID, uint3 vThreadId : SV_DispatchThreadID)
    {       
        if (any(vThreadId >= g_iVolumeResolution)) return;

        float3 center = g_tSource[vThreadId];

        float prevMoisture = center.g;
        
        // Simulate water dripping and update moisture (G channel)
        // Note: If it was ever >0, it will never reach absolute 0
        float dripMoisture = SimulateWaterDrip(vThreadId);
        float moisture = max(
            prevMoisture * (1.0 - WATER_GLOBAL_EVAPORATION_RATE),  // Natural evaporation of existing moisture
            dripMoisture                                           // New moisture from dripping
        );

        // Simulate rust growth (R channel)
        float rustGrowth = SimulateRustGrowth(vThreadId);
        float rust = rustGrowth > center.r ? rustGrowth : center.r;
        float structuralStrength = center.b;

        structuralStrength = SimulateStructuralDetoration(vThreadId, rust, moisture, structuralStrength);

        g_tTarget[vThreadId] = float3(rust, moisture, structuralStrength);
    }
}
