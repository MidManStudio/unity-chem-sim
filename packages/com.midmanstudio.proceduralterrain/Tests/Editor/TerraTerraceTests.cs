// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/Editor/TerraTerraceTests.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MidManStudio.ProceduralTerrain.Tests
{
    [TestFixture]
    public class TerraTerraceTests
    {
        private const double ChunkSide = 128.0;

        private static TerraWorldSettings Terraced(int bands, float tilt, bool flattenLowest)
        {
            TerraWorldSettings s = new TerraWorldSettings();
            s.seed = 77;
            s.height.noise.scale = 130.0;
            s.height.noise.remapMin = 0.29;
            s.height.noise.remapMax = 0.71;
            s.height.baseHeight = 0.0;
            s.height.heightScale = 100.0;
            s.lattice.spacing = 4.0;
            s.lattice.cellsPerChunk = 32;
            s.terrace.mode = TerraTerraceMode.Uniform;
            s.terrace.bands = bands;
            s.terrace.tilt = tilt;
            s.terrace.flattenLowest = flattenLowest;
            s.skirtDepth = 3.0;
            return s;
        }

        private static bool OnBorder(TerraMeshData m, int v)
        {
            float x = m.positions[v * 3];
            float z = m.positions[v * 3 + 2];
            return Math.Abs(x) < 1e-4f || Math.Abs(x - (float)ChunkSide) < 1e-4f || Math.Abs(z) < 1e-4f || Math.Abs(z - (float)ChunkSide) < 1e-4f;
        }

        private static void Classify(TerraMeshData m, out List<int> tops, out List<int> walls, out List<int> skirts)
        {
            tops = new List<int>();
            walls = new List<int>();
            skirts = new List<int>();
            for (int t = 0; t < m.indices.Length; t += 3)
            {
                int a = m.indices[t];
                if (m.normals[a * 3 + 1] > 1e-4f)
                {
                    tops.Add(t);
                }
                else if (OnBorder(m, a) && OnBorder(m, m.indices[t + 1]) && OnBorder(m, m.indices[t + 2]))
                {
                    skirts.Add(t);
                }
                else
                {
                    walls.Add(t);
                }
            }
        }

        private static double ProjectedArea(TerraMeshData m, List<int> tris)
        {
            double area = 0.0;
            foreach (int t in tris)
            {
                int a = m.indices[t];
                int b = m.indices[t + 1];
                int c = m.indices[t + 2];
                area += Math.Abs((m.positions[b * 3] - m.positions[a * 3]) * (double)(m.positions[c * 3 + 2] - m.positions[a * 3 + 2])
                    - (m.positions[b * 3 + 2] - m.positions[a * 3 + 2]) * (double)(m.positions[c * 3] - m.positions[a * 3])) * 0.5;
            }
            return area;
        }

        [Test]
        public void FlatStepsSitOnLevelsAndWallsAreVertical()
        {
            TerraWorld w = new TerraWorld(Terraced(5, 0.0f, true));
            Assert.IsTrue(w.Terracer.Enabled);
            Assert.AreEqual(4, w.Terracer.Edges.Length);
            TerraMeshData m = w.BuildChunk(1, 1, 1).mesh;
            List<int> tops;
            List<int> walls;
            List<int> skirts;
            Classify(m, out tops, out walls, out skirts);
            Assert.Greater(tops.Count, 100);
            Assert.Greater(walls.Count, 20, "a hilly chunk with five bands has cliffs");
            double[] levels = { 0.0, 20.0, 40.0, 60.0, 80.0 };
            foreach (int t in tops)
            {
                for (int k = 0; k < 3; k++)
                {
                    float y = m.positions[m.indices[t + k] * 3 + 1];
                    bool onLevel = false;
                    foreach (double l in levels)
                    {
                        onLevel |= Math.Abs(y - l) < 1e-3;
                    }
                    Assert.IsTrue(onLevel, "a flat step vertex at height " + y);
                }
            }
            foreach (int t in walls)
            {
                for (int k = 0; k < 3; k++)
                {
                    int v = m.indices[t + k];
                    Assert.AreEqual(0.0f, m.normals[v * 3 + 1], 1e-6f, "wall normals are horizontal");
                }
                float y0 = m.positions[m.indices[t] * 3 + 1];
                float y1 = m.positions[m.indices[t + 1] * 3 + 1];
                float y2 = m.positions[m.indices[t + 2] * 3 + 1];
                float lo = Math.Min(y0, Math.Min(y1, y2));
                float hi = Math.Max(y0, Math.Max(y1, y2));
                Assert.AreEqual(20.0f, Math.Round((hi - lo) / 20.0f) * 20.0f, 1e-3f, "a wall spans whole steps");
            }
        }

        [Test]
        public void StepsTileTheChunkWithoutGapsOrOverlap()
        {
            foreach (int bands in new[] { 2, 5, 9 })
            {
                TerraWorld w = new TerraWorld(Terraced(bands, 0.0f, true));
                TerraMeshData m = w.BuildChunk(-2, 3, 1).mesh;
                List<int> tops;
                List<int> walls;
                List<int> skirts;
                Classify(m, out tops, out walls, out skirts);
                Assert.AreEqual(ChunkSide * ChunkSide, ProjectedArea(m, tops), 1e-2, bands + " bands: the steps cover the chunk once");
                Assert.AreEqual(0.0, ProjectedArea(m, walls), 1e-4, "walls have no footprint");
            }
        }

        [Test]
        public void TerracedChunksMeetWithoutACrack()
        {
            TerraWorld w = new TerraWorld(Terraced(6, 0.0f, true));
            TerraMeshData centre = w.BuildChunk(4, 4, 1).mesh;
            TerraMeshData east = w.BuildChunk(5, 4, 1).mesh;
            TerraMeshData north = w.BuildChunk(4, 5, 1).mesh;
            AssertSides(centre, east, 0);
            AssertSides(north, centre, 2);
        }

        // The lower chunk's maximum side against the upper chunk's minimum side along the given axis.
        private static void AssertSides(TerraMeshData first, TerraMeshData second, int axis)
        {
            // axis 0: first is west, second is east. axis 2: first is north, second is south of it.
            TerraMeshData lower = axis == 0 ? first : second;
            TerraMeshData upper = axis == 0 ? second : first;
            List<string> a = SideSet(lower, axis, true);
            List<string> b = SideSet(upper, axis, false);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i], b[i]);
            }
        }

        private static List<string> SideSet(TerraMeshData m, int axis, bool atMax)
        {
            float edge = atMax ? (float)ChunkSide : 0.0f;
            int along = axis == 0 ? 2 : 0;
            HashSet<string> set = new HashSet<string>();
            for (int i = 0; i < m.vertexCount; i++)
            {
                if (Math.Abs(m.positions[i * 3 + (axis == 0 ? 0 : 2)] - edge) < 1e-4f)
                {
                    set.Add(m.positions[i * 3 + along].ToString("F3") + "," + m.positions[i * 3 + 1].ToString("F3"));
                }
            }
            List<string> list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        [Test]
        public void TerracedMeshHasTheRightWinding()
        {
            TerraWorld w = new TerraWorld(Terraced(6, 0.3f, false));
            TerraMeshData m = w.BuildChunk(0, 0, 1).mesh;
            int bad = 0;
            for (int t = 0; t < m.indices.Length; t += 3)
            {
                int a = m.indices[t];
                int b = m.indices[t + 1];
                int c = m.indices[t + 2];
                double ux = m.positions[b * 3] - m.positions[a * 3];
                double uy = m.positions[b * 3 + 1] - m.positions[a * 3 + 1];
                double uz = m.positions[b * 3 + 2] - m.positions[a * 3 + 2];
                double vx = m.positions[c * 3] - m.positions[a * 3];
                double vy = m.positions[c * 3 + 1] - m.positions[a * 3 + 1];
                double vz = m.positions[c * 3 + 2] - m.positions[a * 3 + 2];
                double nx = uy * vz - uz * vy;
                double ny = uz * vx - ux * vz;
                double nz = ux * vy - uy * vx;
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-9)
                {
                    continue;
                }
                double dot = (nx * m.normals[a * 3] + ny * m.normals[a * 3 + 1] + nz * m.normals[a * 3 + 2]) / len;
                if (dot < 0.999)
                {
                    bad++;
                }
            }
            Assert.AreEqual(0, bad);
        }

        [Test]
        public void WallsFaceDownhill()
        {
            TerraWorld w = new TerraWorld(Terraced(5, 0.0f, true));
            TerraMeshData m = w.BuildChunk(2, 2, 1).mesh;
            List<int> tops;
            List<int> walls;
            List<int> skirts;
            Classify(m, out tops, out walls, out skirts);
            Assert.Greater(walls.Count, 20);
            double ox = m.originX;
            double oz = m.originZ;
            int checkedWalls = 0;
            int uphill = 0;
            foreach (int t in walls)
            {
                int a = m.indices[t];
                int b = m.indices[t + 1];
                int c = m.indices[t + 2];
                double cx = (m.positions[a * 3] + m.positions[b * 3] + m.positions[c * 3]) / 3.0;
                double cz = (m.positions[a * 3 + 2] + m.positions[b * 3 + 2] + m.positions[c * 3 + 2]) / 3.0;
                double nx = m.normals[a * 3];
                double nz = m.normals[a * 3 + 2];
                // A step a little way along the normal must be on the lower side of the wall: the terrain there is lower.
                double lowSide = w.HeightAt(ox + cx + nx * 1.0, oz + cz + nz * 1.0);
                double highSide = w.HeightAt(ox + cx - nx * 1.0, oz + cz - nz * 1.0);
                if (Math.Abs(lowSide - highSide) > 0.3)
                {
                    checkedWalls++;
                    if (lowSide > highSide)
                    {
                        uphill++;
                    }
                }
            }
            // A wall faces the downhill of its own triangle, which is the field's downhill to within the triangle's accuracy: a
            // wall can disagree with the field where a coarse triangle misjudges the slope, so a very small share is allowed.
            Assert.Greater(checkedWalls, 100);
            Assert.IsTrue(uphill * 100 <= checkedWalls, uphill + " of " + checkedWalls + " walls face uphill");
        }

        [Test]
        public void FullTiltLeavesTheGroundAsItWasWithNoWalls()
        {
            TerraWorld w = new TerraWorld(Terraced(6, 1.0f, true));
            TerraMeshData m = w.BuildChunk(1, 0, 1).mesh;
            List<int> tops;
            List<int> walls;
            List<int> skirts;
            Classify(m, out tops, out walls, out skirts);
            Assert.AreEqual(0, walls.Count);
            Assert.AreEqual(ChunkSide * ChunkSide, ProjectedArea(m, tops), 1e-2);
            // A vertex that is a lattice node lies on the height field; the vertices where a band edge cut a triangle lie on the
            // triangle, which is what the plain mesh does there too.
            Dictionary<string, double> nodes = new Dictionary<string, double>();
            for (int i = 0; i <= 32; i++)
            {
                for (int j = 0; j <= 32; j++)
                {
                    double x;
                    double z;
                    w.Lattice.Node(32 + i, j, out x, out z);
                    nodes[(x - m.originX).ToString("F3") + "," + (z - m.originZ).ToString("F3")] = w.HeightAt(x, z);
                }
            }
            int onNodes = 0;
            foreach (int t in tops)
            {
                for (int k = 0; k < 3; k++)
                {
                    int v = m.indices[t + k];
                    double h;
                    if (nodes.TryGetValue(m.positions[v * 3].ToString("F3") + "," + m.positions[v * 3 + 2].ToString("F3"), out h))
                    {
                        Assert.AreEqual(h, m.positions[v * 3 + 1], 2e-3);
                        onNodes++;
                    }
                }
            }
            Assert.Greater(onNodes, 1000);
        }

        [Test]
        public void TheLowestBandFollowsTheGroundUnlessFlattened()
        {
            TerraWorld followed = new TerraWorld(Terraced(4, 0.0f, false));
            TerraWorld flattened = new TerraWorld(Terraced(4, 0.0f, true));
            // Find a chunk with ground in the lowest band (below 25 m).
            long cx = 0;
            long cz = 0;
            bool found = false;
            for (long x = -6; x <= 6 && !found; x++)
            {
                for (long z = -6; z <= 6 && !found; z++)
                {
                    double h = followed.HeightAt(x * ChunkSide + 64.0, z * ChunkSide + 64.0);
                    if (h < 15.0)
                    {
                        cx = x;
                        cz = z;
                        found = true;
                    }
                }
            }
            Assert.IsTrue(found, "no low ground in the search area");
            TerraMeshData a = followed.BuildChunk(cx, cz, 1).mesh;
            TerraMeshData b = flattened.BuildChunk(cx, cz, 1).mesh;
            int lowAtNonLevel = 0;
            for (int t = 0; t < a.indices.Length; t += 3)
            {
                int v = a.indices[t];
                if (a.normals[v * 3 + 1] > 1e-4f && a.positions[v * 3 + 1] < 24.0f && Math.Abs(a.positions[v * 3 + 1]) > 1e-3f)
                {
                    lowAtNonLevel++;
                }
            }
            Assert.Greater(lowAtNonLevel, 0, "the lowest band keeps the shape of the ground");
            int flatLow = 0;
            for (int t = 0; t < b.indices.Length; t += 3)
            {
                int v = b.indices[t];
                if (b.normals[v * 3 + 1] > 1e-4f && b.positions[v * 3 + 1] < 24.0f)
                {
                    Assert.AreEqual(0.0f, b.positions[v * 3 + 1], 1e-3f);
                    flatLow++;
                }
            }
            Assert.Greater(flatLow, 0);
        }

        [Test]
        public void BiomeTerracesUseTheBiomeEdges()
        {
            TerraWorldSettings s = Terraced(4, 0.0f, true);
            s.terrace.mode = TerraTerraceMode.Biomes;
            TerraWorld w = new TerraWorld(s);
            Assert.AreEqual(s.biomes.Count - 1, w.Terracer.Edges.Length);
            for (int i = 0; i < w.Terracer.Edges.Length; i++)
            {
                Assert.AreEqual(w.Field.HeightOfValue(s.biomes.biomes[i].upTo), w.Terracer.Edges[i], 1e-9);
            }
        }

        [Test]
        public void OffMeansNoTerracerAndTerracesForceFlatShading()
        {
            TerraWorldSettings s = Terraced(4, 0.0f, true);
            s.terrace.mode = TerraTerraceMode.Off;
            Assert.IsFalse(new TerraWorld(s).Terracer.Enabled);
            s.terrace.mode = TerraTerraceMode.Uniform;
            s.shading = TerraShading.Smooth;
            TerraMeshData m = new TerraWorld(s).BuildChunk(0, 0, 1).mesh;
            Assert.AreEqual(m.TriangleCount * 3, m.vertexCount, "terraced meshes are flat shaded whatever the shading setting says");
        }

        [Test]
        public void TerracesWorkOnAnIslandToo()
        {
            TerraWorldSettings s = Terraced(5, 0.0f, true);
            s.island.minX = -120.0;
            s.island.maxX = 120.0;
            s.island.minZ = -120.0;
            s.island.maxZ = 120.0;
            s.island.radius = 6.0;
            TerraChunkResult r = new TerraWorld(s).BuildIsland();
            Assert.Greater(r.mesh.TriangleCount, 500);
            double top = 0.0;
            for (int t = 0; t < r.mesh.indices.Length; t += 3)
            {
                int a = r.mesh.indices[t];
                int b = r.mesh.indices[t + 1];
                int c = r.mesh.indices[t + 2];
                if (r.mesh.normals[a * 3 + 1] <= 1e-4f)
                {
                    continue;
                }
                top += Math.Abs((r.mesh.positions[b * 3] - r.mesh.positions[a * 3]) * (double)(r.mesh.positions[c * 3 + 2] - r.mesh.positions[a * 3 + 2])
                    - (r.mesh.positions[b * 3 + 2] - r.mesh.positions[a * 3 + 2]) * (double)(r.mesh.positions[c * 3] - r.mesh.positions[a * 3])) * 0.5;
            }
            Assert.AreEqual(240.0 * 240.0, top, 1.0);
        }
    }
}
