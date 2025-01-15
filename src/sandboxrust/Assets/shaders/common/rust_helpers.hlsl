#ifndef RUST_HELPERS_H
#define RUST_HELPERS_H

float3 g_vBoundsMin < Attribute("BoundsMin"); >;
float3 g_vBoundsScale < Attribute("BoundsScale"); >;

// Converts from texture space (0-1) to object space using bounds 
float3 TextureToObjectSpace(float3 texPos, float3 boundsMin, float3 boundsScale)
{
    return texPos / boundsScale + boundsMin;
}

// Converts from object space to texture space (0-1)
float3 ObjectToTextureSpace(float3 objectPos, float3 boundsMin, float3 boundsScale)
{
    return (objectPos - boundsMin) * boundsScale;
}

// Converts from object space to texel coordinates for RWTexture3D
uint3 ObjectToTexelSpace(float3 objectPos, float3 boundsMin, float3 boundsScale, int resolution)
{
    float3 normalizedPos = ObjectToTextureSpace(objectPos, boundsMin, boundsScale);
    return uint3(normalizedPos * resolution);
}

// Converts from texel coordinates to object space
float3 TexelToObjectSpace(uint3 texelPos, float3 boundsMin, float3 boundsScale, int resolution)
{
    float3 normalizedPos = float3(texelPos) / resolution;
    return TextureToObjectSpace(normalizedPos, boundsMin, boundsScale);
}

#endif 