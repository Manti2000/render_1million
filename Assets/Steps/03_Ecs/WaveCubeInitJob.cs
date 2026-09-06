using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace MillionObjects.Steps.Ecs
{
    /// <summary>
    /// Fills the freshly instantiated cubes with their grid identity: index, rest position, resting
    /// transform and palette colour. Chunk iteration is used rather than <c>IJobEntity</c> because
    /// the backend schedules this from a MonoBehaviour, outside any system, and because it also
    /// records the entity of every object index so the recolour demo can address one cube directly.
    /// </summary>
    [BurstCompile]
    internal struct WaveCubeInitJob : IJobChunk
    {
        #region Public fields
        /// <summary>Handle used to read the entity of each cube back out of the chunk.</summary>
        [ReadOnly] public EntityTypeHandle EntityTypeHandle;
        /// <summary>Transform written at the rest pose, with the shared uniform cube scale.</summary>
        public ComponentTypeHandle<LocalTransform> TransformTypeHandle;
        /// <summary>Per-cube simulation state written from the object index.</summary>
        public ComponentTypeHandle<WaveCubeState> StateTypeHandle;
        /// <summary>Per-entity override of the shader's _BaseColor property.</summary>
        public ComponentTypeHandle<URPMaterialPropertyBaseColor> BaseColorTypeHandle;
        /// <summary>Index of the first entity of each chunk within the query, from CalculateBaseEntityIndexArray.</summary>
        [ReadOnly] public NativeArray<int> ChunkBaseEntityIndices;
        /// <summary>The sixteen palette colours in linear space.</summary>
        [ReadOnly] public NativeArray<float4> PaletteColors;
        /// <summary>Object index to entity map, written here so array order matches grid order exactly.</summary>
        [NativeDisableParallelForRestriction] public NativeArray<Entity> EntitiesByIndex;
        /// <summary>Cubes per side of the square sheet.</summary>
        public int SideLength;
        /// <summary>Blittable snapshot of the shared field settings.</summary>
        public FieldParams Params;
        #endregion

        #region Job
        /// <summary>Initialises every cube in one chunk from its packed index within the query.</summary>
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            NativeArray<Entity> entities = chunk.GetNativeArray(EntityTypeHandle);
            NativeArray<LocalTransform> transforms = chunk.GetNativeArray(ref TransformTypeHandle);
            NativeArray<WaveCubeState> states = chunk.GetNativeArray(ref StateTypeHandle);
            NativeArray<URPMaterialPropertyBaseColor> baseColors = chunk.GetNativeArray(ref BaseColorTypeHandle);
            int firstIndexInChunk = ChunkBaseEntityIndices[unfilteredChunkIndex];
            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            int matched = 0;
            while (enumerator.NextEntityIndex(out int entityIndexInChunk))
            {
                int index = firstIndexInChunk + matched++;
                float3 rest = ObjectField.RestPosition(index, SideLength, Params.Spacing);
                EntitiesByIndex[index] = entities[entityIndexInChunk];
                transforms[entityIndexInChunk] = LocalTransform.FromPositionRotationScale(rest, quaternion.identity, Params.CubeScale);
                states[entityIndexInChunk] = new WaveCubeState { Index = index, RestPosition = rest };
                baseColors[entityIndexInChunk] = new URPMaterialPropertyBaseColor { Value = PaletteColors[ObjectField.PaletteIndex(index)] };
            }
        }
        #endregion
    }
}
