using System;
using System.Diagnostics;
using System.IO;
using MillionObjects.Benchmark;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MillionObjects.Editor
{
    /// <summary>Writes the current git hash and time into the BuildInfo resource before every player build.</summary>
    public class BuildInfoStamper : IPreprocessBuildWithReport
    {
        #region Constants
        private const string AssetPath = "Assets/Common/Resources/BuildInfo.asset";
        #endregion

        #region IPreprocessBuildWithReport
        /// <summary>Runs before other build preprocessors.</summary>
        public int callbackOrder => 0;

        /// <summary>Stamps the asset right before the player is built.</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            Stamp();
        }
        #endregion

        #region Public interface
        /// <summary>Stamps the BuildInfo asset with the current commit and time, creating the asset when missing.</summary>
        [MenuItem("Million Objects/Stamp Build Info")]
        public static void Stamp()
        {
            var info = LoadOrCreate();
            info.Stamp(ReadGitShortHash(), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            EditorUtility.SetDirty(info);
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log($"[BuildInfoStamper] Stamped {info.GitHash} at {info.BuildTime}.");
        }
        #endregion

        #region Helpers
        /// <summary>Loads the BuildInfo asset or creates it in the Common Resources folder.</summary>
        private static BuildInfo LoadOrCreate()
        {
            var info = AssetDatabase.LoadAssetAtPath<BuildInfo>(AssetPath);
            if (info != null)
                return info;
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            info = ScriptableObject.CreateInstance<BuildInfo>();
            AssetDatabase.CreateAsset(info, AssetPath);
            return info;
        }

        /// <summary>Short hash of HEAD, or "unknown" when git is unavailable.</summary>
        private static string ReadGitShortHash()
        {
            try
            {
                var startInfo = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                };
                using var process = Process.Start(startInfo);
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return string.IsNullOrEmpty(output) ? "unknown" : output;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
        #endregion
    }
}
