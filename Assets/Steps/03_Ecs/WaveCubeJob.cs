using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace MillionObjects.Steps.Ecs
{
    /// <summary>
    /// Advances one cube per invocation: the attractor spring, then the travelling wave and the
    /// spin. The maths is the shared <see cref="ObjectField"/> code every other rung calls, so the
    /// picture is identical; only the iteration model differs.
    /// </summary>
    [BurstCompile]
    internal partial struct WaveCubeJob : IJobEntity
    {
        #region Public fields
        /// <summary>This frame's time, attractor and field settings, copied in by the system.</summary>
        public WaveFieldFrame Frame;
        #endregion

        #region Job
        /// <summary>Integrates one cube's spring state and writes its world pose.</summary>
        public void Execute(ref LocalTransform transform, ref WaveCubeState state)
        {
            ObjectField.IntegrateSpring(ref state.Displacement, ref state.Velocity, state.RestPosition, Frame.Attractor, Frame.DeltaTime, in Frame.Params);
            transform.Position = ObjectField.Position(state.RestPosition, state.Displacement, Frame.Time, in Frame.Params);
            transform.Rotation = ObjectField.Rotation(state.Index, Frame.Time, in Frame.Params);
        }
        #endregion
    }
}
