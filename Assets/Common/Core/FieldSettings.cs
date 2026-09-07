using UnityEngine;

namespace MillionObjects
{
    /// <summary>
    /// Single source of truth for how the cube field looks and moves. Shared by every backend so the
    /// only variable between steps is the rendering architecture.
    /// </summary>
    [CreateAssetMenu(menuName = "Million Objects/Field Settings", fileName = "FieldSettings")]
    public class FieldSettings : ScriptableObject
    {
        #region Inspector fields
        [Header("Layout")]
        [SerializeField, Tooltip("Distance between neighbouring cube centres.")]
        private float _spacing = 1.5f;
        [SerializeField, Tooltip("Uniform scale of each cube.")]
        private float _cubeScale = 1f;

        [Header("Wave")]
        [SerializeField, Tooltip("Peak vertical displacement of the travelling wave.")]
        private float _waveAmplitude = 1.5f;
        [SerializeField, Tooltip("World-space length of one wave cycle.")]
        private float _waveLength = 60f;
        [SerializeField, Tooltip("Wave phase advance per second, in radians.")]
        private float _waveSpeed = 1.2f;
        [SerializeField, Tooltip("Rotation around Y per second, in radians.")]
        private float _rotationSpeed = 0.6f;

        [Header("Attractor spring")]
        [SerializeField, Tooltip("Spring constant pulling displaced cubes back to rest.")]
        private float _springStiffness = 12f;
        [SerializeField, Tooltip("Velocity damping of the spring-back motion.")]
        private float _springDamping = 2.5f;
        [SerializeField, Tooltip("Push force of the attractor sphere at its centre, per unit of its radius. With stiffness 12, a value of 8 carves a hole about two thirds of the radius deep.")]
        private float _attractorStrength = 8f;

        [Header("Whirlpool")]
        [SerializeField, Tooltip("Radius at which the whirlpool's speed has halved, as a fraction of the cloud's extent. Zero disables it.")]
        private float _swirlRadiusFraction = 0.25f;
        [SerializeField, Tooltip("Angular speed at the whirlpool's centre, in radians per second; fades out with distance.")]
        private float _swirlSpeed = 0.6f;
        [SerializeField, Tooltip("Depth of the funnel at the whirlpool's centre as a fraction of the cloud's extent.")]
        private float _swirlDepthFraction = 0.35f;

        [Header("Rendering")]
        [SerializeField, Tooltip("Mesh drawn for every object. Unit cube by default.")]
        private Mesh _cubeMesh;
        [SerializeField, Tooltip("Material using the MillionObjects/WaveCube shader. Backends derive palette variants from it.")]
        private Material _cubeMaterial;
        [SerializeField, Tooltip("Exactly 16 colours; each object picks one by hashing its index.")]
        private Color[] _palette = new Color[ObjectField.PaletteSize];
        #endregion

        #region Public properties
        /// <summary>Mesh drawn for every object.</summary>
        public Mesh CubeMesh => _cubeMesh;
        /// <summary>Base material every backend derives its variants from.</summary>
        public Material CubeMaterial => _cubeMaterial;
        /// <summary>The 16-entry colour palette.</summary>
        public Color[] Palette => _palette;
        #endregion

        #region Public interface
        /// <summary>Parameters for a field of <paramref name="count"/> objects: the whirlpool radius and depth scale with the cloud's extent.</summary>
        public FieldParams ToParams(int count)
        {
            float extent = ObjectField.SideLength(count) * _spacing;
            return new FieldParams
            {
                Spacing = _spacing,
                CubeScale = _cubeScale,
                WaveAmplitude = _waveAmplitude,
                WaveLength = _waveLength,
                WaveSpeed = _waveSpeed,
                RotationSpeed = _rotationSpeed,
                SpringStiffness = _springStiffness,
                SpringDamping = _springDamping,
                AttractorStrength = _attractorStrength,
                FieldExtent = extent,
                SwirlRadius = extent * _swirlRadiusFraction,
                SwirlSpeed = _swirlSpeed,
                SwirlDepth = extent * _swirlDepthFraction,
            };
        }

        /// <summary>Palette colour for a palette index, wrapping out-of-range values.</summary>
        public Color PaletteColor(int paletteIndex)
        {
            if (_palette == null || _palette.Length == 0)
                return Color.white;
            return _palette[((paletteIndex % _palette.Length) + _palette.Length) % _palette.Length];
        }
        #endregion
    }
}
