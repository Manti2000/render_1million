using Unity.Entities;
using Unity.Mathematics;

namespace MillionObjects.Steps.Ecs
{
    /// <summary>
    /// Per-cube simulation state: everything the wave and the attractor spring need, held on the
    /// entity itself. This is the ECS answer to rung 1's fields and rung 2's parallel arrays, and
    /// it is the "one more per-object feature costs a component" row of the flexibility card.
    /// </summary>
    public struct WaveCubeState : IComponentData
    {
        #region Public fields
        /// <summary>Object index on the shared grid; drives rest position, rotation phase and palette slot.</summary>
        public int Index;
        /// <summary>Grid position the cube springs back to, cached so the job never recomputes it.</summary>
        public float3 RestPosition;
        /// <summary>Current offset from the rest position produced by the attractor spring.</summary>
        public float3 Displacement;
        /// <summary>Spring velocity carried between frames.</summary>
        public float3 Velocity;
        #endregion
    }
}
