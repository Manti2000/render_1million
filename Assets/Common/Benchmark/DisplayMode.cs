using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MillionObjects.Benchmark
{
    /// <summary>
    /// Fixed display state for benchmark and recording runs: vsync off, uncapped frame rate, render
    /// scale 1, and a 1920-wide frame at the display's own aspect on PC (1920x1200 on 16:10 laptops,
    /// 1920x1080 on 16:9) or the Steam Deck's native 800p. Matching the display aspect keeps the image
    /// unskewed and, since the Deck is 16:10 too, frames the cloud identically on every device. Not a
    /// like-for-like pixel comparison, but the realistic one; the report records the resolution used.
    /// </summary>
    public static class DisplayMode
    {
        #region Constants
        private const int PcWidth = 1920;
        private static readonly int2 SteamDeckResolution = new int2(1280, 800);
        #endregion

        #region Public properties
        /// <summary>True when running on a Steam Deck, detected via the device model or Steam's environment variable.</summary>
        public static bool IsSteamDeck
        {
            get
            {
                string model = SystemInfo.deviceModel ?? string.Empty;
                return model.Contains("Jupiter") || model.Contains("Galileo") || Environment.GetEnvironmentVariable("SteamDeck") == "1";
            }
        }
        #endregion

        #region Public interface
        /// <summary>Applies the fixed display state, using <paramref name="requested"/> or the per-device default resolution.</summary>
        public static void Apply(int2? requested)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            int2 resolution = requested ?? (IsSteamDeck ? SteamDeckResolution : PcResolutionForDisplay());
            Screen.SetResolution(resolution.x, resolution.y, FullScreenMode.FullScreenWindow);
            ApplyRenderScale(1f);
        }

        /// <summary>Quits the player, or exits play mode in the editor.</summary>
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }
        #endregion

        #region Helpers
        /// <summary>1920 wide at the desktop's aspect ratio, rounded to an even height, so the fullscreen window is never stretched.</summary>
        private static int2 PcResolutionForDisplay()
        {
            Resolution desktop = Screen.currentResolution;
            float aspect = desktop.height > 0 ? (float)desktop.width / desktop.height : 16f / 9f;
            int height = Mathf.RoundToInt(PcWidth / aspect / 2f) * 2;
            return new int2(PcWidth, height);
        }

        /// <summary>Forces the active URP asset's render scale so upscaling cannot change the pixel count.</summary>
        private static void ApplyRenderScale(float scale)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.renderScale = scale;
        }
        #endregion
    }
}
