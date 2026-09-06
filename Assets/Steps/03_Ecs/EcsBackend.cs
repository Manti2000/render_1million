using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace MillionObjects.Steps.Ecs
{
    /// <summary>
    /// Rung 3: DOTS / ECS. Entities are created from code in the default world (no SubScene, no
    /// baking), drawn by Entities Graphics and animated by a Burst compiled system. Per-object
    /// flexibility is untouched -- every cube is still an addressable thing with its own state --
    /// but the programming model, and with it the MonoBehaviour tooling, is gone.
    /// </summary>
    public class EcsBackend : ObjectBackend
    {
        #region Constants
        /// <summary>Flexibility card shown by the HUD for this rung.</summary>
        private static readonly BackendCard EcsCard = new BackendCard(
            "ISystem + IJobEntity, Burst compiled",
            "bridged: ECS authoring, no MonoBehaviour tooling on the objects",
            "a component");
        #endregion

        #region Public properties
        /// <inheritdoc/>
        public override string DisplayName => "3 · DOTS / ECS";
        /// <inheritdoc/>
        public override BackendCard Card => EcsCard;
        #endregion

        #region Private fields
        private NativeArray<Entity> _entities;         // object index -> entity, filled by the init job
        private NativeArray<float4> _paletteColors;    // the 16 palette colours in linear space
        private Entity _frameEntity;                   // holds the WaveFieldFrame singleton
        #endregion

        #region Backend responsibilities
        /// <inheritdoc/>
        protected override void SpawnObjects(int count)
        {
            EntityManager entityManager = ResolveEntityManager(out bool resolved);
            if (!resolved)
            {
                Debug.LogError($"[{DisplayName}] No default ECS world exists; nothing spawned.");
                return;
            }
            if (!HasRenderAssets())
                return;
            _paletteColors = BuildPaletteColors();
            _entities = InstantiateCubes(entityManager, count);
            InitializeCubes(entityManager, count);
            _frameEntity = CreateFrameEntity(entityManager);
        }

        /// <inheritdoc/>
        protected override void DespawnObjects()
        {
            DestroyEntities();
            _frameEntity = Entity.Null;
            DisposeFieldArrays();
        }

        /// <inheritdoc/>
        protected override void TickObjects(float time, float deltaTime)
        {
            if (_frameEntity == Entity.Null)
                return;
            EntityManager entityManager = ResolveEntityManager(out bool resolved);
            if (!resolved)
                return;
            entityManager.SetComponentData(_frameEntity, BuildFrame(time, deltaTime));
        }
        #endregion

        #region Flexibility demo
        /// <inheritdoc/>
        public override bool TryRecolor(int index, int paletteIndex)
        {
            if (!_entities.IsCreated || index < 0 || index >= _entities.Length)
                return false;
            EntityManager entityManager = ResolveEntityManager(out bool resolved);
            if (!resolved)
                return false;
            entityManager.SetComponentData(_entities[index], new URPMaterialPropertyBaseColor { Value = _paletteColors[WrapPaletteSlot(paletteIndex)] });
            return true;
        }

        /// <summary>Wraps a palette index into the palette range, matching FieldSettings.</summary>
        private static int WrapPaletteSlot(int paletteIndex)
        {
            return ((paletteIndex % ObjectField.PaletteSize) + ObjectField.PaletteSize) % ObjectField.PaletteSize;
        }
        #endregion

        #region World access
        /// <summary>Entity manager of the default world; <paramref name="resolved"/> is false when no world exists.</summary>
        private EntityManager ResolveEntityManager(out bool resolved)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            resolved = world != null && world.IsCreated;
            return resolved ? world.EntityManager : default;
        }

        /// <summary>Reports whether the shared settings carry the mesh and material the renderer needs.</summary>
        private bool HasRenderAssets()
        {
            if (Settings.CubeMesh != null && Settings.CubeMaterial != null)
                return true;
            Debug.LogError($"[{DisplayName}] Field settings are missing the cube mesh or material; nothing spawned.");
            return false;
        }
        #endregion

        #region Spawning
        /// <summary>Creates the entity that every cube is cloned from, complete with render components.</summary>
        private Entity CreateCubePrototype(EntityManager entityManager)
        {
            Entity prototype = entityManager.CreateEntity();
            var description = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: false);
            var renderMeshArray = new RenderMeshArray(new[] { Settings.CubeMaterial }, new[] { Settings.CubeMesh });
            RenderMeshUtility.AddComponents(prototype, entityManager, description, renderMeshArray, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            entityManager.AddComponent<LocalTransform>(prototype);
            entityManager.AddComponent<URPMaterialPropertyBaseColor>(prototype);
            entityManager.AddComponent<WaveCubeState>(prototype);
            return prototype;
        }

        /// <summary>Clones the prototype into a persistent array and drops the prototype again.</summary>
        private NativeArray<Entity> InstantiateCubes(EntityManager entityManager, int count)
        {
            Entity prototype = CreateCubePrototype(entityManager);
            var entities = new NativeArray<Entity>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            entityManager.Instantiate(prototype, entities);
            entityManager.DestroyEntity(prototype);
            return entities;
        }

        /// <summary>Runs the Burst init job that gives every cube its grid identity, colour and rest pose.</summary>
        private void InitializeCubes(EntityManager entityManager, int count)
        {
            EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<WaveCubeState>(), ComponentType.ReadWrite<LocalTransform>(), ComponentType.ReadWrite<URPMaterialPropertyBaseColor>());
            NativeArray<int> chunkBaseEntityIndices = query.CalculateBaseEntityIndexArray(Allocator.TempJob);
            var job = new WaveCubeInitJob
            {
                EntityTypeHandle = entityManager.GetEntityTypeHandle(),
                TransformTypeHandle = entityManager.GetComponentTypeHandle<LocalTransform>(false),
                StateTypeHandle = entityManager.GetComponentTypeHandle<WaveCubeState>(false),
                BaseColorTypeHandle = entityManager.GetComponentTypeHandle<URPMaterialPropertyBaseColor>(false),
                ChunkBaseEntityIndices = chunkBaseEntityIndices,
                PaletteColors = _paletteColors,
                EntitiesByIndex = _entities,
                SideLength = ObjectField.SideLength(count),
                Params = Settings.ToParams(),
            };
            job.ScheduleParallel(query, default).Complete();
            chunkBaseEntityIndices.Dispose();
            query.Dispose();
        }

        /// <summary>Creates the entity holding the frame singleton the system is gated on.</summary>
        private Entity CreateFrameEntity(EntityManager entityManager)
        {
            Entity frame = entityManager.CreateEntity(ComponentType.ReadWrite<WaveFieldFrame>());
            entityManager.SetComponentData(frame, BuildFrame(0f, 0f));
            return frame;
        }

        /// <summary>Copies the palette into linear float4s so the Burst jobs can read it.</summary>
        private NativeArray<float4> BuildPaletteColors()
        {
            var colors = new NativeArray<float4>(ObjectField.PaletteSize, Allocator.Persistent);
            for (int i = 0; i < colors.Length; i++)
            {
                Color linear = Settings.PaletteColor(i).linear;
                colors[i] = new float4(linear.r, linear.g, linear.b, linear.a);
            }
            return colors;
        }
        #endregion

        #region Despawning
        /// <summary>Destroys every cube and the frame singleton, skipping a world that is already gone.</summary>
        private void DestroyEntities()
        {
            EntityManager entityManager = ResolveEntityManager(out bool resolved);
            if (!resolved)
                return;
            if (_entities.IsCreated)
                entityManager.DestroyEntity(_entities);
            if (_frameEntity != Entity.Null && entityManager.Exists(_frameEntity))
                entityManager.DestroyEntity(_frameEntity);
        }

        /// <summary>Releases the persistent native memory the field owns.</summary>
        private void DisposeFieldArrays()
        {
            if (_entities.IsCreated)
                _entities.Dispose();
            if (_paletteColors.IsCreated)
                _paletteColors.Dispose();
        }
        #endregion

        #region Frame context
        /// <summary>This frame's inputs for the system: timing from the caller, attractor and settings from here.</summary>
        private WaveFieldFrame BuildFrame(float time, float deltaTime)
        {
            return new WaveFieldFrame
            {
                Time = time,
                DeltaTime = deltaTime,
                Attractor = Attractor,
                Params = Settings.ToParams(),
            };
        }
        #endregion
    }
}
