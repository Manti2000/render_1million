using System;
using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MillionObjects
{
    /// <summary>
    /// Owns the active step. Loads step scenes additively on top of the bootstrap scene, finds the
    /// <see cref="ObjectBackend"/> inside, injects settings and ticks it every frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]   // publish the frame context before any per-object Update runs
    public class BackendSwitcher : MonoBehaviour
    {
        #region Inspector fields
        [SerializeField, Tooltip("Ladder of step scenes.")]
        private StepCatalog _catalog;
        [SerializeField, Tooltip("Shared field settings injected into every backend.")]
        private FieldSettings _settings;
        [SerializeField, Tooltip("Objects spawned when a step is loaded interactively.")]
        private int _initialCount = 10000;
        [SerializeField, Tooltip("Load the first step automatically on start (disabled by benchmark and recording modes).")]
        private bool _loadFirstStepOnStart = true;
        #endregion

        #region Public properties
        /// <summary>Ladder of step scenes.</summary>
        public StepCatalog Catalog => _catalog;
        /// <summary>Shared field settings.</summary>
        public FieldSettings Settings => _settings;
        /// <summary>Backend of the loaded step, or null while nothing is loaded.</summary>
        public ObjectBackend Active { get; private set; }
        /// <summary>Index into the catalog of the loaded step, or -1.</summary>
        public int ActiveStepIndex { get; private set; } = -1;
        /// <summary>True while a step scene is loading or unloading.</summary>
        public bool IsLoading { get; private set; }
        /// <summary>Object count used by interactive spawns and respawns.</summary>
        public int SpawnCount { get; set; }
        /// <summary>When false the active backend is not ticked, freezing the field.</summary>
        public bool TickingEnabled { get; set; } = true;
        /// <summary>Attractor pushed into the active backend every frame; (x, y, z, radius).</summary>
        public float4 Attractor { get; set; }
        #endregion

        #region Events
        /// <summary>Raised after a step scene finished loading and its backend was initialised.</summary>
        public event Action<ObjectBackend> StepLoaded;
        /// <summary>Raised after the active field was rebuilt in place because its settings changed; framing refreshes, the camera does not restart.</summary>
        public event Action<ObjectBackend> FieldRebuilt;
        #endregion

        #region Private fields
        private Scene _activeScene;   // the additively loaded step scene, unloaded when switching
        #endregion

        #region Lifecycle
        private void Awake()
        {
            SpawnCount = _initialCount;
        }

        private void Start()
        {
            if (_loadFirstStepOnStart && _catalog != null && _catalog.Steps.Count > 0)
                LoadStep(0, true);
        }

        private void OnEnable()
        {
            if (_settings != null)
                _settings.Changed += RespawnWithNewSettings;
        }

        private void OnDisable()
        {
            if (_settings != null)
                _settings.Changed -= RespawnWithNewSettings;
        }

        private void Update()
        {
            if (Active == null || !TickingEnabled)
                return;
            Active.Attractor = Attractor;
            Active.Tick(Time.time, Time.deltaTime);
        }
        #endregion

        #region Public interface
        /// <summary>Prevents the automatic first-step load; call from Awake of a mode controller.</summary>
        public void SuppressAutoLoad()
        {
            _loadFirstStepOnStart = false;
        }

        /// <summary>Switches to a step by catalog index, optionally spawning <see cref="SpawnCount"/> objects once loaded.</summary>
        public Coroutine LoadStep(int stepIndex, bool spawnAfterLoad)
        {
            if (_catalog == null || stepIndex < 0 || stepIndex >= _catalog.Steps.Count)
            {
                Debug.LogError($"[BackendSwitcher] Step index {stepIndex} is out of range.");
                return null;
            }
            if (IsLoading)
            {
                Debug.LogWarning("[BackendSwitcher] Ignoring LoadStep while another load is in progress.");
                return null;
            }
            return StartCoroutine(LoadStepRoutine(stepIndex, spawnAfterLoad));
        }

        /// <summary>Despawns and unloads the active step, leaving only the bootstrap scene.</summary>
        public Coroutine UnloadStep()
        {
            if (IsLoading)
                return null;
            return StartCoroutine(UnloadStepRoutine());
        }

        /// <summary>Pushes edited settings into the active field so they take effect live; a no-op outside play mode or while loading.</summary>
        private void RespawnWithNewSettings()
        {
            if (!Application.isPlaying || IsLoading || Active == null || Active.Count == 0)
                return;
            Active.RefreshSettings();
            FieldRebuilt?.Invoke(Active);
        }

        /// <summary>Respawns the active backend with <see cref="SpawnCount"/> objects.</summary>
        public void Respawn()
        {
            if (Active == null)
                return;
            Active.Spawn(SpawnCount);
        }
        #endregion

        #region Scene loading
        /// <summary>Unloads the previous step scene, loads the new one and binds its backend.</summary>
        private IEnumerator LoadStepRoutine(int stepIndex, bool spawnAfterLoad)
        {
            IsLoading = true;
            yield return UnloadActiveStep();
            yield return LoadStepScene(_catalog.Steps[stepIndex].ScenePath);
            BindBackend(stepIndex);
            if (spawnAfterLoad && Active != null)
                Active.Spawn(SpawnCount);
            IsLoading = false;
            StepLoaded?.Invoke(Active);
        }

        /// <summary>Unload wrapped in the loading flag so hotkeys cannot interleave a load.</summary>
        private IEnumerator UnloadStepRoutine()
        {
            IsLoading = true;
            yield return UnloadActiveStep();
            IsLoading = false;
            StepLoaded?.Invoke(null);
        }

        /// <summary>Despawns and unloads the currently loaded step scene, if any.</summary>
        private IEnumerator UnloadActiveStep()
        {
            if (Active != null)
                Active.Despawn();
            Active = null;
            ActiveStepIndex = -1;
            if (!_activeScene.IsValid() || !_activeScene.isLoaded)
                yield break;
            yield return SceneManager.UnloadSceneAsync(_activeScene);
            _activeScene = default;
        }

        /// <summary>Loads a step scene additively and remembers it for unloading.</summary>
        private IEnumerator LoadStepScene(string scenePath)
        {
            var operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            if (operation == null)
            {
                Debug.LogError($"[BackendSwitcher] Scene '{scenePath}' could not be loaded. Is it in the build settings?");
                yield break;
            }
            yield return operation;
            _activeScene = SceneManager.GetSceneByPath(scenePath);
        }

        /// <summary>Finds the backend in the loaded step scene and injects the shared settings.</summary>
        private void BindBackend(int stepIndex)
        {
            Active = FindBackendIn(_activeScene);
            if (Active == null)
            {
                Debug.LogError($"[BackendSwitcher] Step scene '{_activeScene.path}' contains no ObjectBackend.");
                return;
            }
            Active.Initialize(_settings);
            ActiveStepIndex = stepIndex;
        }

        /// <summary>Searches the root objects of a scene for an <see cref="ObjectBackend"/>.</summary>
        private static ObjectBackend FindBackendIn(Scene scene)
        {
            if (!scene.IsValid())
                return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var backend = root.GetComponentInChildren<ObjectBackend>();
                if (backend != null)
                    return backend;
            }
            return null;
        }
        #endregion
    }
}
