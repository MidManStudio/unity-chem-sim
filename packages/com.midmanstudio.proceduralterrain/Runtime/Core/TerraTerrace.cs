// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraTerrace.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    public enum TerraTerraceMode
    {
        /// <summary>No terraces: the ground follows the height field.</summary>
        Off = 0,
        /// <summary>The height range is cut into equal bands.</summary>
        Uniform = 1,
        /// <summary>The bands are the biomes: each biome is one flat level, and a cliff stands where one biome ends.</summary>
        Biomes = 2
    }

    /// <summary>Settings of the terraces: flat steps with vertical walls between them.</summary>
    [Serializable]
    public class TerraTerraceSettings
    {
        public TerraTerraceMode mode = TerraTerraceMode.Off;

        /// <summary>Uniform: how many bands the height range is cut into.</summary>
        public int bands = 6;

        /// <summary>
        /// 0 makes every step perfectly flat. 1 leaves the ground as it was and the walls shrink to nothing. In between, each
        /// step keeps that share of the slope it covers, so a step can lean.
        /// </summary>
        public float tilt;

        /// <summary>
        /// Flatten the lowest band too. Off by default so a lake bed or a river carved below the first level keeps its shape
        /// instead of being lifted onto a flat floor.
        /// </summary>
        public bool flattenLowest;
    }

    /// <summary>
    /// Cuts triangles at the heights where one terrace level ends and the next begins. A triangle is cut by the horizontal
    /// planes at those heights into pieces; each piece is moved to the level of its band, and a vertical wall closes the gap
    /// where two pieces of different bands meet. The cut points are found along the original triangle edges from the heights of
    /// the edge's two ends alone, taken in a fixed order, so two triangles that share an edge, in the same chunk or in two
    /// neighbouring chunks, produce the same points to the last bit and the walls meet without cracks.
    /// </summary>
    public sealed class TerraTerracer
    {
        private readonly TerraHeightField field;
        private readonly TerraColoring coloring;
        private readonly double[] edges;
        private readonly double[] floor;
        private readonly double[] tilts;
        private readonly bool enabled;

        public TerraTerracer(TerraHeightField field, TerraColoring coloring, TerraBiomeSet biomes, TerraTerraceSettings s)
        {
            this.field = field;
            this.coloring = coloring;
            if (s == null || s.mode == TerraTerraceMode.Off)
            {
                edges = new double[0];
                floor = new double[] { 0.0 };
                tilts = new double[] { 1.0 };
                enabled = false;
                return;
            }
            double[] values;
            if (s.mode == TerraTerraceMode.Biomes)
            {
                int n = biomes == null ? 0 : biomes.Count;
                values = new double[Math.Max(0, n - 1)];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = biomes.biomes[i].upTo;
                }
            }
            else
            {
                int bands = s.bands < 2 ? 2 : (s.bands > 64 ? 64 : s.bands);
                values = new double[bands - 1];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = (i + 1.0) / bands;
                }
            }
            // Keep only strictly rising edges inside (0, 1).
            double[] kept = new double[values.Length];
            int count = 0;
            double previous = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > previous + 1e-6 && values[i] < 1.0 - 1e-6)
                {
                    kept[count++] = values[i];
                    previous = values[i];
                }
            }
            edges = new double[count];
            floor = new double[count + 1];
            tilts = new double[count + 1];
            double t = s.tilt < 0.0f ? 0.0 : (s.tilt > 1.0f ? 1.0 : s.tilt);
            for (int i = 0; i < count; i++)
            {
                edges[i] = field.HeightOfValue(kept[i]);
            }
            for (int b = 0; b <= count; b++)
            {
                floor[b] = b == 0 ? field.HeightOfValue(0.0) : edges[b - 1];
                tilts[b] = (b == 0 && !s.flattenLowest) ? 1.0 : t;
            }
            enabled = count > 0;
        }

        public bool Enabled
        {
            get { return enabled; }
        }

        /// <summary>The heights, in metres, where one level ends and the next begins, rising.</summary>
        public double[] Edges
        {
            get { return edges; }
        }

        private int BandOf(double h)
        {
            int b = 0;
            while (b < edges.Length && h >= edges[b])
            {
                b++;
            }
            return b;
        }

        private double Level(int band, double h)
        {
            double t = tilts[band];
            return t >= 1.0 ? h : floor[band] + t * (h - floor[band]);
        }

        // The point on the edge between two vertices where the height is the level, from the ends in a fixed order.
        private static void Cut(double ax, double az, double ha, double bx, double bz, double hb, double level, out double x, out double z)
        {
            if (ax > bx || (ax == bx && az > bz))
            {
                double tx = ax;
                double tz = az;
                double th = ha;
                ax = bx;
                az = bz;
                ha = hb;
                bx = tx;
                bz = tz;
                hb = th;
            }
            double t = (level - ha) / (hb - ha);
            x = ax + (bx - ax) * t;
            z = az + (bz - az) * t;
        }

        /// <summary>
        /// Adds the terraced form of one triangle to the builder. The corners are true positions with their untouched heights.
        /// </summary>
        public void Add(TerraMeshBuilder builder,
            double x0, double z0, double h0, double x1, double z1, double h1, double x2, double z2, double h2)
        {
            double[] vx = { x0, x1, x2 };
            double[] vz = { z0, z1, z2 };
            double[] vh = { h0, h1, h2 };
            double hmin = Math.Min(h0, Math.Min(h1, h2));
            double hmax = Math.Max(h0, Math.Max(h1, h2));
            int bandMin = BandOf(hmin);
            int bandMax = BandOf(hmax);

            double[] px = new double[8];
            double[] pz = new double[8];
            double[] ph = new double[8];
            for (int b = bandMin; b <= bandMax; b++)
            {
                bool hasLo = b > 0;
                bool hasHi = b < edges.Length;
                double lo = hasLo ? edges[b - 1] : 0.0;
                double hi = hasHi ? edges[b] : 0.0;
                int n = 0;
                for (int e = 0; e < 3; e++)
                {
                    int f = (e + 1) % 3;
                    double ha = vh[e];
                    double hb = vh[f];
                    bool inside = (!hasLo || ha >= lo) && (!hasHi || ha <= hi);
                    if (inside)
                    {
                        px[n] = vx[e];
                        pz[n] = vz[e];
                        ph[n] = ha;
                        n++;
                    }
                    // Crossings strictly between the two ends, in the order they are met walking from e to f.
                    bool rising = hb > ha;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        bool useLo = rising ? pass == 0 : pass == 1;
                        if (useLo ? !hasLo : !hasHi)
                        {
                            continue;
                        }
                        double level = useLo ? lo : hi;
                        if (!((ha < level && level < hb) || (hb < level && level < ha)))
                        {
                            continue;
                        }
                        double cx;
                        double cz;
                        Cut(vx[e], vz[e], ha, vx[f], vz[f], hb, level, out cx, out cz);
                        px[n] = cx;
                        pz[n] = cz;
                        ph[n] = level;
                        n++;
                    }
                }
                if (n >= 3)
                {
                    EmitPolygon(builder, b, px, pz, ph, n);
                }
            }

            for (int k = 0; k < edges.Length; k++)
            {
                double level = edges[k];
                if (!(hmin < level && level < hmax))
                {
                    continue;
                }
                EmitWall(builder, k, level, vx, vz, vh);
            }
        }

        private void EmitPolygon(TerraMeshBuilder builder, int band, double[] px, double[] pz, double[] ph, int n)
        {
            double[] y = new double[n];
            double cx = 0.0;
            double cz = 0.0;
            double ch = 0.0;
            for (int i = 0; i < n; i++)
            {
                y[i] = Level(band, ph[i]);
                cx += px[i];
                cz += pz[i];
                ch += ph[i];
            }
            cx /= n;
            cz /= n;
            ch /= n;

            // A piece that only touches a level, with no area, adds nothing.
            double area2 = 0.0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area2 += (px[i] - px[0]) * (pz[j] - pz[0]) - (px[j] - px[0]) * (pz[i] - pz[0]);
            }
            if (Math.Abs(area2) < 1e-9)
            {
                return;
            }

            // The normal of the first corner pair decides the slope colour; a flat step has none.
            double nyUp = 1.0;
            if (tilts[band] > 0.0)
            {
                double ux = px[1] - px[0];
                double uy = y[1] - y[0];
                double uz = pz[1] - pz[0];
                double vx = px[2] - px[0];
                double vy = y[2] - y[0];
                double vz = pz[2] - pz[0];
                double nx = uy * vz - uz * vy;
                double ny = uz * vx - ux * vz;
                double nz = ux * vy - uy * vx;
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 1e-12)
                {
                    nyUp = Math.Abs(ny) / len;
                }
            }
            TerraColor color = coloring.Ground(cx, cz, ch, nyUp);
            for (int i = 1; i + 1 < n; i++)
            {
                builder.AddFlatTriangle(px[0], y[0], pz[0], px[i], y[i], pz[i], px[i + 1], y[i + 1], pz[i + 1], color, 0.0, 1.0, 0.0);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                builder.NoteEdge(px[i], y[i], pz[i], px[j], y[j], pz[j], color);
            }
        }

        private void EmitWall(TerraMeshBuilder builder, int k, double level, double[] vx, double[] vz, double[] vh)
        {
            double[] wx = new double[3];
            double[] wz = new double[3];
            int n = 0;
            for (int e = 0; e < 3; e++)
            {
                int f = (e + 1) % 3;
                if (vh[e] == level)
                {
                    wx[n] = vx[e];
                    wz[n] = vz[e];
                    n++;
                }
                if ((vh[e] < level && level < vh[f]) || (vh[f] < level && level < vh[e]))
                {
                    double cx;
                    double cz;
                    Cut(vx[e], vz[e], vh[e], vx[f], vz[f], vh[f], level, out cx, out cz);
                    if (n < 3)
                    {
                        wx[n] = cx;
                        wz[n] = cz;
                    }
                    n++;
                }
            }
            if (n != 2)
            {
                return;
            }
            double lower = Level(k, level);
            double upper = Level(k + 1, level);
            if (upper - lower < 1e-6)
            {
                return;
            }
            // The wall faces the way the ground falls.
            double det = (vx[1] - vx[0]) * (vz[2] - vz[0]) - (vz[1] - vz[0]) * (vx[2] - vx[0]);
            if (Math.Abs(det) < 1e-12)
            {
                return;
            }
            double gx = ((vh[1] - vh[0]) * (vz[2] - vz[0]) - (vh[2] - vh[0]) * (vz[1] - vz[0])) / det;
            double gz = ((vh[2] - vh[0]) * (vx[1] - vx[0]) - (vh[1] - vh[0]) * (vx[2] - vx[0])) / det;
            double hintX = -gx;
            double hintZ = -gz;
            TerraColor color = coloring.Wall((wx[0] + wx[1]) * 0.5, (wz[0] + wz[1]) * 0.5, level);
            builder.AddFlatTriangle(wx[0], upper, wz[0], wx[1], upper, wz[1], wx[1], lower, wz[1], color, hintX, 0.0, hintZ);
            builder.AddFlatTriangle(wx[0], upper, wz[0], wx[1], lower, wz[1], wx[0], lower, wz[0], color, hintX, 0.0, hintZ);
        }
    }
}
