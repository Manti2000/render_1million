using System;
using System.Collections.Generic;

namespace MillionObjects.Benchmark
{
    /// <summary>Gaming-benchmark statistics over a window of frame samples: average, 1% low, 0.1% low, draw call range.</summary>
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

        /// <summary>Per-frame draw call range and mean over the frames where a counter reported; all -1 when none did.</summary>
        public static DrawCallStats DrawCalls(List<FrameSample> samples)
        {
            var stats = new DrawCallStats { min = -1, avg = -1f, max = -1 };
            if (samples == null)
                return stats;
            double sum = 0;
            int reported = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                long value = samples[i].DrawCalls;
                if (value < 0)
                    continue;
                if (reported == 0 || value < stats.min)
                    stats.min = value;
                if (reported == 0 || value > stats.max)
                    stats.max = value;
                sum += value;
                reported++;
            }
            if (reported > 0)
                stats.avg = (float)(sum / reported);
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

        /// <summary>Mean of one sample channel over the frames where it was reported (positive); zero when it never was.</summary>
        public static float AverageWhereReported(List<FrameSample> samples, Func<FrameSample, float> channel)
        {
            if (samples == null)
                return 0f;
            double sum = 0;
            int reported = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                float value = channel(samples[i]);
                if (value <= 0f)
                    continue;
                sum += value;
                reported++;
            }
            return reported > 0 ? (float)(sum / reported) : 0f;
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
