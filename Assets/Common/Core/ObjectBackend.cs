using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MillionObjects
{
    /// <summary>
    /// Contract every step implements. One instance lives in each step scene; the bootstrap scene's
    /// <see cref="BackendSwitcher"/> finds it, injects the shared settings and drives it every frame.
    /// </summary>
    public abstract class ObjectBackend : MonoBehaviour
    {
        #region Public properties
        /// <summary>Shared field settings injected by the switcher before the first spawn.</summary>
        public FieldSettings Settings { get; private set; }
        /// <summary>Number of objects currently spawned; zero when the field is empty.</summary>
        public int Count { get; private set; }
        /// <summary>Wall-clock duration of the most recent <see cref="Spawn"/>, in milliseconds.</summary>
        public double LastSpawnMilliseconds { get; private set; }
        /// <summary>Attractor sphere as (x, y, z, radius) in world space; radius of zero or less means inactive.</summary>
        public float4 Attractor { get; set; }
        /// <summary>Human-readable step name for the HUD and reports.</summary>
        public abstract string DisplayName { get; }
        /// <summary>Flexibility card shown by the HUD for this step.</summary>
        public abstract BackendCard Card { get; }
        #endregion

        #region Lifecycle
        /// <summary>Despawns when the step scene unloads, so no backend can leak objects or buffers.</summary>
        protected virtual void OnDestroy()
        {
            Despawn();
        }
        #endregion

        #region Public interface
        /// <summary>Provides the shared settings. Must be called before <see cref="Spawn"/>.</summary>
        public void Initialize(FieldSettings settings)
        {
            Settings = settings;
        }

        /// <summary>Builds a field of <paramref name="count"/> objects, replacing any existing field, and records the spawn time.</summary>
        public void Spawn(int count)
        {
            if (Settings == null)
            {
                Debug.LogError($"[{DisplayName}] Spawn called before Initialize; no settings available.");
                return;
            }
            if (count <= 0)
            {
                Debug.LogWarning($"[{DisplayName}] Spawn ignored: count {count} is not positive.");
                return;
            }
            Despawn();
            var stopwatch = Stopwatch.StartNew();
            SpawnObjects(count);
            stopwatch.Stop();
            LastSpawnMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            Count = count;
        }

        /// <summary>Releases every spawned object and all backing memory. Safe to call on an empty field.</summary>
        public void Despawn()
        {
            if (Count == 0)
                return;
            DespawnObjects();
            Count = 0;
        }

        /// <summary>Advances the field by one frame. No-op while the field is empty.</summary>
        public void Tick(float time, float deltaTime)
        {
            if (Count == 0)
                return;
            TickObjects(time, deltaTime);
        }

        /// <summary>
        /// Applies edited settings to a live field. Backends that can update in place do so; the others
        /// rebuild at the current count, which is cheap for the GPU-driven steps and slow for GameObjects.
        /// </summary>
        public void RefreshSettings()
        {
            if (Count == 0 || Settings == null)
                return;
            FieldParams fresh = Settings.ToParams(Count);
            if (!TryApplySettings(in fresh))
                Spawn(Count);
        }

        /// <summary>
        /// Flexibility demo: recolours one object to a palette slot. Returns false when the backend
        /// does not support it or the index is out of range.
        /// </summary>
        public virtual bool TryRecolor(int index, int paletteIndex)
        {
            return false;
        }
        #endregion

        #region Backend responsibilities
        /// <summary>
        /// Applies new parameters and palette colours to the live field without rebuilding it. Return
        /// false when the change needs a rebuild (layout or scale) or the backend does not support live
        /// updates; the base then respawns. Default: always rebuild.
        /// </summary>
        protected virtual bool TryApplySettings(in FieldParams parameters)
        {
            return false;
        }

        /// <summary>Creates <paramref name="count"/> objects on the shared grid layout. Called with an empty field.</summary>
        protected abstract void SpawnObjects(int count);
        /// <summary>Destroys all objects and frees native memory, GPU buffers and material instances.</summary>
        protected abstract void DespawnObjects();
        /// <summary>Applies the wave, rotation and attractor spring for the current frame, then renders if the backend draws itself.</summary>
        protected abstract void TickObjects(float time, float deltaTime);
        #endregion
    }
}
