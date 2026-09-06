using Unity.Mathematics;
using UnityEngine;

namespace MillionObjects
{
    /// <summary>
    /// Flexibility demo: a sphere sweeping a Lissajous path over the field, pushing cubes aside so
    /// they spring back. Exercises per-object velocity state in every backend.
    /// </summary>
    public class Attractor : MonoBehaviour
    {
        #region Inspector fields
        [SerializeField, Tooltip("Switcher whose active backend receives the attractor.")]
        private BackendSwitcher _switcher;
        [SerializeField, Tooltip("Sphere shown at the attractor position; scaled to the radius.")]
        private Transform _visual;
        [SerializeField, Tooltip("Push radius in world units.")]
        private float _radius = 8f;
        [SerializeField, Tooltip("Vertical sweep as a fraction of the field's half height.")]
        private float _verticalSweep = 0.6f;
        [SerializeField, Tooltip("Seconds for one full sweep over the field.")]
        private float _sweepSeconds = 12f;
        [SerializeField, Tooltip("Whether the attractor pushes at start.")]
        private bool _enabledAtStart;
        #endregion

        #region Public properties
        /// <summary>Whether the attractor pushes. When off the backend receives a zero radius.</summary>
        public bool Enabled { get; set; }
        /// <summary>Current attractor as (x, y, z, radius); radius zero when disabled.</summary>
        public float4 Current { get; private set; }
        #endregion

        #region Private fields
        private Bounds _field = new Bounds(Vector3.zero, new Vector3(50f, 5f, 50f));   // sweep area
        private float _elapsed;   // drives the Lissajous path
        #endregion

        #region Lifecycle
        private void Awake()
        {
            Enabled = _enabledAtStart;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            Vector3 position = SweepPosition(_elapsed / _sweepSeconds);
            Current = Enabled ? new float4(position, _radius) : float4.zero;
            UpdateVisual(position);
            if (_switcher != null)
                _switcher.Attractor = Current;
        }
        #endregion

        #region Public interface
        /// <summary>Sets the area the sphere sweeps. Call whenever the object count changes.</summary>
        public void SetField(Bounds field)
        {
            _field = field;
        }
        #endregion

        #region Path
        /// <summary>Lissajous position through the field volume at a normalised time.</summary>
        private Vector3 SweepPosition(float t)
        {
            float x = Mathf.Sin(t * 2f * Mathf.PI) * _field.extents.x * 0.8f;
            float y = Mathf.Sin(t * 2f * Mathf.PI * 0.5f) * _field.extents.y * _verticalSweep;
            float z = Mathf.Sin(t * 2f * Mathf.PI * 2f / 3f) * _field.extents.z * 0.8f;
            return _field.center + new Vector3(x, y, z);
        }

        /// <summary>Moves and scales the visual sphere, hiding it while disabled.</summary>
        private void UpdateVisual(Vector3 position)
        {
            if (_visual == null)
                return;
            _visual.gameObject.SetActive(Enabled);
            _visual.position = position;
            _visual.localScale = Vector3.one * (_radius * 2f);
        }
        #endregion
    }
}
