using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MillionObjects.Steps.IndirectStep
{
    /// <summary>
    /// Rung 5, the top of the ladder: the field lives entirely on the GPU. A compute kernel owns the
    /// spring state and writes one object-to-world matrix per cube, and a single
    /// <see cref="Graphics.RenderMeshIndirect"/> call draws every one of them from an argument buffer
    /// the CPU fills once. No per-object data crosses the bus after spawn, which is why the C# here
    /// is short and why every new per-object feature costs another buffer and another compute pass.
    /// </summary>
    public sealed class IndirectBackend : ObjectBackend
    {
        #region Constants
        /// <summary>Compute kernel that integrates the springs and writes the matrices.</summary>
        private const string WaveCubesKernelName = "WaveCubes";
        /// <summary>Threads per group of the wave kernel; must match [numthreads] in WaveCube.compute.</summary>
        private const int ThreadGroupSize = 64;
        /// <summary>Hard limit on groups per dispatch dimension; larger fields are dispatched in chunks with an index offset.</summary>
        private const int MaxGroupsPerDispatch = 65535;
        /// <summary>Stride of a float4x4 matrix element, in bytes.</summary>
        private const int MatrixStride = 64;
        /// <summary>Stride of a float4 state element. Never float3: a three-component stride misaligns on Vulkan.</summary>
        private const int Float4Stride = 16;
        /// <summary>Stride of a uint element, in bytes.</summary>
        private const int UIntStride = 4;

        private static readonly int LocalToWorldId = Shader.PropertyToID("_LocalToWorld");
        private static readonly int DisplacementId = Shader.PropertyToID("_Displacement");
        private static readonly int VelocityId = Shader.PropertyToID("_Velocity");
        private static readonly int PaletteOverrideId = Shader.PropertyToID("_PaletteOverride");
        private static readonly int PaletteId = Shader.PropertyToID("_Palette");
        private static readonly int CountId = Shader.PropertyToID("_Count");
        private static readonly int SideId = Shader.PropertyToID("_Side");
        private static readonly int IndexOffsetId = Shader.PropertyToID("_IndexOffset");
        private static readonly int TimeId = Shader.PropertyToID("_Time");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int AttractorId = Shader.PropertyToID("_Attractor");
        private static readonly int SpacingId = Shader.PropertyToID("_Spacing");
        private static readonly int CubeScaleId = Shader.PropertyToID("_CubeScale");
        private static readonly int WaveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
        private static readonly int WaveLengthId = Shader.PropertyToID("_WaveLength");
        private static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
        private static readonly int RotationSpeedId = Shader.PropertyToID("_RotationSpeed");
        private static readonly int SpringStiffnessId = Shader.PropertyToID("_SpringStiffness");
        private static readonly int SpringDampingId = Shader.PropertyToID("_SpringDamping");
        private static readonly int AttractorStrengthId = Shader.PropertyToID("_AttractorStrength");
        private static readonly int FieldExtentId = Shader.PropertyToID("_FieldExtent");
        private static readonly int SwirlRadiusId = Shader.PropertyToID("_SwirlRadius");
        private static readonly int SwirlSpeedId = Shader.PropertyToID("_SwirlSpeed");
        private static readonly int SwirlDepthId = Shader.PropertyToID("_SwirlDepth");
        #endregion

        #region Inspector fields
        [SerializeField, Tooltip("WaveCube.compute: integrates the springs and writes one matrix per object.")]
        private ComputeShader _waveCompute;
        [SerializeField, Tooltip("MillionObjects/WaveCubeIndirect: reads the matrix buffer by instance id.")]
        private Shader _indirectShader;
        #endregion

        #region Public properties
        /// <summary>Human-readable step name for the HUD and reports.</summary>
        public override string DisplayName => "5 · GPU indirect + compute";
        /// <summary>Flexibility card shown by the HUD for this step.</summary>
        public override BackendCard Card => new BackendCard("HLSL compute kernel; C# only dispatches", "manual: the CPU does not know where any cube is", "a buffer + a compute pass, plus a readback to read anything back");
        #endregion

        #region Private fields
        private GraphicsBuffer _localToWorldBuffer;      // one float4x4 per object, written by the kernel, read by the vertex shader
        private GraphicsBuffer _displacementBuffer;      // spring offset per object, float4 for stride alignment
        private GraphicsBuffer _velocityBuffer;          // spring velocity per object, float4 for stride alignment
        private GraphicsBuffer _paletteOverrideBuffer;   // recolour demo: 0 means "hash the index", otherwise paletteIndex + 1
        private GraphicsBuffer _paletteBuffer;           // the 16 palette colours; a buffer because material array properties are dropped at runtime
        private GraphicsBuffer _argumentsBuffer;         // the single IndirectDrawIndexedArgs the draw call reads
        private Material _material;                      // instance of the indirect shader owning the buffer bindings
        private RenderParams _renderParams;              // built once per spawn; worldBounds covers the whole sheet
        private FieldParams _fieldParameters;            // cached once per spawn, pushed to the kernel every dispatch
        private readonly uint[] _recolorScratch = new uint[1];   // one-element staging array for the 4-byte recolour poke
        private int _kernelIndex = -1;                   // index of the WaveCubes kernel
        private int _threadGroups;                       // ceil(count / ThreadGroupSize)
        private int _sideLength;                         // cubes per side of the sheet
        #endregion

        #region Backend responsibilities
        /// <summary>Allocates every GPU buffer, fills the draw arguments and builds the render params.</summary>
        protected override void SpawnObjects(int count)
        {
            if (!HasRequiredAssets())
                return;
            _fieldParameters = Settings.ToParams(count);
            _sideLength = ObjectField.SideLength(count);
            _kernelIndex = _waveCompute.FindKernel(WaveCubesKernelName);
            _threadGroups = (count + ThreadGroupSize - 1) / ThreadGroupSize;
            CreateStateBuffers(count);
            CreateArgumentsBuffer(count);
            CreatePaletteBuffer();
            CreateMaterial();
            BindComputeBuffers();
            _renderParams = CreateRenderParams(count);
        }

        /// <summary>Releases every GPU buffer and the material instance. The GPU owns all the state, so this is the whole teardown.</summary>
        protected override void DespawnObjects()
        {
            ReleaseBuffer(ref _localToWorldBuffer);
            ReleaseBuffer(ref _displacementBuffer);
            ReleaseBuffer(ref _velocityBuffer);
            ReleaseBuffer(ref _paletteOverrideBuffer);
            ReleaseBuffer(ref _paletteBuffer);
            ReleaseBuffer(ref _argumentsBuffer);
            DestroyMaterial();
            _kernelIndex = -1;
        }

        /// <summary>Runs the simulation kernel, then draws the entire field in one indirect call.</summary>
        protected override void TickObjects(float time, float deltaTime)
        {
            if (_material == null || _argumentsBuffer == null)
                return;
            SetComputeConstants(time, deltaTime);
            DispatchInChunks();
            Graphics.RenderMeshIndirect(in _renderParams, Settings.CubeMesh, _argumentsBuffer, 1, 0);
        }
        #endregion

        #region Public interface
        /// <summary>
        /// Flexibility demo: pokes four bytes into the override buffer so one cube picks a different
        /// palette slot. Writing is this cheap only because it is one-way; reading a cube's colour,
        /// position or velocity back would need an <see cref="AsyncGPUReadback"/> and a frame of latency,
        /// which is the real cost of moving the field onto the GPU.
        /// </summary>
        public override bool TryRecolor(int index, int paletteIndex)
        {
            if (_paletteOverrideBuffer == null || index < 0 || index >= Count)
                return false;
            if (paletteIndex < 0 || paletteIndex >= ObjectField.PaletteSize)
                return false;
            _recolorScratch[0] = (uint)paletteIndex + 1u;
            _paletteOverrideBuffer.SetData(_recolorScratch, 0, index, 1);
            return true;
        }
        #endregion

        #region Spawning
        /// <summary>Reports the missing pieces the step cannot spawn without.</summary>
        private bool HasRequiredAssets()
        {
            if (Settings.CubeMesh == null)
            {
                Debug.LogError("[IndirectBackend] Field settings are missing the cube mesh; nothing was spawned.");
                return false;
            }
            if (_waveCompute == null || _indirectShader == null)
            {
                Debug.LogError("[IndirectBackend] Assign the wave compute shader and the indirect shader in the Inspector; nothing was spawned.");
                return false;
            }
            if (!_waveCompute.HasKernel(WaveCubesKernelName))
            {
                Debug.LogError($"[IndirectBackend] The compute shader has no '{WaveCubesKernelName}' kernel; nothing was spawned.");
                return false;
            }
            return true;
        }

        /// <summary>Allocates the per-object buffers and clears the state the kernel integrates from.</summary>
        private void CreateStateBuffers(int count)
        {
            _localToWorldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, MatrixStride);
            _displacementBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, Float4Stride);
            _velocityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, Float4Stride);
            _paletteOverrideBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, UIntStride);
            ClearStateBuffers(count);
        }

        /// <summary>Zeroes displacement, velocity and the recolour overrides; fresh GPU memory holds garbage.</summary>
        private void ClearStateBuffers(int count)
        {
            using var zeroedState = new NativeArray<float4>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _displacementBuffer.SetData(zeroedState);
            _velocityBuffer.SetData(zeroedState);
            using var zeroedOverrides = new NativeArray<uint>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _paletteOverrideBuffer.SetData(zeroedOverrides);
        }

        /// <summary>Fills the one draw command that renders the whole field: the cube's submesh, instanced <paramref name="count"/> times.</summary>
        private void CreateArgumentsBuffer(int count)
        {
            Mesh mesh = Settings.CubeMesh;
            _argumentsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var arguments = new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = (uint)mesh.GetIndexCount(0),
                instanceCount = (uint)count,
                startIndex = (uint)mesh.GetIndexStart(0),
                baseVertexIndex = (uint)mesh.GetBaseVertex(0),
                startInstance = 0,
            };
            _argumentsBuffer.SetData(new[] { arguments });
        }

        /// <summary>
        /// Creates the material instance and gives it the buffers the shaders read. The palette travels
        /// as a structured buffer too: Unity discards Material.SetColorArray data at runtime (the cubes
        /// turned black a few seconds after spawning), whereas buffer bindings persist.
        /// </summary>
        private void CreateMaterial()
        {
            _material = new Material(_indirectShader) { name = $"{_indirectShader.name} (instance)" };
            _material.SetBuffer(LocalToWorldId, _localToWorldBuffer);
            _material.SetBuffer(PaletteOverrideId, _paletteOverrideBuffer);
            _material.SetBuffer(PaletteId, _paletteBuffer);
            _material.SetInt(SideId, _sideLength);
            _material.SetFloat(SpacingId, _fieldParameters.Spacing);
            _material.SetFloat(FieldExtentId, _fieldParameters.FieldExtent);
        }

        /// <summary>Uploads the 16 palette colours, in palette-slot order, as linear float4s.</summary>
        private void CreatePaletteBuffer()
        {
            var colors = new NativeArray<float4>(ObjectField.PaletteSize, Allocator.Temp);
            for (int i = 0; i < colors.Length; i++)
            {
                Color linear = Settings.PaletteColor(i).linear;   // buffers skip the sRGB conversion Color properties get
                colors[i] = new float4(linear.r, linear.g, linear.b, linear.a);
            }
            _paletteBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ObjectField.PaletteSize, Float4Stride);
            _paletteBuffer.SetData(colors);
            colors.Dispose();
        }

        /// <summary>Binds the state buffers to the kernel once; only the constants change per frame.</summary>
        private void BindComputeBuffers()
        {
            _waveCompute.SetBuffer(_kernelIndex, LocalToWorldId, _localToWorldBuffer);
            _waveCompute.SetBuffer(_kernelIndex, DisplacementId, _displacementBuffer);
            _waveCompute.SetBuffer(_kernelIndex, VelocityId, _velocityBuffer);
        }

        /// <summary>Render settings shared with every other rung: no shadows, no probes, bounds covering the whole sheet.</summary>
        private RenderParams CreateRenderParams(int count)
        {
            return new RenderParams(_material)
            {
                worldBounds = ObjectField.FieldBounds(count, _fieldParameters),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                layer = gameObject.layer,
            };
        }
        #endregion

        #region Dispatch
        /// <summary>Dispatches the kernel over every object, in chunks under the per-dimension group limit so counts above 4.19M still animate.</summary>
        private void DispatchInChunks()
        {
            for (int firstGroup = 0; firstGroup < _threadGroups; firstGroup += MaxGroupsPerDispatch)
            {
                int groups = Mathf.Min(MaxGroupsPerDispatch, _threadGroups - firstGroup);
                _waveCompute.SetInt(IndexOffsetId, firstGroup * ThreadGroupSize);
                _waveCompute.Dispatch(_kernelIndex, groups, 1, 1);
            }
        }

        /// <summary>Pushes the frame context and the mirrored FieldParams to the kernel. This is the entire per-frame CPU cost of the step.</summary>
        private void SetComputeConstants(float time, float deltaTime)
        {
            _waveCompute.SetInt(CountId, Count);
            _waveCompute.SetInt(SideId, _sideLength);
            _waveCompute.SetFloat(TimeId, time);
            _waveCompute.SetFloat(DeltaTimeId, deltaTime);
            _waveCompute.SetVector(AttractorId, Attractor);
            _waveCompute.SetFloat(SpacingId, _fieldParameters.Spacing);
            _waveCompute.SetFloat(CubeScaleId, _fieldParameters.CubeScale);
            _waveCompute.SetFloat(WaveAmplitudeId, _fieldParameters.WaveAmplitude);
            _waveCompute.SetFloat(WaveLengthId, _fieldParameters.WaveLength);
            _waveCompute.SetFloat(WaveSpeedId, _fieldParameters.WaveSpeed);
            _waveCompute.SetFloat(RotationSpeedId, _fieldParameters.RotationSpeed);
            _waveCompute.SetFloat(SpringStiffnessId, _fieldParameters.SpringStiffness);
            _waveCompute.SetFloat(SpringDampingId, _fieldParameters.SpringDamping);
            _waveCompute.SetFloat(AttractorStrengthId, _fieldParameters.AttractorStrength);
            _waveCompute.SetFloat(FieldExtentId, _fieldParameters.FieldExtent);
            _waveCompute.SetFloat(SwirlRadiusId, _fieldParameters.SwirlRadius);
            _waveCompute.SetFloat(SwirlSpeedId, _fieldParameters.SwirlSpeed);
            _waveCompute.SetFloat(SwirlDepthId, _fieldParameters.SwirlDepth);
        }
        #endregion

        #region Despawning
        /// <summary>Releases one GraphicsBuffer and clears the field, tolerating a buffer that was never created.</summary>
        private static void ReleaseBuffer(ref GraphicsBuffer buffer)
        {
            buffer?.Release();
            buffer = null;
        }

        /// <summary>Destroys the material instance. Edit mode needs the immediate variant.</summary>
        private void DestroyMaterial()
        {
            if (_material == null)
                return;
            if (Application.isPlaying)
                Destroy(_material);
            else
                DestroyImmediate(_material);
            _material = null;
        }
        #endregion
    }
}
