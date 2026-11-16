// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraCurve.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>One key of a curve.</summary>
    [Serializable]
    public struct TerraCurveKey
    {
        public float time;
        public float value;

        public TerraCurveKey(float time, float value)
        {
            this.time = time;
            this.value = value;
        }
    }

    /// <summary>
    /// A curve from 0..1 to 0..1 that shapes the normalised noise into a height, like the AnimationCurve in Glazycheva's
    /// generator. Keys are expected in ascending order of time. With smooth on, the curve between keys is a monotone cubic
    /// (Fritsch and Butland), so it never overshoots a key. With smooth off it is piecewise linear.
    /// </summary>
    [Serializable]
    public class TerraCurve
    {
        public TerraCurveKey[] keys = new[] { new TerraCurveKey(0.0f, 0.0f), new TerraCurveKey(1.0f, 1.0f) };
        public bool smooth = true;

        public TerraCurve()
        {
        }

        public TerraCurve(bool smooth, params TerraCurveKey[] keys)
        {
            this.smooth = smooth;
            this.keys = keys;
        }

        /// <summary>The identity curve.</summary>
        public static TerraCurve Linear()
        {
            return new TerraCurve(false, new TerraCurveKey(0.0f, 0.0f), new TerraCurveKey(1.0f, 1.0f));
        }

        /// <summary>Builds the evaluator, which holds the slopes the smooth form needs. Build it once and evaluate it many times.</summary>
        public TerraCurveEval Compile()
        {
            return new TerraCurveEval(this);
        }
    }

    /// <summary>A compiled <see cref="TerraCurve"/>. Immutable, so one instance can be read from many threads.</summary>
    public sealed class TerraCurveEval
    {
        private readonly double[] t;
        private readonly double[] v;
        private readonly double[] m;
        private readonly bool smooth;

        public TerraCurveEval(TerraCurve curve)
        {
            TerraCurveKey[] keys = curve != null && curve.keys != null && curve.keys.Length > 0
                ? (TerraCurveKey[])curve.keys.Clone()
                : new[] { new TerraCurveKey(0.0f, 0.0f), new TerraCurveKey(1.0f, 1.0f) };
            Array.Sort(keys, (a, b) => a.time.CompareTo(b.time));
            int n = keys.Length;
            t = new double[n];
            v = new double[n];
            for (int i = 0; i < n; i++)
            {
                t[i] = keys[i].time;
                v[i] = keys[i].value;
            }
            smooth = curve == null || curve.smooth;
            m = new double[n];
            if (n > 1)
            {
                ComputeSlopes();
            }
        }

        // Fritsch and Butland slopes: zero at a local extreme, the weighted harmonic mean of the two secants elsewhere.
        // That is what keeps the cubic between two keys inside the range of those two keys.
        private void ComputeSlopes()
        {
            int n = t.Length;
            double[] d = new double[n - 1];
            double[] h = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = t[i + 1] - t[i];
                d[i] = h[i] > 1e-12 ? (v[i + 1] - v[i]) / h[i] : 0.0;
            }
            if (n == 2)
            {
                m[0] = d[0];
                m[1] = d[0];
                return;
            }
            for (int i = 1; i < n - 1; i++)
            {
                if (d[i - 1] * d[i] <= 0.0)
                {
                    m[i] = 0.0;
                }
                else
                {
                    double w1 = 2.0 * h[i] + h[i - 1];
                    double w2 = h[i] + 2.0 * h[i - 1];
                    m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
                }
            }
            m[0] = EndSlope(h[0], h[1], d[0], d[1]);
            m[n - 1] = EndSlope(h[n - 2], h[n - 3], d[n - 2], d[n - 3]);
        }

        // One sided three point slope at an end, kept monotone: the sign of the nearest secant, and at most three times it.
        private static double EndSlope(double h0, double h1, double d0, double d1)
        {
            double s = ((2.0 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
            if (s * d0 <= 0.0)
            {
                return 0.0;
            }
            if (d0 * d1 <= 0.0 && Math.Abs(s) > 3.0 * Math.Abs(d0))
            {
                return 3.0 * d0;
            }
            return s;
        }

        /// <summary>The curve value at x. Before the first key and after the last the end value holds.</summary>
        public double Evaluate(double x)
        {
            int n = t.Length;
            if (n == 1 || x <= t[0])
            {
                return v[0];
            }
            if (x >= t[n - 1])
            {
                return v[n - 1];
            }
            int i = 0;
            while (i < n - 2 && x > t[i + 1])
            {
                i++;
            }
            double h = t[i + 1] - t[i];
            if (h <= 1e-12)
            {
                return v[i + 1];
            }
            double s = (x - t[i]) / h;
            if (!smooth)
            {
                return v[i] + (v[i + 1] - v[i]) * s;
            }
            double s2 = s * s;
            double s3 = s2 * s;
            double h00 = 2.0 * s3 - 3.0 * s2 + 1.0;
            double h10 = s3 - 2.0 * s2 + s;
            double h01 = -2.0 * s3 + 3.0 * s2;
            double h11 = s3 - s2;
            return h00 * v[i] + h10 * h * m[i] + h01 * v[i + 1] + h11 * h * m[i + 1];
        }
    }
}
