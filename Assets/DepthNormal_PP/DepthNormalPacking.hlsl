#ifndef DEPTH_NORMAL_PP_PACKING_INCLUDED
#define DEPTH_NORMAL_PP_PACKING_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

// Code 0 is the clear value. Codes 1..65535 cover the entire linear depth interval.
float4 EncodeOutlineSample(float3 normalVS, float eyeDepth)
{
    float2 octNormal = PackNormalOctQuadEncode(normalize(normalVS)) * 0.5 + 0.5;
    float depthCode = round(saturate(eyeDepth / _ProjectionParams.z) * 65534.0) + 1.0;
    float highByte = floor(depthCode / 256.0);
    return float4(octNormal, float2(highByte, depthCode - highByte * 256.0) / 255.0);
}

// xyz: unit view normal; w: linear eye depth / far, or negative for background.
float4 DecodeOutlineSample(float4 packed)
{
    float2 depthBytes = round(packed.ba * 255.0);
    float depthCode = dot(depthBytes, float2(256.0, 1.0));
    return float4(UnpackNormalOctQuadEncode(packed.rg * 2.0 - 1.0), (depthCode - 1.0) / 65534.0);
}

#endif
