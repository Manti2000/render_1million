using System;
using System.Collections.Generic;
using UnityEngine;

namespace MillionObjects.Benchmark
{
    /// <summary>
    /// JSON model of one benchmark run. Field names are camelCase public fields because JsonUtility
    /// serialises fields verbatim and this shape is the contract with the chart tool.
    /// </summary>
    [Serializable]
    public class BenchmarkReport
    {
        public DeviceInfo device;
        public RunSettings settings;
        public List<SweepResult> sweep = new List<SweepResult>();
        public List<SweepResult> search = new List<SweepResult>();   // every count-at-target probe, so charts get the extra points
        public List<CountAtTargetResult> countAtTarget = new List<CountAtTargetResult>();
    }

    /// <summary>Machine, resolution and build identity of a run.</summary>
    [Serializable]
    public class DeviceInfo
    {
        public string model;
        public string gpu;
        public string cpu;
        public int ramMb;
        public string os;
        public string resolution;
        public string unity;
        public string gitHash;
        public string buildTime;
        public string runTime;

        /// <summary>Collects the device identity from SystemInfo and the stamped <see cref="BuildInfo"/>.</summary>
        public static DeviceInfo Collect()
        {
            var build = BuildInfo.Load();
            return new DeviceInfo
            {
                model = SystemInfo.deviceModel,
                gpu = SystemInfo.graphicsDeviceName,
                cpu = SystemInfo.processorType,
                ramMb = SystemInfo.systemMemorySize,
                os = SystemInfo.operatingSystem,
                resolution = $"{Screen.width}x{Screen.height}",
                unity = Application.unityVersion,
                gitHash = build != null ? build.GitHash : "unknown",
                buildTime = build != null ? build.BuildTime : "unknown",
                runTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };
        }
    }

    /// <summary>Protocol constants the run used.</summary>
    [Serializable]
    public class RunSettings
    {
        public float warmupSec;
        public float measureSec;
        public float targetMs;
        public float low1ToleranceFactor;
        public float searchResolution;
        public float abortMs;
        public int[] sweepCounts;
        public int searchCeiling;
    }

    /// <summary>Frame-time statistics in milliseconds; fps is derived at chart time.</summary>
    [Serializable]
    public class FrameMsStats
    {
        public float avg;
        public float low1;
        public float low01;
        public float min;
        public float max;
    }

    /// <summary>One measured step of the fixed-count sweep.</summary>
    [Serializable]
    public class SweepResult
    {
        public const string StatusOk = "ok";
        public const string StatusAborted = "aborted";
        public const string StatusSpawnTimeout = "spawn_timeout";
        public const string StatusSkipped = "skipped";

        public string backend;
        public int count;
        public string status;
        public double spawnMs;
        public FrameMsStats frameMs = new FrameMsStats();
        public float mainThreadMs;
        public float renderThreadMs;
        public float gpuMs;
        public long drawCalls;
        public float allocatedMb;
        public float[] samples = Array.Empty<float>();

        /// <summary>True when the step completed its measurement window under the abort ceiling.</summary>
        public bool Succeeded => status == StatusOk;
    }

    /// <summary>Largest count a backend held under the target frame time.</summary>
    [Serializable]
    public class CountAtTargetResult
    {
        public string backend;
        public int count;
        public float avgMs;
        public float low1Ms;
    }
}
