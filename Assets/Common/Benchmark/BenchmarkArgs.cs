using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace MillionObjects.Benchmark
{
    /// <summary>
    /// Command-line switches of the player. <c>-benchmark</c> runs the unattended protocol and quits;
    /// <c>-record</c> plays the recording script with the HUD on. Everything else is optional.
    /// </summary>
    public sealed class BenchmarkArgs
    {
        #region Public properties
        /// <summary>Run the unattended benchmark and quit.</summary>
        public bool Benchmark { get; private set; }
        /// <summary>Play the recording script: HUD on, camera path on, count ramp on a timer.</summary>
        public bool Record { get; private set; }
        /// <summary>Directory the JSON report is written to; null for the default next to the executable.</summary>
        public string OutputDirectory { get; private set; }
        /// <summary>Step ids to run (lower case); null means every step in the catalog.</summary>
        public HashSet<string> BackendFilter { get; private set; }
        /// <summary>Short warm-up and measure windows for smoke tests.</summary>
        public bool Quick { get; private set; }
        /// <summary>Forced window resolution, or null to pick 1080p on PC and 800p on Steam Deck.</summary>
        public int2? Resolution { get; private set; }
        #endregion

        #region Public interface
        /// <summary>Parses the process command line.</summary>
        public static BenchmarkArgs FromCommandLine()
        {
            return Parse(Environment.GetCommandLineArgs());
        }

        /// <summary>Arguments for a run started from the start menu instead of the command line.</summary>
        public static BenchmarkArgs ForManualRun(bool quick)
        {
            return new BenchmarkArgs { Benchmark = true, Quick = quick };
        }

        /// <summary>Parses an argument vector. Unknown switches are ignored so Unity's own flags pass through.</summary>
        public static BenchmarkArgs Parse(string[] args)
        {
            var result = new BenchmarkArgs();
            for (int i = 0; i < args.Length; i++)
                result.ApplySwitch(args, ref i);
            return result;
        }
        #endregion

        #region Parsing
        /// <summary>Applies the switch at <paramref name="index"/>, consuming a value argument when it has one.</summary>
        private void ApplySwitch(string[] args, ref int index)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "-benchmark": Benchmark = true; break;
                case "-record": Record = true; break;
                case "-benchmark-quick": Quick = true; break;
                case "-benchmark-out": OutputDirectory = NextValue(args, ref index); break;
                case "-benchmark-backends": BackendFilter = ParseIdList(NextValue(args, ref index)); break;
                case "-benchmark-resolution": Resolution = ParseResolution(NextValue(args, ref index)); break;
            }
        }

        /// <summary>Returns the argument after <paramref name="index"/> and advances past it, or null when missing.</summary>
        private static string NextValue(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
                return null;
            index++;
            return args[index];
        }

        /// <summary>Splits "mono,ecs,indirect" into a lower-case id set.</summary>
        private static HashSet<string> ParseIdList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                ids.Add(id.Trim());
            return ids;
        }

        /// <summary>Parses "1920x1080"; returns null for anything else.</summary>
        private static int2? ParseResolution(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var parts = value.ToLowerInvariant().Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int width) || !int.TryParse(parts[1], out int height))
                return null;
            return new int2(width, height);
        }
        #endregion
    }
}
