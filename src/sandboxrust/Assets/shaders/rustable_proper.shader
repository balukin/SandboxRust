FEATURES
{
    #include "common/features.hlsl"
}

MODES
{
    VrForward();
    Depth();
}

COMMON
{
	#include "common/shared.hlsl"
	#include "common/classes/AmbientLight.hlsl"	 
    #include "common/rust_helpers.hlsl"
    // https://github.com/Auburn/FastNoiseLite under MIT from Jordan Peck and other contributors
    #include "common/fast_noise_lite.hlsl" 

    #define S_TRANSLUCENT 1
    CreateInputTexture3D( RustDataRead, Srgb, 8, "", "_rustdata_read", "Material,10/10", Default3( 1.0, 1.0, 1.0 ) );
    CreateTexture3D( g_tRustDataRead ) < Channel( RGB, Box( RustDataRead ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;    

    float3 g_fFlashlightPosition < Attribute("FlashlightPosition"); >;
    float3 g_fFlashlightDirection < Attribute("FlashlightDirection"); >;
    float g_fFlashlightIntensity < Attribute("FlashlightIntensity"); >;
    float g_fFlashlightAngle < Attribute("FlashlightAngle"); >;
    bool g_bSoftRustEnabled < Attribute("SoftRustEnabled"); >;
    int g_iVolumeResolution < Attribute("VolumeResolution"); Default(64); >;
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
}; 

struct PixelInput 
{
	#include "common/pixelinput.hlsl"
    float3 vPositionOs : TEXCOORD8;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
        PixelInput o = ProcessVertex( i );
        o.vPositionOs = i.vPositionOs.xyz;
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
        float closestDistance = 100000.0f;
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

    RenderState(BlendEnable, true);
    RenderState(SrcBlend, SRC_ALPHA);
    RenderState(DstBlend, INV_SRC_ALPHA);
    RenderState(BlendOp, ADD);
    RenderState(SrcBlendAlpha, ONE);
    RenderState(DstBlendAlpha, INV_SRC_ALPHA);
    RenderState(BlendOpAlpha, ADD);

    RenderState(DepthWriteEnable, false);
    RenderState(DepthEnable, true);
    RenderState(DepthBias, 500); 
    RenderState(DepthFunc, GREATER);

    struct RustDetail
    {
        float3 color;
        float3 normal;
    };

    struct MoistureEffect
    {
        float specularIntensity;
        float specularSharpness;
        float3 colorTint;
    };

    float3 FilteredVolumeSample(float3 pos)
    {
        // PCF-like sampling but for 3D texture to make jaggies go away
        float3 offsets[8] = {
            float3(-0.5, -0.5, -0.5),
            float3( 0.5, -0.5, -0.5),
            float3(-0.5,  0.5, -0.5),
            float3( 0.5,  0.5, -0.5),
            float3(-0.5, -0.5,  0.5),
            float3( 0.5, -0.5,  0.5),
            float3(-0.5,  0.5,  0.5),
            float3( 0.5,  0.5,  0.5)
        };

        float3 sum = 0.0;
        for (int j = 0; j < 8; j++)
        {
            float3 neighborPos = pos + offsets[j] / float3(g_iVolumeResolution, g_iVolumeResolution, g_iVolumeResolution);
            sum += g_tRustDataRead.Sample(g_sBilinearClamp, neighborPos).rgb;
        }
        return sum * 0.125;
    }

    RustDetail GenerateRustDetail(float3 pos, float baseRust, float3 normal)
    {
        // Create noise states
        fnl_state perlinNoise = fnlCreateState(12345);
        fnl_state cellularNoise = fnlCreateState(67890);
        
        // Configure Perlin (low frequency)
        perlinNoise.frequency = 0.2;  // Lower = larger features
        perlinNoise.noise_type = FNL_NOISE_PERLIN;
        
        // Configure Cellular/Voronoi (high frequency)
        cellularNoise.frequency = 5.0;  // Higher = smaller features
        cellularNoise.noise_type = FNL_NOISE_CELLULAR;
        cellularNoise.cellular_distance_func = FNL_CELLULAR_DISTANCE_EUCLIDEANSQ;
        cellularNoise.cellular_return_type = FNL_CELLULAR_RETURN_TYPE_DISTANCE2;
        
        // Sample noises and remap from [-1,1] to [0,1]
        float lowFreq = fnlGetNoise3D(perlinNoise, pos.x, pos.y, pos.z) * 0.5 + 0.5;
        float highFreq = fnlGetNoise3D(cellularNoise, pos.x, pos.y, pos.z) * 0.5 + 0.5;
        float combined = lerp(lowFreq, highFreq, 0.5);
        float rustAmount = baseRust * combined;
        
        // Add color variation
        fnl_state colorNoise = fnlCreateState(11111);
        colorNoise.frequency = 2.0;
        colorNoise.noise_type = FNL_NOISE_PERLIN;

        float colorVal = fnlGetNoise3D(colorNoise, pos.x, pos.y, pos.z) * 0.5 + 0.5;
        float3 baseColor = lerp(float3(1.0, 0.4, 0.0), float3(0.8, 0.0, 0.0), colorVal);

        RustDetail detail;
        detail.color = baseColor * rustAmount;

        // Simple normal perturbation from noise
        const float PerturbationMultiplier = 0.1;
        float3 rustPerturbation = float3((combined - 0.5) * 2.0, (colorVal - 0.5) * 2.0, (rustAmount - 0.5) * 2.0) * PerturbationMultiplier;
        detail.normal = normalize(normal + rustPerturbation);

        return detail;
    }

    MoistureEffect CalculateMoistureEffect(float moisture)
    {
        MoistureEffect effect;
        
        // Increase reflectivity with moisture
        effect.specularIntensity = lerp(1.0, 2.0, moisture);
        
        // Make reflections sharper/more focused when wet
        effect.specularSharpness = lerp(1.0, 1.5, moisture);
        
        // Add slight blue tint that darkens the surface when wet
        // Hard to get right since most of the effect comes from blend factor
        float3 dryColor = float3(1.0, 1.0, 1.0);
        float3 wetColor = float3(0.85, 0.85, 0.95);
        effect.colorTint = lerp(dryColor, wetColor, moisture * 4);
        
        return effect;
    }

    float4 ShadeStandard(PixelInput i, float3 worldNormal, float3 baseColor, float baseRust, float moisture)
    {
        Material m = Material::From(i);

        // Apply rust details
        m.Normal = worldNormal;
        m.Albedo = baseColor;        

        // Simple take: treat moisture and rust as influences on metalness/roughness
        // This actually works pretty well
        m.Metalness = saturate(moisture);
        m.Roughness = lerp(1.0f, 0.3f, saturate(baseRust));
        m.Opacity = max(baseRust, moisture / 1.4f);

        // Return standard shading
        return ShadingModelStandard::Shade(m);
    }


	float4 MainPs( PixelInput i ) : SV_Target0
	{  
        float3 samplePos = ObjectToTextureSpace(i.vPositionOs, g_vBoundsMin, g_vBoundsScale);
        float3 absoluteWorldPos = i.vPositionWithOffsetWs + g_vCameraPositionWs;

        float3 rustData = g_bSoftRustEnabled ? FilteredVolumeSample(samplePos) : g_tRustDataRead.Sample(g_sBilinearClamp, samplePos).rgb;
        
        float baseRust = rustData.r; 
        float moisture = rustData.g;

        // // DEBUG
        // MoistureEffect effect = CalculateMoistureEffect(moisture);
        // return float4(effect.colorTint, 1.0);
        // // END DEBUG
        RustDetail finalRustDetail = GenerateRustDetail(i.vPositionOs, baseRust, i.vNormalWs);

        // Standard shading for the rust effect
        float4 standardColor = ShadeStandard(i, finalRustDetail.normal, finalRustDetail.color, baseRust, moisture);

        // Calculate view direction for reflections
        float3 viewDir = normalize(g_vCameraPositionWs - absoluteWorldPos);
        
        // Sample environment reflections
        float3 reflectionColor = SampleMetallicReflection(absoluteWorldPos, i.vPositionSs.xy, i.vNormalWs, viewDir);
        
        // Blend reflection based on moisture (wetter = more reflective)
        // Also reduce reflection intensity on very rusty areas
        float reflectionStrength = moisture * (1.0f - baseRust * 0.7f);
        float3 finalColor = lerp(standardColor.rgb, reflectionColor, reflectionStrength * 0.3f);

        // Blend it with rust clearly overlaying the base color and moisture a tiny bit more transparent
        // Probably could use a more sophisticated blend mode config instead
        return float4(finalColor, max(baseRust, moisture/1.4));
	}
}
