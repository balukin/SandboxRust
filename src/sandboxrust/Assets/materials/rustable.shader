FEATURES
{
    #include "common/features.hlsl"
}

MODES
{
    VrForward();
    Depth();
    ToolsVis( S_MODE_TOOLS_VIS );
}

COMMON
{
	#include "common/shared.hlsl"
	#include "common/classes/AmbientLight.hlsl"	
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		
		// float3x4 mtw = CalculateInstancingObjectToWorldMatrix( i );
		
		return FinalizeVertex( o );
	}
}

PS
{
	#include "vr_environment_map.fxc"    
	#include "light_probe_volume.fxc"
	#include "envmap_filtering.hlsl"


	// Finds the closest environment map and samples it, loosely based on base library AmbientLight::FromEnvMapProbe
    float3 SampleMetallicReflection(float3 WorldPosition, float2 ScreenPosition, float3 WorldNormal, float3 ViewDir)
    {
        // Calculate reflection vector
        float3 reflectDir = reflect(-ViewDir, WorldNormal);
        
        // Get the tile based on screen position
        const uint2 tile = GetTileForScreenPosition(ScreenPosition);
        
        float3 reflectionColor = float3(0, 0, 0);
        float closestDistance = 100000.0f; // Initialize with a large value
        uint closestIndex = 0;
        
        // Iterate over environment maps in the tile
        for (uint i = 0; i < GetNumEnvMaps(tile); i++)
        {
            const uint index = TranslateEnvMapIndex(i, tile);
			const float edgeFeathering = EnvMapFeathering(index);

            
            // Transform world position to environment map local space
            const float3 localPos = mul(float4(WorldPosition, 1.0f), EnvMapWorldToLocal(index)).xyz;
            
            float distance = length(localPos);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index;
            }
        }
        
        // Sample only the closest environment map
        if (closestDistance < 100000.0f)
        {
			// What is this second parameter? Seems to sampling mip level?
			// 0.1 should be good enough for semi-blurry reflections
            reflectionColor = SampleEnvironmentMapLevel(reflectDir, 0.1f, closestIndex);		
        }

        return reflectionColor;
    }

    #include "common/pixel.hlsl"

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 absoluteWorldPos = i.vPositionWithOffsetWs + g_vCameraPositionWs;
		float3 viewDir = normalize(g_vCameraPositionWs - i.vPositionWithOffsetWs);
		
		float3 reflectionColor = SampleMetallicReflection(absoluteWorldPos, i.vPositionSs.xy / i.vPositionSs.w, i.vNormalWs, viewDir);
		
		Material m = Material::From( i );
		m.Albedo.rgb = reflectionColor;
		return ShadingModelStandard::Shade( i, m );
	}

	
}
