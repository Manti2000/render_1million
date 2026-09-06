using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace MillionObjects.Steps
{
    /// <summary>
    /// The entire per-object frame of the manager step: integrates the attractor spring, then writes
    /// the wave position and the spin straight into the Transform. One Burst-compiled job over a
    /// <see cref="TransformAccessArray"/> replaces the million <c>Update</c> calls of step 1, so the
    /// per-object state lives in arrays instead of in components.
    /// </summary>
    [BurstCompile]
    public struct ManagerTransformJob : IJobParallelForTransform
    {
        #region Job fields
        /// <summary>Rest position of every object in spawn order; the grid the wave rides on.</summary>
        [ReadOnly] public NativeArray<float3> RestPositions;
        /// <summary>Spring displacement from rest, carried between frames.</summary>
        public NativeArray<float3> Displacements;
        /// <summary>Spring velocity, carried between frames.</summary>
        public NativeArray<float3> Velocities;
        /// <summary>Blittable copy of the shared field settings, snapshotted once per spawn.</summary>
        public FieldParams Parameters;
        /// <summary>Attractor sphere as (x, y, z, radius); a radius of zero or less means no push.</summary>
        public float4 Attractor;
        /// <summary>Field time driving the travelling wave and the rotation.</summary>
        public float ElapsedTime;
        /// <summary>Seconds since the previous tick, used by the spring integrator.</summary>
        public float DeltaTime;
        #endregion

        #region Execution
        /// <summary>Advances one object's spring state and writes its world position and rotation.</summary>
        public void Execute(int index, TransformAccess transform)
        {
            float3 restPosition = RestPositions[index];
            float3 displacement = Displacements[index];
            float3 velocity = Velocities[index];
            ObjectField.IntegrateSpring(ref displacement, ref velocity, restPosition, Attractor, DeltaTime, Parameters);
            Displacements[index] = displacement;
            Velocities[index] = velocity;
            transform.SetPositionAndRotation(ObjectField.Position(restPosition, displacement, ElapsedTime, Parameters), ObjectField.Rotation(index, ElapsedTime, Parameters));
        }
        #endregion
    }
}
