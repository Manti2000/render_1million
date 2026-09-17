using System.Collections;
using System.IO;
using UnityEngine;

namespace MillionObjects.Benchmark
{
    /// <summary>
    /// Unattended benchmark: for every step in the catalog runs the fixed-count sweep and the search for
    /// the largest count under the target frame time, then writes one JSON report. Started by the
    /// <c>-benchmark</c> command-line switch (quits afterwards) or from the start menu (returns to it).
    /// Every window is measured at the wide shot with the camera orbiting, so all steps see the same
    /// view of the whole field and no time is spent zooming.
    /// </summary>
    public class BenchmarkRunner : MonoBehaviour
    {
        #region Inspector fields
        [Header("Scene")]
        [SerializeField] private BackendSwitcher _switcher;
        [SerializeField] private FrameSampler _sampler;
        [SerializeField] private CameraRig _cameraRig;
        [SerializeField] private Hud _hud;

        [Header("Sweep")]
        [SerializeField, Tooltip("Object counts measured in order; measurement stops after the first failure.")]
        private int[] _sweepCounts = { 1_000, 10_000, 100_000, 1_000_000 };
        [SerializeField, Tooltip("Seconds discarded after each spawn before sampling.")]
        private float _warmupSeconds = 2f;
        [SerializeField, Tooltip("Seconds sampled per count at the wide shot.")]
        private float _measureSeconds = 8f;

        [Header("Count at target")]
        [SerializeField, Tooltip("Average frame time a count must hold to pass, in ms. 33.3 is 30 fps.")]
        private float _targetMs = 33.333f;
        [SerializeField, Tooltip("The 1% low may be at most this many times the target for a count to count as stable.")]
        private float _low1ToleranceFactor = 1.5f;
        [SerializeField, Tooltip("First count probed when no sweep count passed.")]
        private int _searchStartCount = 1_000;
        [SerializeField, Tooltip("Largest count the search may try; the search stops earlier at the first count under target.")]
        private int _searchCeiling = 16_000_000;
        [SerializeField, Tooltip("Bisection stops once the bracket is within this fraction of the passing count.")]
        private float _searchResolution = 0.1f;
        [SerializeField, Tooltip("Upper bound on bisection probes per step.")]
        private int _maxBisectionSteps = 6;
        [SerializeField, Tooltip("Warm-up seconds per search probe.")]
        private float _searchWarmupSeconds = 2f;
        [SerializeField, Tooltip("Measured seconds per search probe.")]
        private float _searchMeasureSeconds = 5f;

        [Header("Budget guard")]
        [SerializeField, Tooltip("A step whose running average exceeds this is aborted.")]
        private float _abortMs = 500f;
        [SerializeField, Tooltip("Seconds of measurement before the abort check is applied.")]
        private float _abortAfterSeconds = 3f;
        [SerializeField, Tooltip("A spawn slower than this ends the step as spawn_timeout.")]
        private float _spawnTimeoutMs = 60_000f;

        [Header("Quick mode")]
        [SerializeField, Tooltip("Warm-up seconds with -benchmark-quick.")]
        private float _quickWarmupSeconds = 1f;
        [SerializeField, Tooltip("Measured seconds with -benchmark-quick.")]
        private float _quickMeasureSeconds = 3f;

        [Header("Editor")]
        [SerializeField, Tooltip("Run the protocol in the editor without command-line switches.")]
        private bool _runInEditorWithoutArgs;
        #endregion

        #region Events
        /// <summary>Raised when a run finished, with the report path or null when it could not be written.</summary>
        public event System.Action<string> RunCompleted;
        #endregion

        #region Private fields
        private BenchmarkArgs _args;
        private BenchmarkReport _report;
        private bool _running;   // guards against overlapping runs from menu and command line
        #endregion

        #region Lifecycle
        private void Awake()
        {
            var args = BenchmarkArgs.FromCommandLine();
            if (args.Benchmark || (Application.isEditor && _runInEditorWithoutArgs))
                _switcher.SuppressAutoLoad();
        }

        private void Start()
        {
            var args = BenchmarkArgs.FromCommandLine();
            if (args.Benchmark)
                StartRun(args, true);
            else if (Application.isEditor && _runInEditorWithoutArgs)
                StartRun(BenchmarkArgs.ForManualRun(true), false);
        }
        #endregion

        #region Public interface
        /// <summary>Starts a run. With <paramref name="quitWhenDone"/> the player quits after writing the report; otherwise <see cref="RunCompleted"/> fires.</summary>
        public void StartRun(BenchmarkArgs args, bool quitWhenDone)
        {
            if (_running)
            {
                Debug.LogWarning("[BenchmarkRunner] A run is already in progress.");
                return;
            }
            _args = args;
            StartCoroutine(Run(quitWhenDone));
        }
        #endregion

        #region Protocol
        /// <summary>Whole run: fixed display state, every step, report, then quit or hand back to the menu.</summary>
        private IEnumerator Run(bool quitWhenDone)
        {
            _running = true;
            DisplayMode.Apply(_args.Resolution);
            var windows = ResolveWindows();
            PrepareHud();
            yield return null;
            _report = new BenchmarkReport { device = DeviceInfo.Collect(), settings = CollectSettings(windows) };
            for (int i = 0; i < _switcher.Catalog.Steps.Count; i++)
                if (IsSelected(_switcher.Catalog.Steps[i]))
                    yield return RunStep(i, windows);
            string path = WriteReport();
            _running = false;
            if (quitWhenDone)
                DisplayMode.Quit();
            else
                RunCompleted?.Invoke(path);
        }

        /// <summary>Loads one step and runs both measurements on it.</summary>
        private IEnumerator RunStep(int stepIndex, MeasureWindows windows)
        {
            yield return _switcher.LoadStep(stepIndex, false);
            if (_switcher.Active == null)
            {
                Debug.LogError($"[BenchmarkRunner] Step {stepIndex} has no backend; skipping.");
                yield break;
            }
            yield return Sweep(windows);
            yield return SearchCountAtTarget(windows);
        }

        /// <summary>Measures each sweep count in order; after the first failure the rest are recorded as skipped.</summary>
        private IEnumerator Sweep(MeasureWindows windows)
        {
            bool failed = false;
            foreach (int count in _sweepCounts)
            {
                var result = NewResult(count);
                if (failed)
                    result.status = SweepResult.StatusSkipped;
                else
                    yield return Measure(result, windows.SweepWarmup, windows.SweepMeasure);
                _report.sweep.Add(result);
                failed |= !result.Succeeded;
            }
        }

        /// <summary>
        /// Finds the largest count that holds the target: brackets it from the sweep results (no count is
        /// measured twice), doubles upward while nothing has failed yet, then bisects until the bracket
        /// is within <see cref="_searchResolution"/>, and records the last passing count.
        /// </summary>
        private IEnumerator SearchCountAtTarget(MeasureWindows windows)
        {
            var bracket = BracketFromSweep();
            yield return ExpandBracket(bracket, windows);
            yield return BisectBracket(bracket, windows);
            RecordCountAtTarget(bracket);
        }

        /// <summary>Seeds the bracket with the active step's sweep results: largest pass below, smallest failure above.</summary>
        private SearchBracket BracketFromSweep()
        {
            var bracket = new SearchBracket();
            foreach (var result in _report.sweep)
            {
                if (result.backend != _switcher.Active.DisplayName || result.status == SweepResult.StatusSkipped)
                    continue;
                bracket.Absorb(result, Passes(result));
            }
            return bracket;
        }

        /// <summary>Doubles from the last pass until a count fails or the ceiling is reached.</summary>
        private IEnumerator ExpandBracket(SearchBracket bracket, MeasureWindows windows)
        {
            while (bracket.FirstFail == 0)
            {
                int next = bracket.LastPassCount == 0 ? _searchStartCount : bracket.LastPassCount * 2;
                if (next > _searchCeiling)
                    yield break;
                yield return Probe(bracket, next, windows);
            }
        }

        /// <summary>Halves the bracket until it is narrow enough or the probe budget is spent.</summary>
        private IEnumerator BisectBracket(SearchBracket bracket, MeasureWindows windows)
        {
            for (int step = 0; step < _maxBisectionSteps && bracket.FirstFail > 0; step++)
            {
                int width = bracket.FirstFail - bracket.LastPassCount;
                if (width <= Mathf.Max(1000, bracket.LastPassCount * _searchResolution))
                    yield break;
                int mid = RoundToThousand((bracket.LastPassCount + bracket.FirstFail) / 2);
                if (mid <= bracket.LastPassCount || mid >= bracket.FirstFail)
                    yield break;
                yield return Probe(bracket, mid, windows);
            }
        }

        /// <summary>Measures one count and folds it into the bracket.</summary>
        private IEnumerator Probe(SearchBracket bracket, int count, MeasureWindows windows)
        {
            var probe = NewResult(count);
            yield return Measure(probe, windows.ProbeWarmup, windows.ProbeMeasure);
            _report.search.Add(probe);
            bracket.Absorb(probe, Passes(probe));
        }

        /// <summary>Writes the bracket's last passing count into the report, if any count passed.</summary>
        private void RecordCountAtTarget(SearchBracket bracket)
        {
            if (bracket.LastPass == null)
                return;
            _report.countAtTarget.Add(new CountAtTargetResult
            {
                backend = bracket.LastPass.backend,
                count = bracket.LastPass.count,
                avgMs = bracket.LastPass.frameMs.avg,
                low1Ms = bracket.LastPass.frameMs.low1,
            });
        }

        /// <summary>Nearest multiple of a thousand, so probe counts stay readable.</summary>
        private static int RoundToThousand(int count)
        {
            return Mathf.Max(1000, Mathf.RoundToInt(count / 1000f) * 1000);
        }

        /// <summary>Spawns, warms up, samples the window with the abort guard, fills the result and despawns.</summary>
        private IEnumerator Measure(SweepResult result, float warmupSeconds, float measureSeconds)
        {
            var backend = _switcher.Active;
            SetStatus($"{backend.DisplayName}  {result.count:N0}  spawning");
            backend.Spawn(result.count);
            result.spawnMs = backend.LastSpawnMilliseconds;
            if (result.spawnMs > _spawnTimeoutMs)
            {
                result.status = SweepResult.StatusSpawnTimeout;
                backend.Despawn();
                yield break;
            }
            yield return null;
            _cameraRig.SkipToWideShot();
            _cameraRig.Playing = true;
            SetStatus($"{backend.DisplayName}  {result.count:N0}  warm-up");
            yield return WaitUnscaled(warmupSeconds);
            SetStatus($"{backend.DisplayName}  {result.count:N0}  measuring");
            bool aborted = false;
            yield return SampleWindow(measureSeconds, value => aborted = value);
            FillResult(result, aborted);
            backend.Despawn();
            yield return null;
        }


        /// <summary>Records frames for a duration, ending early when the running average breaches the abort ceiling.</summary>
        private IEnumerator SampleWindow(float seconds, System.Action<bool> setAborted)
        {
            _sampler.BeginWindow();
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < seconds)
            {
                yield return null;
                if (Time.unscaledTime - start < _abortAfterSeconds || _sampler.WindowAverageMs <= _abortMs)
                    continue;
                setAborted(true);
                yield break;
            }
            setAborted(false);
        }
        #endregion

        #region Results
        /// <summary>Empty result for the active backend at a count.</summary>
        private SweepResult NewResult(int count)
        {
            return new SweepResult { backend = _switcher.Active.DisplayName, count = count };
        }

        /// <summary>True when a window completed, held the target average, and its 1% low stayed within tolerance: a stable 30 fps, not a lucky one.</summary>
        private bool Passes(SweepResult probe)
        {
            return probe.Succeeded && probe.frameMs.avg <= _targetMs && probe.frameMs.low1 <= _targetMs * _low1ToleranceFactor;
        }

        /// <summary>Copies the sampler's window into the result and sets its status.</summary>
        private void FillResult(SweepResult result, bool aborted)
        {
            var samples = _sampler.EndWindow();
            result.status = aborted ? SweepResult.StatusAborted : SweepResult.StatusOk;
            result.frameMs = FrameStatistics.FrameMs(samples);
            result.mainThreadMs = FrameStatistics.Average(samples, static s => s.MainThreadMs);
            result.renderThreadMs = FrameStatistics.Average(samples, static s => s.RenderThreadMs);
            result.gpuMs = FrameStatistics.AverageWhereReported(samples, static s => s.GpuMs);   // frames without GPU timing must not dilute the mean
            result.drawCalls = FrameStatistics.DrawCalls(samples);
            result.allocatedMb = _sampler.MemoryMb;
            result.samples = new float[samples.Count];
            for (int i = 0; i < samples.Count; i++)
                result.samples[i] = samples[i].FrameMs;
        }

        /// <summary>Protocol constants as written into the report.</summary>
        private RunSettings CollectSettings(MeasureWindows windows)
        {
            return new RunSettings
            {
                warmupSec = windows.SweepWarmup,
                measureSec = windows.SweepMeasure,
                targetMs = _targetMs,
                low1ToleranceFactor = _low1ToleranceFactor,
                searchResolution = _searchResolution,
                abortMs = _abortMs,
                sweepCounts = _sweepCounts,
                searchCeiling = _searchCeiling,
            };
        }

        /// <summary>Serialises the report next to the executable, falling back to the persistent data path. Returns the path or null.</summary>
        private string WriteReport()
        {
            string json = JsonUtility.ToJson(_report, true);
            string fileName = $"bench_{Slug(_report.device.model)}_{_report.device.gitHash}_{System.DateTime.Now:yyyyMMdd-HHmmss}.json";
            string directory = _args.OutputDirectory ?? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Benchmarks");
            string path = TryWrite(directory, fileName, json) ?? TryWrite(Application.persistentDataPath, fileName, json);
            Debug.Log(path != null ? $"[BenchmarkRunner] Report written to {path}" : "[BenchmarkRunner] Report could not be written anywhere.");
            return path;
        }

        /// <summary>Writes a file, returning its path or null on failure.</summary>
        private static string TryWrite(string directory, string fileName, string content)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, fileName);
                File.WriteAllText(path, content);
                return path;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[BenchmarkRunner] Could not write to {directory}: {exception.Message}");
                return null;
            }
        }

        /// <summary>Lower-case alphanumeric slug of a device name for file names.</summary>
        private static string Slug(string text)
        {
            var builder = new System.Text.StringBuilder();
            foreach (char c in text ?? "device")
                builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
            return builder.ToString().Trim('-');
        }
        #endregion

        #region Helpers
        /// <summary>True when the step is in the command-line filter, or no filter was given.</summary>
        private bool IsSelected(StepEntry step)
        {
            return _args.BackendFilter == null || _args.BackendFilter.Contains(step.Id);
        }

        /// <summary>Window lengths for this run; quick mode shortens everything to the quick values.</summary>
        private MeasureWindows ResolveWindows()
        {
            if (_args.Quick)
                return new MeasureWindows(_quickWarmupSeconds, _quickMeasureSeconds, _quickWarmupSeconds, _quickMeasureSeconds);
            return new MeasureWindows(_warmupSeconds, _measureSeconds, _searchWarmupSeconds, _searchMeasureSeconds);
        }

        /// <summary>Reduces the HUD to the status line so every step renders the same overlay.</summary>
        private void PrepareHud()
        {
            if (_hud == null)
                return;
            _hud.Mode = HudMode.StatusLine;
        }

        /// <summary>Writes the HUD status line and the log.</summary>
        private void SetStatus(string text)
        {
            if (_hud != null)
                _hud.SetStatus(text);
        }

        /// <summary>The count-at-target search state: the largest count seen passing and the smallest seen failing.</summary>
        private sealed class SearchBracket
        {
            /// <summary>Largest count measured so far that passed, with its timings; null until one passes.</summary>
            public SweepResult LastPass;
            /// <summary>Smallest count measured so far that failed; zero while nothing has failed.</summary>
            public int FirstFail;
            /// <summary>Count of <see cref="LastPass"/>, or zero.</summary>
            public int LastPassCount => LastPass != null ? LastPass.count : 0;

            /// <summary>Folds a measured result into the bracket.</summary>
            public void Absorb(SweepResult result, bool passed)
            {
                if (passed && result.count > LastPassCount)
                    LastPass = result;
                else if (!passed && (FirstFail == 0 || result.count < FirstFail))
                    FirstFail = result.count;
            }
        }

        /// <summary>Warm-up and measure lengths for sweep steps and search probes.</summary>
        private readonly struct MeasureWindows
        {
            /// <summary>Seconds discarded before sampling a sweep count.</summary>
            public readonly float SweepWarmup;
            /// <summary>Seconds sampled per sweep count.</summary>
            public readonly float SweepMeasure;
            /// <summary>Seconds discarded before sampling a search probe.</summary>
            public readonly float ProbeWarmup;
            /// <summary>Seconds sampled per search probe.</summary>
            public readonly float ProbeMeasure;

            public MeasureWindows(float sweepWarmup, float sweepMeasure, float probeWarmup, float probeMeasure)
            {
                SweepWarmup = sweepWarmup;
                SweepMeasure = sweepMeasure;
                ProbeWarmup = probeWarmup;
                ProbeMeasure = probeMeasure;
            }
        }

        /// <summary>Waits in unscaled time so a frozen field does not stall the protocol.</summary>
        private static IEnumerator WaitUnscaled(float seconds)
        {
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < seconds)
                yield return null;
        }
        #endregion
    }
}
