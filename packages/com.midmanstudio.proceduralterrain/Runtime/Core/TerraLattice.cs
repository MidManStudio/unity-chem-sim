// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraLattice.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>Settings of the jittered lattice that chunked terrain is built on.</summary>
    [Serializable]
    public class TerraLatticeSettings
    {
        /// <summary>The distance between lattice nodes in metres at full detail.</summary>
        public double spacing = 4.0;

        /// <summary>
        /// How many lattice cells make one chunk along each axis. Must be a multiple of the largest LOD step you use. A chunk
        /// is spacing * cellsPerChunk metres wide: 64 cells of 4 m is a 256 m chunk.
        /// </summary>
        public int cellsPerChunk = 64;

        /// <summary>
        /// How far a node is moved from its lattice point, as a fraction of the spacing, in each axis. 0 is a regular grid. Up to
        /// 0.25 no triangle can fold over. Larger values look wilder and can fold a few triangles over, which still render.
        /// </summary>
        public double jitter = 0.25;

        /// <summary>The width of a chunk in metres.</summary>
        public double ChunkSize
        {
            get { return spacing * cellsPerChunk; }
        }
    }

    /// <summary>
    /// A square lattice of nodes in true coordinates with a hashed offset on each node. The position of a node is a pure
    /// function of its global index, so a node is in the same place whichever chunk asks for it. Nodes on a chunk line (an index
    /// that is a multiple of the cells per chunk) are not moved across the line, so a chunk is exactly a rectangle and the
    /// border vertices of two neighbours are the same points.
    /// </summary>
    public sealed class TerraLattice
    {
        public const double MaxJitter = 0.45;

        private readonly ulong seed;
        private readonly double spacing;
        private readonly int cells;
        private readonly double jitter;

        public TerraLattice(int seed, TerraLatticeSettings s)
        {
            if (s == null)
            {
                s = new TerraLatticeSettings();
            }
            this.seed = TerraHash.SeedFrom(seed) ^ 0x5851F42D4C957F2DUL;
            spacing = s.spacing > 1e-3 ? s.spacing : 1e-3;
            cells = s.cellsPerChunk < 1 ? 1 : s.cellsPerChunk;
            double j = s.jitter < 0.0 ? 0.0 : (s.jitter > MaxJitter ? MaxJitter : s.jitter);
            jitter = j;
        }

        public double Spacing
        {
            get { return spacing; }
        }

        public int CellsPerChunk
        {
            get { return cells; }
        }

        public double ChunkSize
        {
            get { return spacing * cells; }
        }

        /// <summary>The largest distance a node can sit from its lattice point, in metres.</summary>
        public double MaxOffset
        {
            get { return jitter * spacing; }
        }

        private static bool OnChunkLine(long index, int cells)
        {
            long m = index % cells;
            return m == 0;
        }

        /// <summary>The true position of a node.</summary>
        public void Node(long i, long j, out double x, out double z)
        {
            x = i * spacing;
            z = j * spacing;
            if (jitter <= 0.0)
            {
                return;
            }
            if (!OnChunkLine(i, cells))
            {
                x += TerraHash.ToSigned(TerraHash.Hash(seed, i, j, 11)) * jitter * spacing;
            }
            if (!OnChunkLine(j, cells))
            {
                z += TerraHash.ToSigned(TerraHash.Hash(seed, i, j, 12)) * jitter * spacing;
            }
        }

        /// <summary>The south west corner of a chunk in true metres.</summary>
        public void ChunkOrigin(long cx, long cz, out double x, out double z)
        {
            x = (cx * cells) * spacing;
            z = (cz * cells) * spacing;
        }

        /// <summary>The chunk that contains a true position.</summary>
        public void ChunkOf(double x, double z, out long cx, out long cz)
        {
            double size = ChunkSize;
            cx = (long)Math.Floor(x / size);
            cz = (long)Math.Floor(z / size);
        }

        /// <summary>
        /// The triangulation of one chunk at a level of detail. The step is how many lattice cells make one cell of this chunk's
        /// mesh: 1 is full detail, 2 uses every second node, and so on. It must divide the cells per chunk. A node of a coarse
        /// mesh is the node of the fine mesh at the same index, so a coarse border vertex sits exactly on a fine one.
        /// </summary>
        public TerraTriangulation BuildChunk(long cx, long cz, int step)
        {
            if (step < 1)
            {
                step = 1;
            }
            if (cells % step != 0)
            {
                throw new ArgumentException("the LOD step " + step + " does not divide the " + cells + " cells of a chunk");
            }
            int n = cells / step;
            int side = n + 1;
            long i0 = cx * cells;
            long j0 = cz * cells;
            double[] xs = new double[side * side];
            double[] zs = new double[side * side];
            for (int r = 0; r < side; r++)
            {
                for (int c = 0; c < side; c++)
                {
                    double x;
                    double z;
                    Node(i0 + (long)c * step, j0 + (long)r * step, out x, out z);
                    xs[r * side + c] = x;
                    zs[r * side + c] = z;
                }
            }
            int[] tris = new int[n * n * 6];
            int t = 0;
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int a = r * side + c;
                    int b = a + 1;
                    int cc = a + side;
                    int d = cc + 1;
                    ulong h = TerraHash.Hash(seed, i0 + (long)c * step, j0 + (long)r * step, 20 + step);
                    if ((h & 1UL) == 0UL)
                    {
                        // The diagonal from a to d.
                        tris[t++] = a;
                        tris[t++] = b;
                        tris[t++] = d;
                        tris[t++] = a;
                        tris[t++] = d;
                        tris[t++] = cc;
                    }
                    else
                    {
                        // The diagonal from b to c.
                        tris[t++] = a;
                        tris[t++] = b;
                        tris[t++] = cc;
                        tris[t++] = b;
                        tris[t++] = d;
                        tris[t++] = cc;
                    }
                }
            }
            return new TerraTriangulation(xs, zs, tris);
        }
    }
}
