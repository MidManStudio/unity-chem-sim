// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraWorld.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    public enum TerraShading
    {
        /// <summary>Each triangle has its own colour and normal: the faceted low-poly look. The only choice with terraces.</summary>
        Flat = 0,
        /// <summary>Vertices are shared and normals come from the height field: soft, with the colour taken at each vertex.</summary>
        Smooth = 1
    }

    /// <summary>The rectangle and the point spacing of a single-mesh island built from Poisson-disc points.</summary>
    [Serializable]
    public class TerraIslandSettings
    {
        public double minX = -512.0;
        public double minZ = -512.0;
        public double maxX = 512.0;
        public double maxZ = 512.0;

        /// <summary>The least distance between two points, in metres.</summary>
        public double radius = 6.0;

        /// <summary>How many tries each point gets to place a neighbour. More is denser and slower. 30 is the usual number.</summary>
        public int attempts = 30;
    }

    /// <summary>How the level of detail of a chunk follows its distance.</summary>
    [Serializable]
    public class TerraLodSettings
    {
        /// <summary>
        /// Distances in metres. A chunk nearer than the first uses every lattice node, one nearer than the second every second
        /// node, then every fourth, and so on. Past the last, every node of the next power of two.
        /// </summary>
        public double[] distances = { 250.0, 600.0, 1400.0 };
    }

    /// <summary>All the settings of a terrain world.</summary>
    [Serializable]
    public class TerraWorldSettings
    {
        public int seed = 12345;
        public TerraHeightSettings height = new TerraHeightSettings();
        public TerraBiomeSet biomes = TerraBiomeSet.CreateDefault();
        public TerraLatticeSettings lattice = new TerraLatticeSettings();
        public TerraIslandSettings island = new TerraIslandSettings();
        public TerraTerraceSettings terrace = new TerraTerraceSettings();
        public TerraShading shading = TerraShading.Flat;
        public TerraLodSettings lod = new TerraLodSettings();

        /// <summary>How deep the wall under the edge of a chunk or an island goes, in metres. 0 turns skirts off.</summary>
        public double skirtDepth = 3.0;

        /// <summary>The height of the water surface in metres, for whatever draws water. The ground itself is not touched.</summary>
        public double waterLevel;

        public TerraScatterLayer[] scatter = new TerraScatterLayer[0];
    }

    /// <summary>A finished chunk or island.</summary>
    public sealed class TerraChunkResult
    {
        public TerraMeshData mesh;
        public long chunkX;
        public long chunkZ;
        public int step;

        /// <summary>The true position of the south west corner of a chunk, or of the centre of an island, in metres.</summary>
        public double originX;
        public double originZ;

        /// <summary>The true extent of the mesh in metres.</summary>
        public double minX;
        public double minZ;
        public double maxX;
        public double maxZ;

        /// <summary>Set for an island. A chunk's scatter is asked for separately with <see cref="TerraWorld.ScatterChunk"/>.</summary>
        public TerraScatterPoint[] scatter;
    }

    /// <summary>
    /// A terrain world compiled from its settings: the height field, the lattice, the colouring and the scatter, and the
    /// calls that build a chunk of the endless lattice world or a single Poisson-disc island. Everything here is a pure
    /// function of the settings and the arguments, so any call can run on any thread in any order, and building the same
    /// chunk twice gives the same mesh vertex for vertex.
    /// </summary>
    public sealed class TerraWorld
    {
        private readonly TerraWorldSettings settings;
        private readonly TerraHeightField field;
        private readonly TerraLattice lattice;
        private readonly TerraColoring coloring;
        private readonly TerraTerracer terracer;
        private readonly TerraScatter scatter;

        public TerraWorld(TerraWorldSettings s)
        {
            if (s == null)
            {
                s = new TerraWorldSettings();
            }
            settings = s;
            if (s.biomes == null)
            {
                s.biomes = TerraBiomeSet.CreateDefault();
            }
            field = new TerraHeightField(s.seed, s.height);
            lattice = new TerraLattice(s.seed, s.lattice);
            coloring = new TerraColoring(s.seed, field, s.biomes);
            terracer = new TerraTerracer(field, coloring, s.biomes, s.terrace);
            scatter = new TerraScatter(s.seed, field, s.biomes, s.scatter);
        }

        public TerraWorldSettings Settings
        {
            get { return settings; }
        }

        public TerraHeightField Field
        {
            get { return field; }
        }

        public TerraLattice Lattice
        {
            get { return lattice; }
        }

        public TerraColoring Coloring
        {
            get { return coloring; }
        }

        public TerraTerracer Terracer
        {
            get { return terracer; }
        }

        /// <summary>The height of the ground in metres at a true position.</summary>
        public double HeightAt(double x, double z)
        {
            return field.Height(x, z);
        }

        /// <summary>The LOD step for a chunk a distance away, in metres: the largest power of two the tiers ask for that divides the cells per chunk.</summary>
        public int StepForDistance(double distance)
        {
            double[] d = settings.lod == null ? null : settings.lod.distances;
            int tier = 0;
            if (d != null)
            {
                for (int i = 0; i < d.Length; i++)
                {
                    if (distance >= d[i])
                    {
                        tier++;
                    }
                }
            }
            int step = 1 << Math.Min(tier, 12);
            while (step > 1 && lattice.CellsPerChunk % step != 0)
            {
                step >>= 1;
            }
            return step;
        }

        /// <summary>Builds one chunk of the lattice world at a level of detail (1 is full detail; it must divide the cells per chunk).</summary>
        public TerraChunkResult BuildChunk(long cx, long cz, int step)
        {
            TerraTriangulation tri = lattice.BuildChunk(cx, cz, step);
            double ox;
            double oz;
            lattice.ChunkOrigin(cx, cz, out ox, out oz);
            double size = lattice.ChunkSize;
            double x1;
            double z1;
            lattice.ChunkOrigin(cx + 1, cz + 1, out x1, out z1);
            TerraChunkResult r = new TerraChunkResult();
            r.chunkX = cx;
            r.chunkZ = cz;
            r.step = step;
            r.originX = ox;
            r.originZ = oz;
            r.minX = ox;
            r.minZ = oz;
            r.maxX = x1;
            r.maxZ = z1;
            r.mesh = BuildMesh(tri, ox, oz, ox, x1, oz, z1, lattice.Spacing * step * 0.5);
            return r;
        }

        /// <summary>The scatter points of every layer that belong to one chunk. They do not depend on the LOD.</summary>
        public TerraScatterPoint[] ScatterChunk(long cx, long cz)
        {
            return scatter.ForChunk(lattice, cx, cz);
        }

        /// <summary>Builds the whole island as one mesh from Poisson-disc points and a Delaunay triangulation, with its scatter.</summary>
        public TerraChunkResult BuildIsland()
        {
            TerraIslandSettings isl = settings.island ?? new TerraIslandSettings();
            double[] xs;
            double[] zs;
            ulong poissonSeed = TerraHash.SeedFrom(settings.seed) ^ 0x1D8E4E27C47D124FUL;
            TerraPoisson.Generate(poissonSeed, isl.minX, isl.minZ, isl.maxX, isl.maxZ, isl.radius, isl.attempts, out xs, out zs);
            int[] tris = TerraDelaunay.Triangulate(xs, zs);
            TerraTriangulation tri = new TerraTriangulation(xs, zs, tris);
            double ox = (isl.minX + isl.maxX) * 0.5;
            double oz = (isl.minZ + isl.maxZ) * 0.5;
            TerraChunkResult r = new TerraChunkResult();
            r.originX = ox;
            r.originZ = oz;
            r.minX = isl.minX;
            r.minZ = isl.minZ;
            r.maxX = isl.maxX;
            r.maxZ = isl.maxZ;
            r.step = 1;
            r.mesh = BuildMesh(tri, ox, oz, isl.minX, isl.maxX, isl.minZ, isl.maxZ, Math.Max(0.5, isl.radius * 0.5));
            r.scatter = scatter.ForPoints(xs, zs);
            return r;
        }

        private TerraMeshData BuildMesh(TerraTriangulation tri, double ox, double oz,
            double borderMinX, double borderMaxX, double borderMinZ, double borderMaxZ, double normalStep)
        {
            int vn = tri.VertexCount;
            double[] h = new double[vn];
            for (int i = 0; i < vn; i++)
            {
                h[i] = field.Height(tri.x[i], tri.z[i]);
            }
            bool smooth = settings.shading == TerraShading.Smooth && !terracer.Enabled;
            int tn = tri.TriangleCount;
            TerraMeshBuilder b = new TerraMeshBuilder(ox, oz, smooth ? vn : tn * 3);
            b.SetBorder(borderMinX, borderMaxX, borderMinZ, borderMaxZ);
            int[] t = tri.triangles;

            if (terracer.Enabled)
            {
                for (int k = 0; k < tn; k++)
                {
                    int a = t[k * 3];
                    int bb = t[k * 3 + 1];
                    int c = t[k * 3 + 2];
                    terracer.Add(b,
                        tri.x[a], tri.z[a], h[a],
                        tri.x[bb], tri.z[bb], h[bb],
                        tri.x[c], tri.z[c], h[c]);
                }
            }
            else if (smooth)
            {
                int[] index = new int[vn];
                TerraColor[] color = new TerraColor[vn];
                for (int i = 0; i < vn; i++)
                {
                    double nx;
                    double ny;
                    double nz;
                    field.Normal(tri.x[i], tri.z[i], normalStep, out nx, out ny, out nz);
                    color[i] = coloring.Ground(tri.x[i], tri.z[i], h[i], ny);
                    index[i] = b.AddVertex(tri.x[i], h[i], tri.z[i], nx, ny, nz, color[i]);
                }
                for (int k = 0; k < tn; k++)
                {
                    int a = t[k * 3];
                    int bb = t[k * 3 + 1];
                    int c = t[k * 3 + 2];
                    // The triangulation runs counter-clockwise in (x, z); Unity wants the other way round.
                    b.AddIndexed(index[a], index[c], index[bb]);
                    b.NoteEdge(tri.x[a], h[a], tri.z[a], tri.x[bb], h[bb], tri.z[bb], color[a]);
                    b.NoteEdge(tri.x[bb], h[bb], tri.z[bb], tri.x[c], h[c], tri.z[c], color[bb]);
                    b.NoteEdge(tri.x[c], h[c], tri.z[c], tri.x[a], h[a], tri.z[a], color[c]);
                }
            }
            else
            {
                for (int k = 0; k < tn; k++)
                {
                    int a = t[k * 3];
                    int bb = t[k * 3 + 1];
                    int c = t[k * 3 + 2];
                    double ux = tri.x[bb] - tri.x[a];
                    double uy = h[bb] - h[a];
                    double uz = tri.z[bb] - tri.z[a];
                    double vx = tri.x[c] - tri.x[a];
                    double vy = h[c] - h[a];
                    double vz = tri.z[c] - tri.z[a];
                    double nx = uy * vz - uz * vy;
                    double ny = uz * vx - ux * vz;
                    double nz = ux * vy - uy * vx;
                    double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    double up = len > 1e-12 ? Math.Abs(ny) / len : 1.0;
                    double cx = (tri.x[a] + tri.x[bb] + tri.x[c]) / 3.0;
                    double cz = (tri.z[a] + tri.z[bb] + tri.z[c]) / 3.0;
                    double ch = (h[a] + h[bb] + h[c]) / 3.0;
                    TerraColor color = coloring.Ground(cx, cz, ch, up);
                    b.AddFlatTriangle(
                        tri.x[a], h[a], tri.z[a], tri.x[bb], h[bb], tri.z[bb], tri.x[c], h[c], tri.z[c],
                        color, 0.0, 1.0, 0.0);
                    b.NoteEdge(tri.x[a], h[a], tri.z[a], tri.x[bb], h[bb], tri.z[bb], color);
                    b.NoteEdge(tri.x[bb], h[bb], tri.z[bb], tri.x[c], h[c], tri.z[c], color);
                    b.NoteEdge(tri.x[c], h[c], tri.z[c], tri.x[a], h[a], tri.z[a], color);
                }
            }
            b.AddSkirts(settings.skirtDepth);
            return b.ToMeshData();
        }
    }
}
