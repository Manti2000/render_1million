namespace MillionObjects.Benchmark
{
    /// <summary>One frame's timings in milliseconds as captured by <see cref="FrameSampler"/>.</summary>
    public struct FrameSample
    {
        /// <summary>Wall-clock frame time from unscaled delta time.</summary>
        public float FrameMs;
        /// <summary>Main thread time reported by FrameTimingManager; zero when unavailable.</summary>
        public float MainThreadMs;
        /// <summary>Render thread time reported by FrameTimingManager; zero when unavailable.</summary>
        public float RenderThreadMs;
        /// <summary>GPU time reported by FrameTimingManager; zero when unavailable.</summary>
        public float GpuMs;
    }
}
