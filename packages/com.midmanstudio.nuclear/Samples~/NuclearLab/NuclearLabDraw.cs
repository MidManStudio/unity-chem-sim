// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabDraw.cs"
// ============================================================================
using UnityEngine;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// Draws the bar charts of the panel with plain IMGUI rectangles: no
    /// textures to import, no materials, and no render pipeline requirement.
    /// </summary>
    internal static class NuclearLabDraw
    {
        private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color MarkerColor = new Color(1f, 0.85f, 0.2f, 0.9f);

        /// <summary>
        /// Reserves a strip of the given height as wide as the panel and fills it
        /// with one bar per value. With several series the bars of one value sit side by side.
        /// <paramref name="marker"/> is the index of a value to mark with a vertical line, or -1.
        /// </summary>
        public static void Bars(
            float height, double[][] series, Color[] colors, double min, double max, bool log, int marker)
        {
            Rect rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint || series == null || series.Length == 0)
            {
                return;
            }
            int slots = series[0].Length;
            if (slots == 0)
            {
                return;
            }
            Color saved = GUI.color;
            GUI.color = Backdrop;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            float slotWidth = rect.width / slots;
            float barWidth = Mathf.Max(1f, slotWidth / series.Length - 1f);
            float inner = rect.height - 2f;
            for (int s = 0; s < series.Length; s++)
            {
                GUI.color = colors[s % colors.Length];
                double[] values = series[s];
                for (int i = 0; i < values.Length && i < slots; i++)
                {
                    float h = (float)NuclearLabFormat.BarFraction(values[i], min, max, log) * inner;
                    if (h < 0.5f)
                    {
                        continue;
                    }
                    float x = rect.x + i * slotWidth + s * (slotWidth / series.Length);
                    GUI.DrawTexture(new Rect(x, rect.yMax - 1f - h, barWidth, h), Texture2D.whiteTexture);
                }
            }
            if (marker >= 0 && marker < slots)
            {
                GUI.color = MarkerColor;
                GUI.DrawTexture(
                    new Rect(rect.x + (marker + 0.5f) * slotWidth - 1f, rect.y, 2f, rect.height),
                    Texture2D.whiteTexture);
            }
            GUI.color = saved;
        }

        /// <summary>One series in one colour.</summary>
        public static void Bars(float height, double[] values, Color color, double min, double max, bool log)
        {
            Bars(height, new[] { values }, new[] { color }, min, max, log, -1);
        }
    }
}
