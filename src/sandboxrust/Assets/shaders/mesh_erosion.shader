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

    cbuffer ImpactParameters : register(b0)
    {
        // First 16-byte vector
        float3 ImpactPosition;        // 12 bytes
        float ImpactRadius;           // 4 bytes

        // Second 16-byte vector
        float3 ImpactDirection;       // 12 bytes
        float ImpactStrength;         // 4 bytes

        // Third 16-byte vector
        float ImpactConeAngle;        // 4 bytes
        float ImpactMaxPenetration;   // 4 bytes
        float ImpactEnabled;          // 4 bytes
        float ImpactPadding;          // 4 bytes
    };

    // Input data
    RWStructuredBuffer<VertexData> g_InputVertices < Attribute("InputVertices"); >;
    RWTexture3D<float3> g_tRustData < Attribute("RustData"); >; 

    // Output data 
    RWStructuredBuffer<VertexData> g_OutputVertices < Attribute("OutputVertices"); >;

    // Parameters
    float3 g_vErosionTarget < Attribute("ErosionTarget"); >;
    float g_flErosionStrength < Attribute("ErosionStrength"); >;
    int g_uVertexCount < Attribute("VertexCount"); >;
    int g_iVolumeResolution < Attribute("VolumeResolution"); Default(64); >;

    float3 CalculateImpactDisplacement(float3 positionOs, float3 positionTexSpace, float3 rustData)
    {
        // Early exit if impact is not enabled
        if (ImpactEnabled < 0.5f)
            return float3(0, 0, 0);
        
        // Calculate distance from impact point in texture space
        float3 toPoint = positionTexSpace - ImpactPosition;
        float distance = length(toPoint);
        
        // First check spherical impact
        float sphericalFactor = 0;
        if (distance <= ImpactRadius)
        {
            sphericalFactor = 1.0 - (distance / ImpactRadius);
        }
        
        // Then check conical penetration
        float depth = dot(toPoint, ImpactDirection);
        float coneFactor = 0;
        
        if (depth > 0 && depth <= ImpactMaxPenetration)
        {
            float3 projection = ImpactPosition + ImpactDirection * depth;
            float perpDist = length(positionTexSpace - projection);
            float coneRadius = depth * tan(ImpactConeAngle);
            
            if (perpDist <= coneRadius)
            {
                float depthFactor = 1.0 - (depth / ImpactMaxPenetration);
                float radialFactor = 1.0 - (perpDist / coneRadius);
                coneFactor = depthFactor * radialFactor;
            }
        }
        
        // Combine both effects
        float positionalFactor = max(sphericalFactor, coneFactor);
        
        if (positionalFactor > 0)
        {
            // More rusted areas (higher R value) deform more easily
            float rustFactor = lerp(1.0, 2.0, rustData.r);
            
            // Less structural strength (lower B value) means more deformation
            float strengthFactor = 1.0 - rustData.b;
            
            // Calculate final displacement
            const float additionalStrengthMultiplier = 1.0; 
            float strength = ImpactStrength * positionalFactor * rustFactor * strengthFactor * additionalStrengthMultiplier;
            
            return ImpactDirection * strength;
        }
        
        return float3(0, 0, 0);
    }

    float3 CalculateErosionDisplacement(float3 positionOs, float structuralStrength)
    {
        // Structural strength below 70% starts erosion (displacing vertices towards the target)
        float effectiveDamage = max(0, 1.0f - structuralStrength - 0.3f);

        // Calculate erosion displacement with distance-based falloff
        float3 toTarget = g_vErosionTarget - positionOs;
        float3 erosionDirection = normalize(toTarget);
        float distanceToTarget = length(toTarget);
        
        float distanceFalloff = saturate(distanceToTarget / 100.0f);
        float erosionAmount = effectiveDamage * g_flErosionStrength * distanceFalloff;
        
        return erosionDirection * erosionAmount;
    }

    [numthreads(64, 1, 1)]
    void MainCs(uint3 vThreadId : SV_DispatchThreadID, uint3 vGroupId : SV_GroupID, uint3 vGroupThreadId : SV_GroupThreadID)
    {
        uint vertexIndex = vThreadId.x;    
        VertexData vertex = g_InputVertices[vertexIndex];

        // Convert vertex position to object space
        float3 vPositionOs = float3(vertex.x, vertex.y, vertex.z);

        // Sample rust data
        float3 samplePos = ObjectToTextureSpace(vPositionOs, g_vBoundsMin, g_vBoundsScale);
        uint3 coord = ObjectToTexelSpace(vPositionOs, g_vBoundsMin, g_vBoundsScale, g_iVolumeResolution);
        float3 rustData = g_tRustData[coord];

        // Get structural strength from the blue channel
        float structuralStrength = rustData.b;
        
        // Calculate erosion and impact displacements
        float3 erosionOffset = CalculateErosionDisplacement(vPositionOs, structuralStrength);
        float3 impactOffset = CalculateImpactDisplacement(vPositionOs, samplePos, rustData);

        // Apply combined displacement
        vPositionOs += erosionOffset + impactOffset;

        // Write updated vertex data
        vertex.x = vPositionOs.x;
        vertex.y = vPositionOs.y;
        vertex.z = vPositionOs.z;
        g_OutputVertices[vertexIndex] = vertex;
    }
}
