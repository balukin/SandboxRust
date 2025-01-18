// RGB Reminder: R for rust, G for moisture, B for structural strength
// Simulation rules:
// - Simulates rust formation in 3D space using a volume texture
// - Water and rust can propagate across the entire volume, ignoring mesh topology
// MOISTURE
// - Water drips downward along world space Z axis with evaporation
// - Creates natural streaking patterns using procedural noise
// - Global evaporation affects all moisture over time
// RUST
// - Rust growth depends on:
//     - Moisture levels
//     - Atmospheric oxygen (configurable global parameter called "Rusting Speed" in the UI)
//     - Neighboring rust influence (configurable spread factor)
//     - Base growth rate constant
// STRUCTURAL
// - Structural integrity decreases as rust accumulates
// - Degradation begins when rust level exceeds 50%
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
    float g_flNeighborRustInfluence < Attribute("NeighborRustInfluence"); Range(0.0, 5.0); Default(0.2); >;
    int g_iVolumeResolution < Attribute("VolumeResolution"); Default(64); >;

    // Rust growth simulation constants
    // TODO: Expose those with some UI sliders or something
    static const float WATER_DRIP_EVAPORATION_RATE = 0.12;
    static const float WATER_GLOBAL_EVAPORATION_RATE = 0.01; // Maybe this should be based on the amount of water vapor in the air?
    static const float RUST_SPREAD_RADIUS = 1.5;             // How far rust spreads in texels
    static const float MIN_MOISTURE = 0.1;                   // Minimum moisture needed for rust to form
    static const float RUST_GROWTH_RATE = 0.05;              // Base rate of rust formation
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
        return WATER_DRIP_EVAPORATION_RATE * (64.0 / float(g_iVolumeResolution));
    }

    float GetStreakBias(float3 texCoord)
    {
        // x and y represent the horizontal plane, we can use that to create streaky patterns vertically
        float x = texCoord.x + 1;
        float y = texCoord.y + 1;
        float z = texCoord.z + 1;
        
        // Primary streaks
        float streak1 = frac(x * 8.0 + sin(y * 12.0) * 0.6);
        float streak2 = frac(y * 7.0 + cos(x * 10.0) * 0.8);
        float variation = sin(x * 15.0) * cos(y * 13.0) * 0.5 + 0.5;
        
        // Mix it with scientifcally obtained ratios
        float primaryMask = smoothstep(0.4, 0.6, streak1) * smoothstep(0.35, 0.65, streak2);
        
        // Blend with some variation and return the bias that will result in the drip-down
        // being more pronounced in certain points of the xz plane
        return lerp(0.6, 1.2, primaryMask * variation);
    }

    // Modify SimulateWaterDrip function
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
        
        // Apply streak bias to the moisture calculation
        float streakBias = GetStreakBias(texCoord);
        return averageMoisture * streakBias * (1.0 - GetWaterDripEvaporationRate());
    }

    // Simulates rust growth based on moisture, oxygen, and neighboring rust
    float SimulateRustGrowth(uint3 vThreadId)
    {
        float3 currentState = g_tSource[vThreadId];
        float currentRust = currentState.r;
        float currentMoisture = currentState.g;

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
        // - Moisture level
        // - Oxygen level
        // - Neighboring rust
        float moistureGrowth  = currentMoisture / 2.0;
        float neighborInfluence = neighboringRust * g_flNeighborRustInfluence;
        float newGrowth = RUST_GROWTH_RATE * g_flOxygenLevel * (moistureGrowth + neighborInfluence);

        // Clamp the result to valid range
        return saturate(currentRust + newGrowth);
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
