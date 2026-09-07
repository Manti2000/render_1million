using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MillionObjects.Steps
{
    /// <summary>
    /// Step 2 of the ladder: the manager pattern. Every object is still a GameObject with a
    /// MeshFilter and a MeshRenderer, so Unity keeps culling and submitting a million renderers, but
    /// nothing carries a script. This single manager owns all per-object state in NativeArrays and
    /// moves every Transform from one Burst-compiled job over a <see cref="TransformAccessArray"/>.
    /// The cubes are deliberately parentless scene roots: a transform job batches by hierarchy root,
    /// so a shared parent would pin the whole field to a single worker thread.
    /// </summary>
    public class ManagerBackend : ObjectBackend
    {
        #region Public properties
        /// <summary>Human-readable step name for the HUD and reports.</summary>
        public override string DisplayName => "2 · Manager + Burst job";

        /// <summary>Flexibility card: behaviour has left the objects and moved into the manager's arrays.</summary>
        public override BackendCard Card => new BackendCard("one manager, one Burst job over TransformAccessArray", "native for components; behaviour must go through the manager", "an array");
        #endregion

        #region Private fields
        private PaletteMaterialSet _paletteMaterials;      // one material instance per palette slot
        private MeshRenderer[] _renderers;                 // the field itself: recolouring and despawn both go through it
        private TransformAccessArray _transforms;          // job-side view of every cube Transform
        private NativeArray<float3> _restPositions;        // grid position the wave and the spring ride on
        private NativeArray<float3> _displacements;        // spring offset from rest, carried between frames
        private NativeArray<float3> _velocities;           // spring velocity, carried between frames
        private FieldParams _fieldParams;                  // snapshot of the shared settings, taken once per spawn
        private JobHandle _transformJob;                   // completed inside the tick that scheduled it
        #endregion

        #region Public interface
        /// <summary>Recolours one cube by pointing its renderer at another palette material.</summary>
        public override bool TryRecolor(int index, int paletteIndex)
        {
            if (_renderers == null || index < 0 || index >= _renderers.Length)
                return false;
            if (_paletteMaterials == null || paletteIndex < 0 || paletteIndex >= _paletteMaterials.Count)
                return false;
            _renderers[index].sharedMaterial = _paletteMaterials[paletteIndex];
            return true;
        }
        #endregion

        #region Backend responsibilities
        /// <summary>Motion numbers and palette colours update in place; spacing or cube scale changes rebuild, since both are baked into the transforms.</summary>
        protected override bool TryApplySettings(in FieldParams parameters)
        {
            if (parameters.Spacing != _fieldParams.Spacing || parameters.CubeScale != _fieldParams.CubeScale)
                return false;
            _fieldParams = parameters;
            _paletteMaterials?.UpdateColors(Settings);
            return true;
        }

        /// <summary>Allocates the per-object arrays, builds the cube field and places it at time zero.</summary>
        protected override void SpawnObjects(int count)
        {
            if (Settings.CubeMesh == null || Settings.CubeMaterial == null)
            {
                Debug.LogError($"[{DisplayName}] Field settings are missing the cube mesh or the cube material; nothing spawned.");
                return;
            }
            _fieldParams = Settings.ToParams(count);
            _paletteMaterials = new PaletteMaterialSet(Settings);
            AllocateObjectState(count);
            BuildCubes(count);
            RunTransformJob(Time.time, 0f);
        }

        /// <summary>Runs the spring, the wave and the spin for one frame, then waits for the job.</summary>
        protected override void TickObjects(float time, float deltaTime)
        {
            if (!_transforms.isCreated)
                return;
            RunTransformJob(time, deltaTime);
        }

        /// <summary>Completes any pending job, frees the native memory and destroys the cube field.</summary>
        protected override void DespawnObjects()
        {
            _transformJob.Complete();
            if (_transforms.isCreated)
                _transforms.Dispose();
            DisposeIfCreated(ref _restPositions);
            DisposeIfCreated(ref _displacements);
            DisposeIfCreated(ref _velocities);
            DestroyCubes();
            _paletteMaterials?.Dispose();
            _paletteMaterials = null;
        }
        #endregion

        #region Spawning
        /// <summary>Allocates the persistent state arrays and fills in the grid rest positions.</summary>
        private void AllocateObjectState(int count)
        {
            int sideLength = ObjectField.SideLength(count);
            _restPositions = new NativeArray<float3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _displacements = new NativeArray<float3>(count, Allocator.Persistent);
            _velocities = new NativeArray<float3>(count, Allocator.Persistent);
            for (int index = 0; index < count; index++)
                _restPositions[index] = ObjectField.RestPosition(index, sideLength, _fieldParams.Spacing);
        }

        /// <summary>Clones the cube template once per object; every clone stays a scene root.</summary>
        private void BuildCubes(int count)
        {
            _renderers = new MeshRenderer[count];
            _transforms = new TransformAccessArray(count, -1);
            // The template is object zero rather than a throwaway, so no stray cube outlives the spawn.
            MeshRenderer template = CreateCubeTemplate();
            RegisterCube(template, 0);
            for (int index = 1; index < count; index++)
                RegisterCube(Instantiate(template), index);
        }

        /// <summary>Builds the one cube every other cube is cloned from, with all optional rendering work off.</summary>
        private MeshRenderer CreateCubeTemplate()
        {
            var cube = new GameObject("Cube", typeof(MeshFilter), typeof(MeshRenderer));
            cube.transform.localScale = Vector3.one * _fieldParams.CubeScale;
            cube.GetComponent<MeshFilter>().sharedMesh = Settings.CubeMesh;
            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            return renderer;
        }

        /// <summary>Moves one cube into the step scene, gives it its palette material and files it under its index.</summary>
        private void RegisterCube(MeshRenderer cube, int index)
        {
            // New and instantiated objects land in the active scene, which is Bootstrap. Moving them
            // into the step scene keeps unloading that scene the safety net for anything left behind.
            SceneManager.MoveGameObjectToScene(cube.gameObject, gameObject.scene);
            cube.sharedMaterial = _paletteMaterials[ObjectField.PaletteIndex(index, _restPositions[index], in _fieldParams)];
            _renderers[index] = cube;
            _transforms.Add(cube.transform);
        }
        #endregion

        #region Ticking
        /// <summary>Schedules the transform job for one frame and completes it, so the frame is self-contained.</summary>
        private void RunTransformJob(float time, float deltaTime)
        {
            var job = new ManagerTransformJob
            {
                RestPositions = _restPositions,
                Displacements = _displacements,
                Velocities = _velocities,
                Parameters = _fieldParams,
                Attractor = Attractor,
                ElapsedTime = time,
                DeltaTime = deltaTime,
            };
            _transformJob = job.Schedule(_transforms);
            _transformJob.Complete();
        }
        #endregion

        #region Disposal
        /// <summary>Destroys every cube one by one; a parentless field has no single root to destroy.</summary>
        private void DestroyCubes()
        {
            if (_renderers == null)
                return;
            for (int index = 0; index < _renderers.Length; index++)
                if (_renderers[index] != null)
                    Destroy(_renderers[index].gameObject);
            _renderers = null;
        }

        /// <summary>Frees a native array if it was allocated and clears the reference.</summary>
        private static void DisposeIfCreated(ref NativeArray<float3> array)
        {
            if (array.IsCreated)
                array.Dispose();
            array = default;
        }
        #endregion
    }
}
