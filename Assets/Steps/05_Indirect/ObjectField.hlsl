#ifndef MILLION_OBJECTS_OBJECT_FIELD_INCLUDED
#define MILLION_OBJECTS_OBJECT_FIELD_INCLUDED

// HLSL mirror of Assets/Common/Core/ObjectField.cs, function by function. Rung 5 is the step where
// the behavioural code moves off the CPU entirely, so this file is the whole simulation: the compute
// shader calls it to advance the field, the draw shader calls it to pick a palette slot. Every
// function below names the C# member it mirrors; the two must be edited together or the rungs stop
// drawing the identical picture.

// Mirrors ObjectField.PaletteSize.
#define OBJECT_FIELD_PALETTE_SIZE 16
// Mirrors ObjectField.PaletteHashMultiplier (Knuth multiplicative hash).
#define OBJECT_FIELD_PALETTE_HASH_MULTIPLIER 2654435761u
// Mirrors ObjectField.RotationPhaseStep.
#define OBJECT_FIELD_ROTATION_PHASE_STEP 0.618
// Mirrors ObjectField.JitterFraction.
#define OBJECT_FIELD_JITTER_FRACTION 0.35
// Local copy of pi so this file compiles inside a compute shader that includes no URP library.
#define OBJECT_FIELD_PI 3.14159265358979323846

// Mirrors the FieldParams struct in Assets/Common/Core/FieldParams.cs, field for field and in order.
struct FieldParams
{
    float Spacing;
    float CubeScale;
    float WaveAmplitude;
    float WaveLength;
    float WaveSpeed;
    float RotationSpeed;
    float SpringStiffness;
    float SpringDamping;
    float AttractorStrength;
};

// Mirrors ObjectField.SideLength (cube root, rounded up, corrected for pow rounding).
uint ObjectFieldSideLength(uint count)
{
    uint side = (uint)ceil(pow((float)max(count, 1u), 1.0 / 3.0));
    while (side * side * side < count)
        side++;
    return side;
}

// Mirrors ObjectField.HashToUnit.
float ObjectFieldHashToUnit(uint value)
{
    value ^= value >> 16;
    value *= 0x7feb352du;
    value ^= value >> 15;
    value *= 0x846ca68bu;
    value ^= value >> 16;
    return value / 4294967295.0;
}

// Mirrors ObjectField.Jitter.
float3 ObjectFieldJitter(uint index)
{
    uint i = index * 3u;
    return float3(ObjectFieldHashToUnit(i), ObjectFieldHashToUnit(i + 1u), ObjectFieldHashToUnit(i + 2u)) * 2.0 - 1.0;
}

// Mirrors ObjectField.LatticeCell.
uint3 ObjectFieldLatticeCell(uint index, uint sideLength)
{
    uint layer = sideLength * sideLength;
    return uint3(index % sideLength, (index / sideLength) % sideLength, index / layer);
}

// Mirrors ObjectField.RestPosition.
float3 ObjectFieldRestPosition(uint index, uint sideLength, float spacing)
{
    float halfExtent = (sideLength - 1u) * 0.5;
    float3 cell = (float3)ObjectFieldLatticeCell(index, sideLength);
    return (cell - halfExtent + ObjectFieldJitter(index) * OBJECT_FIELD_JITTER_FRACTION) * spacing;
}

// Mirrors ObjectField.WrapAngle. GPU sin/cos return garbage for large arguments; without this 16% of
// the cubes at 1M were drawn at zero scale and popped in and out as time advanced.
float ObjectFieldWrapAngle(float radians)
{
    const float twoPi = 2.0 * OBJECT_FIELD_PI;
    return radians - floor(radians / twoPi) * twoPi;
}

// Mirrors ObjectField.WaveHeight.
float ObjectFieldWaveHeight(float3 restPosition, float time, FieldParams parameters)
{
    float phase = ObjectFieldWrapAngle((restPosition.x + restPosition.z) / parameters.WaveLength * (2.0 * OBJECT_FIELD_PI));
    float travel = ObjectFieldWrapAngle(time * parameters.WaveSpeed);
    return parameters.WaveAmplitude * sin(phase - travel);
}

// Mirrors ObjectField.Rotation. The C# side builds quaternion.RotateY(angle); the matrix below is
// that quaternion expanded, so no quaternion type is needed on the GPU.
float3x3 ObjectFieldRotation(uint index, float time, FieldParams parameters)
{
    float angle = ObjectFieldWrapAngle(time * parameters.RotationSpeed) + ObjectFieldWrapAngle(index * OBJECT_FIELD_ROTATION_PHASE_STEP);
    float sinAngle, cosAngle;
    sincos(angle, sinAngle, cosAngle);
    return float3x3(
        cosAngle, 0.0, sinAngle,
        0.0, 1.0, 0.0,
        -sinAngle, 0.0, cosAngle);
}

// Mirrors ObjectField.Position.
float3 ObjectFieldPosition(float3 restPosition, float3 displacement, float time, FieldParams parameters)
{
    return restPosition + displacement + float3(0.0, ObjectFieldWaveHeight(restPosition, time, parameters), 0.0);
}

// Mirrors float4x4.TRS with a uniform scale, as used by ObjectField.LocalToWorld. Rows are written
// explicitly so the matrix is read back with mul(matrix, float4(positionOS, 1)) in the vertex shader.
float4x4 ObjectFieldCompose(float3 position, float3x3 rotation, float scale)
{
    float3x3 rotationScale = rotation * scale;
    return float4x4(
        float4(rotationScale[0], position.x),
        float4(rotationScale[1], position.y),
        float4(rotationScale[2], position.z),
        float4(0.0, 0.0, 0.0, 1.0));
}

// Mirrors ObjectField.LocalToWorld(int, float3, float3, float, in FieldParams).
float4x4 ObjectFieldLocalToWorld(uint index, float3 restPosition, float3 displacement, float time, FieldParams parameters)
{
    float3 position = ObjectFieldPosition(restPosition, displacement, time, parameters);
    return ObjectFieldCompose(position, ObjectFieldRotation(index, time, parameters), parameters.CubeScale);
}

// Mirrors ObjectField.PaletteIndex.
uint ObjectFieldPaletteIndex(uint index)
{
    return (index * OBJECT_FIELD_PALETTE_HASH_MULTIPLIER) >> 28;
}

// Mirrors ObjectField.AttractorPush. Written without early returns so every path initialises the
// result; the compute compiler otherwise warns about a potentially uninitialised variable.
float3 ObjectFieldAttractorPush(float3 position, float4 attractor, float strength)
{
    float3 offset = position - attractor.xyz;
    float distanceToCentre = length(offset);
    bool inactive = attractor.w <= 0.0 || distanceToCentre >= attractor.w || distanceToCentre < 1e-4;
    float falloff = 1.0 - distanceToCentre / max(attractor.w, 1e-4);
    float3 push = offset / max(distanceToCentre, 1e-4) * (strength * falloff);
    return inactive ? float3(0.0, 0.0, 0.0) : push;
}

// Mirrors ObjectField.IntegrateSpring, including the semi-implicit Euler step order.
void ObjectFieldIntegrateSpring(inout float3 displacement, inout float3 velocity, float3 restPosition, float4 attractor, float deltaTime, FieldParams parameters)
{
    float3 push = ObjectFieldAttractorPush(restPosition + displacement, attractor, parameters.AttractorStrength);
    float3 acceleration = push - parameters.SpringStiffness * displacement - parameters.SpringDamping * velocity;
    velocity += acceleration * deltaTime;
    displacement += velocity * deltaTime;
}

#endif
