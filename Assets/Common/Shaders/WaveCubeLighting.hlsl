#ifndef MILLION_OBJECTS_WAVE_CUBE_LIGHTING_INCLUDED
#define MILLION_OBJECTS_WAVE_CUBE_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Shared shading for every backend: one directional light, Lambert, ambient from the sky probe.
// Deliberately cheap so fill rate never hides the CPU-side differences between steps.
half3 ShadeCube(float3 normalWS, half3 albedo)
{
    float3 normal = normalize(normalWS);
    Light mainLight = GetMainLight();
    half diffuse = saturate(dot(normal, mainLight.direction));
    half3 ambient = SampleSH(normal);
    return albedo * (mainLight.color * diffuse + ambient);
}

#endif
