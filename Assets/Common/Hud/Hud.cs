using MillionObjects.Benchmark;
using UnityEngine;
using UnityEngine.UIElements;

namespace MillionObjects
{
    /// <summary>
    /// The on-screen read-out shared by every step: a top-left panel with the frame time split, draw
    /// calls, memory, the frame-time strip, the flexibility card and a wall clock, and a bottom-right
    /// counter with the object count and the ramp countdown. <see cref="Mode"/> picks how much of that
    /// is shown. Text refreshes at a fixed interval to keep string allocation off the per-frame path.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        #region Constants
        private const float BarFullScaleMs = 50f;
        private const float BarMaxWidthPx = 300f;
        #endregion

        #region Inspector fields
        [SerializeField] private UIDocument _document;
        [SerializeField] private BackendSwitcher _switcher;
        [SerializeField] private FrameSampler _sampler;
        [SerializeField, Tooltip("Ramp owner, read for the countdown to the next object count.")]
        private RecordingDirector _director;
        [SerializeField, Tooltip("Seconds between text refreshes.")]
        private float _textRefreshInterval = 0.1f;
        #endregion

        #region Public properties
        /// <summary>How much of the read-out is on screen; applied to the elements as soon as it is set.</summary>
        public HudMode Mode
        {
            get => _mode;
            set { _mode = value; ApplyMode(); }
        }
        #endregion

        #region Private fields
        private VisualElement _root;
        private Label _stepName, _count, _frameMs, _fps, _mainMs, _renderMs, _gpuMs, _drawCalls, _memory, _spawnMs;
        private Label _cardBehaviour, _cardEcosystem, _cardCost, _clock, _status, _rampTimer;
        private VisualElement _barMain, _barRender, _barGpu;
        private VisualElement _frameRow, _stats, _card, _counter;
        private FrameTimeStrip _strip;
        private HudMode _mode;            // Hidden until a mode controller or the menu asks for a layout
        private float _nextTextRefresh;   // realtime at which labels are rewritten again
        #endregion

        #region Lifecycle
        private void OnEnable()
        {
            _root = _document.rootVisualElement.Q<VisualElement>("hud");
            QueryLabels();
            QueryBars();
            QueryGroups();
            ApplyMode();
            if (_switcher != null)
                _switcher.StepLoaded += OnStepLoaded;
            OnStepLoaded(_switcher != null ? _switcher.Active : null);
        }

        private void OnDisable()
        {
            if (_switcher != null)
                _switcher.StepLoaded -= OnStepLoaded;
        }

        private void Update()
        {
            if (_root == null || _mode == HudMode.Hidden)
                return;
            _strip?.MarkDirtyRepaint();
            UpdateBars();
            if (Time.unscaledTime < _nextTextRefresh)
                return;
            _nextTextRefresh = Time.unscaledTime + _textRefreshInterval;
            UpdateText();
        }
        #endregion

        #region Public interface
        /// <summary>Sets the free-form status line used by benchmark and recording modes.</summary>
        public void SetStatus(string text)
        {
            if (_status != null)
                _status.text = text ?? string.Empty;
        }

        /// <summary>Steps the interactive layouts on the HUD hotkey: hidden, framerate, full, back to hidden. The benchmark's status line stays out of the cycle.</summary>
        public void CycleMode()
        {
            Mode = _mode switch
            {
                HudMode.Hidden => HudMode.Framerate,
                HudMode.Framerate => HudMode.Full,
                _ => HudMode.Hidden
            };
        }
        #endregion

        #region Layout
        /// <summary>Which elements each mode puts on screen: framerate keeps the frame row, the strip and the counter, full keeps everything, the benchmark's layout keeps the status line alone.</summary>
        private void ApplyMode()
        {
            if (_root == null)
                return;
            bool interactive = _mode == HudMode.Framerate || _mode == HudMode.Full;
            bool full = _mode == HudMode.Full;
            ShowElement(_root, _mode != HudMode.Hidden);
            ShowElement(_stepName, full);
            ShowElement(_frameRow, interactive);
            ShowElement(_stats, full);
            ShowElement(_strip, interactive);
            ShowElement(_card, full);
            ShowElement(_clock, full);
            ShowElement(_status, full || _mode == HudMode.StatusLine);
            ShowElement(_counter, interactive);
        }

        /// <summary>Shows or hides one element, tolerating a missing one.</summary>
        private static void ShowElement(VisualElement element, bool shown)
        {
            if (element != null)
                element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        }
        #endregion

        #region Element lookup
        /// <summary>Caches every label by its UXML name.</summary>
        private void QueryLabels()
        {
            _stepName = _root.Q<Label>("step-name");
            _count = _root.Q<Label>("count");
            _frameMs = _root.Q<Label>("frame-ms");
            _fps = _root.Q<Label>("fps");
            _mainMs = _root.Q<Label>("main-ms");
            _renderMs = _root.Q<Label>("render-ms");
            _gpuMs = _root.Q<Label>("gpu-ms");
            _drawCalls = _root.Q<Label>("draw-calls");
            _memory = _root.Q<Label>("memory");
            _spawnMs = _root.Q<Label>("spawn-ms");
            _cardBehaviour = _root.Q<Label>("card-behaviour");
            _cardEcosystem = _root.Q<Label>("card-ecosystem");
            _cardCost = _root.Q<Label>("card-cost");
            _clock = _root.Q<Label>("clock");
            _status = _root.Q<Label>("status");
            _rampTimer = _root.Q<Label>("ramp-timer");
        }

        /// <summary>Caches the timing bars.</summary>
        private void QueryBars()
        {
            _barMain = _root.Q<VisualElement>("bar-main");
            _barRender = _root.Q<VisualElement>("bar-render");
            _barGpu = _root.Q<VisualElement>("bar-gpu");
        }

        /// <summary>Caches the containers the mode switches between and binds the strip to the sampler.</summary>
        private void QueryGroups()
        {
            _frameRow = _root.Q<VisualElement>("frame-row");
            _stats = _root.Q<VisualElement>("stats");
            _card = _root.Q<VisualElement>("card");
            _counter = _root.Q<VisualElement>("counter");
            _strip = _root.Q<FrameTimeStrip>("frame-strip");
            if (_strip != null)
                _strip.Sampler = _sampler;
        }
        #endregion

        #region Refresh
        /// <summary>Writes the step name and flexibility card when a step loads.</summary>
        private void OnStepLoaded(ObjectBackend backend)
        {
            if (backend == null)
            {
                _stepName.text = "no step loaded";
                _cardBehaviour.text = _cardEcosystem.text = _cardCost.text = string.Empty;
                return;
            }
            _stepName.text = backend.DisplayName;
            _cardBehaviour.text = backend.Card.BehaviourLivesIn;
            _cardEcosystem.text = backend.Card.Ecosystem;
            _cardCost.text = backend.Card.FeatureCost;
        }

        /// <summary>Rewrites the numeric labels from the sampler and active backend.</summary>
        private void UpdateText()
        {
            var sample = _sampler != null ? _sampler.Latest : default;
            var backend = _switcher != null ? _switcher.Active : null;
            _count.text = backend != null ? backend.Count.ToString("N0") : "0";
            _spawnMs.text = backend != null ? $"{backend.LastSpawnMilliseconds:N0} ms" : "-";
            _frameMs.text = $"{sample.FrameMs:F1} ms";
            _fps.text = sample.FrameMs > 0f ? $"{1000f / sample.FrameMs:F0} fps" : "-";
            _mainMs.text = $"{sample.MainThreadMs:F1} ms";
            _renderMs.text = $"{sample.RenderThreadMs:F1} ms";
            _gpuMs.text = sample.GpuMs > 0f ? $"{sample.GpuMs:F1} ms" : "n/a";   // FrameTimingManager reports 0 where the driver gives no GPU timing
            _drawCalls.text = _sampler != null && _sampler.DrawCalls >= 0 ? _sampler.DrawCalls.ToString("N0") : "n/a";
            _memory.text = _sampler != null && _sampler.MemoryMb >= 0f ? $"{_sampler.MemoryMb:N0} MB" : "n/a";
            _clock.text = FormatClock(Time.realtimeSinceStartup);
            UpdateRampTimer();
        }

        /// <summary>Writes the countdown to the next ramp count, or blanks it while no ramp is walking.</summary>
        private void UpdateRampTimer()
        {
            if (_rampTimer == null)
                return;
            float seconds = _director != null ? _director.SecondsToNextRampCount : 0f;
            _rampTimer.text = seconds > 0f ? $"next in {seconds:F0} s" : string.Empty;
        }

        /// <summary>Scales the three timing bars and marks the longest one as the bottleneck.</summary>
        private void UpdateBars()
        {
            var sample = _sampler != null ? _sampler.Latest : default;
            float cpuMs = Mathf.Max(sample.MainThreadMs, sample.RenderThreadMs);
            SetBar(_barMain, sample.MainThreadMs, sample.MainThreadMs >= sample.GpuMs && sample.MainThreadMs >= sample.RenderThreadMs);
            SetBar(_barRender, sample.RenderThreadMs, sample.RenderThreadMs > sample.MainThreadMs && sample.RenderThreadMs >= sample.GpuMs);
            SetBar(_barGpu, sample.GpuMs, sample.GpuMs > cpuMs);
        }

        /// <summary>Sets one bar's width from a millisecond value and toggles its bottleneck class.</summary>
        private static void SetBar(VisualElement bar, float ms, bool bottleneck)
        {
            if (bar == null)
                return;
            bar.style.width = Mathf.Clamp01(ms / BarFullScaleMs) * BarMaxWidthPx;
            bar.EnableInClassList("bottleneck", bottleneck);
        }

        /// <summary>Formats seconds as mm:ss.ff.</summary>
        private static string FormatClock(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            float remainder = seconds - minutes * 60f;
            return $"{minutes:00}:{remainder:00.00}";
        }
        #endregion
    }
}
