using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MillionObjects.Steps
{
    /// <summary>
    /// One frame of the cube field for the GPU instanced backend: integrates the attractor spring for
    /// every object and writes its local-to-world matrix into the shared matrix array. Matrices are
    /// stored bucket by bucket rather than by object index, so each object writes to the slot its
    /// palette bucket reserved for it (<see cref="MatrixSlots"/>).
    /// </summary>
    [BurstCompile]
    internal struct InstancedWaveJob : IJobParallelFor
    {
        #region Job data
        /// <summary>Grid position each object springs back to.</summary>
        [ReadOnly] public NativeArray<float3> RestPositions;
        /// <summary>Offset from rest, carried across frames.</summary>
        public NativeArray<float3> Displacements;
        /// <summary>Spring velocity, carried across frames.</summary>
        public NativeArray<float3> Velocities;
        /// <summary>Object index -> index into Matrices.</summary>
        [ReadOnly] public NativeArray<int> MatrixSlots;
        /// <summary>Scattered writes: slot order is bucket order, not object order.</summary>
        [NativeDisableParallelForRestriction]
        public NativeArray<float4x4> Matrices;
        /// <summary>Blittable copy of the shared field settings.</summary>
        public FieldParams Parameters;
        /// <summary>(x, y, z, radius); radius <= 0 means inactive.</summary>
        public float4 Attractor;
        /// <summary>Scene time the wave is evaluated at.</summary>
        public float Time;
        /// <summary>Seconds since the previous tick, for the spring integration.</summary>
        public float DeltaTime;
        #endregion

        #region Execution
        /// <summary>Advances one object's spring state, then writes its matrix to its bucket slot.</summary>
        public void Execute(int index)
        {
            float3 restPosition = RestPositions[index];
            float3 displacement = Displacements[index];
            float3 velocity = Velocities[index];
            ObjectField.IntegrateSpring(ref displacement, ref velocity, restPosition, Attractor, DeltaTime, Parameters);
            Displacements[index] = displacement;
            Velocities[index] = velocity;
            Matrices[MatrixSlots[index]] = ObjectField.LocalToWorld(index, restPosition, displacement, Time, Parameters);
        }
        #endregion
    }
}
