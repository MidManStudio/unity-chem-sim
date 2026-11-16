// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/Editor/TerraWorldTests.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MidManStudio.ProceduralTerrain.Tests
{
    [TestFixture]
    public class TerraWorldTests
    {
        private static TerraWorldSettings Settings(TerraShading shading)
        {
            TerraWorldSettings s = new TerraWorldSettings();
            s.seed = 2024;
            s.height.noise.scale = 140.0;
            s.height.noise.remapMin = 0.29;
            s.height.noise.remapMax = 0.71;
            s.height.heightScale = 70.0;
            s.lattice.spacing = 4.0;
            s.lattice.cellsPerChunk = 32;
            s.shading = shading;
            return s;
        }

        // Every triangle of a mesh must face the way its stored normal says, which is the way Unity's winding rule gives.
        private static void CheckWinding(TerraMeshData m, bool exactNormals)
        {
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
                if (exactNormals ? dot < 0.999 : dot <= 0.0)
                {
                    bad++;
                }
            }
            Assert.AreEqual(0, bad, bad + " triangles face away from their normal");
        }

        private static void CheckIndicesAndSizes(TerraMeshData m)
        {
            Assert.AreEqual(m.vertexCount * 3, m.positions.Length);
            Assert.AreEqual(m.vertexCount * 3, m.normals.Length);
            Assert.AreEqual(m.vertexCount * 4, m.colors.Length);
            Assert.AreEqual(0, m.indices.Length % 3);
            foreach (int i in m.indices)
            {
                Assert.IsTrue(i >= 0 && i < m.vertexCount, "index " + i);
            }
            foreach (float f in m.positions)
            {
                Assert.IsFalse(float.IsNaN(f) || float.IsInfinity(f));
            }
            foreach (float f in m.normals)
            {
                Assert.IsFalse(float.IsNaN(f) || float.IsInfinity(f));
            }
        }

        // The distinct (position along the side, height) pairs of the vertices lying on one side of the chunk, sorted. Flat
        // meshes repeat a vertex once per triangle, so only the set of pairs can be compared between two neighbours.
        private static List<string> Side(TerraMeshData m, int axis, bool atMax)
        {
            float edge = axis == 0 ? (atMax ? m.maxX : m.minX) : (atMax ? m.maxZ : m.minZ);
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

        private static void AssertSameSide(List<string> a, List<string> b, string what)
        {
            Assert.AreEqual(a.Count, b.Count, what + " vertex count");
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i], b[i], what + " vertex " + i);
            }
        }

        [Test]
        public void FlatChunkHasTheExpectedShapeAndWinding()
        {
            TerraWorld w = new TerraWorld(Settings(TerraShading.Flat));
            TerraChunkResult r = w.BuildChunk(3, -2, 1);
            CheckIndicesAndSizes(r.mesh);
            CheckWinding(r.mesh, true);
            Assert.AreEqual(32 * 32 * 2, r.mesh.TriangleCount - SkirtTriangles(32, 1), "top triangles");
            Assert.AreEqual(3 * 128.0, r.originX, 1e-12);
            Assert.AreEqual(-2 * 128.0, r.originZ, 1e-12);
            Assert.IsTrue(r.mesh.minX >= -1e-4f && r.mesh.maxX <= 128.0f + 1e-4f);
            Assert.IsFalse(r.mesh.RequiresUInt32);
            // Flat: every triangle owns its three vertices.
            Assert.AreEqual(r.mesh.TriangleCount * 3, r.mesh.vertexCount);
        }

        private static int SkirtTriangles(int cells, int step)
        {
            return 4 * (cells / step) * 2;
        }

        [Test]
        public void SmoothChunkSharesVerticesAndHasUpwardNormals()
        {
            TerraWorld w = new TerraWorld(Settings(TerraShading.Smooth));
            TerraChunkResult r = w.BuildChunk(0, 0, 1);
            CheckIndicesAndSizes(r.mesh);
            CheckWinding(r.mesh, false);
            Assert.AreEqual(33 * 33, r.mesh.vertexCount - 4 * 32 * 2 * 3, "shared grid vertices plus the unshared skirt vertices");
            for (int i = 0; i < 33 * 33; i++)
            {
                Assert.IsTrue(r.mesh.normals[i * 3 + 1] > 0.0f, "ground normal " + i);
            }
        }

        [Test]
        public void BuildingTheSameChunkTwiceGivesTheSameMesh()
        {
            TerraWorldSettings s = Settings(TerraShading.Flat);
            TerraWorld a = new TerraWorld(s);
            TerraWorld b = new TerraWorld(s);
            TerraMeshData x = a.BuildChunk(-4, 7, 1).mesh;
            TerraMeshData y = b.BuildChunk(-4, 7, 1).mesh;
            Assert.AreEqual(x.vertexCount, y.vertexCount);
            for (int i = 0; i < x.positions.Length; i++)
            {
                Assert.AreEqual(x.positions[i], y.positions[i]);
            }
            for (int i = 0; i < x.colors.Length; i++)
            {
                Assert.AreEqual(x.colors[i], y.colors[i]);
            }
            for (int i = 0; i < x.indices.Length; i++)
            {
                Assert.AreEqual(x.indices[i], y.indices[i]);
            }
        }

        [Test]
        public void NeighbouringChunksMeetWithoutACrack()
        {
            foreach (TerraShading shading in new[] { TerraShading.Flat, TerraShading.Smooth })
            {
                TerraWorld w = new TerraWorld(Settings(shading));
                TerraMeshData centre = w.BuildChunk(5, 5, 1).mesh;
                TerraMeshData east = w.BuildChunk(6, 5, 1).mesh;
                TerraMeshData south = w.BuildChunk(5, 4, 1).mesh;
                AssertSameSide(Side(centre, 0, true), Side(east, 0, false), shading + " east border");
                AssertSameSide(Side(centre, 2, false), Side(south, 2, true), shading + " south border");
            }
        }

        [Test]
        public void SmoothNormalsAndColoursAgreeAcrossTheBorder()
        {
            TerraWorld w = new TerraWorld(Settings(TerraShading.Smooth));
            TerraMeshData left = w.BuildChunk(0, 0, 1).mesh;
            TerraMeshData right = w.BuildChunk(1, 0, 1).mesh;
            // Grid vertices come first. Compare the right column of the left chunk with the left column of the right one.
            for (int r = 0; r <= 32; r++)
            {
                int a = r * 33 + 32;
                int b = r * 33;
                Assert.AreEqual(left.positions[a * 3 + 1], right.positions[b * 3 + 1]);
                for (int k = 0; k < 3; k++)
                {
                    Assert.AreEqual(left.normals[a * 3 + k], right.normals[b * 3 + k]);
                }
                for (int k = 0; k < 4; k++)
                {
                    Assert.AreEqual(left.colors[a * 4 + k], right.colors[b * 4 + k]);
                }
            }
        }

        [Test]
        public void CoarseChunkBorderVerticesSitOnTheFineOnes()
        {
            TerraWorld w = new TerraWorld(Settings(TerraShading.Flat));
            TerraMeshData fine = w.BuildChunk(2, 2, 1).mesh;
            TerraMeshData coarse = w.BuildChunk(2, 2, 4).mesh;
            Assert.Less(coarse.TriangleCount, fine.TriangleCount / 10);
            HashSet<string> fineSet = new HashSet<string>(Side(fine, 0, true));
            foreach (string s in Side(coarse, 0, true))
            {
                Assert.IsTrue(fineSet.Contains(s), "coarse border vertex " + s + " is not a fine one");
            }
            CheckWinding(coarse, true);
        }

        [Test]
        public void SkirtsHangUnderTheBorderAndFaceOutward()
        {
            TerraWorldSettings s = Settings(TerraShading.Flat);
            s.skirtDepth = 5.0;
            TerraWorld w = new TerraWorld(s);
            TerraMeshData m = w.BuildChunk(0, 0, 1).mesh;
            int skirtVertices = 0;
            for (int i = 0; i < m.vertexCount; i++)
            {
                if (m.normals[i * 3 + 1] == 0.0f && (Math.Abs(m.normals[i * 3]) == 1.0f || Math.Abs(m.normals[i * 3 + 2]) == 1.0f))
                {
                    skirtVertices++;
                    float x = m.positions[i * 3];
                    float z = m.positions[i * 3 + 2];
                    bool onBorder = x < 1e-4f || x > 128.0f - 1e-4f || z < 1e-4f || z > 128.0f - 1e-4f;
                    Assert.IsTrue(onBorder, "skirt vertex off the border");
                }
            }
            Assert.AreEqual(4 * 32 * 2 * 3, skirtVertices);
            s.skirtDepth = 0.0;
            TerraMeshData none = new TerraWorld(s).BuildChunk(0, 0, 1).mesh;
            Assert.AreEqual(32 * 32 * 2, none.TriangleCount);
        }

        [Test]
        public void StepForDistanceFollowsTheTiers()
        {
            TerraWorld w = new TerraWorld(Settings(TerraShading.Flat));
            Assert.AreEqual(1, w.StepForDistance(0.0));
            Assert.AreEqual(1, w.StepForDistance(249.0));
            Assert.AreEqual(2, w.StepForDistance(250.0));
            Assert.AreEqual(4, w.StepForDistance(700.0));
            Assert.AreEqual(8, w.StepForDistance(5000.0));
            TerraWorldSettings s = Settings(TerraShading.Flat);
            s.lattice.cellsPerChunk = 12;
            Assert.AreEqual(4, new TerraWorld(s).StepForDistance(5000.0), "the step must divide the cells of a chunk");
        }

        [Test]
        public void StampsShapeTheMeshAndPaintIt()
        {
            TerraWorldSettings s = Settings(TerraShading.Flat);
            TerraStamp pad = new TerraStamp();
            pad.kind = TerraStampKind.Pad;
            pad.x = 64.0;
            pad.z = 64.0;
            pad.radius = 30.0;
            pad.falloff = 10.0;
            pad.paint = true;
            pad.paintColor = new TerraColor(0.9f, 0.1f, 0.1f, 1f);
            s.height.stamps = new[] { pad };
            s.biomes.colorVariation = 0.0f;
            TerraWorld w = new TerraWorld(s);
            TerraMeshData m = w.BuildChunk(0, 0, 1).mesh;
            double target = w.HeightAt(64.0, 64.0);
            int flatRed = 0;
            for (int t = 0; t < m.indices.Length; t += 3)
            {
                int a = m.indices[t];
                float cx = (m.positions[a * 3]);
                float cz = (m.positions[a * 3 + 2]);
                double d = Math.Sqrt((cx - 64.0) * (cx - 64.0) + (cz - 64.0) * (cz - 64.0));
                if (d < 25.0 && m.normals[a * 3 + 1] > 0.99f)
                {
                    Assert.AreEqual(target, m.positions[a * 3 + 1], 1e-3, "the town floor is level");
                    Assert.AreEqual(0.9, m.colors[a * 4], 1e-5, "and painted");
                    flatRed++;
                }
            }
            Assert.Greater(flatRed, 200);
        }

        [Test]
        public void IslandIsOneClosedMeshOverTheWholeRectangle()
        {
            TerraWorldSettings s = Settings(TerraShading.Flat);
            s.island.minX = -200.0;
            s.island.maxX = 200.0;
            s.island.minZ = -150.0;
            s.island.maxZ = 150.0;
            s.island.radius = 7.0;
            s.height.falloff.mode = TerraFalloffMode.Radial;
            s.height.falloff.radius = 180.0;
            s.scatter = new[] { new TerraScatterLayer() };
            TerraWorld w = new TerraWorld(s);
            TerraChunkResult r = w.BuildIsland();
            CheckIndicesAndSizes(r.mesh);
            CheckWinding(r.mesh, true);
            Assert.AreEqual(0.0, r.originX, 1e-12);
            // Top area of the island equals the rectangle (skirts are vertical and have no top area).
            double area = 0.0;
            for (int t = 0; t < r.mesh.indices.Length; t += 3)
            {
                int a = r.mesh.indices[t];
                int b = r.mesh.indices[t + 1];
                int c = r.mesh.indices[t + 2];
                if (r.mesh.normals[a * 3 + 1] <= 0.0f)
                {
                    continue;
                }
                double ax = r.mesh.positions[a * 3];
                double az = r.mesh.positions[a * 3 + 2];
                double bx = r.mesh.positions[b * 3];
                double bz = r.mesh.positions[b * 3 + 2];
                double cx = r.mesh.positions[c * 3];
                double cz = r.mesh.positions[c * 3 + 2];
                area += Math.Abs((bx - ax) * (cz - az) - (bz - az) * (cx - ax)) * 0.5;
            }
            Assert.AreEqual(400.0 * 300.0, area, 1.0);
            Assert.IsNotNull(r.scatter);
            // The edge of the island is at the ground value.
            Assert.AreEqual(w.Field.BaseHeight, w.HeightAt(-200.0, 0.0), 1e-9);
        }

        [Test]
        public void SmoothIslandBuildsAndWinds()
        {
            TerraWorldSettings s = Settings(TerraShading.Smooth);
            s.island.minX = -100.0;
            s.island.maxX = 100.0;
            s.island.minZ = -100.0;
            s.island.maxZ = 100.0;
            TerraWorld w = new TerraWorld(s);
            TerraChunkResult r = w.BuildIsland();
            CheckIndicesAndSizes(r.mesh);
            CheckWinding(r.mesh, false);
        }

        [Test]
        public void DefaultChunkFitsA16BitIndexBuffer()
        {
            TerraWorldSettings s = new TerraWorldSettings();
            TerraWorld w = new TerraWorld(s);
            TerraChunkResult r = w.BuildChunk(0, 0, 1);
            Assert.AreEqual(256.0, w.Lattice.ChunkSize, 1e-12);
            Assert.IsFalse(r.mesh.RequiresUInt32, "vertices " + r.mesh.vertexCount);
            s.lattice.spacing = 2.0;
            s.lattice.cellsPerChunk = 128;
            Assert.IsTrue(new TerraWorld(s).BuildChunk(0, 0, 1).mesh.RequiresUInt32, "a 256 m chunk at 2 m spacing needs 32 bit indices");
        }

        [Test]
        public void TheReadmeExampleRuns()
        {
            TerraWorldSettings settings = TerraPresets.RollingHills();
            settings.seed = 424242;
            TerraStamp town = new TerraStamp();
            town.kind = TerraStampKind.Pad;
            town.x = 300.0;
            town.z = 300.0;
            town.radius = 60.0;
            town.paint = true;
            TerraStamp road = new TerraStamp();
            road.kind = TerraStampKind.Road;
            road.points = new[] { new TerraPoint(300.0, 300.0), new TerraPoint(320.0, 700.0) };
            road.paint = true;
            settings.height.stamps = new[] { town, road };
            TerraWorld world = new TerraWorld(settings);
            TerraChunkResult chunk = world.BuildChunk(1, 1, 1);
            TerraMeshData mesh = chunk.mesh;
            TerraScatterPoint[] trees = world.ScatterChunk(1, 1);
            double y = world.HeightAt(300.0, 300.0);
            Assert.Greater(mesh.TriangleCount, 1000);
            Assert.IsNotNull(trees);
            Assert.AreEqual(y, world.HeightAt(310.0, 290.0), 1e-9, "inside the town pad the ground is level");
            foreach (TerraScatterPoint t in trees)
            {
                double d = Math.Sqrt((t.x - 300.0) * (t.x - 300.0) + (t.z - 300.0) * (t.z - 300.0));
                Assert.IsTrue(d > 55.0, "no tree on the town floor");
            }
        }

        [Test]
        public void FarFromTheOriginTheMeshStaysPrecise()
        {
            TerraWorld w = new TerraWorld(Settings(TerraShading.Flat));
            long far = 50000L;
            TerraChunkResult r = w.BuildChunk(far, -far, 1);
            Assert.AreEqual(far * 128.0, r.originX, 1e-6);
            Assert.IsTrue(r.mesh.maxX - r.mesh.minX > 127.9f && r.mesh.maxX - r.mesh.minX < 128.1f);
            CheckWinding(r.mesh, true);
        }
    }
}
