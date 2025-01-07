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

#endif 