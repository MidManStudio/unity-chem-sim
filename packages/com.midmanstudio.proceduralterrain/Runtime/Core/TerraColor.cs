// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraColor.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>A colour as four floats, so the Core needs no Unity type. Values are as picked in an inspector (gamma).</summary>
    [Serializable]
    public struct TerraColor
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public TerraColor(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public TerraColor(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            a = 1.0f;
        }

        /// <summary>A colour from 0xRRGGBB.</summary>
        public static TerraColor FromHex(int rgb)
        {
            return new TerraColor(
                ((rgb >> 16) & 0xFF) / 255.0f,
                ((rgb >> 8) & 0xFF) / 255.0f,
                (rgb & 0xFF) / 255.0f);
        }

        public static TerraColor Lerp(TerraColor x, TerraColor y, float t)
        {
            if (t < 0.0f)
            {
                t = 0.0f;
            }
            else if (t > 1.0f)
            {
                t = 1.0f;
            }
            return new TerraColor(
                x.r + (y.r - x.r) * t,
                x.g + (y.g - x.g) * t,
                x.b + (y.b - x.b) * t,
                x.a + (y.a - x.a) * t);
        }

        /// <summary>Scales the colour channels, not alpha, and keeps them in 0..1.</summary>
        public TerraColor Scaled(float k)
        {
            return new TerraColor(Clamp01(r * k), Clamp01(g * k), Clamp01(b * k), a);
        }

        private static float Clamp01(float v)
        {
            return v < 0.0f ? 0.0f : (v > 1.0f ? 1.0f : v);
        }

        public static readonly TerraColor White = new TerraColor(1.0f, 1.0f, 1.0f, 1.0f);
        public static readonly TerraColor Gray = new TerraColor(0.5f, 0.5f, 0.5f, 1.0f);
    }

    /// <summary>One stop of a gradient: a position in 0..1 and the colour there.</summary>
    [Serializable]
    public struct TerraColorStop
    {
        public float position;
        public TerraColor color;

        public TerraColorStop(float position, TerraColor color)
        {
            this.position = position;
            this.color = color;
        }
    }

    /// <summary>A colour gradient. Stops are expected in ascending order of position. Outside the first and last stop the end colour holds.</summary>
    [Serializable]
    public class TerraGradient
    {
        public TerraColorStop[] stops = new TerraColorStop[0];

        public TerraGradient()
        {
        }

        public TerraGradient(params TerraColorStop[] stops)
        {
            this.stops = stops;
        }

        /// <summary>A gradient of one colour.</summary>
        public static TerraGradient Solid(TerraColor c)
        {
            return new TerraGradient(new TerraColorStop(0.0f, c), new TerraColorStop(1.0f, c));
        }

        /// <summary>A gradient from one colour to another.</summary>
        public static TerraGradient Two(TerraColor from, TerraColor to)
        {
            return new TerraGradient(new TerraColorStop(0.0f, from), new TerraColorStop(1.0f, to));
        }

        public TerraColor Evaluate(float t)
        {
            if (stops == null || stops.Length == 0)
            {
                return TerraColor.Gray;
            }
            if (t <= stops[0].position || stops.Length == 1)
            {
                return stops[0].color;
            }
            int last = stops.Length - 1;
            if (t >= stops[last].position)
            {
                return stops[last].color;
            }
            for (int i = 1; i <= last; i++)
            {
                if (t <= stops[i].position)
                {
                    float span = stops[i].position - stops[i - 1].position;
                    float f = span > 1e-6f ? (t - stops[i - 1].position) / span : 1.0f;
                    return TerraColor.Lerp(stops[i - 1].color, stops[i].color, f);
                }
            }
            return stops[last].color;
        }
    }
}
