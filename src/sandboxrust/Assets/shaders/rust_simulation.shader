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
    #include "common/shared.hlsl"

    Texture3D<float4> Source;
    RWTexture3D<float4> Target;
    static const uint TextureSize = 64;

    [numthreads(8, 8, 8)]
    void MainCs(uint3 id : SV_DispatchThreadID)
    {
        if (any(id >= TextureSize)) return;
        
        // TODO: Implement actual rust simulation
        // For now just copy source to target
        Target[id] = Source[id];
    } 
}
