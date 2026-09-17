using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MillionObjects.Benchmark
{
    /// <summary>
    /// Owns the object-count ramp used for footage. Started by <c>-record</c> it plays the unattended
    /// script: HUD on, camera path on, every step in ladder order, each holding the ramp for a fixed
    /// time. <see cref="ToggleRamp"/> plays the same ladder by hand on the step already on screen, for
    /// filming one step's ramp with the camera driven manually. Either way the ramp stops climbing once
    /// the step collapses, so a low step is never pushed to a count that hangs the editor. Frame times
    /// are sampled only to make that call; nothing is recorded here and capture runs outside.
    /// </summary>
    public class RecordingDirector : MonoBehaviour
    {
        #region Inspector fields
        [SerializeField] private BackendSwitcher _switcher;
        [SerializeField] private CameraRig _cameraRig;
        [SerializeField] private FrameSampler _sampler;
        [SerializeField] private Hud _hud;
        [SerializeField, Tooltip("Object counts the ramp walks, in order.")]
        private int[] _rampCounts = { 1_000, 10_000, 100_000, 1_000_000 };
        [SerializeField, Tooltip("Seconds each count is held on screen before the ramp moves to the next one.")]
        private float _holdSeconds = 6f;
        [SerializeField, Tooltip("Stop the ramp once the step collapses instead of spawning the next count. Untick to ramp to the top whatever it costs.")]
        private bool _stopRampWhenUnplayable = true;
        [SerializeField, Tooltip("Frame time that counts as collapsed: 200 ms is 5 fps, 500 ms is 2 fps. A count that measures past it ends the ramp.")]
        private float _unplayableFrameMs = 500f;
        [SerializeField, Tooltip("Frames measured after each spawn, before the hold begins. The spawn stall and the cold first frame are skipped on top of these.")]
        private int _measuredFramesPerCount = 5;
        [SerializeField, Tooltip("Log every count the ramp visits, what it measured and why it ended. For tracking down a ramp that misbehaves; noisy during long recordings.")]
        private bool _logRampProgress = true;
        [SerializeField, Tooltip("Quit after the last step instead of looping.")]
        private bool _quitWhenDone = true;
        #endregion

        #region Public properties
        /// <summary>Counts the ramp climbs, ascending; the count hotkeys step through the same rungs.</summary>
        public IReadOnlyList<int> RampLadder => _rampLadder;
        /// <summary>Seconds the count on screen is still held before either ramp moves on, or zero while none is walking. The HUD counts it down.</summary>
        public float SecondsToNextRampCount => Mathf.Max(0f, _nextRampCountTime - Time.realtimeSinceStartup);
        #endregion

        #region Private fields
        private BenchmarkArgs _args;
        private Coroutine _rampRoutine;     // hand-toggled ramp on the active step; null while idle
        private float _nextRampCountTime;   // realtime at which the held count gives way to the next; zero while no ramp walks
        private float _measuredFrameMs;     // what the count just spawned costs per frame, averaged over the measured frames
        private bool _rampHitCeiling;       // the last count measured past the ceiling, so the ladder stops here
        private int[] _rampLadder;          // _rampCounts made positive, unique and ascending; what the ramp actually climbs
        #endregion

        #region Lifecycle
        private void Awake()
        {
            BuildRampLadder();
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

        /// <summary>Starts the hand-toggled ramp from the count already on screen, or stops it on the count it reached.</summary>
        public void ToggleRamp()
        {
            if (_rampRoutine != null)
            {
                StopCoroutine(_rampRoutine);
                _rampRoutine = null;
                _nextRampCountTime = 0f;
                LogRampProgress($"stopped by hand at {_switcher.SpawnCount:N0} objects");
                return;
            }
            if (_switcher.Active == null)
                return;
            LogRampProgress($"starts from {_switcher.SpawnCount:N0} objects");
            _rampRoutine = StartCoroutine(RampActiveStep());
        }
        #endregion

        #region Ramp
        /// <summary>Plays every step's ramp, then quits or loops.</summary>
        private IEnumerator Run()
        {
            DisplayMode.Apply(_args.Resolution);
            if (_hud != null)
                _hud.Mode = HudMode.Full;
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
            foreach (int count in _rampLadder)
            {
                _cameraRig.Restart();
                yield return HoldRampCount(count);
                if (_rampHitCeiling)
                    break;
            }
            _switcher.Active.Despawn();
        }

        /// <summary>Climbs the ladder from the count on screen upward, on the step that was active when the ramp started, leaving the last count standing; a step switch ends it.</summary>
        private IEnumerator RampActiveStep()
        {
            ObjectBackend rampedBackend = _switcher.Active;
            for (int i = FirstRampIndexAtOrAbove(_switcher.SpawnCount); i < _rampLadder.Length; i++)
            {
                if (_switcher.Active != rampedBackend)
                {
                    LogRampProgress("ends: the active step changed");
                    break;
                }
                yield return HoldRampCount(_rampLadder[i]);
                if (_rampHitCeiling)
                    break;
                if (i == _rampLadder.Length - 1)
                    LogRampProgress($"ends: {_rampLadder[i]:N0} is the top of the ladder");
            }
            _rampRoutine = null;
        }

        /// <summary>Brings the active step to one ramp count, measures what it costs, and holds it for the full time unless it measured past the ceiling. A field already at that count is kept rather than respawned.</summary>
        private IEnumerator HoldRampCount(int count)
        {
            _switcher.SpawnCount = count;
            if (_switcher.Active == null || _switcher.Active.Count != count)
                _switcher.Respawn();
            yield return MeasureSpawnedCount();
            int fieldCount = _switcher.Active != null ? _switcher.Active.Count : 0;
            LogRampProgress($"holds {count:N0} objects, field has {fieldCount:N0}, at {_measuredFrameMs:N0} ms per frame");
            _rampHitCeiling = IsSpawnedCountUnplayable(count);
            if (_rampHitCeiling)
                yield break;
            _nextRampCountTime = Time.realtimeSinceStartup + _holdSeconds;
            yield return new WaitForSecondsRealtime(_holdSeconds);
            _nextRampCountTime = 0f;
        }

        /// <summary>Averages the frame time of the count now on screen, skipping the spawn stall and the cold first frame so only the settled cost is weighed.</summary>
        private IEnumerator MeasureSpawnedCount()
        {
            _measuredFrameMs = 0f;
            yield return null;   // the spawn blocks its own frame
            yield return null;   // whose stall lands in the next frame's delta
            if (_sampler == null)
                yield break;
            int frames = Mathf.Max(1, _measuredFramesPerCount);
            float total = 0f;
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                total += _sampler.Latest.FrameMs;
            }
            _measuredFrameMs = total / frames;
        }

        /// <summary>True when the count just measured is already past the ceiling; the ramp then stops on it without starting a countdown to a next one.</summary>
        private bool IsSpawnedCountUnplayable(int count)
        {
            if (!_stopRampWhenUnplayable)
                return false;
            if (_sampler == null)
            {
                Debug.LogError("[RecordingDirector] The ramp ceiling is on but no FrameSampler is assigned, so the ramp cannot tell when a count has collapsed.");
                return false;
            }
            if (_measuredFrameMs <= _unplayableFrameMs)
                return false;
            Debug.LogWarning($"[RecordingDirector] Ramp stopped at {count:N0} objects: {_measuredFrameMs:N0} ms per frame is past the {_unplayableFrameMs:N0} ms ceiling.");
            return true;
        }

        /// <summary>Writes one line of ramp progress when progress logging is on, so a ramp that misbehaves leaves a trail.</summary>
        private void LogRampProgress(string what)
        {
            if (_logRampProgress)
                Debug.Log($"[RecordingDirector] Ramp {what}.");
        }
        #endregion

        #region Ladder
        /// <summary>Builds the ladder from the serialized counts, keeping only positive ones, dropping duplicates and sorting ascending, so a mistyped or left-over inspector entry cannot send the ramp backwards.</summary>
        private void BuildRampLadder()
        {
            var ladder = new List<int>(_rampCounts.Length);
            foreach (int count in _rampCounts)
                if (count > 0 && !ladder.Contains(count))
                    ladder.Add(count);
            ladder.Sort();
            _rampLadder = ladder.ToArray();
            if (LadderMatchesInspector())
                return;
            Debug.LogWarning($"[RecordingDirector] The ramp counts are not an ascending ladder; climbing {string.Join(", ", _rampLadder)} instead. Tidy the list on the component to silence this.");
        }

        /// <summary>True when the built ladder is exactly the serialized list, so nothing had to be reordered or dropped.</summary>
        private bool LadderMatchesInspector()
        {
            if (_rampLadder.Length != _rampCounts.Length)
                return false;
            for (int i = 0; i < _rampLadder.Length; i++)
                if (_rampLadder[i] != _rampCounts[i])
                    return false;
            return true;
        }

        /// <summary>Ladder index the hand-toggled ramp starts at: the first count that is not below the one on screen, so it picks up where the count was left rather than at the bottom.</summary>
        private int FirstRampIndexAtOrAbove(int count)
        {
            for (int i = 0; i < _rampLadder.Length; i++)
                if (_rampLadder[i] >= count)
                    return i;
            return _rampLadder.Length;
        }
        #endregion
    }
}
