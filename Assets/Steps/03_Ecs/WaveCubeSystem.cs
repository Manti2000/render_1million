using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace MillionObjects.Steps.Ecs
{
    /// <summary>
    /// Drives the whole cube field once per world update. The system is Burst compiled end to end
    /// and only exists while <see cref="EcsBackend"/> keeps a <see cref="WaveFieldFrame"/> singleton
    /// alive, so an unspawned field costs nothing.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WaveCubeSystem : ISystem
    {
        #region Lifecycle
        /// <summary>Gates the system on the frame singleton the backend creates when it spawns.</summary>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WaveFieldFrame>();
        }

        /// <summary>Schedules the wave job across every cube; the dependency chain handles completion.</summary>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new WaveCubeJob { Frame = SystemAPI.GetSingleton<WaveFieldFrame>() };
            job.ScheduleParallel();
        }
        #endregion
    }
}
