using UnityEngine;

namespace MillionObjects
{
    /// <summary>
    /// Keeps camera and attractor scaled to the active field: whenever a step loads or respawns,
    /// recomputes the field bounds and hands them to the rig and the attractor. Every step load also
    /// restarts the camera path so the pull-back plays from the start.
    /// </summary>
    public class FieldFraming : MonoBehaviour
    {
        #region Inspector fields
        [SerializeField] private BackendSwitcher _switcher;
        [SerializeField] private CameraRig _cameraRig;
        [SerializeField] private Attractor _attractor;
        #endregion

        #region Private fields
        private int _framedCount = -1;   // count the bounds were last computed for
        #endregion

        #region Lifecycle
        private void OnEnable()
        {
            if (_switcher != null)
                _switcher.StepLoaded += OnStepLoaded;
        }

        private void OnDisable()
        {
            if (_switcher != null)
                _switcher.StepLoaded -= OnStepLoaded;
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

        /// <summary>Recomputes the field bounds for a count and applies them to camera and attractor.</summary>
        private void Reframe(int count)
        {
            _framedCount = count;
            if (count <= 0)
                return;
            var parameters = _switcher.Settings.ToParams();
            var bounds = ObjectField.FieldBounds(count, parameters);
            if (_cameraRig != null)
                _cameraRig.SetField(bounds);
            if (_attractor != null)
                _attractor.SetField(bounds);
        }
        #endregion
    }
}
