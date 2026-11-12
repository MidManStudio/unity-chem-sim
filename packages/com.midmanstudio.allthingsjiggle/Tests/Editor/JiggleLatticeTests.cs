// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Tests/Editor/JiggleLatticeTests.cs"
// ============================================================================
using System;
using NUnit.Framework;

namespace MidManStudio.AllThingsJiggle.EditorTests
{
    /// <summary>
    /// The lattice of drop 4, which has no Unity in it: the grid and its tree, the free form deformation maths, tree rigs in
    /// the solver on both backends, and the whole path from a moving object to deformed vertices. The file runs under plain
    /// .NET in CI (see Tests~/DotnetHarness), so it must stay free of UnityEngine.
    /// </summary>
    [TestFixture]
    public unsafe class JiggleLatticeTests
    {
        private static int Bits(float v)
        {
            return BitConverter.SingleToInt32Bits(v);
        }

        private sealed class Lcg
        {
            private uint s;

            public Lcg(uint seed)
            {
                s = seed * 2654435761u + 12345u;
            }

            public float Range(float lo, float hi)
            {
                s = s * 1664525u + 1013904223u;
                float u = (float)(s >> 8) / (float)(1 << 24);
                return lo + (hi - lo) * u;
            }
        }

        // ------------------------------------------------------------------ the grid and its tree

        [Test]
        public void TheGridClampsItsDivisionsAndCountsItsPoints()
        {
            Assert.AreEqual(1, JiggleLatticeGrid.ClampDivisions(-3));
            Assert.AreEqual(1, JiggleLatticeGrid.ClampDivisions(0));
            Assert.AreEqual(3, JiggleLatticeGrid.ClampDivisions(3));
            Assert.AreEqual(5, JiggleLatticeGrid.ClampDivisions(9));
            Assert.AreEqual(27, JiggleLatticeGrid.PointCount(3, 3, 3));
            Assert.AreEqual(125, JiggleLatticeGrid.PointCount(5, 5, 5));
            Assert.AreEqual(125, JiggleLatticeGrid.PointCount(50, 7, 9));
            Assert.AreEqual(1, JiggleLatticeGrid.PointCount(0, 0, 0));
            Assert.AreEqual(125, JiggleLatticeGrid.MaxPoints);
            Assert.Less(JiggleLatticeGrid.MaxPoints + 1, JiggleConst.MaxParticles + 1, "with the anchor a lattice fits a rig");
            Assert.AreEqual(0, JiggleLatticeGrid.PointIndex(0, 0, 0, 3, 4));
            Assert.AreEqual(1, JiggleLatticeGrid.PointIndex(1, 0, 0, 3, 4));
            Assert.AreEqual(3, JiggleLatticeGrid.PointIndex(0, 1, 0, 3, 4));
            Assert.AreEqual(12, JiggleLatticeGrid.PointIndex(0, 0, 1, 3, 4));
        }

        [Test]
        public void PointsSitEvenlyAcrossTheBoxAndAnAxisWithOnePointSitsInTheMiddle()
        {
            JVec3 min = new JVec3(-1.0f, 2.0f, 0.5f);
            JVec3 size = new JVec3(2.0f, 4.0f, 1.0f);
            JVec3 a = JiggleLatticeGrid.PointRest(0, 0, 0, 3, 5, 2, min, size);
            JVec3 b = JiggleLatticeGrid.PointRest(2, 4, 1, 3, 5, 2, min, size);
            JVec3 c = JiggleLatticeGrid.PointRest(1, 2, 0, 3, 5, 2, min, size);
            Assert.AreEqual(-1.0, a.X, 1e-6);
            Assert.AreEqual(2.0, a.Y, 1e-6);
            Assert.AreEqual(0.5, a.Z, 1e-6);
            Assert.AreEqual(1.0, b.X, 1e-6);
            Assert.AreEqual(6.0, b.Y, 1e-6);
            Assert.AreEqual(1.5, b.Z, 1e-6);
            Assert.AreEqual(0.0, c.X, 1e-6);
            Assert.AreEqual(4.0, c.Y, 1e-6);
            JVec3 flat = JiggleLatticeGrid.PointRest(0, 0, 0, 3, 3, 1, min, size);
            Assert.AreEqual(1.0, flat.Z, 1e-6, "a single point on an axis is in the middle of the box");

            // The particle order puts the anchor first, then every point once, at the position of its grid point.
            JiggleLatticeTree tree = JiggleLatticeGrid.BuildTree(3, 2, 2);
            JVec3[] rest = JiggleLatticeGrid.ParticleRest(tree, min, size);
            Assert.AreEqual(12, rest.Length, "one particle for every point, the anchor being one of them");
            for (int p = 0; p < tree.Count; p++)
            {
                int pt = tree.PointOfParticle[p];
                JVec3 expect = JiggleLatticeGrid.PointRest(pt % 3, (pt / 3) % 2, pt / 6, 3, 2, 2, min, size);
                Assert.AreEqual(expect.X, rest[p].X, 0.0, "particle " + p);
                Assert.AreEqual(expect.Y, rest[p].Y, 0.0, "particle " + p);
                Assert.AreEqual(expect.Z, rest[p].Z, 0.0, "particle " + p);
            }
        }

        [Test]
        public void EveryGridMakesATreeWithTheParentBeforeTheChildAndOneRootAtTheMiddle()
        {
            for (int nz = 1; nz <= 5; nz++)
            {
                for (int ny = 1; ny <= 5; ny++)
                {
                    for (int nx = 1; nx <= 5; nx++)
                    {
                        JiggleLatticeTree t = JiggleLatticeGrid.BuildTree(nx, ny, nz);
                        string at = nx + "x" + ny + "x" + nz;
                        int count = nx * ny * nz;
                        Assert.AreEqual(count, t.Count, at);
                        Assert.AreEqual(count, t.Parent.Length, at);
                        Assert.Less(count, JiggleConst.MaxParticles + 1, at);
                        Assert.AreEqual(-1, t.Parent[0], at);
                        Assert.AreEqual(t.RootPoint, t.PointOfParticle[0], at + ": the anchor stands for the root point");
                        Assert.AreEqual(0, t.ParticleOfPoint[t.RootPoint], at);

                        bool[] seen = new bool[count];
                        int onAnchor = 0;
                        for (int p = 0; p < count; p++)
                        {
                            int pt = t.PointOfParticle[p];
                            Assert.IsTrue(pt >= 0 && pt < count, at + ": point of particle " + p);
                            Assert.IsFalse(seen[pt], at + ": point " + pt + " is simulated twice");
                            seen[pt] = true;
                            Assert.AreEqual(p, t.ParticleOfPoint[pt], at + ": the two maps are inverses");
                            if (p == 0)
                            {
                                continue;
                            }
                            Assert.IsTrue(t.Parent[p] >= 0 && t.Parent[p] < p, at + ": particle " + p + " has a parent that is not before it");
                            if (t.Parent[p] == 0)
                            {
                                onAnchor++;
                            }
                            int pp = t.PointOfParticle[t.Parent[p]];
                            int dx = Math.Abs(pt % nx - pp % nx);
                            int dy = Math.Abs((pt / nx) % ny - (pp / nx) % ny);
                            int dz = Math.Abs(pt / (nx * ny) - pp / (nx * ny));
                            Assert.AreEqual(1, dx + dy + dz, at + ": the parent of particle " + p + " is not a neighbour");
                        }
                        // The anchor has as many children as the root point has neighbours on the grid.
                        int rx = t.RootPoint % nx;
                        int ry = (t.RootPoint / nx) % ny;
                        int rz = t.RootPoint / (nx * ny);
                        int neighbours = (rx > 0 ? 1 : 0) + (rx < nx - 1 ? 1 : 0) + (ry > 0 ? 1 : 0) + (ry < ny - 1 ? 1 : 0)
                            + (rz > 0 ? 1 : 0) + (rz < nz - 1 ? 1 : 0);
                        Assert.AreEqual(neighbours, onAnchor, at);

                        // No point is nearer the middle than the root.
                        float cx = (nx - 1) * 0.5f;
                        float cy = (ny - 1) * 0.5f;
                        float cz = (nz - 1) * 0.5f;
                        int r = t.RootPoint;
                        float rd = Sq(r % nx - cx) + Sq((r / nx) % ny - cy) + Sq(r / (nx * ny) - cz);
                        for (int pt = 0; pt < count; pt++)
                        {
                            float d = Sq(pt % nx - cx) + Sq((pt / nx) % ny - cy) + Sq(pt / (nx * ny) - cz);
                            Assert.IsTrue(d >= rd - 1e-6f, at + ": point " + pt + " is nearer the middle than the root");
                        }
                    }
                }
            }
        }

        private static float Sq(float v)
        {
            return v * v;
        }

        [Test]
        public void TheTreeOfAGridIsTheSameEveryTimeAndThreeByThreeByThreeHasTheRootInTheMiddle()
        {
            JiggleLatticeTree a = JiggleLatticeGrid.BuildTree(3, 3, 3);
            JiggleLatticeTree b = JiggleLatticeGrid.BuildTree(3, 3, 3);
            for (int p = 0; p < a.Count; p++)
            {
                Assert.AreEqual(a.Parent[p], b.Parent[p]);
                Assert.AreEqual(a.PointOfParticle[p], b.PointOfParticle[p]);
            }
            Assert.AreEqual(13, a.RootPoint, "(1, 1, 1) is the middle of three by three by three");
            // Six face points reach the middle, then twelve edge points hang on those, then eight corners hang on edge points.
            int depth1 = 0;
            for (int p = 1; p < a.Count; p++)
            {
                if (a.Parent[p] == 0)
                {
                    depth1++;
                }
            }
            Assert.AreEqual(6, depth1);
            int[] parents = a.ParentsOfParticles();
            Assert.AreEqual(a.Count - 1, parents.Length);
            Assert.AreEqual(0, parents[0]);
            Assert.AreEqual(0, JiggleLatticeGrid.BuildTree(1, 1, 1).ParentsOfParticles().Length, "one point has nothing to simulate");
        }

        [Test]
        public void AnyPointCanBeTheRootAndTheNearestPointToAPositionIsFound()
        {
            int nx = 3;
            int ny = 2;
            int nz = 4;
            int count = nx * ny * nz;
            for (int root = 0; root < count; root++)
            {
                JiggleLatticeTree t = JiggleLatticeGrid.BuildTree(nx, ny, nz, root);
                string at = "root " + root;
                Assert.AreEqual(root, t.RootPoint, at);
                Assert.AreEqual(root, t.PointOfParticle[0], at);
                Assert.AreEqual(0, t.ParticleOfPoint[root], at);
                bool[] seen = new bool[count];
                for (int p = 0; p < count; p++)
                {
                    int pt = t.PointOfParticle[p];
                    Assert.IsFalse(seen[pt], at + ": point " + pt + " twice");
                    seen[pt] = true;
                    Assert.AreEqual(p, t.ParticleOfPoint[pt], at);
                    if (p > 0)
                    {
                        Assert.IsTrue(t.Parent[p] >= 0 && t.Parent[p] < p, at + ": parent of " + p);
                        int pp = t.PointOfParticle[t.Parent[p]];
                        int dist = Math.Abs(pt % nx - pp % nx) + Math.Abs((pt / nx) % ny - (pp / nx) % ny) + Math.Abs(pt / (nx * ny) - pp / (nx * ny));
                        Assert.AreEqual(1, dist, at + ": neighbour " + p);
                    }
                }
            }
            Assert.AreEqual(JiggleLatticeGrid.MiddlePoint(nx, ny, nz), JiggleLatticeGrid.BuildTree(nx, ny, nz, -1).RootPoint);
            Assert.AreEqual(JiggleLatticeGrid.MiddlePoint(nx, ny, nz), JiggleLatticeGrid.BuildTree(nx, ny, nz, count).RootPoint, "a point past the grid gives the middle");
            Assert.AreEqual(13, JiggleLatticeGrid.MiddlePoint(3, 3, 3));
            Assert.AreEqual(0, JiggleLatticeGrid.MiddlePoint(2, 2, 2), "the lowest index wins a tie");

            JVec3 min = new JVec3(-1.0f, 0.0f, 2.0f);
            JVec3 size = new JVec3(2.0f, 4.0f, 6.0f);
            Assert.AreEqual(13, JiggleLatticeGrid.NearestPoint(3, 3, 3, min, size, new JVec3(0.0f, 2.0f, 5.0f)));
            Assert.AreEqual(0, JiggleLatticeGrid.NearestPoint(3, 3, 3, min, size, new JVec3(-9.0f, -9.0f, -9.0f)));
            Assert.AreEqual(26, JiggleLatticeGrid.NearestPoint(3, 3, 3, min, size, new JVec3(9.0f, 9.0f, 9.0f)));
            Assert.AreEqual(2, JiggleLatticeGrid.NearestPoint(3, 3, 3, min, size, new JVec3(0.9f, 0.1f, 2.1f)));
            Assert.AreEqual(0, JiggleLatticeGrid.NearestPoint(2, 1, 1, min, size, new JVec3(0.0f, 2.0f, 5.0f)), "the middle of two points is a tie");
        }

        // ------------------------------------------------------------------ the maths

        [Test]
        public void BernsteinWeightsAreNeverNegativeAddUpToOneAndKnowTheirEnds()
        {
            float* w = stackalloc float[JiggleFfd.MaxAxis];
            for (int count = 1; count <= JiggleFfd.MaxAxis; count++)
            {
                for (int s = 0; s <= 40; s++)
                {
                    float t = (float)s / 40.0f;
                    JiggleFfd.Weights(count, t, w);
                    float sum = 0.0f;
                    for (int k = 0; k < count; k++)
                    {
                        Assert.IsTrue(w[k] >= 0.0f, "weight " + k + " of " + count + " at " + t);
                        sum += w[k];
                    }
                    Assert.AreEqual(1.0, sum, 1e-6, "weights of " + count + " at " + t);
                }
                JiggleFfd.Weights(count, 0.0f, w);
                Assert.AreEqual(1.0, w[0], 0.0, "the first point owns t = 0");
                JiggleFfd.Weights(count, 1.0f, w);
                Assert.AreEqual(1.0, w[count - 1], 0.0, "the last point owns t = 1");
            }
            JiggleFfd.Weights(3, 0.5f, w);
            Assert.AreEqual(0.25, w[0], 1e-7);
            Assert.AreEqual(0.5, w[1], 1e-7);
            Assert.AreEqual(0.25, w[2], 1e-7);
            JiggleFfd.Weights(4, 0.5f, w);
            Assert.AreEqual(0.125, w[0], 1e-7);
            Assert.AreEqual(0.375, w[1], 1e-7);
            Assert.AreEqual(0.375, w[2], 1e-7);
            Assert.AreEqual(0.125, w[3], 1e-7);
        }

        [Test]
        public void TheDerivativesOfTheWeightsAddUpToZeroAndMatchAFiniteDifference()
        {
            float* dw = stackalloc float[JiggleFfd.MaxAxis];
            float* lo = stackalloc float[JiggleFfd.MaxAxis];
            float* hi = stackalloc float[JiggleFfd.MaxAxis];
            for (int count = 1; count <= JiggleFfd.MaxAxis; count++)
            {
                for (int s = 1; s < 20; s++)
                {
                    float t = (float)s / 20.0f;
                    JiggleFfd.Derivatives(count, t, dw);
                    float h = 1.0e-3f;
                    JiggleFfd.Weights(count, t - h, lo);
                    JiggleFfd.Weights(count, t + h, hi);
                    float sum = 0.0f;
                    for (int k = 0; k < count; k++)
                    {
                        sum += dw[k];
                        float fd = (hi[k] - lo[k]) / (2.0f * h);
                        Assert.AreEqual(fd, dw[k], 2e-3, "derivative " + k + " of " + count + " at " + t);
                    }
                    Assert.AreEqual(0.0, sum, 1e-5, "derivatives of " + count + " at " + t);
                }
            }
            JiggleFfd.Derivatives(1, 0.3f, dw);
            Assert.AreEqual(0.0, dw[0], 0.0, "one point has nothing to vary");
        }

        // A box for the maths tests, deliberately not at the origin and not a cube.
        private static readonly JVec3 BoxMin = new JVec3(-1.0f, 0.5f, 2.0f);
        private static readonly JVec3 BoxSize = new JVec3(2.0f, 1.0f, 3.0f);

        private static JVec3[] RandomPoints(Lcg rng, int n, float over)
        {
            JVec3[] p = new JVec3[n];
            for (int i = 0; i < n; i++)
            {
                p[i] = new JVec3(
                    BoxMin.X + BoxSize.X * rng.Range(-over, 1.0f + over),
                    BoxMin.Y + BoxSize.Y * rng.Range(-over, 1.0f + over),
                    BoxMin.Z + BoxSize.Z * rng.Range(-over, 1.0f + over));
            }
            return p;
        }

        // The control displacements of grid (nx, ny, nz) in the box above, from a function of the rest position.
        private static JVec3[] ControlField(int nx, int ny, int nz, Func<JVec3, JVec3> field)
        {
            JVec3[] d = new JVec3[nx * ny * nz];
            for (int k = 0; k < nz; k++)
            {
                for (int j = 0; j < ny; j++)
                {
                    for (int i = 0; i < nx; i++)
                    {
                        d[JiggleLatticeGrid.PointIndex(i, j, k, nx, ny)] =
                            field(JiggleLatticeGrid.PointRest(i, j, k, nx, ny, nz, BoxMin, BoxSize));
                    }
                }
            }
            return d;
        }

        private static JVec3[] Deform(int nx, int ny, int nz, JVec3[] verts, JVec3[] normals, JVec3[] d, out JVec3[] outNormals)
        {
            JVec3[] coords = new JVec3[verts.Length];
            JVec3[] result = new JVec3[verts.Length];
            outNormals = normals == null ? null : new JVec3[verts.Length];
            fixed (JVec3* pv = verts)
            fixed (JVec3* pc = coords)
            fixed (JVec3* pr = result)
            fixed (JVec3* pd = d)
            fixed (JVec3* pn = normals)
            fixed (JVec3* po = outNormals)
            {
                JiggleFfd.Coordinates(pv, verts.Length, BoxMin, BoxSize, pc);
                JiggleFfd.DeformRange(0, verts.Length, nx, ny, nz, BoxSize, pc, pd, pv, pn, pr, po);
            }
            return result;
        }

        [Test]
        public void AStraightStretchAndTurnOfTheWholeBoxIsReproducedExactly()
        {
            int[][] grids = { new[] { 2, 2, 2 }, new[] { 3, 4, 2 }, new[] { 5, 2, 3 }, new[] { 5, 5, 5 }, new[] { 4, 3, 5 } };
            Lcg rng = new Lcg(11u);
            for (int g = 0; g < grids.Length; g++)
            {
                int nx = grids[g][0];
                int ny = grids[g][1];
                int nz = grids[g][2];
                float[] a = new float[9];
                for (int i = 0; i < 9; i++)
                {
                    a[i] = rng.Range(-0.5f, 0.5f);
                }
                JVec3 b = new JVec3(rng.Range(-0.3f, 0.3f), rng.Range(-0.3f, 0.3f), rng.Range(-0.3f, 0.3f));
                Func<JVec3, JVec3> affine = delegate(JVec3 p)
                {
                    return new JVec3(
                        a[0] * p.X + a[1] * p.Y + a[2] * p.Z + b.X,
                        a[3] * p.X + a[4] * p.Y + a[5] * p.Z + b.Y,
                        a[6] * p.X + a[7] * p.Y + a[8] * p.Z + b.Z);
                };
                JVec3[] d = ControlField(nx, ny, nz, affine);
                JVec3[] verts = RandomPoints(rng, 200, 0.0f);
                JVec3[] nrm;
                JVec3[] moved = Deform(nx, ny, nz, verts, null, d, out nrm);
                for (int v = 0; v < verts.Length; v++)
                {
                    JVec3 expect = verts[v] + affine(verts[v]);
                    string at = nx + "x" + ny + "x" + nz + " vertex " + v;
                    Assert.AreEqual(expect.X, moved[v].X, 2e-4, at);
                    Assert.AreEqual(expect.Y, moved[v].Y, 2e-4, at);
                    Assert.AreEqual(expect.Z, moved[v].Z, 2e-4, at);
                }
            }
        }

        [Test]
        public void ADisplacementIsNeverLargerThanTheLargestControlDisplacement()
        {
            Lcg rng = new Lcg(23u);
            int[][] grids = { new[] { 1, 1, 1 }, new[] { 2, 3, 1 }, new[] { 3, 3, 3 }, new[] { 5, 4, 5 } };
            for (int g = 0; g < grids.Length; g++)
            {
                int nx = grids[g][0];
                int ny = grids[g][1];
                int nz = grids[g][2];
                JVec3[] d = new JVec3[nx * ny * nz];
                for (int i = 0; i < d.Length; i++)
                {
                    d[i] = new JVec3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                }
                float max;
                fixed (JVec3* pd = d)
                {
                    max = JiggleFfd.MaxLength(pd, d.Length);
                }
                JVec3[] verts = RandomPoints(rng, 300, 0.4f);
                JVec3[] nrm;
                JVec3[] moved = Deform(nx, ny, nz, verts, null, d, out nrm);
                for (int v = 0; v < verts.Length; v++)
                {
                    float len = (moved[v] - verts[v]).Length();
                    Assert.Less(len, max * 1.00001f + 1e-6f, nx + "x" + ny + "x" + nz + " vertex " + v + " moved " + len + " of at most " + max);
                }
            }
        }

        [Test]
        public void APositionOutsideTheBoxTakesTheDisplacementOfTheNearestPointOnItsSurface()
        {
            Lcg rng = new Lcg(31u);
            JVec3[] d = new JVec3[27];
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = new JVec3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));
            }
            JVec3[] outside = RandomPoints(rng, 100, 0.6f);
            JVec3[] clamped = new JVec3[outside.Length];
            for (int i = 0; i < outside.Length; i++)
            {
                clamped[i] = new JVec3(
                    Math.Min(Math.Max(outside[i].X, BoxMin.X), BoxMin.X + BoxSize.X),
                    Math.Min(Math.Max(outside[i].Y, BoxMin.Y), BoxMin.Y + BoxSize.Y),
                    Math.Min(Math.Max(outside[i].Z, BoxMin.Z), BoxMin.Z + BoxSize.Z));
            }
            JVec3[] nrm;
            JVec3[] a = Deform(3, 3, 3, outside, null, d, out nrm);
            JVec3[] b = Deform(3, 3, 3, clamped, null, d, out nrm);
            for (int i = 0; i < outside.Length; i++)
            {
                JVec3 da = a[i] - outside[i];
                JVec3 db = b[i] - clamped[i];
                Assert.AreEqual(db.X, da.X, 1e-5, "vertex " + i);
                Assert.AreEqual(db.Y, da.Y, 1e-5, "vertex " + i);
                Assert.AreEqual(db.Z, da.Z, 1e-5, "vertex " + i);
            }
        }

        [Test]
        public void ANormalTurnsByTheInverseTransposeOfTheDeformation()
        {
            // The displacement (R - I) p turns every position by R about the origin, so every normal must turn by R.
            float ang = 0.5f;
            float c = (float)Math.Cos(ang);
            float s = (float)Math.Sin(ang);
            Func<JVec3, JVec3> turn = delegate(JVec3 p)
            {
                return new JVec3((c - 1.0f) * p.X + s * p.Z, 0.0f, -s * p.X + (c - 1.0f) * p.Z);
            };
            Lcg rng = new Lcg(41u);
            JVec3[] d = ControlField(3, 3, 3, turn);
            JVec3[] verts = RandomPoints(rng, 80, 0.0f);
            JVec3[] normals = new JVec3[verts.Length];
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = new JVec3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f)).NormalizeSafe(JVec3.Up);
            }
            JVec3[] outNormals;
            Deform(3, 3, 3, verts, normals, d, out outNormals);
            for (int i = 0; i < normals.Length; i++)
            {
                JVec3 n = normals[i];
                JVec3 expect = new JVec3(c * n.X + s * n.Z, n.Y, -s * n.X + c * n.Z);
                Assert.AreEqual(expect.X, outNormals[i].X, 1e-4, "normal " + i);
                Assert.AreEqual(expect.Y, outNormals[i].Y, 1e-4, "normal " + i);
                Assert.AreEqual(expect.Z, outNormals[i].Z, 1e-4, "normal " + i);
            }

            // Stretching y to twice as long makes a surface that was tilted flatter, so its normal leans toward y less.
            Func<JVec3, JVec3> stretch = delegate(JVec3 p) { return new JVec3(0.0f, p.Y, 0.0f); };
            JVec3[] ds = ControlField(3, 3, 3, stretch);
            JVec3[] one = { BoxMin + BoxSize * 0.5f };
            JVec3[] nrm1 = { new JVec3(1.0f, 1.0f, 0.0f).NormalizeSafe(JVec3.Up) };
            JVec3[] outN;
            Deform(3, 3, 3, one, nrm1, ds, out outN);
            JVec3 want = new JVec3(1.0f, 0.5f, 0.0f).NormalizeSafe(JVec3.Up);
            Assert.AreEqual(want.X, outN[0].X, 1e-4);
            Assert.AreEqual(want.Y, outN[0].Y, 1e-4);
            Assert.AreEqual(0.0, outN[0].Z, 1e-4);
        }

        [Test]
        public void TheGradientOfTheDisplacementMatchesAFiniteDifference()
        {
            Lcg rng = new Lcg(53u);
            int nx = 4;
            int ny = 3;
            int nz = 5;
            JVec3[] d = new JVec3[nx * ny * nz];
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = new JVec3(rng.Range(-0.3f, 0.3f), rng.Range(-0.3f, 0.3f), rng.Range(-0.3f, 0.3f));
            }
            float* w = stackalloc float[JiggleFfd.MaxAxis * 6];
            float* wx = w;
            float* wy = w + 5;
            float* wz = w + 10;
            float* dx = w + 15;
            float* dy = w + 20;
            float* dz = w + 25;
            fixed (JVec3* pd = d)
            {
                for (int trial = 0; trial < 25; trial++)
                {
                    float[] t = { rng.Range(0.15f, 0.85f), rng.Range(0.15f, 0.85f), rng.Range(0.15f, 0.85f) };
                    JVec3 value, gx, gy, gz;
                    JiggleFfd.Weights(nx, t[0], wx);
                    JiggleFfd.Weights(ny, t[1], wy);
                    JiggleFfd.Weights(nz, t[2], wz);
                    JiggleFfd.Derivatives(nx, t[0], dx);
                    JiggleFfd.Derivatives(ny, t[1], dy);
                    JiggleFfd.Derivatives(nz, t[2], dz);
                    JiggleFfd.DisplaceWithGradient(nx, ny, nz, wx, wy, wz, dx, dy, dz, pd, out value, out gx, out gy, out gz);
                    JVec3 plain = JiggleFfd.Displace(nx, ny, nz, wx, wy, wz, pd);
                    Assert.AreEqual(plain.X, value.X, 1e-6, "value x");
                    Assert.AreEqual(plain.Y, value.Y, 1e-6, "value y");
                    Assert.AreEqual(plain.Z, value.Z, 1e-6, "value z");

                    JVec3[] grads = { gx, gy, gz };
                    for (int axis = 0; axis < 3; axis++)
                    {
                        float h = 1.0e-3f;
                        float[] lo = { t[0], t[1], t[2] };
                        float[] hi = { t[0], t[1], t[2] };
                        lo[axis] -= h;
                        hi[axis] += h;
                        JiggleFfd.Weights(nx, lo[0], wx);
                        JiggleFfd.Weights(ny, lo[1], wy);
                        JiggleFfd.Weights(nz, lo[2], wz);
                        JVec3 a = JiggleFfd.Displace(nx, ny, nz, wx, wy, wz, pd);
                        JiggleFfd.Weights(nx, hi[0], wx);
                        JiggleFfd.Weights(ny, hi[1], wy);
                        JiggleFfd.Weights(nz, hi[2], wz);
                        JVec3 b = JiggleFfd.Displace(nx, ny, nz, wx, wy, wz, pd);
                        JVec3 fd = (b - a) * (1.0f / (2.0f * h));
                        Assert.AreEqual(fd.X, grads[axis].X, 6e-3, "axis " + axis + " x");
                        Assert.AreEqual(fd.Y, grads[axis].Y, 6e-3, "axis " + axis + " y");
                        Assert.AreEqual(fd.Z, grads[axis].Z, 6e-3, "axis " + axis + " z");
                    }
                }
            }
        }

        [Test]
        public void NoDisplacementLeavesPositionsAndNormalsAsTheyWereAndRangesCanBeSplit()
        {
            Lcg rng = new Lcg(67u);
            JVec3[] verts = RandomPoints(rng, 100, 0.1f);
            JVec3[] normals = new JVec3[verts.Length];
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = new JVec3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f)).NormalizeSafe(JVec3.Up);
            }
            JVec3[] zero = new JVec3[27];
            JVec3[] outN;
            JVec3[] moved = Deform(3, 3, 3, verts, normals, zero, out outN);
            for (int i = 0; i < verts.Length; i++)
            {
                Assert.AreEqual(Bits(verts[i].X), Bits(moved[i].X), "vertex " + i);
                Assert.AreEqual(Bits(verts[i].Y), Bits(moved[i].Y), "vertex " + i);
                Assert.AreEqual(Bits(verts[i].Z), Bits(moved[i].Z), "vertex " + i);
                Assert.AreEqual(normals[i].X, outN[i].X, 1e-6, "normal " + i);
                Assert.AreEqual(normals[i].Y, outN[i].Y, 1e-6, "normal " + i);
                Assert.AreEqual(normals[i].Z, outN[i].Z, 1e-6, "normal " + i);
            }

            // Two ranges give the bits of one, which is what lets a job split the mesh over threads.
            JVec3[] d = new JVec3[27];
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = new JVec3(rng.Range(-0.2f, 0.2f), rng.Range(-0.2f, 0.2f), rng.Range(-0.2f, 0.2f));
            }
            JVec3[] whole = Deform(3, 3, 3, verts, normals, d, out outN);
            JVec3[] coords = new JVec3[verts.Length];
            JVec3[] split = new JVec3[verts.Length];
            JVec3[] splitN = new JVec3[verts.Length];
            fixed (JVec3* pv = verts)
            fixed (JVec3* pn = normals)
            fixed (JVec3* pc = coords)
            fixed (JVec3* pd = d)
            fixed (JVec3* ps = split)
            fixed (JVec3* psn = splitN)
            {
                JiggleFfd.Coordinates(pv, verts.Length, BoxMin, BoxSize, pc);
                JiggleFfd.DeformRange(0, 37, 3, 3, 3, BoxSize, pc, pd, pv, pn, ps, psn);
                JiggleFfd.DeformRange(37, verts.Length - 37, 3, 3, 3, BoxSize, pc, pd, pv, pn, ps, psn);
            }
            for (int i = 0; i < verts.Length; i++)
            {
                Assert.AreEqual(Bits(whole[i].X), Bits(split[i].X), "vertex " + i);
                Assert.AreEqual(Bits(whole[i].Y), Bits(split[i].Y), "vertex " + i);
                Assert.AreEqual(Bits(whole[i].Z), Bits(split[i].Z), "vertex " + i);
                Assert.AreEqual(Bits(outN[i].X), Bits(splitN[i].X), "normal " + i);
            }
        }

        [Test]
        public void FitBoxPadsTheBoundsAndRefusesBadInput()
        {
            JVec3[] p = { new JVec3(0.0f, 0.0f, 1.0f), new JVec3(2.0f, 4.0f, 1.0f), new JVec3(1.0f, 1.0f, 1.0f) };
            JVec3 min;
            JVec3 size;
            fixed (JVec3* pp = p)
            {
                Assert.IsTrue(JiggleFfd.FitBox(pp, 3, 0.0f, out min, out size));
                Assert.AreEqual(0.0, min.X, 0.0);
                Assert.AreEqual(2.0, size.X, 1e-6);
                Assert.AreEqual(4.0, size.Y, 1e-6);
                Assert.AreEqual(0.0, size.Z, 0.0, "a flat mesh stays flat");
                Assert.IsTrue(JiggleFfd.FitBox(pp, 3, 0.25f, out min, out size));
                Assert.AreEqual(-0.5, min.X, 1e-6);
                Assert.AreEqual(3.0, size.X, 1e-6);
                Assert.AreEqual(-1.0, min.Y, 1e-6);
                Assert.AreEqual(6.0, size.Y, 1e-6);
                Assert.AreEqual(1.0, min.Z, 0.0);
                Assert.AreEqual(0.0, size.Z, 0.0, "padding of nothing is nothing");
                Assert.IsFalse(JiggleFfd.FitBox(pp, 0, 0.1f, out min, out size));
                Assert.AreEqual(1.0, size.X, 0.0);
                Assert.IsFalse(JiggleFfd.FitBox(null, 3, 0.1f, out min, out size));
            }
            p[1].Y = float.NaN;
            fixed (JVec3* pp = p)
            {
                Assert.IsFalse(JiggleFfd.FitBox(pp, 3, 0.1f, out min, out size));
            }

            // A flat axis has no coordinate to speak of and costs nothing.
            Assert.AreEqual(0.0, JiggleFfd.Coordinate(5.0f, 1.0f, JiggleFfd.InverseSize(0.0f)), 0.0);
            Assert.AreEqual(0.0, JiggleFfd.Coordinate(float.NaN, 1.0f, 1.0f), 0.0);
            Assert.AreEqual(1.0, JiggleFfd.Coordinate(9.0f, 1.0f, 0.5f), 0.0);
            Assert.AreEqual(0.0, JiggleFfd.Coordinate(-9.0f, 1.0f, 0.5f), 0.0);
            Assert.AreEqual(0.5, JiggleFfd.Coordinate(2.0f, 1.0f, 0.5f), 1e-7);
        }

        [Test]
        public void TheMatrixInverseUndoesTheMatrixAndRefusesAFlatOne()
        {
            JiggleMat3 m = new JiggleMat3(2.0f, 0.5f, 0.0f, -0.3f, 1.5f, 0.2f, 0.1f, 0.0f, 0.8f);
            JiggleMat3 inv;
            Assert.IsTrue(JiggleMat3.TryInvert(m, out inv));
            JVec3 v = new JVec3(0.7f, -1.2f, 2.5f);
            JVec3 back = inv.Mul(m.Mul(v));
            Assert.AreEqual(v.X, back.X, 1e-5);
            Assert.AreEqual(v.Y, back.Y, 1e-5);
            Assert.AreEqual(v.Z, back.Z, 1e-5);

            JiggleMat3 flat = new JiggleMat3(1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f);
            Assert.IsFalse(JiggleMat3.TryInvert(flat, out inv));
            JVec3 same = inv.Mul(v);
            Assert.AreEqual(v.Y, same.Y, 0.0, "a matrix that cannot be inverted gives the identity");
            JiggleMat3 nan = JiggleMat3.Identity;
            nan.M11 = float.NaN;
            Assert.IsFalse(JiggleMat3.TryInvert(nan, out inv));
        }

        [Test]
        public void ControlDisplacementIsWhereTheSolverPutAPointMinusThePoseInTheSpaceOfTheMesh()
        {
            JiggleParticleOut[] output = new JiggleParticleOut[4];
            JVec4[] pose = new JVec4[4];
            for (int i = 0; i < 4; i++)
            {
                pose[i] = new JVec4((float)i, 0.0f, 0.0f, 0.0f);
                output[i].Pos = new JVec4((float)i + 0.2f * (float)i, 0.1f, -0.4f, 0.0f);
            }
            int[] particleOfPoint = { 3, 1, 2 };
            JVec3[] result = new JVec3[3];
            JiggleMat3 worldToLocal = new JiggleMat3(0.5f, 0.0f, 0.0f, 0.0f, 0.5f, 0.0f, 0.0f, 0.0f, 0.5f);
            fixed (JiggleParticleOut* po = output)
            fixed (JVec4* pp = pose)
            fixed (int* pm = particleOfPoint)
            fixed (JVec3* pr = result)
            {
                JiggleFfd.ControlDisplacement(po, pp, pm, 3, worldToLocal, pr);
            }
            Assert.AreEqual(0.3, result[0].X, 1e-6, "point 0 is particle 3, off by 0.6 and scaled by half");
            Assert.AreEqual(0.05, result[0].Y, 1e-6);
            Assert.AreEqual(-0.2, result[0].Z, 1e-6);
            Assert.AreEqual(0.1, result[1].X, 1e-6, "point 1 is particle 1");
            Assert.AreEqual(0.2, result[2].X, 1e-6, "point 2 is particle 2");
        }

        // ------------------------------------------------------------------ tree rigs in the solver

        private sealed class Lat
        {
            public JiggleLatticeTree Tree;
            public JVec3[] Rest;
            public JiggleWorld World;
            public int Rig;
        }

        private static JiggleParams LatticeParams()
        {
            JiggleParams p = JiggleParams.Default();
            p.Stiffness = 0.5f;
            p.Stretch = 0.3f;
            p.Drag = 0.1f;
            p.AirDrag = 0.0f;
            p.Gravity = 0.0f;
            p.RootStretch = 0.0f;
            p.Mass = 1.0f;
            p.Collide = false;
            return p;
        }

        private static Lat Build(int nx, int ny, int nz, JiggleParams p)
        {
            Lat l = new Lat();
            l.Tree = JiggleLatticeGrid.BuildTree(nx, ny, nz);
            l.Rest = JiggleLatticeGrid.ParticleRest(l.Tree, new JVec3(-0.5f, -0.5f, -0.5f), new JVec3(1.0f, 1.0f, 1.0f));
            JiggleParams[] ps = new JiggleParams[l.Tree.Count - 1];
            for (int i = 0; i < ps.Length; i++)
            {
                ps[i] = p;
            }
            l.World = new JiggleWorld();
            l.Rig = l.World.AddTree(l.Rest, l.Tree.ParentsOfParticles(), ps);
            return l;
        }

        // The pose of a lattice whose object is at offset and turned by yaw about the vertical axis.
        private static JVec3[] Pose(Lat l, JVec3 offset, float yaw)
        {
            float c = (float)Math.Cos(yaw);
            float s = (float)Math.Sin(yaw);
            JVec3[] pose = new JVec3[l.Rest.Length];
            for (int i = 0; i < pose.Length; i++)
            {
                JVec3 r = l.Rest[i];
                pose[i] = new JVec3(c * r.X + s * r.Z, r.Y, -s * r.X + c * r.Z) + offset;
            }
            return pose;
        }

        private static float MaxAway(Lat l, JVec3[] pose)
        {
            float worst = 0.0f;
            for (int i = 0; i < pose.Length; i++)
            {
                float d = (l.World.WorldPos(l.Rig, i) - pose[i]).Length();
                if (d > worst)
                {
                    worst = d;
                }
            }
            return worst;
        }

        [Test]
        public void AddTreeRefusesAnythingThatIsNotATree()
        {
            JiggleWorld w = new JiggleWorld();
            JVec3[] rest = { JVec3.Zero, new JVec3(0.0f, 1.0f, 0.0f), new JVec3(0.0f, 2.0f, 0.0f) };
            JiggleParams[] p = { LatticeParams(), LatticeParams() };
            int[] ok = { 0, 1 };
            Assert.AreEqual(0, w.AddTree(rest, ok, p));
            Assert.Throws<ArgumentException>(delegate { w.AddTree(null, ok, p); }, "no rest pose");
            Assert.Throws<ArgumentException>(delegate { w.AddTree(rest, null, p); }, "no parents");
            Assert.Throws<ArgumentException>(delegate { w.AddTree(rest, ok, null); }, "no parameters");
            Assert.Throws<ArgumentException>(delegate { w.AddTree(new[] { JVec3.Zero }, new int[0], new JiggleParams[0]); }, "an anchor alone");
            Assert.Throws<ArgumentException>(delegate { w.AddTree(rest, new[] { 0 }, p); }, "too few parents");
            Assert.Throws<ArgumentException>(delegate { w.AddTree(rest, ok, new[] { p[0] }); }, "too few parameter sets");
            Assert.Throws<ArgumentException>(delegate { w.AddTree(rest, new[] { 1, 1 }, p); }, "a particle that is its own parent");
            Assert.Throws<ArgumentException>(delegate { w.AddTree(rest, new[] { 0, 2 }, p); }, "a parent that comes later");
            Assert.Throws<ArgumentException>(delegate { w.AddTree(rest, new[] { -1, 1 }, p); }, "a negative parent");

            int big = JiggleConst.MaxParticles + 1;
            JVec3[] tooMany = new JVec3[big];
            int[] parents = new int[big - 1];
            JiggleParams[] ps = new JiggleParams[big - 1];
            for (int i = 0; i < ps.Length; i++)
            {
                ps[i] = p[0];
                parents[i] = i;
            }
            Assert.Throws<ArgumentException>(delegate { w.AddTree(tooMany, parents, ps); }, "more particles than a rig holds");
        }

        [Test]
        public void ATreeAndAChainAreToldApartAndEachRefusesThePoseOfTheOther()
        {
            Lat l = Build(2, 2, 2, LatticeParams());
            Assert.IsTrue(l.World.IsTree(l.Rig));
            Assert.AreEqual(8, (int)l.World.Rigs[l.Rig].ParticleCount, "eight points, one of them the anchor, and no virtual end");
            JVec3[] chain = { JVec3.Zero, new JVec3(0.0f, -0.1f, 0.0f), new JVec3(0.0f, -0.2f, 0.0f) };
            int c = l.World.AddChain(chain, new[] { JiggleParams.Default(), JiggleParams.Default() });
            Assert.IsFalse(l.World.IsTree(c));
            Assert.Throws<ArgumentException>(delegate { l.World.SetPose(l.Rig, new JVec3[7]); }, "a chain pose for a tree");
            Assert.Throws<ArgumentException>(delegate { l.World.SetTreePose(c, new JVec3[3]); }, "a tree pose for a chain");
            Assert.Throws<ArgumentException>(delegate { l.World.SetTreePose(l.Rig, new JVec3[7]); }, "one position short");
            l.World.SetTreePose(l.Rig, l.Rest);
        }

        [Test]
        public void ATreePoseHasNoVirtualEndAndRecordsTheMoveOfTheAnchor()
        {
            Lat l = Build(3, 3, 3, LatticeParams());
            JVec3[] pose = Pose(l, new JVec3(1.0f, 2.0f, 3.0f), 0.0f);
            l.World.SetTreePose(l.Rig, pose);
            int off = (int)l.World.Rigs[l.Rig].ParticleOffset;
            for (int i = 0; i < pose.Length; i++)
            {
                JVec3 rel = pose[i] - pose[0];
                Assert.AreEqual(rel.X, l.World.PoseCurr[off + i].X, 1e-6, "particle " + i);
                Assert.AreEqual(rel.Y, l.World.PoseCurr[off + i].Y, 1e-6, "particle " + i);
                Assert.AreEqual(rel.Z, l.World.PoseCurr[off + i].Z, 1e-6, "particle " + i);
            }
            Assert.AreEqual(1.0, l.World.Rigs[l.Rig].AnchorWorld.X, 1e-6);
            Assert.AreEqual(1.0, l.World.Rigs[l.Rig].AnchorDelta.X, 1e-6, "the anchor moved one meter from the origin");
            l.World.SetTreePose(l.Rig, pose);
            Assert.AreEqual(0.0, l.World.Rigs[l.Rig].AnchorDelta.X, 1e-6, "and then it did not move");
        }

        [Test]
        public void ALatticeAtRestStaysAtRestOnBothBackends()
        {
            foreach (JiggleBackend backend in new[] { JiggleBackend.Managed, JiggleBackend.Native })
            {
                Lat l = Build(3, 3, 3, LatticeParams());
                for (int f = 0; f < 120; f++)
                {
                    l.World.SetTreePose(l.Rig, l.Rest);
                    Assert.AreEqual(JiggleConst.Ok, l.World.StepFixed(1, backend), backend + " frame " + f);
                }
                Assert.Less(MaxAway(l, l.Rest), 1e-5f, backend + " moved with nothing to move it");
            }
        }

        [Test]
        public void AJumpOfTheObjectLeavesTheOuterPointsBehindAndTheMiddleWithItThenTheyCatchUp()
        {
            foreach (JiggleBackend backend in new[] { JiggleBackend.Managed, JiggleBackend.Native })
            {
                Lat l = Build(3, 3, 3, LatticeParams());
                JVec3 jump = new JVec3(0.2f, 0.0f, 0.0f);
                JVec3[] moved = Pose(l, jump, 0.0f);
                for (int f = 0; f < 3; f++)
                {
                    l.World.SetTreePose(l.Rig, f == 0 ? l.Rest : moved);
                    Assert.AreEqual(JiggleConst.Ok, l.World.StepFixed(1, backend));
                }
                float anchorAway = (l.World.WorldPos(l.Rig, 0) - moved[0]).Length();
                Assert.Less(anchorAway, 1e-5f, backend + ": the middle point is the anchor and goes with the object");

                float worst = MaxAway(l, moved);
                Assert.Greater(worst, 0.02f, backend + ": the outer points should lag the jump");
                Assert.Less(worst, 0.2f * 1.5f, backend + ": no point is thrown further than the jump");
                int last = l.Tree.Count - 1;
                JVec3 lag = l.World.WorldPos(l.Rig, last) - moved[last];
                Assert.Less(lag.X, 0.0f, backend + ": the corner is left behind, against the way the object went");

                for (int f = 0; f < 400; f++)
                {
                    l.World.SetTreePose(l.Rig, moved);
                    Assert.AreEqual(JiggleConst.Ok, l.World.StepFixed(1, backend));
                }
                Assert.Less(MaxAway(l, moved), 1e-3f, backend + ": the lattice settles back to its rest shape");
            }
        }

        [Test]
        public void ATurnOfTheObjectBendsTheCornersAndLeavesTheMiddle()
        {
            Lat l = Build(3, 3, 3, LatticeParams());
            l.World.SetTreePose(l.Rig, l.Rest);
            l.World.StepFixed(1, JiggleBackend.Managed);
            JVec3[] turned = Pose(l, JVec3.Zero, 0.4f);
            l.World.SetTreePose(l.Rig, turned);
            l.World.StepFixed(1, JiggleBackend.Managed);
            Assert.Less((l.World.WorldPos(l.Rig, 0) - turned[0]).Length(), 1e-5f, "the middle does not move when the object turns about it");
            int corner = l.Tree.Count - 1;
            Assert.Greater((l.World.WorldPos(l.Rig, corner) - turned[corner]).Length(), 0.02f, "a corner lags the turn");
            for (int f = 0; f < 400; f++)
            {
                l.World.SetTreePose(l.Rig, turned);
                l.World.StepFixed(1, JiggleBackend.Managed);
            }
            Assert.Less(MaxAway(l, turned), 1e-3f);
        }

        [Test]
        public void PointsPullTheirNeighboursWhenTheyHaveAMassAndNotWhenTheyDoNot()
        {
            float coupledMove = 0.0f;
            float freeMove = 0.0f;
            for (int variant = 0; variant < 2; variant++)
            {
                JiggleParams p = LatticeParams();
                p.Mass = variant == 0 ? 1.0f : 0.0f;
                Lat l = Build(3, 3, 3, p);
                int corner = l.Tree.Count - 1;
                int parent = l.Tree.Parent[corner];
                Assert.Greater(l.Tree.Parent[parent], 0, "the parent of the corner does not hang on the anchor");
                l.World.SetTreePose(l.Rig, l.Rest);
                l.World.StepFixed(1, JiggleBackend.Managed);
                // A blow on the corner alone: it is put out of place by hand.
                int idx = (int)l.World.Rigs[l.Rig].ParticleOffset + corner;
                JVec4 s = l.World.State[idx].Pos;
                s.X += 0.2f;
                l.World.State[idx].Pos = s;
                JVec4 q = l.World.State[idx].Prev;
                q.X += 0.2f;
                l.World.State[idx].Prev = q;
                l.World.SetTreePose(l.Rig, l.Rest);
                l.World.StepFixed(1, JiggleBackend.Managed);
                float moved = (l.World.WorldPos(l.Rig, parent) - l.Rest[parent]).Length();
                if (variant == 0)
                {
                    coupledMove = moved;
                }
                else
                {
                    freeMove = moved;
                }
            }
            Assert.Greater(coupledMove, 1e-3f, "with masses the corner drags its parent");
            Assert.Less(freeMove, 1e-6f, "without masses the corner cannot move its parent");
        }

        [Test]
        public void ASleepingLatticeOutputsItsPoseExactly()
        {
            foreach (JiggleBackend backend in new[] { JiggleBackend.Managed, JiggleBackend.Native })
            {
                Lat l = Build(3, 3, 3, LatticeParams());
                l.World.Cfg.SleepSpeed = 0.002f;
                l.World.Cfg.SleepDeviation = 0.05f;
                l.World.Cfg.SleepFrames = 6;
                l.World.Cfg.WakeMotion = 0.01f;
                JVec3[] moved = Pose(l, new JVec3(0.1f, 0.0f, 0.0f), 0.0f);
                for (int f = 0; f < 600; f++)
                {
                    l.World.SetTreePose(l.Rig, f < 2 ? l.Rest : moved);
                    Assert.AreEqual(JiggleConst.Ok, l.World.StepFixed(1, backend));
                }
                Assert.AreNotEqual(0u, l.World.Rigs[l.Rig].Flags & JiggleConst.RigAsleep, backend + " should be asleep by now");
                int off = (int)l.World.Rigs[l.Rig].ParticleOffset;
                for (int i = 0; i < moved.Length; i++)
                {
                    JVec3 pos = JVec3.From4(l.World.Out[off + i].Pos);
                    JVec3 pose = JVec3.From4(l.World.PoseCurr[off + i]);
                    Assert.AreEqual(Bits(pose.X), Bits(pos.X), backend + " particle " + i);
                    Assert.AreEqual(Bits(pose.Y), Bits(pos.Y), backend + " particle " + i);
                    Assert.AreEqual(Bits(pose.Z), Bits(pos.Z), backend + " particle " + i);
                }
            }
        }

        private static string FirstDifference(JiggleWorld a, JiggleWorld b)
        {
            for (int i = 0; i < a.State.Length; i++)
            {
                if (Bits(a.State[i].Pos.X) != Bits(b.State[i].Pos.X) || Bits(a.State[i].Pos.Y) != Bits(b.State[i].Pos.Y)
                    || Bits(a.State[i].Pos.Z) != Bits(b.State[i].Pos.Z))
                {
                    return "state pos " + i;
                }
                if (Bits(a.State[i].Prev.X) != Bits(b.State[i].Prev.X) || Bits(a.State[i].Prev.Y) != Bits(b.State[i].Prev.Y)
                    || Bits(a.State[i].Prev.Z) != Bits(b.State[i].Prev.Z))
                {
                    return "state prev " + i;
                }
                if (Bits(a.Out[i].Pos.X) != Bits(b.Out[i].Pos.X) || Bits(a.Out[i].Pos.Y) != Bits(b.Out[i].Pos.Y)
                    || Bits(a.Out[i].Pos.Z) != Bits(b.Out[i].Pos.Z))
                {
                    return "out pos " + i;
                }
            }
            for (int r = 0; r < a.Rigs.Length; r++)
            {
                if (a.Rigs[r].Flags != b.Rigs[r].Flags || a.Rigs[r].SleepCounter != b.Rigs[r].SleepCounter)
                {
                    return "rig " + r;
                }
            }
            return null;
        }

        [Test]
        public void BranchingTreesGiveTheSameBitsOnBothBackends()
        {
            int[][] grids =
            {
                new[] { 3, 3, 3 }, new[] { 2, 4, 3 }, new[] { 5, 5, 5 }, new[] { 2, 1, 1 }, new[] { 4, 1, 3 },
                new[] { 1, 5, 1 }, new[] { 2, 2, 2 },
            };
            for (int g = 0; g < grids.Length; g++)
            {
                JiggleParams p = LatticeParams();
                p.Gravity = 0.5f;
                p.Collide = true;
                p.CollisionRadius = 0.08f;
                p.Soften = 0.2f;
                p.AngleLimited = (g % 2) == 0;
                p.AngleLimit = 0.6f;
                Lat m = Build(grids[g][0], grids[g][1], grids[g][2], p);
                Lat n = Build(grids[g][0], grids[g][1], grids[g][2], p);
                JiggleWorld[] worlds = { m.World, n.World };
                for (int w = 0; w < 2; w++)
                {
                    uint s = (uint)worlds[w].AddCollider(JiggleShapes.Sphere(new JVec3(0.45f, 0.1f, 0.0f), 0.3f));
                    worlds[w].SetColliderList(0, new[] { s });
                    worlds[w].AddField(JiggleShapes.Field(
                        JiggleConst.FieldDirectional, JiggleConst.FieldAcceleration, JVec3.Zero, new JVec3(1.0f, 0.0f, 0.3f),
                        0.7f, 2.0f, 0.0f, 0u, 0.5f, 1.5f));
                    worlds[w].Cfg.SleepSpeed = 0.002f;
                    worlds[w].Cfg.SleepDeviation = 0.05f;
                    worlds[w].Cfg.SleepFrames = 12;
                    worlds[w].Cfg.WakeMotion = 0.01f;
                    worlds[w].Cfg.CouplingPasses = 1 + (uint)(g % 3);
                }
                for (int f = 0; f < 240; f++)
                {
                    float t = (float)f * 0.05f;
                    JVec3 off = f < 120 ? new JVec3((float)Math.Sin(t) * 0.15f, (float)Math.Cos(t * 1.3) * 0.05f, 0.0f) : JVec3.Zero;
                    float yaw = f < 120 ? (float)Math.Sin(t * 0.7) * 0.4f : 0.0f;
                    JVec3[] pose = Pose(m, off, yaw);
                    m.World.SetTreePose(0, pose);
                    n.World.SetTreePose(0, pose);
                    Assert.AreEqual(JiggleConst.Ok, m.World.StepFixed(1, JiggleBackend.Managed));
                    Assert.AreEqual(JiggleConst.Ok, n.World.StepFixed(1, JiggleBackend.Native));
                    string diff = FirstDifference(m.World, n.World);
                    Assert.IsTrue(diff == null, grids[g][0] + "x" + grids[g][1] + "x" + grids[g][2] + " frame " + f + ": " + diff);
                    for (int i = 0; i < pose.Length; i++)
                    {
                        Assert.IsTrue(m.World.WorldPos(0, i).IsFinite(), "frame " + f + " particle " + i + " is not finite");
                    }
                }
            }
        }

        [Test]
        public void HostAndWorldBuildTheSameTreeAndTheHostRebakesItsParametersInPlace()
        {
            Lat l = Build(3, 2, 4, LatticeParams());
            int[] parents = l.Tree.ParentsOfParticles();
            JiggleParams[] ps = new JiggleParams[parents.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                ps[i] = LatticeParams();
            }
            using (JiggleHost h = new JiggleHost(1, 4))
            {
                int rig = h.AddTree(l.Rest, parents, ps);
                Assert.AreEqual(0, rig);
                Assert.IsTrue(h.IsTree(rig));
                Assert.AreEqual(l.Rest.Length, h.ParticleCount);
                fixed (JiggleParticleDef* wd = l.World.Defs)
                fixed (JiggleParticleState* ws = l.World.State)
                {
                    Assert.IsTrue(SameBytes(wd, h.Defs, (long)h.ParticleCount * sizeof(JiggleParticleDef)), "definitions");
                    Assert.IsTrue(SameBytes(ws, h.State, (long)h.ParticleCount * sizeof(JiggleParticleState)), "state");
                }
                Assert.AreEqual(l.World.Rigs[0].Extent, h.Rigs[0].Extent, 0.0);
                for (int i = 1; i < h.ParticleCount; i++)
                {
                    Assert.AreEqual(0u, h.Defs[i].Flags & JiggleConst.FlagRoot, "no particle is a root unless asked for, particle " + i);
                }

                JiggleParams stiff = LatticeParams();
                stiff.Stiffness = 0.9f;
                stiff.Mass = 3.0f;
                for (int i = 0; i < ps.Length; i++)
                {
                    ps[i] = stiff;
                }
                h.SetTreeParams(rig, ps);
                Assert.AreEqual(0.81, h.Defs[2].AngleElasticity, 1e-6, "stiffness squared");
                Assert.AreEqual(3.0, h.Defs[2].Mass, 0.0);
                Assert.AreEqual(l.Tree.Parent[5], h.Defs[5].Parent, "the parents are kept");
                Assert.Throws<ArgumentException>(delegate { h.SetTreeParams(rig, new JiggleParams[2]); }, "wrong number of sets");
                int chain = h.AddChain(
                    new[] { JVec3.Zero, new JVec3(0.0f, -0.1f, 0.0f), new JVec3(0.0f, -0.2f, 0.0f) },
                    new[] { JiggleParams.Default(), JiggleParams.Default() });
                Assert.IsFalse(h.IsTree(chain));
                Assert.Throws<ArgumentException>(delegate { h.SetTreeParams(chain, ps); }, "a chain is not a tree");
                Assert.Throws<ArgumentException>(delegate { h.SetParams(rig, new JiggleParams[2]); }, "a tree is not a chain");
            }

            // With pinRoots the particles that hang on the anchor are root particles, and a rebake keeps them so.
            using (JiggleHost h = new JiggleHost(1, 4))
            {
                int rig = h.AddTree(l.Rest, parents, ps, true);
                int pinned = 0;
                for (int i = 1; i < h.ParticleCount; i++)
                {
                    bool root = (h.Defs[i].Flags & JiggleConst.FlagRoot) != 0;
                    Assert.AreEqual(h.Defs[i].Parent == 0, root, "particle " + i);
                    if (root)
                    {
                        pinned++;
                    }
                }
                Assert.Greater(pinned, 0);
                h.SetTreeParams(rig, ps);
                for (int i = 1; i < h.ParticleCount; i++)
                {
                    Assert.AreEqual(h.Defs[i].Parent == 0, (h.Defs[i].Flags & JiggleConst.FlagRoot) != 0, "after the rebake, particle " + i);
                }
            }
        }

        private static bool SameBytes(void* a, void* b, long len)
        {
            byte* x = (byte*)a;
            byte* y = (byte*)b;
            for (long i = 0; i < len; i++)
            {
                if (x[i] != y[i])
                {
                    return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ the whole path

        [Test]
        public void ADeformedMeshFollowsTheObjectWithALagAndSettlesBack()
        {
            Lat l = Build(3, 3, 3, LatticeParams());
            Lcg rng = new Lcg(77u);
            // A mesh that fills the box: its eight corners first, then random points inside.
            JVec3[] verts = new JVec3[308];
            for (int i = 0; i < 8; i++)
            {
                verts[i] = new JVec3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
            }
            for (int i = 8; i < verts.Length; i++)
            {
                verts[i] = new JVec3(rng.Range(-0.5f, 0.5f), rng.Range(-0.5f, 0.5f), rng.Range(-0.5f, 0.5f));
            }
            JVec3[] coords = new JVec3[verts.Length];
            JVec3[] outPos = new JVec3[verts.Length];
            JVec3[] disp = new JVec3[l.Tree.Count];
            int[] particleOfPoint = l.Tree.ParticleOfPoint;
            JVec3 jump = new JVec3(0.2f, 0.0f, 0.0f);
            JVec3[] moved = Pose(l, jump, 0.0f);
            JVec3 min = new JVec3(-0.5f, -0.5f, -0.5f);
            JVec3 size = new JVec3(1.0f, 1.0f, 1.0f);
            float maxPeak = 0.0f;
            for (int f = 0; f < 480; f++)
            {
                l.World.SetTreePose(l.Rig, f < 2 ? l.Rest : moved);
                l.World.StepFixed(1, JiggleBackend.Managed);
                int off = (int)l.World.Rigs[l.Rig].ParticleOffset;
                float peakControl;
                fixed (JiggleParticleOut* po = &l.World.Out[off])
                fixed (JVec4* pp = &l.World.PoseCurr[off])
                fixed (int* pm = particleOfPoint)
                fixed (JVec3* pd = disp)
                fixed (JVec3* pv = verts)
                fixed (JVec3* pc = coords)
                fixed (JVec3* pr = outPos)
                {
                    JiggleFfd.ControlDisplacement(po, pp, pm, disp.Length, JiggleMat3.Identity, pd);
                    peakControl = JiggleFfd.MaxLength(pd, disp.Length);
                    JiggleFfd.Coordinates(pv, verts.Length, min, size, pc);
                    JiggleFfd.DeformRange(0, verts.Length, 3, 3, 3, size, pc, pd, pv, null, pr, null);
                }
                for (int v = 0; v < verts.Length; v++)
                {
                    float len = (outPos[v] - verts[v]).Length();
                    Assert.Less(len, peakControl * 1.00001f + 1e-6f, "frame " + f + " vertex " + v);
                }
                if (f == 3)
                {
                    // The corners of the box are the corner control points, which a lattice interpolates, so a corner of the
                    // mesh moves exactly as its control point does, and every one of them is left behind against the jump.
                    for (int c = 0; c < 8; c++)
                    {
                        int point = (c & 1) * 2 + ((c >> 1) & 1) * 6 + ((c >> 2) & 1) * 18;
                        JVec3 expect = disp[point];
                        JVec3 got = outPos[c] - verts[c];
                        Assert.AreEqual(expect.X, got.X, 1e-5, "corner " + c);
                        Assert.AreEqual(expect.Y, got.Y, 1e-5, "corner " + c);
                        Assert.AreEqual(expect.Z, got.Z, 1e-5, "corner " + c);
                        Assert.Less(got.X, -0.01f, "corner " + c + " is left behind against the jump");
                    }
                }
                if (peakControl > maxPeak)
                {
                    maxPeak = peakControl;
                }
            }
            Assert.Greater(maxPeak, 0.02f, "the lattice moved");
            for (int v = 0; v < verts.Length; v++)
            {
                Assert.Less((outPos[v] - verts[v]).Length(), 1e-3f, "vertex " + v + " is back at rest at the end");
            }
        }
    }
}
