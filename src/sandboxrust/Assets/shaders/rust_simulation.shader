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
    

    /// Simulate water dripping from the top of the object down (z axis)
    float SimulateWaterDrip(uint3 globalId)
    {
        // Check if we are at the top of the texture
        if (globalId.z >= TextureSize - 1)
        {
            return 0.0; // No dripping from above the top layer
        }

        float totalMoisture = 0.0;
        
        // Sample three texels above the current one, offset horizontally
        for (int xOffset = -1; xOffset <= 1; xOffset++)
        {
            uint3 samplePos = globalId + uint3(xOffset, 0, 1);
            
            // Ensure we don't sample outside the texture bounds on the sides
            samplePos.x = clamp(samplePos.x, 0, TextureSize - 1);
            totalMoisture += g_tSource[samplePos].g;
        }
 
        // Calculate the average moisture     
        float averageMoisture = totalMoisture / 3.0;

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
    
        // If above threshold, mark as wet, otherwise keep the original moisture        
        center.g = dripMoisture > center.g ? dripMoisture : center.g;

        g_tTarget[vThreadId] = center;
    }
}
