using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MillionObjects.Steps
{
    /// <summary>
    /// Step 4: GPU instancing driven from the CPU. There are no GameObjects at all — every object is
    /// a row in a set of <see cref="NativeArray{T}"/>s, a Burst job turns those rows into one
    /// <see cref="float4x4"/> per object each frame, and <see cref="Graphics.RenderMeshInstanced"/>
    /// uploads them. Objects are grouped into one bucket per palette slot so a bucket is a contiguous
    /// range of the matrix array and therefore a single instanced draw with its own palette material.
    /// The honest costs of this rung: a full matrix upload every frame, and any per-object property
    /// that the shader cannot derive needs its own array or its own bucket.
    /// </summary>
    public class InstancedBackend : ObjectBackend
    {
        #region Constants
        /// <summary>One bucket per palette slot; a bucket is one instanced draw call.</summary>
        private const int BucketCount = ObjectField.PaletteSize;
        /// <summary>Objects per worker batch in the matrix job.</summary>
        private const int MatrixJobBatchSize = 256;
        #endregion

        #region Public properties
        /// <inheritdoc />
        public override string DisplayName => "4 · GPU instanced";
        /// <inheritdoc />
        public override BackendCard Card => new BackendCard(
            "C# + Burst job, matrices uploaded every frame",
            "manual: no components, no Inspector on objects, picking by index maths",
            "an array + a per-frame upload; per-object variety = a new bucket");
        #endregion

        #region Private fields
        private readonly int[] _bucketMemberCounts = new int[BucketCount];   // objects per palette bucket
        private readonly int[] _bucketStarts = new int[BucketCount];         // first matrix slot of each bucket
        private readonly int[] _bucketFillCursors = new int[BucketCount];    // next free slot per bucket while laying out
        private PaletteMaterialSet _paletteMaterials;                        // 16 instancing-enabled material instances
        private RenderParams[] _bucketRenderParams;                          // one cached RenderParams per bucket
        private NativeArray<float3> _restPositions;                          // grid position each object springs back to
        private NativeArray<float3> _displacements;                          // offset from rest, persistent across frames
        private NativeArray<float3> _velocities;                             // spring velocity, persistent across frames
        private NativeArray<int> _paletteSlots;                              // object index -> palette slot, mutable by recolouring
        private NativeArray<int> _matrixSlots;                               // object index -> index into _matrices
        private NativeArray<float4x4> _matrices;                             // all matrices, laid out bucket by bucket
        private FieldParams _parameters;                                     // blittable copy of the shared settings
        private JobHandle _matrixJobHandle;                                  // outstanding matrix job, completed before any read
        private int _sideLength;                                             // cubes per side of the square sheet
        #endregion

        #region Public interface
        /// <summary>
        /// Recolours one object by moving it into another palette bucket. Because a bucket is a
        /// contiguous range of the matrix array, changing one object's colour invalidates every
        /// object's matrix slot: this costs an O(n) relayout of the whole field. That is exactly the
        /// flexibility price this rung pays — colour is no longer a property of an object, it is the
        /// object's position in an array.
        /// </summary>
        public override bool TryRecolor(int index, int paletteIndex)
        {
            if (!_matrices.IsCreated || index < 0 || index >= Count)
                return false;
            if (paletteIndex < 0 || paletteIndex >= BucketCount)
            {
                Debug.LogWarning($"[{DisplayName}] Recolour ignored: palette index {paletteIndex} is outside the {BucketCount}-slot palette.");
                return false;
            }
            _matrixJobHandle.Complete();
            _paletteSlots[index] = paletteIndex;
            RebuildBucketLayout(Count);
            return true;
        }
        #endregion

        #region Backend responsibilities
        /// <inheritdoc />
        protected override void SpawnObjects(int count)
        {
            if (!HasRenderAssets())
                return;
            _parameters = Settings.ToParams(count);
            _sideLength = ObjectField.SideLength(count);
            _paletteMaterials = CreateInstancedPaletteMaterials();
            AllocateFieldState(count);
            RebuildBucketLayout(count);
            _bucketRenderParams = CreateBucketRenderParams(count);
        }

        /// <inheritdoc />
        protected override void DespawnObjects()
        {
            _matrixJobHandle.Complete();
            DisposeFieldState();
            DisposePaletteMaterials();
            _bucketRenderParams = null;
        }

        /// <inheritdoc />
        protected override void TickObjects(float time, float deltaTime)
        {
            if (!_matrices.IsCreated)
                return;
            _matrixJobHandle = ScheduleMatrixJob(time, deltaTime);
            _matrixJobHandle.Complete();
            DrawBuckets();
        }
        #endregion

        #region Spawn
        /// <summary>Reports missing render assets once instead of failing deeper in the spawn.</summary>
        private bool HasRenderAssets()
        {
            if (Settings.CubeMesh == null)
            {
                Debug.LogError($"[{DisplayName}] No cube mesh assigned in the field settings; nothing to render.");
                return false;
            }
            if (Settings.CubeMaterial == null)
            {
                Debug.LogError($"[{DisplayName}] No cube material assigned in the field settings; nothing to render.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Builds the palette materials and switches instancing on for each of them.
        /// <see cref="Graphics.RenderMeshInstanced"/> rejects a material whose
        /// <see cref="Material.enableInstancing"/> is off, so this is not optional.
        /// </summary>
        private PaletteMaterialSet CreateInstancedPaletteMaterials()
        {
            var materials = new PaletteMaterialSet(Settings);
            for (int slot = 0; slot < materials.Count; slot++)
                materials[slot].enableInstancing = true;
            return materials;
        }

        /// <summary>Allocates the persistent per-object arrays and fills rest positions and palette slots.</summary>
        private void AllocateFieldState(int count)
        {
            _restPositions = new NativeArray<float3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _displacements = new NativeArray<float3>(count, Allocator.Persistent);
            _velocities = new NativeArray<float3>(count, Allocator.Persistent);
            _paletteSlots = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _matrixSlots = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _matrices = new NativeArray<float4x4>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int index = 0; index < count; index++)
            {
                _restPositions[index] = ObjectField.RestPosition(index, _sideLength, _parameters.Spacing);
                _paletteSlots[index] = ObjectField.PaletteIndex(index, _restPositions[index], in _parameters);
            }
        }

        /// <summary>Caches the draw parameters of every bucket; only the material differs between them.</summary>
        private RenderParams[] CreateBucketRenderParams(int count)
        {
            Bounds worldBounds = ObjectField.FieldBounds(count, _parameters);
            var perBucket = new RenderParams[BucketCount];
            for (int bucket = 0; bucket < BucketCount; bucket++)
                perBucket[bucket] = BucketRenderParams(bucket, worldBounds);
            return perBucket;
        }

        /// <summary>Draw parameters for one bucket: its palette material, the whole field's bounds, no shadows.</summary>
        private RenderParams BucketRenderParams(int bucket, Bounds worldBounds)
        {
            return new RenderParams(_paletteMaterials[bucket])
            {
                worldBounds = worldBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = 0,
                camera = null,
            };
        }
        #endregion

        #region Frame
        /// <summary>Schedules the spring integration and matrix write for every object.</summary>
        private JobHandle ScheduleMatrixJob(float time, float deltaTime)
        {
            var job = new InstancedWaveJob
            {
                RestPositions = _restPositions,
                Displacements = _displacements,
                Velocities = _velocities,
                MatrixSlots = _matrixSlots,
                Matrices = _matrices,
                Parameters = _parameters,
                Attractor = Attractor,
                Time = time,
                DeltaTime = deltaTime,
            };
            return job.Schedule(Count, MatrixJobBatchSize);
        }

        /// <summary>Submits one instanced draw per non-empty bucket from the shared matrix array.</summary>
        private void DrawBuckets()
        {
            NativeArray<Matrix4x4> instanceData = _matrices.Reinterpret<Matrix4x4>();
            for (int bucket = 0; bucket < BucketCount; bucket++)
                DrawBucket(bucket, instanceData);
        }

        /// <summary>Draws one bucket as the contiguous matrix range that belongs to it.</summary>
        private void DrawBucket(int bucket, NativeArray<Matrix4x4> instanceData)
        {
            if (_bucketMemberCounts[bucket] == 0)
                return;
            Graphics.RenderMeshInstanced(_bucketRenderParams[bucket], Settings.CubeMesh, 0, instanceData, _bucketMemberCounts[bucket], _bucketStarts[bucket]);
        }
        #endregion

        #region Bucket layout
        /// <summary>
        /// Recomputes bucket member counts, bucket start offsets and every object's matrix slot from
        /// the current palette slots. The matrix contents themselves need no shuffling: the job
        /// rewrites all of them before the next draw. The object count is passed in because the base
        /// class only publishes <see cref="ObjectBackend.Count"/> after the spawn returns.
        /// </summary>
        private void RebuildBucketLayout(int count)
        {
            CountBucketMembers(count);
            AccumulateBucketStarts();
            AssignMatrixSlots(count);
        }

        /// <summary>Counts how many objects each palette bucket holds.</summary>
        private void CountBucketMembers(int count)
        {
            for (int bucket = 0; bucket < BucketCount; bucket++)
                _bucketMemberCounts[bucket] = 0;
            for (int index = 0; index < count; index++)
                _bucketMemberCounts[_paletteSlots[index]]++;
        }

        /// <summary>Turns the member counts into the first matrix slot of each bucket.</summary>
        private void AccumulateBucketStarts()
        {
            int start = 0;
            for (int bucket = 0; bucket < BucketCount; bucket++)
            {
                _bucketStarts[bucket] = start;
                _bucketFillCursors[bucket] = start;
                start += _bucketMemberCounts[bucket];
            }
        }

        /// <summary>Hands every object the next free matrix slot inside its bucket.</summary>
        private void AssignMatrixSlots(int count)
        {
            for (int index = 0; index < count; index++)
            {
                int bucket = _paletteSlots[index];
                _matrixSlots[index] = _bucketFillCursors[bucket];
                _bucketFillCursors[bucket]++;
            }
        }
        #endregion

        #region Teardown
        /// <summary>Releases every persistent native array. Safe to call when nothing was allocated.</summary>
        private void DisposeFieldState()
        {
            if (_restPositions.IsCreated)
                _restPositions.Dispose();
            if (_displacements.IsCreated)
                _displacements.Dispose();
            if (_velocities.IsCreated)
                _velocities.Dispose();
            if (_paletteSlots.IsCreated)
                _paletteSlots.Dispose();
            if (_matrixSlots.IsCreated)
                _matrixSlots.Dispose();
            if (_matrices.IsCreated)
                _matrices.Dispose();
        }

        /// <summary>Destroys the palette material instances this backend created.</summary>
        private void DisposePaletteMaterials()
        {
            if (_paletteMaterials == null)
                return;
            _paletteMaterials.Dispose();
            _paletteMaterials = null;
        }
        #endregion
    }
}
