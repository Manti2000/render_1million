using System;
using System.Collections.Generic;

namespace MillionObjects.Benchmark
{
    /// <summary>Gaming-benchmark statistics over a window of frame samples: average, 1% low, 0.1% low.</summary>
    public static class FrameStatistics
    {
        #region Public interface
        /// <summary>Frame-time statistics of a window. Percentiles are of the slowest frames, so "low1" is the 99th percentile in ms.</summary>
        public static FrameMsStats FrameMs(List<FrameSample> samples)
        {
            var stats = new FrameMsStats();
            if (samples == null || samples.Count == 0)
                return stats;
            var sorted = new float[samples.Count];
            for (int i = 0; i < sorted.Length; i++)
                sorted[i] = samples[i].FrameMs;
            Array.Sort(sorted);
            stats.avg = Average(samples, static s => s.FrameMs);
            stats.low1 = Percentile(sorted, 0.99f);
            stats.low01 = Percentile(sorted, 0.999f);
            stats.min = sorted[0];
            stats.max = sorted[sorted.Length - 1];
            return stats;
        }

        /// <summary>Mean of one sample channel.</summary>
        public static float Average(List<FrameSample> samples, Func<FrameSample, float> channel)
        {
            if (samples == null || samples.Count == 0)
                return 0f;
            double sum = 0;
            for (int i = 0; i < samples.Count; i++)
                sum += channel(samples[i]);
            return (float)(sum / samples.Count);
        }
        #endregion

        #region Helpers
        /// <summary>Nearest-rank percentile of an ascending array.</summary>
        private static float Percentile(float[] sorted, float rank)
        {
            int index = (int)Math.Ceiling(rank * sorted.Length) - 1;
            return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
        }
        #endregion
    }
}
