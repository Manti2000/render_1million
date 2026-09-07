using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MillionObjects
{
    /// <summary>
    /// Flexibility demo: click a cube to recolour it. No colliders; the click ray is marched through
    /// the lattice in half-spacing steps and each visited cell is inverted to an object index through
    /// <see cref="ObjectField"/>, accepting the first cube whose rest position lies near the ray.
    /// </summary>
    public class ObjectPicker : MonoBehaviour
    {
        #region Inspector fields
        [SerializeField, Tooltip("Switcher whose active backend is asked to recolour.")]
        private BackendSwitcher _switcher;
        [SerializeField, Tooltip("Camera the click ray is cast from.")]
        private Camera _camera;
        [SerializeField, Tooltip("Palette slot picked cubes are recoloured to.")]
        private int _highlightPaletteIndex = 15;   // white: the foam colour, visible anywhere in the blue cloud
        #endregion

        #region Events
        /// <summary>Raised after a click with the object index (or -1) and whether the backend recoloured it.</summary>
        public event System.Action<int, bool> Picked;
        #endregion

        #region Lifecycle
        private void Update()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                return;
            if (_switcher == null || _switcher.Active == null || _camera == null)
                return;
            Pick(Mouse.current.position.ReadValue());
        }
        #endregion

        #region Picking
        /// <summary>Resolves a screen position to an object index and asks the backend to recolour it.</summary>
        private void Pick(Vector2 screenPosition)
        {
            var backend = _switcher.Active;
            int index = IndexUnder(screenPosition, backend);
            bool recoloured = index >= 0 && backend.TryRecolor(index, _highlightPaletteIndex);
            Picked?.Invoke(index, recoloured);
        }

        /// <summary>First object along the click ray, or -1 when the ray misses the cloud.</summary>
        private int IndexUnder(Vector2 screenPosition, ObjectBackend backend)
        {
            var parameters = backend.Settings.ToParams(backend.Count);
            var ray = _camera.ScreenPointToRay(screenPosition);
            var bounds = ObjectField.FieldBounds(backend.Count, parameters);
            if (!bounds.IntersectRay(ray, out float entry))
                return -1;
            int side = ObjectField.SideLength(backend.Count);
            float step = parameters.Spacing * 0.5f;
            float exit = entry + bounds.size.magnitude;
            for (float t = Mathf.Max(entry, 0f); t < exit; t += step)
            {
                int index = CubeNear(ray, t, side, backend.Count, parameters);
                if (index >= 0)
                    return index;
            }
            return -1;
        }

        /// <summary>Object whose lattice cell holds the ray point at <paramref name="distance"/>, if its rest position is within a cube of the ray.</summary>
        private static int CubeNear(Ray ray, float distance, int side, int count, in FieldParams parameters)
        {
            float3 point = ray.GetPoint(distance);
            int index = ObjectField.IndexAt(point, side, parameters.Spacing);
            if (index < 0 || index >= count)
                return -1;
            float3 rest = ObjectField.RestPosition(index, side, parameters.Spacing);
            float3 toRest = rest - (float3)ray.origin;
            float along = math.dot(toRest, (float3)ray.direction);
            float offAxis = math.length(toRest - along * (float3)ray.direction);
            return offAxis <= parameters.CubeScale * 0.75f ? index : -1;
        }
        #endregion
    }
}
