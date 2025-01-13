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


    RWTexture3D<float3> g_tSource < Attribute("SourceTexture"); >;
    RWTexture3D<float3> g_tTarget < Attribute("TargetTexture"); >;
    
    [numthreads(8, 8, 8)]
    void MainCs(uint3 localId : SV_GroupThreadID, uint3 groupId : SV_GroupID, uint3 vThreadId : SV_DispatchThreadID)
    {
        g_tTarget[vThreadId] = g_tSource[vThreadId];
    }
}
