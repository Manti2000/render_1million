using System.Collections;
using UnityEngine;

namespace MillionObjects.Benchmark
{
    /// <summary>
    /// Recording script started by <c>-record</c>: HUD on, camera path on, every step in ladder order,
    /// each holding the count ramp for a fixed time. Nothing is measured; screen capture runs outside.
    /// </summary>
    public class RecordingDirector : MonoBehaviour
    {
        #region Inspector fields
        [SerializeField] private BackendSwitcher _switcher;
        [SerializeField] private CameraRig _cameraRig;
        [SerializeField] private Hud _hud;
        [SerializeField, Tooltip("Object counts shown per step, in order.")]
        private int[] _rampCounts = { 1_000, 10_000, 100_000, 1_000_000 };
        [SerializeField, Tooltip("Seconds each count is held on screen.")]
        private float _holdSeconds = 6f;
        [SerializeField, Tooltip("Quit after the last step instead of looping.")]
        private bool _quitWhenDone = true;
        #endregion

        #region Private fields
        private BenchmarkArgs _args;
        #endregion

        #region Lifecycle
        private void Awake()
        {
            if (BenchmarkArgs.FromCommandLine().Record)
                _switcher.SuppressAutoLoad();
        }

        private void Start()
        {
            var args = BenchmarkArgs.FromCommandLine();
            if (args.Record)
                StartRun(args);
        }
        #endregion

        #region Public interface
        /// <summary>Plays the recording script with the given display arguments.</summary>
        public void StartRun(BenchmarkArgs args)
        {
            _args = args;
            StartCoroutine(Run());
        }
        #endregion

        #region Script
        /// <summary>Plays every step's ramp, then quits or loops.</summary>
        private IEnumerator Run()
        {
            DisplayMode.Apply(_args.Resolution);
            if (_hud != null)
                _hud.Visible = true;
            do
            {
                for (int i = 0; i < _switcher.Catalog.Steps.Count; i++)
                    yield return PlayStep(i);
            }
            while (!_quitWhenDone);
            DisplayMode.Quit();
        }

        /// <summary>Loads a step and holds each ramp count with the camera path restarted.</summary>
        private IEnumerator PlayStep(int stepIndex)
        {
            yield return _switcher.LoadStep(stepIndex, false);
            if (_switcher.Active == null)
                yield break;
            foreach (int count in _rampCounts)
            {
                _switcher.Active.Spawn(count);
                _cameraRig.Restart();
                yield return new WaitForSecondsRealtime(_holdSeconds);
            }
            _switcher.Active.Despawn();
        }
        #endregion
    }
}
