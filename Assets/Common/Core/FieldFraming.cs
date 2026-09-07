using UnityEngine;

namespace MillionObjects
{
    /// <summary>
    /// Keeps camera, attractor and shading scaled to the active field: whenever a step loads or respawns,
    /// recomputes the field bounds, hands them to the rig and the attractor, and publishes the height band
    /// the shaders darken across. Every step load also restarts the camera path so the pull-back plays
    /// from the start.
    /// </summary>
    public class FieldFraming : MonoBehaviour
    {
        #region Constants
        private static readonly int ShadeTopId = Shader.PropertyToID("_ShadeTopY");
        private static readonly int ShadeBottomId = Shader.PropertyToID("_ShadeBottomY");
        private static readonly int ShadeFloorId = Shader.PropertyToID("_ShadeFloor");
        #endregion

        #region Inspector fields
        [SerializeField] private BackendSwitcher _switcher;
        [SerializeField] private CameraRig _cameraRig;
        [SerializeField] private Attractor _attractor;
        [SerializeField, Tooltip("Brightness of cubes at the deepest point of the funnel relative to the cloud's top.")]
        private float _shadeFloor = 0.35f;
        #endregion

        #region Private fields
        private int _framedCount = -1;   // count the bounds were last computed for
        #endregion

        #region Lifecycle
        private void OnEnable()
        {
            if (_switcher == null)
                return;
            _switcher.StepLoaded += OnStepLoaded;
            _switcher.FieldRebuilt += OnFieldRebuilt;
        }

        private void OnDisable()
        {
            if (_switcher == null)
                return;
            _switcher.StepLoaded -= OnStepLoaded;
            _switcher.FieldRebuilt -= OnFieldRebuilt;
        }

        private void Update()
        {
            if (_switcher == null || _switcher.Active == null)
                return;
            if (_switcher.Active.Count == _framedCount)
                return;
            Reframe(_switcher.Active.Count);
        }
        #endregion

        #region Framing
        /// <summary>Reframes immediately and rewinds the camera path when a step comes in.</summary>
        private void OnStepLoaded(ObjectBackend backend)
        {
            if (backend == null)
                return;
            Reframe(backend.Count);
            if (_cameraRig != null)
                _cameraRig.Restart();
        }

        /// <summary>Tells every cube shader where the cloud's top is and how deep the funnel reaches, so both shaders darken identically.</summary>
        private void PublishShadeBand(Bounds framing, Bounds full)
        {
            Shader.SetGlobalFloat(ShadeTopId, framing.max.y);
            Shader.SetGlobalFloat(ShadeBottomId, full.min.y);
            Shader.SetGlobalFloat(ShadeFloorId, _shadeFloor);
        }

        /// <summary>Reframes after a live settings edit without rewinding the camera, so values can be tuned while watching.</summary>
        private void OnFieldRebuilt(ObjectBackend backend)
        {
            if (backend != null)
                Reframe(backend.Count);
        }

        /// <summary>Recomputes the field bounds for a count and applies them to camera and attractor.</summary>
        private void Reframe(int count)
        {
            _framedCount = count;
            if (count <= 0)
                return;
            var parameters = _switcher.Settings.ToParams(count);
            var framing = ObjectField.FramingBounds(count, parameters);
            if (_cameraRig != null)
                _cameraRig.SetField(framing);
            if (_attractor != null)
                _attractor.SetField(framing);
            PublishShadeBand(framing, ObjectField.FieldBounds(count, parameters));
        }
        #endregion
    }
}
