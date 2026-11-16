// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/Editor/TerraGeometryTests.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MidManStudio.ProceduralTerrain.Tests
{
    [TestFixture]
    public class TerraGeometryTests
    {
        private static double TotalArea(double[] xs, double[] zs, int[] tris)
        {
            double total = 0.0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                double c = TerraGeom.Cross(xs[tris[i]], zs[tris[i]], xs[tris[i + 1]], zs[tris[i + 1]], xs[tris[i + 2]], zs[tris[i + 2]]);
                Assert.IsTrue(c > 0.0, "triangle " + (i / 3) + " is not counter-clockwise: " + c);
                total += c * 0.5;
            }
            return total;
        }

        // ---------------------------------------------------------------- lattice

        [Test]
        public void LatticeChunkIsARectangleOfCounterClockwiseTriangles()
        {
            TerraLatticeSettings s = new TerraLatticeSettings();
            s.spacing = 4.0;
            s.cellsPerChunk = 16;
            s.jitter = 0.25;
            TerraLattice lattice = new TerraLattice(3, s);
            TerraTriangulation t = lattice.BuildChunk(2, -1, 1);
            Assert.AreEqual(17 * 17, t.VertexCount);
            Assert.AreEqual(16 * 16 * 2, t.TriangleCount);
            double area = TotalArea(t.x, t.z, t.triangles);
            Assert.AreEqual(64.0 * 64.0, area, 1e-6, "the chunk covers exactly its rectangle");
            double ox;
            double oz;
            lattice.ChunkOrigin(2, -1, out ox, out oz);
            Assert.AreEqual(128.0, ox, 1e-12);
            Assert.AreEqual(-64.0, oz, 1e-12);
            foreach (double x in t.x)
            {
                Assert.IsTrue(x >= ox && x <= ox + 64.0);
            }
        }

        [Test]
        public void LatticeNodesDoNotMoveWhichChunkAsks()
        {
            TerraLatticeSettings s = new TerraLatticeSettings();
            s.cellsPerChunk = 8;
            TerraLattice lattice = new TerraLattice(9, s);
            TerraTriangulation a = lattice.BuildChunk(0, 0, 1);
            TerraTriangulation b = lattice.BuildChunk(1, 0, 1);
            // The right border column of chunk 0 is the left border column of chunk 1, point for point.
            for (int r = 0; r <= 8; r++)
            {
                int ia = r * 9 + 8;
                int ib = r * 9;
                Assert.AreEqual(a.x[ia], b.x[ib]);
                Assert.AreEqual(a.z[ia], b.z[ib]);
            }
            TerraTriangulation c = lattice.BuildChunk(0, 1, 1);
            for (int col = 0; col <= 8; col++)
            {
                Assert.AreEqual(a.x[8 * 9 + col], c.x[col]);
                Assert.AreEqual(a.z[8 * 9 + col], c.z[col]);
            }
            double x1;
            double z1;
            lattice.Node(5, 3, out x1, out z1);
            double x2;
            double z2;
            lattice.Node(5, 3, out x2, out z2);
            Assert.AreEqual(x1, x2);
            Assert.AreEqual(z1, z2);
        }

        [Test]
        public void LatticeJitterStaysWithinItsBound()
        {
            TerraLatticeSettings s = new TerraLatticeSettings();
            s.spacing = 5.0;
            s.cellsPerChunk = 10;
            s.jitter = 0.3;
            TerraLattice lattice = new TerraLattice(4, s);
            double max = 0.0;
            int moved = 0;
            for (long i = -40; i <= 40; i++)
            {
                for (long j = -40; j <= 40; j++)
                {
                    double x;
                    double z;
                    lattice.Node(i, j, out x, out z);
                    double dx = Math.Abs(x - i * 5.0);
                    double dz = Math.Abs(z - j * 5.0);
                    max = Math.Max(max, Math.Max(dx, dz));
                    if (dx > 0.0 || dz > 0.0)
                    {
                        moved++;
                    }
                    if (i % 10 == 0)
                    {
                        Assert.AreEqual(0.0, dx, "a node on a chunk line does not leave it in x");
                    }
                    if (j % 10 == 0)
                    {
                        Assert.AreEqual(0.0, dz, "a node on a chunk line does not leave it in z");
                    }
                }
            }
            Assert.IsTrue(max <= 0.3 * 5.0 + 1e-12, "offset " + max);
            Assert.Greater(max, 0.25 * 5.0, "the jitter should reach most of its range");
            Assert.Greater(moved, 5000);
        }

        [Test]
        public void LatticeCoarseNodesAreFineNodes()
        {
            TerraLatticeSettings s = new TerraLatticeSettings();
            s.cellsPerChunk = 16;
            TerraLattice lattice = new TerraLattice(2, s);
            TerraTriangulation fine = lattice.BuildChunk(-3, 4, 1);
            TerraTriangulation coarse = lattice.BuildChunk(-3, 4, 4);
            Assert.AreEqual(5 * 5, coarse.VertexCount);
            for (int r = 0; r <= 4; r++)
            {
                for (int c = 0; c <= 4; c++)
                {
                    Assert.AreEqual(fine.x[(r * 4) * 17 + c * 4], coarse.x[r * 5 + c]);
                    Assert.AreEqual(fine.z[(r * 4) * 17 + c * 4], coarse.z[r * 5 + c]);
                }
            }
            Assert.Throws<ArgumentException>(() => lattice.BuildChunk(0, 0, 3));
            TerraTriangulation coarseTris = coarse;
            Assert.AreEqual(4 * 4 * 2, coarseTris.TriangleCount);
        }

        [Test]
        public void LatticeWithoutJitterIsARegularGrid()
        {
            TerraLatticeSettings s = new TerraLatticeSettings();
            s.jitter = 0.0;
            s.spacing = 2.0;
            s.cellsPerChunk = 4;
            TerraLattice lattice = new TerraLattice(1, s);
            TerraTriangulation t = lattice.BuildChunk(0, 0, 1);
            for (int r = 0; r <= 4; r++)
            {
                for (int c = 0; c <= 4; c++)
                {
                    Assert.AreEqual(c * 2.0, t.x[r * 5 + c]);
                    Assert.AreEqual(r * 2.0, t.z[r * 5 + c]);
                }
            }
        }

        [Test]
        public void LatticeChunkOfFindsTheChunk()
        {
            TerraLatticeSettings s = new TerraLatticeSettings();
            s.spacing = 4.0;
            s.cellsPerChunk = 64;
            TerraLattice lattice = new TerraLattice(1, s);
            long cx;
            long cz;
            lattice.ChunkOf(255.9, -0.1, out cx, out cz);
            Assert.AreEqual(0L, cx);
            Assert.AreEqual(-1L, cz);
            lattice.ChunkOf(256.0, 512.0, out cx, out cz);
            Assert.AreEqual(1L, cx);
            Assert.AreEqual(2L, cz);
        }

        // ---------------------------------------------------------------- Poisson

        [Test]
        public void PoissonKeepsItsDistanceAndCoversTheRectangle()
        {
            double[] xs;
            double[] zs;
            TerraPoisson.Generate(5UL, -50.0, -30.0, 150.0, 90.0, 6.0, 30, out xs, out zs);
            Assert.Greater(xs.Length, 300);
            double minDistance = double.MaxValue;
            for (int i = 0; i < xs.Length; i++)
            {
                Assert.IsTrue(xs[i] >= -50.0 && xs[i] <= 150.0 && zs[i] >= -30.0 && zs[i] <= 90.0);
                for (int j = i + 1; j < xs.Length; j++)
                {
                    double d = Math.Sqrt((xs[i] - xs[j]) * (xs[i] - xs[j]) + (zs[i] - zs[j]) * (zs[i] - zs[j]));
                    minDistance = Math.Min(minDistance, d);
                }
            }
            Assert.IsTrue(minDistance > 6.0 - 1e-9, "closest pair " + minDistance);
            bool c00 = false, c10 = false, c01 = false, c11 = false;
            for (int i = 0; i < xs.Length; i++)
            {
                c00 |= xs[i] == -50.0 && zs[i] == -30.0;
                c10 |= xs[i] == 150.0 && zs[i] == -30.0;
                c01 |= xs[i] == -50.0 && zs[i] == 90.0;
                c11 |= xs[i] == 150.0 && zs[i] == 90.0;
            }
            Assert.IsTrue(c00 && c10 && c01 && c11, "all four corners are points");
            // No hole: every spot of the rectangle is within two radii of a point.
            for (int i = 0; i < 400; i++)
            {
                double px = -50.0 + (i % 20) * 10.0 + 0.37;
                double pz = -30.0 + (i / 20) * 6.0 + 0.11;
                double best = double.MaxValue;
                for (int j = 0; j < xs.Length; j++)
                {
                    best = Math.Min(best, Math.Sqrt((xs[j] - px) * (xs[j] - px) + (zs[j] - pz) * (zs[j] - pz)));
                }
                Assert.IsTrue(best < 12.0, "hole at " + px + "," + pz + " nearest " + best);
            }
        }

        [Test]
        public void PoissonIsDeterministicAndSeedDependent()
        {
            double[] ax;
            double[] az;
            double[] bx;
            double[] bz;
            double[] cx;
            double[] cz;
            TerraPoisson.Generate(1UL, 0.0, 0.0, 100.0, 100.0, 5.0, 30, out ax, out az);
            TerraPoisson.Generate(1UL, 0.0, 0.0, 100.0, 100.0, 5.0, 30, out bx, out bz);
            TerraPoisson.Generate(2UL, 0.0, 0.0, 100.0, 100.0, 5.0, 30, out cx, out cz);
            Assert.AreEqual(ax.Length, bx.Length);
            for (int i = 0; i < ax.Length; i++)
            {
                Assert.AreEqual(ax[i], bx[i]);
                Assert.AreEqual(az[i], bz[i]);
            }
            bool same = ax.Length == cx.Length;
            for (int i = 0; same && i < ax.Length; i++)
            {
                same = ax[i] == cx[i] && az[i] == cz[i];
            }
            Assert.IsFalse(same);
            Assert.Throws<ArgumentException>(() => TerraPoisson.Generate(1UL, 5.0, 5.0, 5.0, 9.0, 1.0, 30, out ax, out az));
        }

        // ---------------------------------------------------------------- Delaunay

        [Test]
        public void DelaunayHasEmptyCircumcirclesAndCoversTheHull()
        {
            TerraRng rng = new TerraRng(99UL);
            int n = 600;
            double[] xs = new double[n];
            double[] zs = new double[n];
            // Four corners make the hull a known rectangle.
            xs[0] = 0.0; zs[0] = 0.0;
            xs[1] = 200.0; zs[1] = 0.0;
            xs[2] = 200.0; zs[2] = 100.0;
            xs[3] = 0.0; zs[3] = 100.0;
            for (int i = 4; i < n; i++)
            {
                xs[i] = 1.0 + rng.NextDouble() * 198.0;
                zs[i] = 1.0 + rng.NextDouble() * 98.0;
            }
            int[] tris = TerraDelaunay.Triangulate(xs, zs);
            double area = TotalArea(xs, zs, tris);
            Assert.AreEqual(200.0 * 100.0, area, 1e-6, "the triangles tile the rectangle");
            // Euler: a triangulation of n points with h hull points has 2n - 2 - h triangles.
            Assert.AreEqual(2 * n - 2 - 4, tris.Length / 3);
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t];
                int b = tris[t + 1];
                int c = tris[t + 2];
                for (int p = 0; p < n; p++)
                {
                    if (p == a || p == b || p == c)
                    {
                        continue;
                    }
                    double d = TerraGeom.InCircle(xs[a], zs[a], xs[b], zs[b], xs[c], zs[c], xs[p], zs[p]);
                    Assert.IsTrue(d < 1e-6, "point " + p + " lies inside the circle of triangle " + (t / 3) + " by " + d);
                }
            }
        }

        [Test]
        public void DelaunayHandlesCollinearBorderPointsAndASmallInput()
        {
            double[] xs = { 0.0, 10.0, 20.0, 20.0, 20.0, 10.0, 0.0, 0.0, 10.0 };
            double[] zs = { 0.0, 0.0, 0.0, 10.0, 20.0, 20.0, 20.0, 10.0, 10.0 };
            int[] tris = TerraDelaunay.Triangulate(xs, zs);
            Assert.AreEqual(400.0, TotalArea(xs, zs, tris), 1e-9);
            Assert.AreEqual(0, TerraDelaunay.Triangulate(new[] { 0.0, 1.0 }, new[] { 0.0, 1.0 }).Length);
            int[] one = TerraDelaunay.Triangulate(new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 });
            Assert.AreEqual(3, one.Length);
            Assert.Throws<ArgumentException>(() => TerraDelaunay.Triangulate(new[] { 0.0 }, new[] { 0.0, 1.0 }));
        }

        [Test]
        public void PoissonPointsTriangulateToTheWholeRectangle()
        {
            double[] xs;
            double[] zs;
            TerraPoisson.Generate(8UL, -100.0, -60.0, 140.0, 80.0, 5.0, 30, out xs, out zs);
            int[] tris = TerraDelaunay.Triangulate(xs, zs);
            double area = TotalArea(xs, zs, tris);
            Assert.AreEqual(240.0 * 140.0, area, 1e-5);
            // Every point is used by at least one triangle.
            bool[] used = new bool[xs.Length];
            foreach (int v in tris)
            {
                used[v] = true;
            }
            for (int i = 0; i < used.Length; i++)
            {
                Assert.IsTrue(used[i], "point " + i + " is not in any triangle");
            }
        }

        [Test]
        public void DelaunayIsFastEnoughForAnIsland()
        {
            double[] xs;
            double[] zs;
            TerraPoisson.Generate(3UL, 0.0, 0.0, 1024.0, 1024.0, 6.0, 30, out xs, out zs);
            System.Diagnostics.Stopwatch w = System.Diagnostics.Stopwatch.StartNew();
            int[] tris = TerraDelaunay.Triangulate(xs, zs);
            w.Stop();
            Assert.Greater(xs.Length, 10000);
            Assert.AreEqual(1024.0 * 1024.0, TotalArea(xs, zs, tris), 1e-3);
            Assert.Less(w.ElapsedMilliseconds, 10000, "triangulating " + xs.Length + " points took " + w.ElapsedMilliseconds + " ms");
        }
    }
}
