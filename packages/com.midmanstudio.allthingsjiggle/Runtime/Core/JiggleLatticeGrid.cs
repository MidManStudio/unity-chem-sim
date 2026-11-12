// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleLatticeGrid.cs"
// ============================================================================
namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// The tree a lattice is simulated as. The control point nearest the middle of the grid is the anchor of the rig, particle 0,
    /// which is kinematic and so never moves from its pose. Every other control point is a particle whose parent is a grid
    /// neighbour one step nearer the anchor. Particles are numbered in the order they are reached from the anchor, so every
    /// parent has a lower index than its children, which is what the solver requires. The anchor is a control point itself
    /// and not a separate particle next to it, because a particle at the same place as its parent has no direction to
    /// measure a swing from, and the solver would turn all its children by a swing that is not there.
    /// </summary>
    public sealed class JiggleLatticeTree
    {
        /// <summary>Control points along x.</summary>
        public readonly int Nx;
        /// <summary>Control points along y.</summary>
        public readonly int Ny;
        /// <summary>Control points along z.</summary>
        public readonly int Nz;
        /// <summary>Number of control points, which is also the number of particles of the rig, the anchor included.</summary>
        public readonly int Count;
        /// <summary>The grid point at the middle. It is the anchor, particle 0, and it never moves from its pose.</summary>
        public int RootPoint;
        /// <summary>Per particle, the grid point it stands for. Entry 0 is the anchor and holds <see cref="RootPoint"/>.</summary>
        public readonly int[] PointOfParticle;
        /// <summary>Per grid point, the particle that simulates it. The grid index is x + nx * (y + ny * z).</summary>
        public readonly int[] ParticleOfPoint;
        /// <summary>Per particle, the index of its parent particle. Entry 0 is the anchor and holds -1.</summary>
        public readonly int[] Parent;

        internal JiggleLatticeTree(int nx, int ny, int nz)
        {
            Nx = nx;
            Ny = ny;
            Nz = nz;
            Count = nx * ny * nz;
            PointOfParticle = new int[Count];
            ParticleOfPoint = new int[Count];
            Parent = new int[Count];
        }

        /// <summary>
        /// The parent of each particle after the anchor, Count - 1 entries, as <see cref="JiggleWorld.AddTree"/> takes them:
        /// entry i belongs to particle i + 1 and is at most i.
        /// </summary>
        public int[] ParentsOfParticles()
        {
            int[] p = new int[Count > 0 ? Count - 1 : 0];
            for (int i = 0; i < p.Length; i++)
            {
                p[i] = Parent[i + 1];
            }
            return p;
        }
    }

    /// <summary>
    /// The control grid of a lattice: how many points it has, where they sit in a box, and the tree they are
    /// simulated as. A grid has 1 to 5 points on each axis, at most 125 points, and one of them is the anchor, so it
    /// fits the 128 particles of a rig. A lattice needs at least two points to move at all. The points sit evenly from one side of the box to the other, which is what makes the
    /// deformation reproduce any straight stretch or turn exactly. An axis with one point puts it in the middle of the box.
    /// </summary>
    public static class JiggleLatticeGrid
    {
        /// <summary>Fewest control points on an axis.</summary>
        public const int MinDivisions = 1;
        /// <summary>Most control points on an axis.</summary>
        public const int MaxDivisions = 5;
        /// <summary>Most control points of one lattice.</summary>
        public const int MaxPoints = MaxDivisions * MaxDivisions * MaxDivisions;
        /// <summary>Fewest control points of a lattice that can move: one is the anchor and one is left to simulate.</summary>
        public const int MinPoints = 2;

        /// <summary>The number of points on an axis, kept between <see cref="MinDivisions"/> and <see cref="MaxDivisions"/>.</summary>
        public static int ClampDivisions(int d)
        {
            if (d < MinDivisions)
            {
                return MinDivisions;
            }
            return d > MaxDivisions ? MaxDivisions : d;
        }

        /// <summary>The number of control points of a grid, with every axis clamped.</summary>
        public static int PointCount(int nx, int ny, int nz)
        {
            return ClampDivisions(nx) * ClampDivisions(ny) * ClampDivisions(nz);
        }

        /// <summary>The index of grid point (i, j, k): i runs fastest, then j, then k.</summary>
        public static int PointIndex(int i, int j, int k, int nx, int ny)
        {
            return i + nx * (j + ny * k);
        }

        /// <summary>The position of point <paramref name="index"/> of <paramref name="count"/> on one axis of a box that starts at <paramref name="min"/>.</summary>
        public static float AxisPosition(int index, int count, float min, float size)
        {
            if (count <= 1)
            {
                return min + size * 0.5f;
            }
            return min + size * ((float)index / (float)(count - 1));
        }

        /// <summary>The rest position of grid point (i, j, k) in a box given by its smallest corner and its size.</summary>
        public static JVec3 PointRest(int i, int j, int k, int nx, int ny, int nz, JVec3 min, JVec3 size)
        {
            return new JVec3(
                AxisPosition(i, nx, min.X, size.X),
                AxisPosition(j, ny, min.Y, size.Y),
                AxisPosition(k, nz, min.Z, size.Z));
        }

        /// <summary>
        /// The rest positions of the control points in particle order for <see cref="JiggleWorld.AddTree"/>: entry p is the grid
        /// point of particle p of <paramref name="tree"/>, so entry 0, the anchor, is the point at the middle.
        /// </summary>
        public static JVec3[] ParticleRest(JiggleLatticeTree tree, JVec3 min, JVec3 size)
        {
            JVec3[] rest = new JVec3[tree.Count];
            int nx = tree.Nx;
            int ny = tree.Ny;
            int nz = tree.Nz;
            for (int p = 0; p < tree.Count; p++)
            {
                int point = tree.PointOfParticle[p];
                int i = point % nx;
                int j = (point / nx) % ny;
                int k = point / (nx * ny);
                rest[p] = PointRest(i, j, k, nx, ny, nz, min, size);
            }
            return rest;
        }

        /// <summary>The grid point nearest the middle of the grid, the lowest index when several are equally near.</summary>
        public static int MiddlePoint(int nx, int ny, int nz)
        {
            nx = ClampDivisions(nx);
            ny = ClampDivisions(ny);
            nz = ClampDivisions(nz);
            float cx = (float)(nx - 1) * 0.5f;
            float cy = (float)(ny - 1) * 0.5f;
            float cz = (float)(nz - 1) * 0.5f;
            int best = 0;
            float bestD = float.MaxValue;
            for (int k = 0; k < nz; k++)
            {
                for (int j = 0; j < ny; j++)
                {
                    for (int i = 0; i < nx; i++)
                    {
                        float dx = (float)i - cx;
                        float dy = (float)j - cy;
                        float dz = (float)k - cz;
                        float d = dx * dx + dy * dy + dz * dz;
                        if (d < bestD)
                        {
                            bestD = d;
                            best = PointIndex(i, j, k, nx, ny);
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// The grid point nearest to <paramref name="position"/>, in the space the box is given in, the lowest index when several
        /// are equally near. A lattice uses it to hang the rig on the control point nearest where the object is held.
        /// </summary>
        public static int NearestPoint(int nx, int ny, int nz, JVec3 min, JVec3 size, JVec3 position)
        {
            nx = ClampDivisions(nx);
            ny = ClampDivisions(ny);
            nz = ClampDivisions(nz);
            int best = 0;
            float bestD = float.MaxValue;
            for (int k = 0; k < nz; k++)
            {
                for (int j = 0; j < ny; j++)
                {
                    for (int i = 0; i < nx; i++)
                    {
                        float d = (PointRest(i, j, k, nx, ny, nz, min, size) - position).LengthSq();
                        if (d < bestD)
                        {
                            bestD = d;
                            best = PointIndex(i, j, k, nx, ny);
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Builds the tree of a grid. The root, which becomes the anchor of the rig, is <paramref name="rootPoint"/>, or the point
        /// nearest the middle of the grid, the lowest index when several are equally near, when that is not a point of the grid.
        /// The tree grows from the root one neighbour at a time (x, then y, then z, each upward first), so a point's parent is
        /// always a grid neighbour one step nearer the root and the result is the same every time.
        /// </summary>
        public static JiggleLatticeTree BuildTree(int nx, int ny, int nz, int rootPoint = -1)
        {
            nx = ClampDivisions(nx);
            ny = ClampDivisions(ny);
            nz = ClampDivisions(nz);
            JiggleLatticeTree tree = new JiggleLatticeTree(nx, ny, nz);
            int count = tree.Count;

            int root = rootPoint;
            if (root < 0 || root >= count)
            {
                root = MiddlePoint(nx, ny, nz);
            }
            tree.RootPoint = root;

            for (int i = 0; i < count; i++)
            {
                tree.ParticleOfPoint[i] = -1;
            }
            tree.PointOfParticle[0] = root;
            tree.ParticleOfPoint[root] = 0;
            tree.Parent[0] = -1;

            // Breadth first from the root. A point gets its particle index when it is reached, and every point that
            // is reached later gets a higher one, so the parent of a particle is always a lower particle.
            int[] queue = new int[count];
            int head = 0;
            int tail = 0;
            queue[tail++] = root;
            int next = 1;
            while (head < tail)
            {
                int point = queue[head++];
                int particle = tree.ParticleOfPoint[point];
                int pi = point % nx;
                int pj = (point / nx) % ny;
                int pk = point / (nx * ny);
                for (int dir = 0; dir < 6; dir++)
                {
                    int i = pi;
                    int j = pj;
                    int k = pk;
                    switch (dir)
                    {
                        case 0: i++; break;
                        case 1: i--; break;
                        case 2: j++; break;
                        case 3: j--; break;
                        case 4: k++; break;
                        default: k--; break;
                    }
                    if (i < 0 || i >= nx || j < 0 || j >= ny || k < 0 || k >= nz)
                    {
                        continue;
                    }
                    int other = PointIndex(i, j, k, nx, ny);
                    if (tree.ParticleOfPoint[other] >= 0)
                    {
                        continue;
                    }
                    tree.ParticleOfPoint[other] = next;
                    tree.PointOfParticle[next] = other;
                    tree.Parent[next] = particle;
                    next++;
                    queue[tail++] = other;
                }
            }
            return tree;
        }
    }
}
