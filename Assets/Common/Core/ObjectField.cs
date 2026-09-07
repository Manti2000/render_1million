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
        /// <summary>Brightness levels per hue in the palette.</summary>
        public const int PaletteLevels = 32;
        /// <summary>Number of colours in the palette: <see cref="PaletteLevels"/> brightness levels for each of two hues, interleaved even/odd.</summary>
        public const int PaletteSize = PaletteLevels * 2;
        /// <summary>Azimuth sectors the hue alternates over; six sectors give three arms of each hue.</summary>
        private const int HueSectors = 6;
        /// <summary>Spread of the hashed brightness jitter in palette levels, centred on the radial level; mirrored in HLSL.</summary>
        private const int JitterLevels = 8;
        /// <summary>Knuth multiplicative hash constant used for palette selection; mirrored in HLSL.</summary>
        private const uint PaletteHashMultiplier = 2654435761u;
        /// <summary>Per-object rotation phase offset in radians, so neighbours do not rotate in lockstep.</summary>
        private const float RotationPhaseStep = 0.618f;
        /// <summary>Random offset from the lattice point as a fraction of the spacing, per axis; mirrored in HLSL.</summary>
        private const float JitterFraction = 0.35f;
        /// <summary>Radius of the whirlpool's open eye as a fraction of the swirl radius; mirrored in HLSL.</summary>
        private const float SwirlCoreFraction = 0.3f;
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

        /// <summary>The cloud's visible mass: the jittered lattice cube itself, without wave or funnel travel. What the camera frames.</summary>
        public static Bounds FramingBounds(int count, in FieldParams parameters)
        {
            float extent = (SideLength(count) + 2f * JitterFraction) * parameters.Spacing + parameters.CubeScale;
            return new Bounds(Vector3.zero, new Vector3(extent, extent, extent));
        }

        /// <summary>World-space bounds of everything that can be drawn, including jitter, wave and funnel travel, for culling.</summary>
        public static Bounds FieldBounds(int count, in FieldParams parameters)
        {
            float extent = (SideLength(count) + 2f * JitterFraction) * parameters.Spacing + parameters.CubeScale + 2f * parameters.SwirlRadius * SwirlCoreFraction;   // the open eye pushes the outer cubes outward
            float height = extent + (parameters.WaveAmplitude + math.max(parameters.SwirlDepth, 0f)) * 2f;   // wave travel and funnel dip both leave the lattice
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
        /// <summary>
        /// How much the whirlpool affects a rest position: 1 at the centre, 1/2 at the swirl radius,
        /// then a 1/r² tail like a real vortex, so the whole cloud turns slowly while the core spins.
        /// Zero when disabled.
        /// </summary>
        public static float SwirlWeight(float3 restPosition, in FieldParams parameters)
        {
            if (parameters.SwirlRadius <= 0f)
                return 0f;
            return 1f / (1f + math.lengthsq(restPosition.xz) / (parameters.SwirlRadius * parameters.SwirlRadius));
        }

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

        /// <summary>Final world position: rest position, plus the whirlpool, plus wave height, plus any spring displacement.</summary>
        public static float3 Position(float3 restPosition, float3 displacement, float time, in FieldParams parameters)
        {
            return restPosition + displacement + Swirl(restPosition, time, parameters) + new float3(0f, WaveHeight(restPosition, time, parameters), 0f);
        }

        /// <summary>
        /// Whirlpool at the cloud's centre: cubes are pushed outward from the axis to open an eye, rotated
        /// around the axis with the vortex speed profile, and pulled down into a funnel with the same
        /// profile. Stateless, so every rung pays the same few operations per object.
        /// </summary>
        public static float3 Swirl(float3 restPosition, float time, in FieldParams parameters)
        {
            if (parameters.SwirlRadius <= 0f)
                return float3.zero;
            float2 planar = restPosition.xz;
            float weight = SwirlWeight(restPosition, parameters);
            float core = parameters.SwirlRadius * SwirlCoreFraction;
            float radius = math.length(planar);
            float2 opened = planar * (math.sqrt(radius * radius + core * core) / math.max(radius, 1e-4f));
            float angle = WrapAngle(time * parameters.SwirlSpeed * weight);
            math.sincos(angle, out float sin, out float cos);
            float2 rotated = new float2(opened.x * cos - opened.y * sin, opened.x * sin + opened.y * cos);
            return new float3(rotated.x - planar.x, -parameters.SwirlDepth * weight, rotated.y - planar.y);
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
        /// <summary>
        /// Palette slot of an object. The palette is laid out as 32 brightness levels × 2 hues (even slots
        /// one hue, odd slots the other). Brightness comes from the horizontal distance to the centre, dark
        /// at the edge and white at the vortex, plus up to <see cref="JitterLevels"/> levels of hashed
        /// jitter so a colour region is speckled rather than flat. The hue comes from the azimuth sector
        /// of the rest position, alternating every 60 degrees, with one cube in eight flipped to the other
        /// hue for extra grain: the vortex's differential rotation then shears those sectors into visible
        /// spiral arms, which a radius-only colouring could never show. Static per object, so no rung
        /// recolours anything per frame.
        /// </summary>
        public static int PaletteIndex(int index, float3 restPosition, in FieldParams parameters)
        {
            uint hash = (uint)index * PaletteHashMultiplier;
            float radial = math.length(restPosition.xz) / math.max(parameters.FieldExtent * 0.5f, 1e-3f);
            int level = (int)math.floor(math.saturate(1f - radial) * (PaletteLevels - 1) + 0.5f);
            int jitter = (int)(hash >> 29) * JitterLevels / 8;
            float azimuth = math.atan2(restPosition.z, restPosition.x) + math.PI;
            int hue = (int)math.floor(azimuth / (2f * math.PI) * HueSectors) & 1;
            if (((hash >> 26) & 7u) == 0u)
                hue ^= 1;
            return math.clamp(level + jitter - JitterLevels / 2, 0, PaletteLevels - 1) * 2 + hue;
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

        /// <summary>
        /// Push force on a point from the attractor sphere, falling off linearly to its radius. The
        /// force scales with the radius, so <paramref name="strength"/> is "force per unit of radius":
        /// the carved hole stays proportionally deep whether the sphere is 5 or 50 units wide.
        /// </summary>
        private static float3 AttractorPush(float3 position, float4 attractor, float strength)
        {
            if (attractor.w <= 0f)
                return float3.zero;
            float3 offset = position - attractor.xyz;
            float distance = math.length(offset);
            if (distance >= attractor.w || distance < 1e-4f)
                return float3.zero;
            float falloff = 1f - distance / attractor.w;
            return offset / distance * (strength * falloff * attractor.w);
        }
        #endregion
    }
}
