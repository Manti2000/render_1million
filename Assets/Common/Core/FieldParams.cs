namespace MillionObjects
{
    /// <summary>
    /// Blittable snapshot of the shared field parameters. Safe to copy into Burst jobs and mirrored
    /// field for field by the compute shader of the indirect backend, so every rung animates the
    /// identical picture from the identical numbers.
    /// </summary>
    public struct FieldParams
    {
        /// <summary>Distance between neighbouring cube centres on the grid.</summary>
        public float Spacing;
        /// <summary>Uniform scale applied to the unit cube mesh.</summary>
        public float CubeScale;
        /// <summary>Peak vertical displacement of the travelling wave.</summary>
        public float WaveAmplitude;
        /// <summary>World-space length of one full wave cycle.</summary>
        public float WaveLength;
        /// <summary>Wave phase advance per second, in radians.</summary>
        public float WaveSpeed;
        /// <summary>Cube rotation around the Y axis per second, in radians.</summary>
        public float RotationSpeed;
        /// <summary>Spring constant pulling a displaced cube back to its rest position.</summary>
        public float SpringStiffness;
        /// <summary>Velocity damping of the spring-back motion.</summary>
        public float SpringDamping;
        /// <summary>Peak push force the attractor sphere applies at its centre.</summary>
        public float AttractorStrength;
    }
}
