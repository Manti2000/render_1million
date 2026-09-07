using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MillionObjects.Steps.MonoBehaviourStep
{
    /// <summary>
    /// Rung 1, the naive baseline: every object is a real GameObject with a MeshFilter, a
    /// MeshRenderer and its own <see cref="WaveCube"/> component that animates it from its own
    /// <c>Update</c>. The backend never touches a transform; it only publishes the frame context
    /// every cube reads, so the cost measured here is the cost of a million MonoBehaviours.
    /// </summary>
    public sealed class MonoBehaviourBackend : ObjectBackend
    {
        #region Public properties
        /// <summary>Human-readable step name for the HUD and reports.</summary>
        public override string DisplayName => "1 · MonoBehaviour per object";
        /// <summary>Flexibility card shown by the HUD for this step.</summary>
        public override BackendCard Card => new BackendCard("MonoBehaviour.Update on every object", "native: Inspector, Animator, physics, any asset", "a field on the component");
        /// <summary>Field parameters snapshotted at spawn time, read by every cube each frame.</summary>
        public FieldParams FieldParameters => _fieldParameters;
        /// <summary>Time the current frame animates at, published by the most recent tick.</summary>
        public float FrameTime { get; private set; }
        /// <summary>Delta time the current frame integrates with, published by the most recent tick.</summary>
        public float FrameDeltaTime { get; private set; }
        #endregion

        #region Private fields
        private Transform _fieldRoot;               // flat parent of every spawned cube, destroyed as one on despawn
        private PaletteMaterialSet _palette;        // one material instance per palette slot, owned by this backend
        private MeshRenderer[] _renderers;          // one per object, kept so a single cube can be recoloured
        private FieldParams _fieldParameters;       // cached once per spawn, never rebuilt per object or per frame
        #endregion

        #region Backend responsibilities
        /// <summary>Motion numbers and palette colours update in place; spacing or cube scale changes rebuild, since both are baked into the transforms.</summary>
        protected override bool TryApplySettings(in FieldParams parameters)
        {
            if (parameters.Spacing != _fieldParameters.Spacing || parameters.CubeScale != _fieldParameters.CubeScale)
                return false;
            _fieldParameters = parameters;
            _palette?.UpdateColors(Settings);
            return true;
        }

        /// <summary>Builds the field: one palette set, one template cube, then a clone per object.</summary>
        protected override void SpawnObjects(int count)
        {
            if (Settings.CubeMesh == null || Settings.CubeMaterial == null)
            {
                Debug.LogError("[MonoBehaviourBackend] Field settings are missing the cube mesh or material; nothing was spawned.");
                return;
            }
            _fieldParameters = Settings.ToParams(count);
            _palette = new PaletteMaterialSet(Settings);
            _renderers = new MeshRenderer[count];
            CreateFieldRoot();
            SpawnCubes(CreateTemplateCube(), count);
        }

        /// <summary>Destroys the whole field in one go and releases the palette materials.</summary>
        protected override void DespawnObjects()
        {
            DestroyFieldRoot();
            _palette?.Dispose();
            _palette = null;
            _renderers = null;
        }

        /// <summary>
        /// Publishes the frame context the cubes animate from. The backend deliberately moves
        /// nothing itself: the work happens once per object in <see cref="WaveCube.Update"/>.
        /// </summary>
        protected override void TickObjects(float time, float deltaTime)
        {
            FrameTime = time;
            FrameDeltaTime = deltaTime;
        }
        #endregion

        #region Public interface
        /// <summary>Flexibility demo: swaps one cube's material for another palette slot.</summary>
        public override bool TryRecolor(int index, int paletteIndex)
        {
            if (_renderers == null || index < 0 || index >= _renderers.Length)
                return false;
            if (_palette == null || paletteIndex < 0 || paletteIndex >= _palette.Count)
                return false;
            _renderers[index].sharedMaterial = _palette[paletteIndex];
            return true;
        }
        #endregion

        #region Spawning
        /// <summary>Creates the single parent every cube is attached to, keeping the hierarchy flat.</summary>
        private void CreateFieldRoot()
        {
            var root = new GameObject("MonoBehaviour Field");
            root.transform.SetParent(transform, false);
            _fieldRoot = root.transform;
        }

        /// <summary>Builds the cube that is cloned for every object, already carrying mesh, renderer settings and scale.</summary>
        private WaveCube CreateTemplateCube()
        {
            var cube = new GameObject("WaveCube", typeof(MeshFilter), typeof(MeshRenderer), typeof(WaveCube));
            cube.transform.SetParent(_fieldRoot, false);
            cube.transform.localScale = Vector3.one * _fieldParameters.CubeScale;
            cube.GetComponent<MeshFilter>().sharedMesh = Settings.CubeMesh;
            ConfigureRenderer(cube.GetComponent<MeshRenderer>());
            return cube.GetComponent<WaveCube>();
        }

        /// <summary>Strips every renderer feature the comparison does not use, so all rungs draw the same picture.</summary>
        private static void ConfigureRenderer(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
        }

        /// <summary>Clones the template into the rest of the field; the template itself becomes object zero.</summary>
        private void SpawnCubes(WaveCube template, int count)
        {
            int sideLength = ObjectField.SideLength(count);
            for (int index = 0; index < count; index++)
                InitializeCube(index == 0 ? template : Instantiate(template, _fieldRoot), index, sideLength);
        }

        /// <summary>Gives one cube its palette material, its grid slot and its link back to this backend.</summary>
        private void InitializeCube(WaveCube cube, int index, int sideLength)
        {
            float3 rest = ObjectField.RestPosition(index, sideLength, _fieldParameters.Spacing);
            MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _palette[ObjectField.PaletteIndex(index, rest, in _fieldParameters)];
            _renderers[index] = renderer;
            cube.Initialize(this, index, rest);
        }
        #endregion

        #region Despawning
        /// <summary>Destroys the field parent, taking every cube with it. Edit mode needs the immediate variant.</summary>
        private void DestroyFieldRoot()
        {
            if (_fieldRoot == null)
                return;
            if (Application.isPlaying)
                Destroy(_fieldRoot.gameObject);
            else
                DestroyImmediate(_fieldRoot.gameObject);
            _fieldRoot = null;
        }
        #endregion
    }
}
