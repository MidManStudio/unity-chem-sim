// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraHeightField.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>
    /// A region of the world whose noise comes from a seed of its own instead of the world seed. Change the world seed and
    /// everything changes except the land inside a lock: a hand picked valley that took an hour to find stays where it is.
    /// The region blends into the surrounding noise over its falloff.
    /// </summary>
    [Serializable]
    public class TerraLock
    {
        public string name = "Lock";
        public bool enabled = true;
        public TerraStampShape shape = TerraStampShape.Circle;
        public double x;
        public double z;
        public double radius = 150.0;
        public double halfX = 150.0;
        public double halfZ = 150.0;
        public double rotationDegrees;
        public double falloff = 60.0;

        /// <summary>The seed the noise inside the lock is generated from.</summary>
        public int seed = 1;

        /// <summary>1 inside the region, easing to 0 across the falloff.</summary>
        public double Weight(double px, double pz)
        {
            double dist;
            if (shape == TerraStampShape.Box)
            {
                dist = TerraShape.BoxDistance(px, pz, x, z, halfX, halfZ, rotationDegrees);
            }
            else
            {
                double dx = px - x;
                double dz = pz - z;
                dist = Math.Sqrt(dx * dx + dz * dz) - radius;
            }
            return TerraShape.FalloffWeight(dist, falloff);
        }
    }

    /// <summary>Everything that decides the height of the ground at a point.</summary>
    [Serializable]
    public class TerraHeightSettings
    {
        public TerraNoiseSettings noise = new TerraNoiseSettings();
        public TerraFalloffSettings falloff = new TerraFalloffSettings();

        /// <summary>Shapes the normalised noise into a normalised height. The identity by default.</summary>
        public TerraCurve curve = new TerraCurve();

        /// <summary>The height in metres of a normalised height of 0.</summary>
        public double baseHeight;

        /// <summary>The metres between a normalised height of 0 and one of 1.</summary>
        public double heightScale = 60.0;

        public TerraLock[] locks = new TerraLock[0];
        public TerraPatch[] patches = new TerraPatch[0];
        public TerraStamp[] stamps = new TerraStamp[0];
    }

    /// <summary>
    /// The compiled height of the world as a pure function of the seed, the settings and a true position. It owns no mesh and no
    /// chunk, so the same call gives the same answer from any chunk, any LOD and any thread. Build it once; do not edit the
    /// settings objects while it is in use (the heights of pads are worked out when it is built).
    /// </summary>
    public sealed class TerraHeightField
    {
        /// <summary>A stamp whose weight is below this does not keep scatter off.</summary>
        public const double ScatterBlockWeight = 0.35;

        private readonly TerraHeightSettings settings;
        private readonly TerraNoise noise;
        private readonly TerraLock[] locks;
        private readonly TerraNoise[] lockNoise;
        private readonly TerraCurveEval curve;
        private readonly TerraPatch[] patches;
        private readonly TerraStamp[] stamps;
        private readonly double[] padTarget;
        private readonly double[] boundMinX;
        private readonly double[] boundMaxX;
        private readonly double[] boundMinZ;
        private readonly double[] boundMaxZ;
        private readonly double baseHeight;
        private readonly double heightScale;

        public TerraHeightField(int seed, TerraHeightSettings s)
        {
            if (s == null)
            {
                s = new TerraHeightSettings();
            }
            settings = s;
            noise = new TerraNoise(seed, s.noise);
            curve = (s.curve ?? new TerraCurve()).Compile();
            baseHeight = s.baseHeight;
            heightScale = s.heightScale > 1e-9 ? s.heightScale : 1e-9;

            int lockCount = 0;
            if (s.locks != null)
            {
                for (int i = 0; i < s.locks.Length; i++)
                {
                    if (s.locks[i] != null && s.locks[i].enabled)
                    {
                        lockCount++;
                    }
                }
            }
            locks = new TerraLock[lockCount];
            lockNoise = new TerraNoise[lockCount];
            int li = 0;
            for (int i = 0; s.locks != null && i < s.locks.Length; i++)
            {
                if (s.locks[i] != null && s.locks[i].enabled)
                {
                    locks[li] = s.locks[i];
                    lockNoise[li] = new TerraNoise(s.locks[i].seed, s.noise);
                    li++;
                }
            }

            int patchCount = 0;
            for (int i = 0; s.patches != null && i < s.patches.Length; i++)
            {
                if (s.patches[i] != null && s.patches[i].enabled)
                {
                    patchCount++;
                }
            }
            patches = new TerraPatch[patchCount];
            int pi = 0;
            for (int i = 0; s.patches != null && i < s.patches.Length; i++)
            {
                if (s.patches[i] != null && s.patches[i].enabled)
                {
                    patches[pi++] = s.patches[i];
                }
            }

            int stampCount = 0;
            for (int i = 0; s.stamps != null && i < s.stamps.Length; i++)
            {
                if (s.stamps[i] != null && s.stamps[i].enabled)
                {
                    stampCount++;
                }
            }
            stamps = new TerraStamp[stampCount];
            padTarget = new double[stampCount];
            boundMinX = new double[stampCount];
            boundMaxX = new double[stampCount];
            boundMinZ = new double[stampCount];
            boundMaxZ = new double[stampCount];
            int si = 0;
            for (int i = 0; s.stamps != null && i < s.stamps.Length; i++)
            {
                if (s.stamps[i] != null && s.stamps[i].enabled)
                {
                    stamps[si++] = s.stamps[i];
                }
            }
            for (int i = 0; i < stamps.Length; i++)
            {
                ComputeBounds(i);
                TerraStamp st = stamps[i];
                if (st.kind == TerraStampKind.Pad)
                {
                    double target = st.autoHeight ? HeightUpTo(i, st.x, st.z) : st.targetHeight;
                    padTarget[i] = target + st.heightOffset;
                }
            }
        }

        public TerraHeightSettings Settings
        {
            get { return settings; }
        }

        /// <summary>The height in metres of a normalised height of 0.</summary>
        public double BaseHeight
        {
            get { return baseHeight; }
        }

        /// <summary>The metres between a normalised height of 0 and one of 1.</summary>
        public double HeightScale
        {
            get { return heightScale; }
        }

        /// <summary>The normalised value (0..1) a height in metres stands for. Biomes, terrace levels and scatter ranges read this.</summary>
        public double ValueOfHeight(double height)
        {
            double v = (height - baseHeight) / heightScale;
            return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
        }

        /// <summary>The height in metres of a normalised value.</summary>
        public double HeightOfValue(double value)
        {
            return baseHeight + value * heightScale;
        }

        /// <summary>The noise after the locks and the falloff, 0..1.</summary>
        public double NoiseValue(double x, double z)
        {
            double n = noise.Sample01(x, z);
            for (int i = 0; i < locks.Length; i++)
            {
                double w = locks[i].Weight(x, z);
                if (w > 0.0)
                {
                    double ln = lockNoise[i].Sample01(x, z);
                    n += (ln - n) * w;
                }
            }
            return TerraFalloff.Apply(settings.falloff, x, z, n);
        }

        /// <summary>The height in metres from the noise and the patches, before any stamp.</summary>
        public double HeightBeforeStamps(double x, double z)
        {
            double h01 = curve.Evaluate(NoiseValue(x, z));
            double h = baseHeight + h01 * heightScale;
            for (int i = 0; i < patches.Length; i++)
            {
                TerraPatch p = patches[i];
                double w = p.Weight(x, z) * p.strength;
                if (w <= 0.0)
                {
                    continue;
                }
                double ph = p.SampleMetres(x, z);
                switch (p.mode)
                {
                    case TerraPatchMode.Add:
                        h += ph * w;
                        break;
                    case TerraPatchMode.Max:
                        if (ph > h)
                        {
                            h += (ph - h) * w;
                        }
                        break;
                    case TerraPatchMode.Min:
                        if (ph < h)
                        {
                            h += (ph - h) * w;
                        }
                        break;
                    default:
                        h += (ph - h) * w;
                        break;
                }
            }
            return h;
        }

        /// <summary>The height of the ground in metres at a true position, with everything applied.</summary>
        public double Height(double x, double z)
        {
            return HeightUpTo(stamps.Length, x, z);
        }

        // The height with only the first count stamps applied.
        private double HeightUpTo(int count, double x, double z)
        {
            double h = HeightBeforeStamps(x, z);
            for (int i = 0; i < count; i++)
            {
                h = ApplyStamp(i, x, z, h);
            }
            return h;
        }

        private double ApplyStamp(int i, double x, double z, double h)
        {
            if (x < boundMinX[i] || x > boundMaxX[i] || z < boundMinZ[i] || z > boundMaxZ[i])
            {
                return h;
            }
            TerraStamp s = stamps[i];
            double nearX;
            double nearZ;
            double w = s.Influence(x, z, out nearX, out nearZ) * s.strength;
            if (w <= 0.0)
            {
                return h;
            }
            switch (s.kind)
            {
                case TerraStampKind.Pad:
                    return h + (padTarget[i] - h) * w;
                case TerraStampKind.Road:
                    {
                        double target = HeightUpTo(i, nearX, nearZ) + s.heightOffset;
                        return h + (target - h) * w;
                    }
                case TerraStampKind.Carve:
                    return h - s.amount * w;
                default:
                    return h + s.amount * w;
            }
        }

        private void ComputeBounds(int i)
        {
            TerraStamp s = stamps[i];
            double minX;
            double maxX;
            double minZ;
            double maxZ;
            bool polyline = s.kind == TerraStampKind.Road || (s.kind == TerraStampKind.Carve && s.points != null && s.points.Length >= 2);
            if (polyline)
            {
                minX = double.MaxValue;
                maxX = double.MinValue;
                minZ = double.MaxValue;
                maxZ = double.MinValue;
                for (int p = 0; s.points != null && p < s.points.Length; p++)
                {
                    minX = Math.Min(minX, s.points[p].x);
                    maxX = Math.Max(maxX, s.points[p].x);
                    minZ = Math.Min(minZ, s.points[p].z);
                    maxZ = Math.Max(maxZ, s.points[p].z);
                }
                if (minX > maxX)
                {
                    minX = maxX = minZ = maxZ = 0.0;
                }
                double reach = s.width * 0.5 + Math.Max(0.0, s.falloff);
                minX -= reach;
                maxX += reach;
                minZ -= reach;
                maxZ += reach;
            }
            else
            {
                double reach = (s.shape == TerraStampShape.Box ? Math.Sqrt(s.halfX * s.halfX + s.halfZ * s.halfZ) : s.radius)
                    + Math.Max(0.0, s.falloff);
                minX = s.x - reach;
                maxX = s.x + reach;
                minZ = s.z - reach;
                maxZ = s.z + reach;
            }
            boundMinX[i] = minX - 1e-6;
            boundMaxX[i] = maxX + 1e-6;
            boundMinZ[i] = minZ - 1e-6;
            boundMaxZ[i] = maxZ + 1e-6;
        }

        /// <summary>
        /// The unit normal of the ground at a position, from differences of the height field a step either side. Any step gives
        /// the same normal for the same position in every chunk, so smooth shading has no seam at a chunk border.
        /// </summary>
        public void Normal(double x, double z, double step, out double nx, out double ny, out double nz)
        {
            if (step < 1e-4)
            {
                step = 1e-4;
            }
            double hx0 = Height(x - step, z);
            double hx1 = Height(x + step, z);
            double hz0 = Height(x, z - step);
            double hz1 = Height(x, z + step);
            double dx = (hx1 - hx0) / (2.0 * step);
            double dz = (hz1 - hz0) / (2.0 * step);
            double len = Math.Sqrt(dx * dx + 1.0 + dz * dz);
            nx = -dx / len;
            ny = 1.0 / len;
            nz = -dz / len;
        }

        /// <summary>The slope of the ground at a position in degrees, 0 flat to 90 a wall.</summary>
        public double SlopeDegrees(double x, double z, double step)
        {
            double nx;
            double ny;
            double nz;
            Normal(x, z, step, out nx, out ny, out nz);
            double c = ny > 1.0 ? 1.0 : (ny < -1.0 ? -1.0 : ny);
            return Math.Acos(c) * 180.0 / Math.PI;
        }

        /// <summary>Blends the colour of the stamps that paint into a base colour at a position.</summary>
        public TerraColor ApplyPaint(double x, double z, TerraColor baseColor)
        {
            TerraColor c = baseColor;
            for (int i = 0; i < stamps.Length; i++)
            {
                TerraStamp s = stamps[i];
                if (!s.paint)
                {
                    continue;
                }
                if (x < boundMinX[i] || x > boundMaxX[i] || z < boundMinZ[i] || z > boundMaxZ[i])
                {
                    continue;
                }
                double nearX;
                double nearZ;
                double w = s.Influence(x, z, out nearX, out nearZ) * s.strength;
                double t = s.paintThreshold;
                double amount;
                if (t >= 1.0)
                {
                    amount = w >= 1.0 ? 1.0 : 0.0;
                }
                else
                {
                    amount = TerraShape.Smooth((w - t) / (1.0 - t));
                }
                if (amount > 0.0)
                {
                    c = TerraColor.Lerp(c, s.paintColor, (float)amount);
                }
            }
            return c;
        }

        /// <summary>True where a stamp that blocks scatter covers a position.</summary>
        public bool BlocksScatter(double x, double z)
        {
            for (int i = 0; i < stamps.Length; i++)
            {
                TerraStamp s = stamps[i];
                if (!s.blocksScatter)
                {
                    continue;
                }
                if (x < boundMinX[i] || x > boundMaxX[i] || z < boundMinZ[i] || z > boundMaxZ[i])
                {
                    continue;
                }
                double nearX;
                double nearZ;
                if (s.Influence(x, z, out nearX, out nearZ) * s.strength >= ScatterBlockWeight)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
