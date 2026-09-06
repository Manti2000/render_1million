using MillionObjects.Benchmark;
using UnityEngine;
using UnityEngine.UIElements;

namespace MillionObjects
{
    /// <summary>
    /// One-second frame-time graph: a bar per recent frame with reference lines at 60 and 30 fps.
    /// Stalls that a 30 fps recording smooths away show up here as spikes.
    /// </summary>
    [UxmlElement]
    public partial class FrameTimeStrip : VisualElement
    {
        #region Constants
        private const float CeilingMs = 50f;
        private const float SixtyFpsMs = 16.667f;
        private const float ThirtyFpsMs = 33.333f;
        private static readonly Color BarColor = new Color(0.55f, 0.8f, 1f, 0.9f);
        private static readonly Color SlowBarColor = new Color(1f, 0.35f, 0.3f, 0.95f);
        private static readonly Color GuideColor = new Color(1f, 1f, 1f, 0.35f);
        #endregion

        #region Public properties
        /// <summary>Sampler whose history is drawn. Null draws an empty strip.</summary>
        public FrameSampler Sampler { get; set; }
        #endregion

        #region Construction
        public FrameTimeStrip()
        {
            generateVisualContent += Draw;
        }
        #endregion

        #region Drawing
        /// <summary>Paints guide lines and one bar per history entry.</summary>
        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            float width = contentRect.width;
            float height = contentRect.height;
            DrawGuide(painter, width, height, SixtyFpsMs);
            DrawGuide(painter, width, height, ThirtyFpsMs);
            if (Sampler == null)
                return;
            float barWidth = width / FrameSampler.HistoryLength;
            for (int i = 0; i < FrameSampler.HistoryLength; i++)
                DrawBar(painter, i * barWidth, barWidth, height, Sampler.HistoryAt(i));
        }

        /// <summary>Horizontal reference line at a frame time.</summary>
        private static void DrawGuide(Painter2D painter, float width, float height, float ms)
        {
            float y = height - Mathf.Clamp01(ms / CeilingMs) * height;
            painter.strokeColor = GuideColor;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, y));
            painter.LineTo(new Vector2(width, y));
            painter.Stroke();
        }

        /// <summary>Vertical bar for one frame, red once it misses 30 fps.</summary>
        private static void DrawBar(Painter2D painter, float x, float barWidth, float height, float ms)
        {
            float barHeight = Mathf.Clamp01(ms / CeilingMs) * height;
            painter.fillColor = ms > ThirtyFpsMs ? SlowBarColor : BarColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, height));
            painter.LineTo(new Vector2(x + barWidth, height));
            painter.LineTo(new Vector2(x + barWidth, height - barHeight));
            painter.LineTo(new Vector2(x, height - barHeight));
            painter.ClosePath();
            painter.Fill();
        }
        #endregion
    }
}
