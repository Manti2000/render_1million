#ifndef MILLION_OBJECTS_WAVE_CUBE_LIGHTING_INCLUDED
#define MILLION_OBJECTS_WAVE_CUBE_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Height band of the field, set as globals by FieldFraming: the cloud's top and the deepest point the
// funnel reaches. Cubes darken the further below the top they sit, so the inside of the whirlpool's
// tube reads dark against the white rim from any distance.
float _ShadeTopY;
float _ShadeBottomY;
float _ShadeFloor;   // brightness multiplier at the very bottom, e.g. 0.35

half DepthShade(float3 positionWS)
{
    float span = _ShadeTopY - _ShadeBottomY;
    if (span <= 1e-3)
        return 1.0;
    float depth = saturate((_ShadeTopY - positionWS.y) / span);
    return lerp(1.0, _ShadeFloor, pow(depth, 1.25));   // gentle power: the top stays bright, the darkening stretches down the tube
}

// Shared shading for every backend: one directional light, Lambert, ambient from the sky probe, and
// the height-based darkening. Deliberately cheap so fill rate never hides the CPU-side differences.
half3 ShadeCube(float3 normalWS, float3 positionWS, half3 albedo)
{
    float3 normal = normalize(normalWS);
    Light mainLight = GetMainLight();
    half diffuse = saturate(dot(normal, mainLight.direction));
    half3 ambient = SampleSH(normal);
    return albedo * (mainLight.color * diffuse + ambient) * DepthShade(positionWS);
}

#endif
