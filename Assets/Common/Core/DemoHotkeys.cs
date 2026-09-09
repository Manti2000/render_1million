using UnityEngine;
using UnityEngine.InputSystem;

namespace MillionObjects
{
    /// <summary>
    /// Interactive controls for exploring the demo by hand. Digits switch steps, plus/minus scale the
    /// object count by ten, the arrows drive the camera (up/down zoom along the path, left/right
    /// orbit), and letters toggle the attractor, camera, HUD and ticking.
    /// </summary>
    public class DemoHotkeys : MonoBehaviour
    {
        #region Constants
        private const int MinCount = 1000;
        private const int MaxCount = 10_000_000;
        #endregion

        #region Inspector fields
        [SerializeField] private BackendSwitcher _switcher;
        [SerializeField] private Attractor _attractor;
        [SerializeField] private CameraRig _cameraRig;
        [SerializeField] private Hud _hud;
        [SerializeField, Tooltip("Seconds of pull-back path travelled per second while up or down is held; 6 crosses the 14 second path in about two seconds.")]
        private float _zoomPathSecondsPerSecond = 6f;
        [SerializeField, Tooltip("Orbit speed in degrees per second while left or right is held.")]
        private float _orbitDegreesPerSecond = 45f;
        #endregion

        #region Lifecycle
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            HandleStepKeys(keyboard);
            HandleCountKeys(keyboard);
            HandleCameraKeys(keyboard);
            HandleToggleKeys(keyboard);
        }
        #endregion

        #region Key groups
        /// <summary>Digits 1-9 load the matching step from the catalog.</summary>
        private void HandleStepKeys(Keyboard keyboard)
        {
            if (_switcher == null || _switcher.Catalog == null)
                return;
            for (int i = 0; i < _switcher.Catalog.Steps.Count && i < 9; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                    _switcher.LoadStep(i, true);
        }

        /// <summary>Plus/minus (main row or numpad) multiply or divide the count by ten and respawn; R respawns as is.</summary>
        private void HandleCountKeys(Keyboard keyboard)
        {
            if (_switcher == null)
                return;
            if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
                ChangeCount(_switcher.SpawnCount * 10);
            if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
                ChangeCount(_switcher.SpawnCount / 10);
            if (keyboard.rKey.wasPressedThisFrame)
                _switcher.Respawn();
        }

        /// <summary>Up/down zoom in and out along the pull-back path, left/right orbit; held keys move continuously.</summary>
        private void HandleCameraKeys(Keyboard keyboard)
        {
            if (_cameraRig == null)
                return;
            float zoom = (keyboard.downArrowKey.isPressed ? 1f : 0f) - (keyboard.upArrowKey.isPressed ? 1f : 0f);
            if (zoom != 0f)
                _cameraRig.NudgePullBack(zoom * _zoomPathSecondsPerSecond * Time.deltaTime);
            float orbit = (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
            if (orbit != 0f)
                _cameraRig.NudgeOrbit(orbit * _orbitDegreesPerSecond * Time.deltaTime);
        }

        /// <summary>A toggles the attractor, C the camera path, H the HUD, Space freezes the field.</summary>
        private void HandleToggleKeys(Keyboard keyboard)
        {
            if (keyboard.aKey.wasPressedThisFrame && _attractor != null)
                _attractor.Enabled = !_attractor.Enabled;
            if (keyboard.cKey.wasPressedThisFrame && _cameraRig != null)
                _cameraRig.Playing = !_cameraRig.Playing;
            if (keyboard.hKey.wasPressedThisFrame && _hud != null)
                _hud.Visible = !_hud.Visible;
            if (keyboard.spaceKey.wasPressedThisFrame && _switcher != null)
                _switcher.TickingEnabled = !_switcher.TickingEnabled;
        }
        #endregion

        #region Helpers
        /// <summary>Clamps and applies a new spawn count, then respawns.</summary>
        private void ChangeCount(int count)
        {
            _switcher.SpawnCount = Mathf.Clamp(count, MinCount, MaxCount);
            _switcher.Respawn();
        }
        #endregion
    }
}
