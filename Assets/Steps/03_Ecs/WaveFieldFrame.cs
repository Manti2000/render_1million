using Unity.Entities;
using Unity.Mathematics;

namespace MillionObjects.Steps.Ecs
{
    /// <summary>
    /// Singleton carrying this frame's inputs from the backend MonoBehaviour into the ECS world.
    /// The backend owns timing (the benchmark drives it, not <c>Update</c>), so the system reads
    /// time from here rather than from the world's own clock. Its existence also gates
    /// <see cref="WaveCubeSystem"/>: no singleton means no field is spawned.
    /// </summary>
    public struct WaveFieldFrame : IComponentData
    {
        #region Public fields
        /// <summary>Field time in seconds, as handed to the backend's tick.</summary>
        public float Time;
        /// <summary>Seconds elapsed since the previous tick; used to integrate the spring.</summary>
        public float DeltaTime;
        /// <summary>Attractor sphere as (x, y, z, radius); a radius of zero or less means inactive.</summary>
        public float4 Attractor;
        /// <summary>Blittable snapshot of the shared field settings.</summary>
        public FieldParams Params;
        #endregion
    }
}
