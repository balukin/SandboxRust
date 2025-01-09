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

    // Had problems with alignment of float3 and Vector3 and was getting garbage data
    struct VertexData
    {
        float x;
        float y;
        float z;
        float padding;
    };

    // Input data
    RWStructuredBuffer<VertexData> g_InputVertices < Attribute("InputVertices"); >;
    Texture3D<float3> g_tRustData < Attribute("RustData"); >; 

    // Output data 
    RWStructuredBuffer<VertexData> g_OutputVertices < Attribute("OutputVertices"); >;

    // Parameters
    float3 g_vMeshCenter < Attribute("MeshCenter"); >;
    float3 g_vBoundsMin < Attribute("BoundsMin"); >;
    float3 g_vBoundsScale < Attribute("BoundsScale"); >; 
    float g_flErosionStrength < Attribute("ErosionStrength"); >;
    int g_uVertexCount < Attribute("VertexCount"); >;

    [numthreads(64, 1, 1)]
    void MainCs(uint3 vThreadId : SV_DispatchThreadID, uint3 vGroupId : SV_GroupID, uint3 vGroupThreadId : SV_GroupThreadID)
    {
        uint vertexIndex = vThreadId.x;    
        VertexData vertex = g_InputVertices[vertexIndex];
        vertex.x += 1;
        g_OutputVertices[vertexIndex] = vertex;
    }
}
