using UnityEngine;

namespace MillionObjects
{
    /// <summary>
    /// The one camera path every clip and benchmark uses: starts inside the eye of the whirlpool looking
    /// down the tube, rises out of it along the axis while the view tilts toward the final elevation, and
    /// holds at the distance where the whole cloud just fits the frame, orbiting above the funnel. Computed
    /// from the field bounds and the camera's field of view, so any object count and aspect ratio is
    /// framed the same way.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        #region Inspector fields
        [SerializeField, Tooltip("Seconds the pull-back from the start pose to the wide shot takes.")]
        private float _pullBackSeconds = 14f;
        [SerializeField, Tooltip("Orbit speed around the field in degrees per second; continues after the pull-back.")]
        private float _orbitDegreesPerSecond = 5f;
        [SerializeField, Tooltip("Distance from the centre at the start as a fraction of the field extent; small enough to sit inside the whirlpool's eye.")]
        private float _startDistanceFactor = 0.08f;
        [SerializeField, Tooltip("Camera elevation at the start, in degrees; near vertical so the camera looks down the tube.")]
        private float _startElevationDegrees = 82f;
        [SerializeField, Tooltip("Camera elevation at the wide shot, in degrees; steep enough to look into the funnel while orbiting.")]
        private float _elevationDegrees = 61f;
        [SerializeField, Tooltip("Fraction of the frame the cloud's bounding box fills at the wide shot; 1 touches the edges, above 1 lets the box corners leave the frame while the cloud itself still fits.")]
        private float _fitMargin = 1.05f;
        #endregion

        #region Public properties
        /// <summary>Whether the rig advances along its path. When false the camera holds its pose.</summary>
        public bool Playing { get; set; } = true;
        /// <summary>Pull-back progress, 0 at the start pose and 1 once the wide shot is reached.</summary>
        public float PullBackProgress => Mathf.Clamp01(_pullBackTime / _pullBackSeconds);
        #endregion

        #region Private fields
        private Bounds _field = new Bounds(Vector3.zero, new Vector3(50f, 5f, 50f));   // framed field, set by the mode controller
        private float _pullBackTime;      // seconds along the pull-back path, 0 to _pullBackSeconds
        private float _orbitDegrees;      // accumulated orbit angle
        private bool _autoPullBack = true;   // false after a manual zoom, so the camera stays where the user put it
        private Camera _camera;   // for field of view and aspect; null falls back to 60 degrees at 16:10
        private const int AzimuthSamples = 12;   // orbit angles sampled over a quarter turn for the rotation-invariant fit
        #endregion

        #region Lifecycle
        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (Playing)
                Advance(Time.deltaTime);
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
            _pullBackTime = 0f;
            _orbitDegrees = 0f;
            _autoPullBack = true;
            ApplyPose();
        }

        /// <summary>Jumps straight to the wide shot, for short probes that should not spend time zooming out.</summary>
        public void SkipToWideShot()
        {
            _pullBackTime = _pullBackSeconds;
            _orbitDegrees = 0f;
            _autoPullBack = true;
            ApplyPose();
        }

        /// <summary>Moves along the pull-back path by hand: negative zooms in, positive zooms out, in seconds of path. Stops the automatic pull-back.</summary>
        public void NudgePullBack(float seconds)
        {
            _autoPullBack = false;
            _pullBackTime = Mathf.Clamp(_pullBackTime + seconds, 0f, _pullBackSeconds);
        }

        /// <summary>Turns the orbit by hand, in degrees; positive is the same direction as the automatic orbit.</summary>
        public void NudgeOrbit(float degrees)
        {
            _orbitDegrees += degrees;
        }
        #endregion

        #region Path
        /// <summary>Advances the automatic motion: the pull-back until it completes (unless a manual zoom took over), and the orbit forever.</summary>
        private void Advance(float deltaTime)
        {
            if (_autoPullBack)
                _pullBackTime = Mathf.Min(_pullBackTime + deltaTime, _pullBackSeconds);
            _orbitDegrees += deltaTime * _orbitDegreesPerSecond;
        }

        /// <summary>Moves the camera to the pose for the current pull-back and orbit: eased pull-back to a distance that just fits the cloud, then a steady orbit.</summary>
        private void ApplyPose()
        {
            float pullBack = Mathf.SmoothStep(0f, 1f, PullBackProgress);
            float extent = Mathf.Max(_field.extents.x, Mathf.Max(_field.extents.y, _field.extents.z)) * 2f;
            float elevation = Mathf.Lerp(_startElevationDegrees, _elevationDegrees, pullBack) * Mathf.Deg2Rad;
            float azimuth = _orbitDegrees * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(azimuth) * Mathf.Cos(elevation), Mathf.Sin(elevation), -Mathf.Cos(azimuth) * Mathf.Cos(elevation));
            float distance = Mathf.Lerp(extent * _startDistanceFactor, FitDistance(elevation), pullBack);
            transform.position = _field.center + direction * distance;
            transform.LookAt(_field.center);
        }

        /// <summary>
        /// Smallest distance at which the framed box fits the frame from every orbit angle at this
        /// elevation. Taking the worst azimuth makes the distance depend on elevation only, so the orbit
        /// never zooms; fitting the box's corners keeps that distance as short as the frame allows.
        /// </summary>
        private float FitDistance(float elevation)
        {
            float distance = 0f;
            for (int sample = 0; sample < AzimuthSamples; sample++)
            {
                float azimuth = sample * (0.5f * Mathf.PI / AzimuthSamples);   // a quarter turn covers every corner arrangement of a box
                Vector3 direction = new Vector3(Mathf.Sin(azimuth) * Mathf.Cos(elevation), Mathf.Sin(elevation), -Mathf.Cos(azimuth) * Mathf.Cos(elevation));
                distance = Mathf.Max(distance, FitDistance(direction));
            }
            return distance;
        }

        /// <summary>Smallest distance along one view direction from which all eight corners of the framed box fit the frame.</summary>
        private float FitDistance(Vector3 direction)
        {
            float fieldOfView = _camera != null ? _camera.fieldOfView : 60f;
            float aspect = _camera != null ? _camera.aspect : 1.6f;
            float tanVertical = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) * _fitMargin;
            float tanHorizontal = tanVertical * aspect;
            Vector3 forward = -direction;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right);
            Vector3 half = _field.extents;
            float distance = 0f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = new Vector3((corner & 1) == 0 ? -half.x : half.x, (corner & 2) == 0 ? -half.y : half.y, (corner & 4) == 0 ? -half.z : half.z);
                distance = Mathf.Max(distance, DistanceToFit(offset, forward, right, up, tanHorizontal, tanVertical));
            }
            return distance;
        }

        /// <summary>Distance from the centre at which one point (relative to the centre) sits inside both frustum half-angles.</summary>
        private static float DistanceToFit(Vector3 offset, Vector3 forward, Vector3 right, Vector3 up, float tanHorizontal, float tanVertical)
        {
            float depth = Vector3.Dot(offset, forward);
            float horizontal = Mathf.Abs(Vector3.Dot(offset, right)) / tanHorizontal - depth;
            float vertical = Mathf.Abs(Vector3.Dot(offset, up)) / tanVertical - depth;
            return Mathf.Max(horizontal, vertical);
        }
        #endregion
    }
}
