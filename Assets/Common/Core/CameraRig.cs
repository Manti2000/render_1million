using UnityEngine;

namespace MillionObjects
{
    /// <summary>
    /// The one camera path every clip and benchmark uses: starts low inside the field, pulls back once
    /// over a slow ramp until the whole sheet is in frame, then holds that distance while orbiting so
    /// every rendered object stays visible. Scaled to the field bounds so any object count is framed
    /// the same way.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        #region Inspector fields
        [SerializeField, Tooltip("Seconds the pull-back from the start pose to the wide shot takes.")]
        private float _pullBackSeconds = 14f;
        [SerializeField, Tooltip("Orbit speed around the field in degrees per second; continues after the pull-back.")]
        private float _orbitDegreesPerSecond = 5f;
        [SerializeField, Tooltip("Camera height above the sheet at the start of the pull-back, in world units.")]
        private float _startHeight = 2.5f;
        [SerializeField, Tooltip("Horizontal start distance from the centre as a fraction of the field extent.")]
        private float _startDistanceFactor = 0.08f;
        [SerializeField, Tooltip("Camera height at the wide shot as a fraction of the field extent.")]
        private float _endHeightFactor = 0.8f;
        [SerializeField, Tooltip("Horizontal distance at the wide shot as a fraction of the field extent.")]
        private float _endDistanceFactor = 1.5f;
        #endregion

        #region Public properties
        /// <summary>Whether the rig advances along its path. When false the camera holds its pose.</summary>
        public bool Playing { get; set; } = true;
        /// <summary>Pull-back progress, 0 at the start pose and 1 once the wide shot is reached.</summary>
        public float PullBackProgress => Mathf.Clamp01(_elapsed / _pullBackSeconds);
        #endregion

        #region Private fields
        private Bounds _field = new Bounds(Vector3.zero, new Vector3(50f, 5f, 50f));   // framed field, set by the mode controller
        private float _elapsed;   // seconds since Restart, drives pull-back and orbit
        #endregion

        #region Lifecycle
        private void LateUpdate()
        {
            if (Playing)
                _elapsed += Time.deltaTime;
            ApplyPose();
        }
        #endregion

        #region Public interface
        /// <summary>Sets the bounds the path is scaled to. Call whenever the object count changes.</summary>
        public void SetField(Bounds field)
        {
            _field = field;
        }

        /// <summary>Rewinds the path to the start so every measurement window sees the same motion.</summary>
        public void Restart()
        {
            _elapsed = 0f;
            ApplyPose();
        }

        /// <summary>Jumps straight to the wide shot, for short probes that should not spend time zooming out.</summary>
        public void SkipToWideShot()
        {
            _elapsed = _pullBackSeconds;
            ApplyPose();
        }
        #endregion

        #region Path
        /// <summary>Moves the camera to the pose for the elapsed time: eased pull-back, then a steady orbit.</summary>
        private void ApplyPose()
        {
            float pullBack = Mathf.SmoothStep(0f, 1f, PullBackProgress);
            float extent = Mathf.Max(_field.extents.x, Mathf.Max(_field.extents.y, _field.extents.z)) * 2f;
            float distance = Mathf.Lerp(extent * _startDistanceFactor, extent * _endDistanceFactor, pullBack);
            float height = Mathf.Lerp(_startHeight, extent * _endHeightFactor, pullBack);
            float angle = _elapsed * _orbitDegreesPerSecond * Mathf.Deg2Rad;
            Vector3 centre = _field.center;
            transform.position = centre + new Vector3(Mathf.Sin(angle) * distance, height, -Mathf.Cos(angle) * distance);
            transform.LookAt(centre);
        }
        #endregion
    }
}
