// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabShape.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// The opacity of the three shapes of the fission stage, as functions of the distance from the centre.
    /// The view bakes them into small textures at start-up, so the sample imports no image. Has no UnityEngine
    /// references.
    /// </summary>
    public static class NuclearLabShape
    {
        private const float DiscEdge = 0.12f;
        private const float RingInner = 0.78f;

        /// <summary>
        /// The opacity at distance <paramref name="d"/>, as a share of the radius, from the centre of a shape.
        /// A disc is solid and falls to 0 over its outer 12 percent, a glow falls off with the square of the
        /// distance and a ring is a band between 78 and 100 percent of the radius.
        /// </summary>
        public static float Alpha(StageKind kind, float d)
        {
            if (!(d < 1f))
            {
                return 0f;
            }
            if (d < 0f)
            {
                d = 0f;
            }
            switch (kind)
            {
                case StageKind.Disc:
                    return d <= 1f - DiscEdge ? 1f : Smooth((1f - d) / DiscEdge);
                case StageKind.Glow:
                    return (1f - d) * (1f - d);
                case StageKind.Ring:
                    if (d <= RingInner)
                    {
                        return 0f;
                    }
                    float x = (d - RingInner) / (1f - RingInner);
                    return Smooth(x < 0.5f ? x * 2f : (1f - x) * 2f);
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        /// <summary>
        /// The opacity of every pixel of a square image of <paramref name="size"/> pixels, 0 to 255, row after row.
        /// The shape fills the image from edge to edge.
        /// </summary>
        public static byte[] Bake(StageKind kind, int size)
        {
            if (size < 2)
            {
                throw new ArgumentOutOfRangeException("size", "The size must be at least 2.");
            }
            byte[] pixels = new byte[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    pixels[y * size + x] = (byte)Math.Round(Alpha(kind, d) * 255f);
                }
            }
            return pixels;
        }

        private static float Smooth(float x)
        {
            return x * x * (3f - 2f * x);
        }
    }
}
