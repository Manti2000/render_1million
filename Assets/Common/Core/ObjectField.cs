using Unity.Mathematics;
using UnityEngine;

namespace MillionObjects
{
    /// <summary>
    /// The shared maths of the cube field: where an object rests, how it moves, what colour it gets.
    /// Objects fill a cubic lattice with a hashed jitter per object, so a million of them read as a
    /// cloud rather than a moiré sheet. Every backend calls these functions (or their HLSL mirror in
    /// the indirect backend), so the picture is identical across steps by construction.
    /// </summary>
    public static class ObjectField
    {
        #region Constants
        /// <summary>Number of colours in the palette. Palette indices are hashed into this range.</summary>
        public const int PaletteSize = 16;
        /// <summary>Knuth multiplicative hash constant used for palette selection; mirrored in HLSL.</summary>
        private const uint PaletteHashMultiplier = 2654435761u;
        /// <summary>Per-object rotation phase offset in radians, so neighbours do not rotate in lockstep.</summary>
        private const float RotationPhaseStep = 0.618f;
        /// <summary>Random offset from the lattice point as a fraction of the spacing, per axis; mirrored in HLSL.</summary>
        private const float JitterFraction = 0.35f;
        #endregion

        #region Layout
        /// <summary>Cubes per edge of the cubic lattice that holds <paramref name="count"/> objects.</summary>
        public static int SideLength(int count)
        {
            int side = (int)math.ceil(math.pow(math.max(count, 1), 1f / 3f));
            while (side * side * side < count)   // guard against pow rounding just under a perfect cube
                side++;
            return side;
        }

        /// <summary>Lattice cell of an object: x fastest, then y, then z.</summary>
        public static int3 LatticeCell(int index, int sideLength)
        {
            int layer = sideLength * sideLength;
            return new int3(index % sideLength, (index / sideLength) % sideLength, index / layer);
        }

        /// <summary>Rest position of an object: its lattice cell centred on the origin, plus a hashed jitter so the lattice reads as a cloud.</summary>
        public static float3 RestPosition(int index, int sideLength, float spacing)
        {
            float half = (sideLength - 1) * 0.5f;
            float3 cell = LatticeCell(index, sideLength);
            return (cell - half + Jitter(index) * JitterFraction) * spacing;
        }

        /// <summary>World-space bounds of the whole cloud including jitter and wave travel, for cameras and culling.</summary>
        public static Bounds FieldBounds(int count, in FieldParams parameters)
        {
            float extent = (SideLength(count) + 2f * JitterFraction) * parameters.Spacing + parameters.CubeScale;
            float height = extent + parameters.WaveAmplitude * 2f;
            return new Bounds(Vector3.zero, new Vector3(extent, height, extent));
        }

        /// <summary>Object index whose lattice cell contains a world position, or -1 when outside the lattice.</summary>
        public static int IndexAt(float3 worldPosition, int sideLength, float spacing)
        {
            float half = (sideLength - 1) * 0.5f;
            int3 cell = (int3)math.round(worldPosition / spacing + half);
            if (math.any(cell < 0) || math.any(cell >= sideLength))
                return -1;
            return cell.z * sideLength * sideLength + cell.y * sideLength + cell.x;
        }

        /// <summary>Deterministic per-object offset in [-1, 1] per axis; mirrored in HLSL.</summary>
        private static float3 Jitter(int index)
        {
            uint i = (uint)index * 3u;
            return new float3(HashToUnit(i), HashToUnit(i + 1u), HashToUnit(i + 2u)) * 2f - 1f;
        }

        /// <summary>Integer hash to [0, 1]; the same bit mix as the HLSL mirror.</summary>
        private static float HashToUnit(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value / 4294967295f;
        }
        #endregion

        #region Motion
        /// <summary>Vertical offset of the travelling diagonal wave at a rest position.</summary>
        public static float WaveHeight(float3 restPosition, float time, in FieldParams parameters)
        {
            float phase = WrapAngle((restPosition.x + restPosition.z) / parameters.WaveLength * (2f * math.PI));
            float travel = WrapAngle(time * parameters.WaveSpeed);
            return parameters.WaveAmplitude * math.sin(phase - travel);
        }

        /// <summary>Rotation of an object around the Y axis at a point in time.</summary>
        public static quaternion Rotation(int index, float time, in FieldParams parameters)
        {
            float angle = WrapAngle(time * parameters.RotationSpeed) + WrapAngle(index * RotationPhaseStep);
            return quaternion.RotateY(angle);
        }

        /// <summary>
        /// Wraps an angle into [0, 2π). Both trig arguments grow without bound (index phase up to
        /// hundreds of thousands of radians at 1M objects, time forever); GPU sin/cos return garbage
        /// for large arguments, which drew 16% of the indirect step's cubes at zero scale. Mirrored in HLSL.
        /// </summary>
        private static float WrapAngle(float radians)
        {
            const float twoPi = 2f * math.PI;
            return radians - math.floor(radians / twoPi) * twoPi;
        }

        /// <summary>Final world position: rest position, plus wave height, plus any spring displacement.</summary>
        public static float3 Position(float3 restPosition, float3 displacement, float time, in FieldParams parameters)
        {
            return restPosition + displacement + new float3(0f, WaveHeight(restPosition, time, parameters), 0f);
        }

        /// <summary>Complete local-to-world matrix for an object with no spring displacement.</summary>
        public static float4x4 LocalToWorld(int index, int sideLength, float time, in FieldParams parameters)
        {
            float3 rest = RestPosition(index, sideLength, parameters.Spacing);
            return LocalToWorld(index, rest, float3.zero, time, parameters);
        }

        /// <summary>Complete local-to-world matrix for an object with an explicit spring displacement.</summary>
        public static float4x4 LocalToWorld(int index, float3 restPosition, float3 displacement, float time, in FieldParams parameters)
        {
            float3 position = Position(restPosition, displacement, time, parameters);
            return float4x4.TRS(position, Rotation(index, time, parameters), parameters.CubeScale);
        }
        #endregion

        #region Colour
        /// <summary>Palette slot of an object; a multiplicative hash so neighbours differ.</summary>
        public static int PaletteIndex(int index)
        {
            return (int)(((uint)index * PaletteHashMultiplier) >> 28);
        }
        #endregion

        #region Attractor spring
        /// <summary>
        /// Advances one object's spring state by one step. The attractor is (x, y, z, radius); a
        /// radius of zero or less disables pushing so the spring only settles back to rest.
        /// </summary>
        public static void IntegrateSpring(ref float3 displacement, ref float3 velocity, float3 restPosition, float4 attractor, float deltaTime, in FieldParams parameters)
        {
            float3 push = AttractorPush(restPosition + displacement, attractor, parameters.AttractorStrength);
            float3 acceleration = push - parameters.SpringStiffness * displacement - parameters.SpringDamping * velocity;
            velocity += acceleration * deltaTime;
            displacement += velocity * deltaTime;
        }

        /// <summary>Push force on a point from the attractor sphere, falling off linearly to its radius.</summary>
        private static float3 AttractorPush(float3 position, float4 attractor, float strength)
        {
            if (attractor.w <= 0f)
                return float3.zero;
            float3 offset = position - attractor.xyz;
            float distance = math.length(offset);
            if (distance >= attractor.w || distance < 1e-4f)
                return float3.zero;
            float falloff = 1f - distance / attractor.w;
            return offset / distance * (strength * falloff);
        }
        #endregion
    }
}
