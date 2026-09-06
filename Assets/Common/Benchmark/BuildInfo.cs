using UnityEngine;

namespace MillionObjects.Benchmark
{
    /// <summary>Git hash and build time stamped into Resources by the editor before every build.</summary>
    public class BuildInfo : ScriptableObject
    {
        #region Constants
        /// <summary>Resources path the asset is loaded from.</summary>
        public const string ResourcePath = "BuildInfo";
        #endregion

        #region Inspector fields
        [SerializeField, Tooltip("Short git hash of the built commit.")]
        private string _gitHash = "unknown";
        [SerializeField, Tooltip("Build timestamp, yyyy-MM-dd HH:mm:ss.")]
        private string _buildTime = "unknown";
        #endregion

        #region Public properties
        /// <summary>Short git hash of the built commit, or "unknown".</summary>
        public string GitHash => _gitHash;
        /// <summary>Build timestamp, or "unknown".</summary>
        public string BuildTime => _buildTime;
        #endregion

        #region Public interface
        /// <summary>Loads the stamped asset, or null when none exists.</summary>
        public static BuildInfo Load()
        {
            return Resources.Load<BuildInfo>(ResourcePath);
        }

        /// <summary>Overwrites the stamp. Editor-only callers save the asset afterwards.</summary>
        public void Stamp(string gitHash, string buildTime)
        {
            _gitHash = gitHash;
            _buildTime = buildTime;
        }
        #endregion
    }
}
