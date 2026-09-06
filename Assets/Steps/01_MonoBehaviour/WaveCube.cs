using Unity.Mathematics;
using UnityEngine;

namespace MillionObjects.Steps.MonoBehaviourStep
{
    /// <summary>
    /// One cube of the rung 1 field. Owns its own spring state and animates itself from its own
    /// <c>Update</c>, reading the shared frame context from the backend. This is the "every object
    /// is a thing" pattern the ladder starts from: the per-object feature cost here is one field.
    /// </summary>
    public sealed class WaveCube : MonoBehaviour
    {
        #region Private fields
        private MonoBehaviourBackend _backend;   // publisher of the per-frame time, delta and attractor
        private Transform _transform;            // cached, so Update never pays for the component lookup
        private int _index;                      // grid index, drives rotation phase and palette slot
        private float3 _restPosition;            // grid slot this cube springs back to
        private float3 _displacement;            // current offset from rest, pushed by the attractor
        private float3 _velocity;                // spring velocity carried between frames
        #endregion

        #region Lifecycle
        private void Update()
        {
            if (_backend == null)
                return;
            FieldParams parameters = _backend.FieldParameters;
            float time = _backend.FrameTime;
            ObjectField.IntegrateSpring(ref _displacement, ref _velocity, _restPosition, _backend.Attractor, _backend.FrameDeltaTime, parameters);
            float3 position = ObjectField.Position(_restPosition, _displacement, time, parameters);
            quaternion rotation = ObjectField.Rotation(_index, time, parameters);
            _transform.SetPositionAndRotation(position, rotation);
        }
        #endregion

        #region Public interface
        /// <summary>Binds the cube to its backend and its slot on the grid. Called once, right after spawning.</summary>
        public void Initialize(MonoBehaviourBackend backend, int index, float3 restPosition)
        {
            _backend = backend;
            _transform = transform;
            _index = index;
            _restPosition = restPosition;
            _displacement = float3.zero;
            _velocity = float3.zero;
        }
        #endregion
    }
}
