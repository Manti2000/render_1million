using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace MillionObjects.Benchmark
{
    /// <summary>
    /// Samples frame timing every frame for the HUD and, on request, records a measurement window
    /// for the benchmark runner. Uses FrameTimingManager for the CPU/GPU split because it works in
    /// IL2CPP release players, and ProfilerRecorder only for counters that survive release builds:
    /// the per-path draw call counters (summed) and total used memory.
    /// </summary>
    public class FrameSampler : MonoBehaviour
    {
        #region Constants
        /// <summary>Frames kept for the HUD strip.</summary>
        public const int HistoryLength = 240;
        private const float BytesPerMegabyte = 1024f * 1024f;
        /// <summary>GPU frame times above this are driver garbage (Windows reported 3e8 ms on the first frames), treated as unavailable.</summary>
        private const float MaxCredibleGpuMs = 60_000f;
        /// <summary>Release-build render counters whose sum is the frame's draw call count.</summary>
        private static readonly string[] DrawCallCounterNames =
        {
            "Standard Draw Calls Count",
            "Standard Instanced Draw Calls Count",
            "Standard Indirect Draw Calls Count",
            "SRP Batcher Draw Calls Count",
            "BRG Draw Calls Count",
            "BRG Indirect Draw Calls Count",
        };
        #endregion

        #region Public properties
        /// <summary>Most recent frame's timings.</summary>
        public FrameSample Latest { get; private set; }
        /// <summary>True when FrameTimingManager delivered data this frame.</summary>
        public bool TimingAvailable { get; private set; }
        /// <summary>Draw calls this frame summed over the SRP Batcher, BRG, instanced and indirect paths, or -1 when no counter is available.</summary>
        public long DrawCalls => SumDrawCalls();
        /// <summary>Total used memory in MB, or -1 when the counter is unavailable in this build.</summary>
        public float MemoryMb => _memoryRecorder.Valid ? _memoryRecorder.LastValue / BytesPerMegabyte : -1f;
        /// <summary>True between <see cref="BeginWindow"/> and <see cref="EndWindow"/>.</summary>
        public bool IsRecording => _window != null;
        /// <summary>Running average frame time of the open window; zero when not recording.</summary>
        public float WindowAverageMs => _window == null || _window.Count == 0 ? 0f : (float)(_windowSum / _window.Count);
        #endregion

        #region Private fields
        private readonly FrameTiming[] _timings = new FrameTiming[1];      // reused FrameTimingManager output
        private readonly float[] _history = new float[HistoryLength];     // ring buffer of frame ms for the HUD
        private int _historyHead;                                          // next write slot in the ring buffer
        private List<FrameSample> _window;                                 // open measurement window, null when idle
        private double _windowSum;                                         // sum of FrameMs in the window for the running average
        private ProfilerRecorder[] _drawCallRecorders;                     // one per render path; Unity 6 has no single batches counter
        private ProfilerRecorder _memoryRecorder;
        #endregion

        #region Lifecycle
        private void OnEnable()
        {
            _drawCallRecorders = new ProfilerRecorder[DrawCallCounterNames.Length];
            for (int i = 0; i < DrawCallCounterNames.Length; i++)
                _drawCallRecorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, DrawCallCounterNames[i]);
            _memoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
        }

        private void OnDisable()
        {
            foreach (var recorder in _drawCallRecorders)
                recorder.Dispose();
            _memoryRecorder.Dispose();
        }

        private void Update()
        {
            var sample = CaptureSample();
            Latest = sample;
            PushHistory(sample.FrameMs);
            if (_window == null)
                return;
            _window.Add(sample);
            _windowSum += sample.FrameMs;
        }
        #endregion

        #region Public interface
        /// <summary>Starts recording frames into a fresh window.</summary>
        public void BeginWindow()
        {
            _window = new List<FrameSample>(4096);
            _windowSum = 0;
        }

        /// <summary>Stops recording and returns the window's samples; empty list when none was open.</summary>
        public List<FrameSample> EndWindow()
        {
            var window = _window ?? new List<FrameSample>();
            _window = null;
            _windowSum = 0;
            return window;
        }

        /// <summary>Each draw call counter with its latest value, for checking which render paths a step actually uses.</summary>
        public string DescribeDrawCalls()
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < _drawCallRecorders.Length; i++)
                builder.Append(DrawCallCounterNames[i]).Append('=').Append(_drawCallRecorders[i].Valid ? _drawCallRecorders[i].LastValue.ToString() : "n/a").Append("; ");
            return builder.ToString();
        }

        /// <summary>Frame time from the history ring, index 0 oldest to <see cref="HistoryLength"/> - 1 newest.</summary>
        public float HistoryAt(int index)
        {
            return _history[(_historyHead + index) % HistoryLength];
        }
        #endregion

        #region Sampling
        /// <summary>Sums the valid draw call recorders; -1 when none is valid.</summary>
        private long SumDrawCalls()
        {
            long total = 0;
            bool any = false;
            foreach (var recorder in _drawCallRecorders)
            {
                if (!recorder.Valid)
                    continue;
                total += recorder.LastValue;
                any = true;
            }
            return any ? total : -1;
        }

        /// <summary>Reads this frame's wall-clock time, draw call count and the FrameTimingManager split.</summary>
        private FrameSample CaptureSample()
        {
            var sample = new FrameSample { FrameMs = Time.unscaledDeltaTime * 1000f, DrawCalls = SumDrawCalls() };
            FrameTimingManager.CaptureFrameTimings();
            TimingAvailable = FrameTimingManager.GetLatestTimings(1, _timings) > 0;
            if (!TimingAvailable)
                return sample;
            sample.MainThreadMs = (float)_timings[0].cpuMainThreadFrameTime;
            sample.RenderThreadMs = (float)_timings[0].cpuRenderThreadFrameTime;
            sample.GpuMs = CredibleGpuMs(_timings[0].gpuFrameTime);
            return sample;
        }

        /// <summary>The reported GPU time, or zero (unavailable) when it is negative, not finite or absurdly large.</summary>
        private static float CredibleGpuMs(double gpuFrameTime)
        {
            if (double.IsNaN(gpuFrameTime) || double.IsInfinity(gpuFrameTime))
                return 0f;
            if (gpuFrameTime <= 0 || gpuFrameTime > MaxCredibleGpuMs)
                return 0f;
            return (float)gpuFrameTime;
        }

        /// <summary>Appends a frame time to the ring buffer.</summary>
        private void PushHistory(float frameMs)
        {
            _history[_historyHead] = frameMs;
            _historyHead = (_historyHead + 1) % HistoryLength;
        }
        #endregion
    }
}
