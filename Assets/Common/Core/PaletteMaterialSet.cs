using UnityEngine;

namespace MillionObjects
{
    /// <summary>
    /// Sixteen material instances, one per palette colour, derived from the shared base material.
    /// Lets renderer-based backends colour objects while staying SRP Batcher compatible, because a
    /// MaterialPropertyBlock per renderer would break batching.
    /// </summary>
    public sealed class PaletteMaterialSet
    {
        #region Constants
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        /// <summary>Entities Graphics enables this on materials it registers; renderer and instanced draws must not inherit it.</summary>
        private const string DotsInstancingKeyword = "DOTS_INSTANCING_ON";
        #endregion

        #region Private fields
        private readonly Material[] _materials;   // one instance per palette slot, owned by this set
        #endregion

        #region Public properties
        /// <summary>Material for a palette slot.</summary>
        public Material this[int paletteIndex] => _materials[paletteIndex];
        /// <summary>Number of materials in the set; equals the palette size.</summary>
        public int Count => _materials.Length;
        #endregion

        #region Construction
        public PaletteMaterialSet(FieldSettings settings)
        {
            _materials = new Material[ObjectField.PaletteSize];
            for (int i = 0; i < _materials.Length; i++)
            {
                _materials[i] = new Material(settings.CubeMaterial) { name = $"{settings.CubeMaterial.name}_Palette{i:00}" };
                _materials[i].SetColor(BaseColorProperty, settings.PaletteColor(i));
                _materials[i].DisableKeyword(DotsInstancingKeyword);
            }
        }
        #endregion

        #region Public interface
        /// <summary>Destroys the material instances. The set must not be used afterwards.</summary>
        public void Dispose()
        {
            for (int i = 0; i < _materials.Length; i++)
            {
                if (_materials[i] != null)
                    Object.Destroy(_materials[i]);
                _materials[i] = null;
            }
        }
        #endregion
    }
}
