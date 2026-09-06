using System;
using System.Collections.Generic;
using UnityEngine;

namespace MillionObjects
{
    /// <summary>Ordered list of the ladder's step scenes. Single source of truth for hotkeys, HUD and benchmark runner.</summary>
    [CreateAssetMenu(menuName = "Million Objects/Step Catalog", fileName = "StepCatalog")]
    public class StepCatalog : ScriptableObject
    {
        #region Inspector fields
        [SerializeField, Tooltip("Steps in ladder order: slowest and most flexible first.")]
        private StepEntry[] _steps = Array.Empty<StepEntry>();
        #endregion

        #region Public properties
        /// <summary>Steps in ladder order.</summary>
        public IReadOnlyList<StepEntry> Steps => _steps;
        #endregion

        #region Public interface
        /// <summary>Index of the step with the given command-line id, or -1.</summary>
        public int IndexOf(string id)
        {
            for (int i = 0; i < _steps.Length; i++)
                if (string.Equals(_steps[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
        #endregion
    }

    /// <summary>One rung of the ladder: its scene and how it is named on the command line and the HUD.</summary>
    [Serializable]
    public class StepEntry
    {
        #region Inspector fields
        [SerializeField, Tooltip("Short id used by -benchmark-backends, e.g. 'mono'.")]
        private string _id;
        [SerializeField, Tooltip("Name shown in the HUD and reports.")]
        private string _displayName;
        [SerializeField, Tooltip("Scene path relative to the project, e.g. Assets/Steps/01_MonoBehaviour/01_MonoBehaviour.unity.")]
        private string _scenePath;
        #endregion

        #region Public properties
        /// <summary>Short id used on the command line, e.g. "mono".</summary>
        public string Id => _id;
        /// <summary>Name shown in the HUD, menu and reports.</summary>
        public string DisplayName => _displayName;
        /// <summary>Project-relative path of the step scene.</summary>
        public string ScenePath => _scenePath;
        #endregion
    }
}
