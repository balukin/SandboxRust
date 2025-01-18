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

    #define S_TRANSLUCENT 1
    
    CreateInputTexture3D( RustDataRead, Srgb, 8, "", "_rustdata_read", "Material,10/10", Default3( 1.0, 1.0, 1.0 ) );
    CreateTexture3D( g_tRustDataRead ) < Channel( RGB, Box( RustDataRead ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;    

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


    float4 MainPs( PixelInput i ) : SV_Target0
    {    
        float3 samplePos = ObjectToTextureSpace(i.vPositionOs, g_vBoundsMin, g_vBoundsScale);
        float3 rustData = g_tRustDataRead.Sample(g_sPointClamp, samplePos);
        
        return float4(rustData.rgb, 1.0);
    }
}
