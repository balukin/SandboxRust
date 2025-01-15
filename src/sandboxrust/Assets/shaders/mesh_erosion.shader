MODES
{
    Default();
}

COMMON
{
    #include "common/shared.hlsl"
    #include "common/rust_helpers.hlsl"
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
    float3 g_vErosionTarget < Attribute("ErosionTarget"); >;
    float g_flErosionStrength < Attribute("ErosionStrength"); >;
    int g_uVertexCount < Attribute("VertexCount"); >;

    [numthreads(64, 1, 1)]
    void MainCs(uint3 vThreadId : SV_DispatchThreadID, uint3 vGroupId : SV_GroupID, uint3 vGroupThreadId : SV_GroupThreadID)
    {
        uint vertexIndex = vThreadId.x;    
        VertexData vertex = g_InputVertices[vertexIndex];

        // Convert vertex position to object space
        float3 vPositionOs = float3(vertex.x, vertex.y, vertex.z);

        // Sample rust data
        float3 samplePos = ObjectToTextureSpace(vPositionOs, g_vBoundsMin, g_vBoundsScale);
        float3 rustData = g_tRustData.SampleLevel(g_sBilinearClamp, samplePos, 0).rgb;

        // Get structural strength from the blue channel
        float structuralStrength = rustData.b;

        // DEBUG: FROM rust data but the first 1.0-0.5 loss has no effect
        structuralStrength = saturate(1 - rustData.r + 0.5f);

        // Calculate erosion offset
        float3 offsetDirection = normalize(g_vErosionTarget - vPositionOs);
        float erosionAmount = (1.0 - structuralStrength) * g_flErosionStrength;
        float3 offset = offsetDirection * erosionAmount;

        // Apply offset to vertex position
        vPositionOs += offset;

        // Write updated vertex data
        vertex.x = vPositionOs.x;
        vertex.y = vPositionOs.y;
        vertex.z = vPositionOs.z;
        g_OutputVertices[vertexIndex] = vertex;
    }
}
