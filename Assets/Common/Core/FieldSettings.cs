using System;
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
        [SerializeField, Tooltip("Colour ramp of the first hue from the cloud's edge (t = 0) to the whirlpool's eye (t = 1). Sampled at 32 levels.")]
        private Gradient _edgeToEyeHueA = DefaultGradient("#0a2660", "#2e8fdc", "#ffffff");
        [SerializeField, Tooltip("Colour ramp of the second hue, used on alternating azimuth sectors so the vortex shear shows as spiral arms.")]
        private Gradient _edgeToEyeHueB = DefaultGradient("#0b4a55", "#2cbfc6", "#ffffff");
        #endregion

        #region Public properties
        /// <summary>Mesh drawn for every object.</summary>
        public Mesh CubeMesh => _cubeMesh;
        /// <summary>Base material every backend derives its variants from.</summary>
        public Material CubeMaterial => _cubeMaterial;
        #endregion

        #region Events
        /// <summary>Raised when a value is edited in the Inspector, so a running field can rebuild itself with the new settings.</summary>
        public event Action Changed;
        #endregion

        #region Lifecycle
#if UNITY_EDITOR   // OnValidate only exists in the Editor; players never edit settings at runtime
        private void OnValidate()
        {
            Changed?.Invoke();
        }
#endif
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

        /// <summary>
        /// Palette colour for a slot: even slots sample the first hue's gradient, odd slots the second,
        /// at the slot's brightness level. Out-of-range slots wrap. See <see cref="ObjectField.PaletteIndex"/>.
        /// </summary>
        public Color PaletteColor(int paletteIndex)
        {
            int slot = ((paletteIndex % ObjectField.PaletteSize) + ObjectField.PaletteSize) % ObjectField.PaletteSize;
            Gradient gradient = (slot & 1) == 0 ? _edgeToEyeHueA : _edgeToEyeHueB;
            float t = (slot >> 1) / (float)(ObjectField.PaletteLevels - 1);
            return gradient != null ? gradient.Evaluate(t) : Color.white;
        }
        #endregion

        #region Defaults
        /// <summary>Three-stop gradient from hex colours, used as the code default before the asset is tuned in the Inspector.</summary>
        private static Gradient DefaultGradient(string edge, string middle, string eye)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Parse(edge), 0f), new GradientColorKey(Parse(middle), 0.55f), new GradientColorKey(Parse(eye), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        /// <summary>Hex colour to Color; white when the string does not parse.</summary>
        private static Color Parse(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.white;
        }
        #endregion
    }
}
