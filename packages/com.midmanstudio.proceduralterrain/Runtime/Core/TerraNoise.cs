// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraNoise.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>The basic noise one octave is made of.</summary>
    public enum TerraNoiseKind
    {
        /// <summary>Gradient (Perlin style) noise: a hashed direction at each lattice corner. Softer, less blocky.</summary>
        Gradient = 0,
        /// <summary>Value noise: a hashed number at each lattice corner, blended. Cheaper, a little more blocky.</summary>
        Value = 1
    }

    /// <summary>What is done to each octave before the octaves are added.</summary>
    public enum TerraNoiseMode
    {
        /// <summary>The octave as it is: rolling hills.</summary>
        Fbm = 0,
        /// <summary>The absolute distance from the middle value: rounded ridges and creases at the sea level of the noise.</summary>
        Billow = 1,
        /// <summary>One minus that: sharp ridges.</summary>
        Ridged = 2
    }

    /// <summary>How much each octave counts relative to the one before. The named values are the ones Glazycheva's generator offers.</summary>
    public enum TerraPersistenceKind
    {
        /// <summary>Use <see cref="TerraNoiseSettings.persistence"/>.</summary>
        Custom = 0,
        Quarter = 1,
        Half = 2,
        InverseSqrt = 3,
        /// <summary>Every octave counts the same.</summary>
        One = 4
    }

    /// <summary>The settings of the base noise. Plain public fields so Unity can serialise it.</summary>
    [Serializable]
    public class TerraNoiseSettings
    {
        public TerraNoiseKind kind = TerraNoiseKind.Gradient;
        public TerraNoiseMode mode = TerraNoiseMode.Fbm;

        /// <summary>Metres per cell of the first octave. Bigger is smoother and has wider hills.</summary>
        public double scale = 220.0;

        public int octaves = 4;
        public TerraPersistenceKind persistenceKind = TerraPersistenceKind.Custom;
        public double persistence = 0.5;

        /// <summary>The frequency factor between octaves.</summary>
        public double lacunarity = 2.0;

        /// <summary>A shift of the whole noise in metres, a second way to pick a different map next to the seed.</summary>
        public double offsetX;
        public double offsetZ;

        /// <summary>
        /// The summed octaves land in 0..1 but spend most of their time in the middle, because adding octaves pulls values
        /// toward the mean. These two stretch the practical range to 0..1: a value at remapMin becomes 0 and one at remapMax
        /// becomes 1, clamped. They are fixed numbers, never taken from the generated data, so two chunks always agree. 0 and 1
        /// leave the noise as it is.
        /// </summary>
        public double remapMin = 0.0;
        public double remapMax = 1.0;
    }

    /// <summary>
    /// Layered noise, compiled from <see cref="TerraNoiseSettings"/> and a seed. Pure: the value at a point depends on the
    /// point and the settings only, so it can be evaluated for any chunk in any order.
    /// </summary>
    public sealed class TerraNoise
    {
        /// <summary>The largest magnitude 2D gradient noise with unit gradients can reach (the square root of 2 over 2).</summary>
        public const double GradientBound = 0.70710678118654752;

        // Sixteen unit gradients, written out as numbers so no platform sin and cos can change a result.
        private static readonly double[] GradX =
        {
            1.0, 0.92387953251128674, 0.70710678118654752, 0.38268343236508978,
            0.0, -0.38268343236508978, -0.70710678118654752, -0.92387953251128674,
            -1.0, -0.92387953251128674, -0.70710678118654752, -0.38268343236508978,
            0.0, 0.38268343236508978, 0.70710678118654752, 0.92387953251128674
        };

        private static readonly double[] GradZ =
        {
            0.0, 0.38268343236508978, 0.70710678118654752, 0.92387953251128674,
            1.0, 0.92387953251128674, 0.70710678118654752, 0.38268343236508978,
            0.0, -0.38268343236508978, -0.70710678118654752, -0.92387953251128674,
            -1.0, -0.92387953251128674, -0.70710678118654752, -0.38268343236508978
        };

        private readonly ulong seed;
        private readonly TerraNoiseKind kind;
        private readonly TerraNoiseMode mode;
        private readonly int octaves;
        private readonly double[] frequency;
        private readonly double[] amplitude;
        private readonly double[] offX;
        private readonly double[] offZ;
        private readonly double amplitudeSum;
        private readonly double shiftX;
        private readonly double shiftZ;
        private readonly double remapMin;
        private readonly double remapScale;

        public TerraNoise(int seed, TerraNoiseSettings s)
        {
            if (s == null)
            {
                s = new TerraNoiseSettings();
            }
            this.seed = TerraHash.SeedFrom(seed);
            kind = s.kind;
            mode = s.mode;
            octaves = s.octaves < 1 ? 1 : (s.octaves > 16 ? 16 : s.octaves);
            double scale = s.scale > 1e-6 ? s.scale : 1e-6;
            double persistence = ResolvePersistence(s);
            double lacunarity = s.lacunarity > 1e-6 ? s.lacunarity : 1e-6;
            frequency = new double[octaves];
            amplitude = new double[octaves];
            offX = new double[octaves];
            offZ = new double[octaves];
            double f = 1.0 / scale;
            double a = 1.0;
            double sum = 0.0;
            for (int i = 0; i < octaves; i++)
            {
                frequency[i] = f;
                amplitude[i] = a;
                sum += a;
                // A hashed offset per octave keeps the octaves from lining up at the origin.
                offX[i] = TerraHash.ToSigned(TerraHash.Hash(this.seed, i, 0, 101)) * 1000.0;
                offZ[i] = TerraHash.ToSigned(TerraHash.Hash(this.seed, i, 0, 102)) * 1000.0;
                f *= lacunarity;
                a *= persistence;
            }
            amplitudeSum = sum;
            shiftX = s.offsetX;
            shiftZ = s.offsetZ;
            remapMin = s.remapMin;
            double span = s.remapMax - s.remapMin;
            remapScale = span > 1e-9 ? 1.0 / span : 1.0;
        }

        /// <summary>The persistence a settings object stands for.</summary>
        public static double ResolvePersistence(TerraNoiseSettings s)
        {
            switch (s.persistenceKind)
            {
                case TerraPersistenceKind.Quarter:
                    return 0.25;
                case TerraPersistenceKind.Half:
                    return 0.5;
                case TerraPersistenceKind.InverseSqrt:
                    return 0.70710678118654752;
                case TerraPersistenceKind.One:
                    return 1.0;
                default:
                    return s.persistence < 0.0 ? 0.0 : s.persistence;
            }
        }

        /// <summary>
        /// The noise at a true position in 0..1, before any remap. This is exact: the octave sum is divided by the sum of the
        /// amplitudes, which is the largest value it can reach, so no look at the generated data is needed.
        /// </summary>
        public double SampleRaw(double x, double z)
        {
            x += shiftX;
            z += shiftZ;
            double total = 0.0;
            for (int i = 0; i < octaves; i++)
            {
                double n = Octave(x * frequency[i] + offX[i], z * frequency[i] + offZ[i], i);
                total += n * amplitude[i];
            }
            double v = total / amplitudeSum;
            return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
        }

        /// <summary>
        /// Measures where the raw noise spends its time, for choosing the remap. It samples a fixed scatter of points and returns
        /// the values below which the given shares of them fall, for example 0.01 and 0.99. Run it once, in the editor or in a
        /// test, and type the two numbers into <see cref="TerraNoiseSettings.remapMin"/> and
        /// <see cref="TerraNoiseSettings.remapMax"/>. The numbers then stay fixed; nothing is measured while chunks are built.
        /// </summary>
        public void MeasureRange(double lowShare, double highShare, int samples, out double low, out double high)
        {
            if (samples < 100)
            {
                samples = 100;
            }
            double[] v = new double[samples];
            TerraRng rng = new TerraRng(0x7F4A7C15UL);
            double reach = 1000.0 / frequency[0];
            for (int i = 0; i < samples; i++)
            {
                v[i] = SampleRaw((rng.NextDouble() - 0.5) * reach, (rng.NextDouble() - 0.5) * reach);
            }
            Array.Sort(v);
            low = v[Math.Min(samples - 1, Math.Max(0, (int)(lowShare * samples)))];
            high = v[Math.Min(samples - 1, Math.Max(0, (int)(highShare * samples)))];
        }

        /// <summary>The noise at a true position after the fixed remap, in 0..1.</summary>
        public double Sample01(double x, double z)
        {
            double v = (SampleRaw(x, z) - remapMin) * remapScale;
            return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
        }

        // One octave in 0..1, with the mode applied.
        private double Octave(double px, double pz, int index)
        {
            double n01 = kind == TerraNoiseKind.Value ? ValueNoise(px, pz, index) : GradientNoise(px, pz, index);
            switch (mode)
            {
                case TerraNoiseMode.Billow:
                    return Math.Abs(n01 * 2.0 - 1.0);
                case TerraNoiseMode.Ridged:
                    return 1.0 - Math.Abs(n01 * 2.0 - 1.0);
                default:
                    return n01;
            }
        }

        private static double Fade(double t)
        {
            return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        }

        private double GradientNoise(double px, double pz, int octave)
        {
            double fx = Math.Floor(px);
            double fz = Math.Floor(pz);
            long ix = (long)fx;
            long iz = (long)fz;
            double dx = px - fx;
            double dz = pz - fz;
            double n00 = Dot(ix, iz, dx, dz, octave);
            double n10 = Dot(ix + 1, iz, dx - 1.0, dz, octave);
            double n01 = Dot(ix, iz + 1, dx, dz - 1.0, octave);
            double n11 = Dot(ix + 1, iz + 1, dx - 1.0, dz - 1.0, octave);
            double u = Fade(dx);
            double w = Fade(dz);
            double a = n00 + (n10 - n00) * u;
            double b = n01 + (n11 - n01) * u;
            double n = a + (b - a) * w;
            double r = (n / GradientBound) * 0.5 + 0.5;
            return r < 0.0 ? 0.0 : (r > 1.0 ? 1.0 : r);
        }

        private double Dot(long cx, long cz, double dx, double dz, int octave)
        {
            int g = (int)(TerraHash.Hash(seed, cx, cz, octave) & 15UL);
            return GradX[g] * dx + GradZ[g] * dz;
        }

        private double ValueNoise(double px, double pz, int octave)
        {
            double fx = Math.Floor(px);
            double fz = Math.Floor(pz);
            long ix = (long)fx;
            long iz = (long)fz;
            double dx = px - fx;
            double dz = pz - fz;
            double v00 = TerraHash.ToUnit(TerraHash.Hash(seed, ix, iz, octave));
            double v10 = TerraHash.ToUnit(TerraHash.Hash(seed, ix + 1, iz, octave));
            double v01 = TerraHash.ToUnit(TerraHash.Hash(seed, ix, iz + 1, octave));
            double v11 = TerraHash.ToUnit(TerraHash.Hash(seed, ix + 1, iz + 1, octave));
            double u = Fade(dx);
            double w = Fade(dz);
            double a = v00 + (v10 - v00) * u;
            double b = v01 + (v11 - v01) * u;
            return a + (b - a) * w;
        }
    }
}
