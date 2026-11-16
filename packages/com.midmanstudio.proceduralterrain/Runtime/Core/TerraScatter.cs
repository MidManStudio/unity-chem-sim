// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraScatter.cs"
// ============================================================================
using System;
using System.Collections.Generic;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>One kind of thing scattered over the ground: trees, rocks, grass tufts, herbs.</summary>
    [Serializable]
    public class TerraScatterLayer
    {
        public string name = "Trees";
        public bool enabled = true;

        /// <summary>The normalised height range, 0..1, the layer grows in. The biome bands are bands of the same value.</summary>
        public float minValue = 0.30f;
        public float maxValue = 0.80f;

        /// <summary>Steeper ground than this keeps the layer off.</summary>
        public float maxSlopeDegrees = 30.0f;

        /// <summary>The share of lattice nodes (or Poisson points) that may carry one, 0..1.</summary>
        public float density = 0.5f;

        /// <summary>No two things of this layer closer than this, in metres.</summary>
        public double minDistance = 6.0;

        public float scaleMin = 0.8f;
        public float scaleMax = 1.2f;

        /// <summary>How many different prefabs or meshes the layer has. Each point gets one of them by number.</summary>
        public int variants = 1;

        /// <summary>A node closer than this to ground of another biome band is skipped, so trees do not stand on a shore line. 0 turns it off.</summary>
        public double edgeMargin = 3.0;

        /// <summary>Added to the ground height.</summary>
        public double yOffset;

        /// <summary>Tell the placer to tilt the thing to the ground normal. The normal is in every point either way.</summary>
        public bool alignToNormal;

        public bool randomYaw = true;
    }

    /// <summary>A place chosen for one scattered thing. True world coordinates.</summary>
    public struct TerraScatterPoint
    {
        public double x;
        public double y;
        public double z;
        public float yawDegrees;
        public float scale;
        public int layer;
        public int variant;
        public float normalX;
        public float normalY;
        public float normalZ;
    }

    /// <summary>
    /// Chooses where things stand. Candidates are the nodes of the mesh, so everything stands on a vertex. A candidate is kept
    /// when it passes the layer's tests and no better candidate of the same layer lies within the minimum distance, "better"
    /// being a hashed priority of the node's own index. That rule looks at nothing but the candidates near a point, so it
    /// gives the same answer whichever order or chunk it is asked in, and a chunk border cannot cut a forest in two.
    /// </summary>
    public sealed class TerraScatter
    {
        private const double SlopeStep = 1.0;

        private readonly ulong seed;
        private readonly TerraHeightField field;
        private readonly TerraBiomeSet biomes;
        private readonly TerraScatterLayer[] layers;

        public TerraScatter(int seed, TerraHeightField field, TerraBiomeSet biomes, TerraScatterLayer[] layers)
        {
            this.seed = TerraHash.SeedFrom(seed) ^ 0x9E6C63D0876A9A99UL;
            this.field = field;
            this.biomes = biomes ?? TerraBiomeSet.CreateDefault();
            this.layers = layers ?? new TerraScatterLayer[0];
        }

        /// <summary>The points of every layer that belong to one chunk of the lattice. A node belongs to the chunk that holds its index.</summary>
        public TerraScatterPoint[] ForChunk(TerraLattice lattice, long cx, long cz)
        {
            int cells = lattice.CellsPerChunk;
            long i0 = cx * cells;
            long j0 = cz * cells;
            List<TerraScatterPoint> output = new List<TerraScatterPoint>();
            for (int layer = 0; layer < layers.Length; layer++)
            {
                TerraScatterLayer l = layers[layer];
                if (l == null || !l.enabled)
                {
                    continue;
                }
                int margin = (int)Math.Ceiling(l.minDistance / lattice.Spacing + 2.0 * lattice.MaxOffset / lattice.Spacing) + 1;
                int side = cells + 2 * margin + 1;
                int total = side * side;
                double[] xs = new double[total];
                double[] zs = new double[total];
                long[] ia = new long[total];
                long[] ib = new long[total];
                bool[] owned = new bool[total];
                int n = 0;
                for (int r = -margin; r <= cells + margin; r++)
                {
                    for (int c = -margin; c <= cells + margin; c++)
                    {
                        long i = i0 + c;
                        long j = j0 + r;
                        double x;
                        double z;
                        lattice.Node(i, j, out x, out z);
                        xs[n] = x;
                        zs[n] = z;
                        ia[n] = i;
                        ib[n] = j;
                        owned[n] = c >= 0 && c < cells && r >= 0 && r < cells;
                        n++;
                    }
                }
                Select(layer, l, xs, zs, ia, ib, owned, n, output);
            }
            return output.ToArray();
        }

        /// <summary>The points of every layer over a set of free points, such as the vertices of a Poisson-disc mesh. All points are owned.</summary>
        public TerraScatterPoint[] ForPoints(double[] xs, double[] zs)
        {
            int n = xs.Length;
            long[] ia = new long[n];
            long[] ib = new long[n];
            bool[] owned = new bool[n];
            for (int i = 0; i < n; i++)
            {
                ia[i] = i;
                owned[i] = true;
            }
            List<TerraScatterPoint> output = new List<TerraScatterPoint>();
            for (int layer = 0; layer < layers.Length; layer++)
            {
                TerraScatterLayer l = layers[layer];
                if (l == null || !l.enabled)
                {
                    continue;
                }
                Select(layer, l, xs, zs, ia, ib, owned, n, output);
            }
            return output.ToArray();
        }

        private void Select(int layer, TerraScatterLayer l, double[] xs, double[] zs, long[] ia, long[] ib, bool[] owned, int n,
            List<TerraScatterPoint> output)
        {
            double[] heights = new double[n];
            double[] priority = new double[n];
            bool[] accepted = new bool[n];
            double cell = l.minDistance > 1e-3 ? l.minDistance : 1e-3;
            Dictionary<ulong, List<int>> grid = new Dictionary<ulong, List<int>>();
            for (int k = 0; k < n; k++)
            {
                if (TerraHash.ToUnit(TerraHash.Hash(seed, ia[k], ib[k], 50 + layer)) >= l.density)
                {
                    continue;
                }
                double h = field.Height(xs[k], zs[k]);
                double v = field.ValueOfHeight(h);
                if (v < l.minValue || v > l.maxValue)
                {
                    continue;
                }
                if (field.BlocksScatter(xs[k], zs[k]))
                {
                    continue;
                }
                if (field.SlopeDegrees(xs[k], zs[k], SlopeStep) > l.maxSlopeDegrees)
                {
                    continue;
                }
                if (l.edgeMargin > 0.0 && OnBiomeEdge(xs[k], zs[k], v, l.edgeMargin))
                {
                    continue;
                }
                accepted[k] = true;
                heights[k] = h;
                priority[k] = TerraHash.ToUnit(TerraHash.Hash(seed, ia[k], ib[k], 70 + layer));
                long gx = (long)Math.Floor(xs[k] / cell);
                long gz = (long)Math.Floor(zs[k] / cell);
                ulong key = TerraHash.Key(gx, gz);
                List<int> list;
                if (!grid.TryGetValue(key, out list))
                {
                    list = new List<int>();
                    grid[key] = list;
                }
                list.Add(k);
            }

            double r2 = l.minDistance * l.minDistance;
            for (int k = 0; k < n; k++)
            {
                if (!accepted[k] || !owned[k])
                {
                    continue;
                }
                long gx = (long)Math.Floor(xs[k] / cell);
                long gz = (long)Math.Floor(zs[k] / cell);
                bool beaten = false;
                for (long dz = -1; dz <= 1 && !beaten; dz++)
                {
                    for (long dx = -1; dx <= 1 && !beaten; dx++)
                    {
                        List<int> list;
                        if (!grid.TryGetValue(TerraHash.Key(gx + dx, gz + dz), out list))
                        {
                            continue;
                        }
                        for (int q = 0; q < list.Count; q++)
                        {
                            int o = list[q];
                            if (o == k)
                            {
                                continue;
                            }
                            double ex = xs[o] - xs[k];
                            double ez = zs[o] - zs[k];
                            if (ex * ex + ez * ez >= r2)
                            {
                                continue;
                            }
                            if (priority[o] > priority[k]
                                || (priority[o] == priority[k] && (ia[o] > ia[k] || (ia[o] == ia[k] && ib[o] > ib[k]))))
                            {
                                beaten = true;
                                break;
                            }
                        }
                    }
                }
                if (beaten)
                {
                    continue;
                }
                output.Add(MakePoint(layer, l, xs[k], zs[k], heights[k], ia[k], ib[k]));
            }
        }

        private bool OnBiomeEdge(double x, double z, double value, double margin)
        {
            int band = biomes.IndexAt(value);
            for (int s = 0; s < 4; s++)
            {
                double px = x + (s == 0 ? margin : (s == 1 ? -margin : 0.0));
                double pz = z + (s == 2 ? margin : (s == 3 ? -margin : 0.0));
                if (biomes.IndexAt(field.ValueOfHeight(field.Height(px, pz))) != band)
                {
                    return true;
                }
            }
            return false;
        }

        private TerraScatterPoint MakePoint(int layer, TerraScatterLayer l, double x, double z, double h, long a, long b)
        {
            double nx;
            double ny;
            double nz;
            field.Normal(x, z, SlopeStep, out nx, out ny, out nz);
            TerraScatterPoint p = new TerraScatterPoint();
            p.x = x;
            p.y = h + l.yOffset;
            p.z = z;
            p.yawDegrees = l.randomYaw ? (float)(TerraHash.ToUnit(TerraHash.Hash(seed, a, b, 90 + layer)) * 360.0) : 0.0f;
            double t = TerraHash.ToUnit(TerraHash.Hash(seed, a, b, 110 + layer));
            p.scale = (float)(l.scaleMin + (l.scaleMax - l.scaleMin) * t);
            int variants = l.variants < 1 ? 1 : l.variants;
            int variant = (int)(TerraHash.ToUnit(TerraHash.Hash(seed, a, b, 130 + layer)) * variants);
            p.variant = variant >= variants ? variants - 1 : variant;
            p.layer = layer;
            p.normalX = (float)nx;
            p.normalY = (float)ny;
            p.normalZ = (float)nz;
            return p;
        }
    }
}
