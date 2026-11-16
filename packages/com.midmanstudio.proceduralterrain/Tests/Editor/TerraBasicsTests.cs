// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/Editor/TerraBasicsTests.cs"
// ============================================================================
using System;
using NUnit.Framework;

namespace MidManStudio.ProceduralTerrain.Tests
{
    [TestFixture]
    public class TerraBasicsTests
    {
        [Test]
        public void HashIsDeterministicAndSpreadsOut()
        {
            Assert.AreEqual(TerraHash.Hash(5UL, 3L, 4L, 5L), TerraHash.Hash(5UL, 3L, 4L, 5L));
            Assert.AreNotEqual(TerraHash.Hash(5UL, 3L, 4L, 5L), TerraHash.Hash(6UL, 3L, 4L, 5L));
            Assert.AreNotEqual(TerraHash.Hash(5UL, 3L, 4L, 5L), TerraHash.Hash(5UL, 4L, 3L, 5L));
            double sum = 0.0;
            int n = 20000;
            for (int i = 0; i < n; i++)
            {
                double u = TerraHash.ToUnit(TerraHash.Hash(9UL, i, -i, 7L));
                Assert.IsTrue(u >= 0.0 && u < 1.0, "unit out of range " + u);
                sum += u;
                double s = TerraHash.ToSigned(TerraHash.Hash(9UL, i, 1L));
                Assert.IsTrue(s >= -1.0 && s < 1.0, "signed out of range " + s);
            }
            Assert.AreEqual(0.5, sum / n, 0.01);
        }

        [Test]
        public void HashKeyIsUniqueForNeighbouringCells()
        {
            System.Collections.Generic.HashSet<ulong> seen = new System.Collections.Generic.HashSet<ulong>();
            for (long a = -50; a <= 50; a++)
            {
                for (long b = -50; b <= 50; b++)
                {
                    Assert.IsTrue(seen.Add(TerraHash.Key(a, b)), "key collision at " + a + "," + b);
                }
            }
        }

        [Test]
        public void RngIsDeterministicAndStaysInRange()
        {
            TerraRng a = new TerraRng(42UL);
            TerraRng b = new TerraRng(42UL);
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(a.NextU64(), b.NextU64());
            }
            TerraRng r = new TerraRng(1UL);
            for (int i = 0; i < 1000; i++)
            {
                int k = r.NextInt(7);
                Assert.IsTrue(k >= 0 && k < 7);
                double d = r.Range(2.0, 3.0);
                Assert.IsTrue(d >= 2.0 && d < 3.0);
            }
        }

        [Test]
        public void GradientEvaluatesStopsAndEnds()
        {
            TerraGradient g = TerraGradient.Two(new TerraColor(0f, 0f, 0f, 1f), new TerraColor(1f, 1f, 1f, 1f));
            Assert.AreEqual(0.0, g.Evaluate(0f).r, 1e-6);
            Assert.AreEqual(1.0, g.Evaluate(1f).r, 1e-6);
            Assert.AreEqual(0.5, g.Evaluate(0.5f).g, 1e-6);
            Assert.AreEqual(0.0, g.Evaluate(-3f).r, 1e-6);
            Assert.AreEqual(1.0, g.Evaluate(9f).b, 1e-6);
            TerraGradient solid = TerraGradient.Solid(TerraColor.FromHex(0xFF8000));
            Assert.AreEqual(1.0, solid.Evaluate(0.3f).r, 1e-6);
            Assert.AreEqual(128.0 / 255.0, solid.Evaluate(0.3f).g, 1e-6);
            Assert.AreEqual(0.0, solid.Evaluate(0.3f).b, 1e-6);
        }

        [Test]
        public void CurveIdentityIsLinearAndSmoothCurveNeverOvershoots()
        {
            TerraCurveEval id = new TerraCurve().Compile();
            for (int i = 0; i <= 20; i++)
            {
                Assert.AreEqual(i / 20.0, id.Evaluate(i / 20.0), 1e-9);
            }
            TerraCurve c = new TerraCurve(true,
                new TerraCurveKey(0f, 0f), new TerraCurveKey(0.4f, 0.05f), new TerraCurveKey(0.6f, 0.9f), new TerraCurveKey(1f, 1f));
            TerraCurveEval e = c.Compile();
            double previous = -1.0;
            for (int i = 0; i <= 1000; i++)
            {
                double v = e.Evaluate(i / 1000.0);
                Assert.IsTrue(v >= -1e-9 && v <= 1.0 + 1e-9, "overshoot " + v);
                Assert.IsTrue(v >= previous - 1e-9, "not monotone at " + i);
                previous = v;
            }
            Assert.AreEqual(0.05, e.Evaluate(0.4), 1e-6);
            Assert.AreEqual(0.9, e.Evaluate(0.6), 1e-6);
            TerraCurveEval lin = new TerraCurve(false, new TerraCurveKey(0f, 0f), new TerraCurveKey(0.5f, 1f), new TerraCurveKey(1f, 0f)).Compile();
            Assert.AreEqual(0.5, lin.Evaluate(0.25), 1e-9);
            Assert.AreEqual(0.5, lin.Evaluate(0.75), 1e-9);
        }

        [Test]
        public void FalloffRadialAndEdge()
        {
            TerraFalloffSettings radial = new TerraFalloffSettings();
            radial.mode = TerraFalloffMode.Radial;
            radial.radius = 100.0;
            Assert.AreEqual(1.0, TerraFalloff.Weight(radial, 0.0, 0.0), 1e-12);
            Assert.AreEqual(0.0, TerraFalloff.Weight(radial, 100.0, 0.0), 1e-12);
            Assert.AreEqual(0.0, TerraFalloff.Weight(radial, 300.0, 40.0), 1e-12);
            Assert.AreEqual(0.75, TerraFalloff.Weight(radial, 50.0, 0.0), 1e-12);
            TerraFalloffSettings edge = new TerraFalloffSettings();
            edge.mode = TerraFalloffMode.Edge;
            edge.halfExtentX = 100.0;
            edge.halfExtentZ = 100.0;
            edge.border = 50.0;
            edge.exponent = 1.0;
            Assert.AreEqual(1.0, TerraFalloff.Weight(edge, 0.0, 0.0), 1e-12);
            Assert.AreEqual(1.0, TerraFalloff.Weight(edge, 50.0, 0.0), 1e-12);
            Assert.AreEqual(0.5, TerraFalloff.Weight(edge, 75.0, 0.0), 1e-12);
            Assert.AreEqual(0.0, TerraFalloff.Weight(edge, 100.0, 0.0), 1e-12);
            Assert.AreEqual(0.0, TerraFalloff.Weight(edge, 0.0, -120.0), 1e-12);
            edge.groundValue = 0.1;
            Assert.AreEqual(0.1, TerraFalloff.Apply(edge, 100.0, 0.0, 0.9), 1e-12);
            Assert.AreEqual(0.9, TerraFalloff.Apply(edge, 0.0, 0.0, 0.9), 1e-12);
            Assert.AreEqual(0.7, TerraFalloff.Apply(null, 0.0, 0.0, 0.7), 1e-12);
        }

        [Test]
        public void NoiseIsPureAndInRange()
        {
            TerraNoiseSettings s = new TerraNoiseSettings();
            TerraNoise a = new TerraNoise(11, s);
            TerraNoise b = new TerraNoise(11, s);
            TerraNoise other = new TerraNoise(12, s);
            int different = 0;
            for (int i = 0; i < 400; i++)
            {
                double x = i * 37.31 - 5000.0;
                double z = i * -19.77 + 3000.0;
                double v = a.SampleRaw(x, z);
                Assert.IsTrue(v >= 0.0 && v <= 1.0, "raw out of range " + v);
                Assert.AreEqual(v, b.SampleRaw(x, z));
                // Asking for other points in between changes nothing: no state, no normalisation from data.
                a.SampleRaw(x + 1.0, z - 1.0);
                Assert.AreEqual(v, a.SampleRaw(x, z));
                if (v != other.SampleRaw(x, z))
                {
                    different++;
                }
            }
            Assert.Greater(different, 390, "a different seed should give different noise");
        }

        [Test]
        public void NoiseIsContinuousAcrossLatticeLines()
        {
            // No jump anywhere, in particular none where the integer lattice of an octave changes cell.
            foreach (TerraNoiseKind kind in new[] { TerraNoiseKind.Gradient, TerraNoiseKind.Value })
            {
                TerraNoiseSettings s = new TerraNoiseSettings();
                s.kind = kind;
                s.scale = 50.0;
                TerraNoise n = new TerraNoise(3, s);
                double previous = n.SampleRaw(-400.0, 17.0);
                for (int i = 1; i <= 8000; i++)
                {
                    double v = n.SampleRaw(-400.0 + i * 0.1, 17.0);
                    Assert.IsTrue(Math.Abs(v - previous) < 0.03, kind + " jumps by " + Math.Abs(v - previous) + " at step " + i);
                    previous = v;
                }
            }
        }

        [Test]
        public void NoiseModesHaveTheirShape()
        {
            TerraNoiseSettings s = new TerraNoiseSettings();
            s.octaves = 1;
            s.scale = 100.0;
            TerraNoise fbm = new TerraNoise(5, s);
            s.mode = TerraNoiseMode.Billow;
            TerraNoise billow = new TerraNoise(5, s);
            s.mode = TerraNoiseMode.Ridged;
            TerraNoise ridged = new TerraNoise(5, s);
            for (int i = 0; i < 300; i++)
            {
                double x = i * 7.1;
                double z = i * -3.3;
                double f = fbm.SampleRaw(x, z);
                Assert.AreEqual(Math.Abs(f * 2.0 - 1.0), billow.SampleRaw(x, z), 1e-12);
                Assert.AreEqual(1.0 - Math.Abs(f * 2.0 - 1.0), ridged.SampleRaw(x, z), 1e-12);
            }
        }

        [Test]
        public void NoisePersistenceKindsResolve()
        {
            TerraNoiseSettings s = new TerraNoiseSettings();
            s.persistenceKind = TerraPersistenceKind.Quarter;
            Assert.AreEqual(0.25, TerraNoise.ResolvePersistence(s), 1e-12);
            s.persistenceKind = TerraPersistenceKind.Half;
            Assert.AreEqual(0.5, TerraNoise.ResolvePersistence(s), 1e-12);
            s.persistenceKind = TerraPersistenceKind.InverseSqrt;
            Assert.AreEqual(Math.Sqrt(0.5), TerraNoise.ResolvePersistence(s), 1e-12);
            s.persistenceKind = TerraPersistenceKind.One;
            Assert.AreEqual(1.0, TerraNoise.ResolvePersistence(s), 1e-12);
            s.persistenceKind = TerraPersistenceKind.Custom;
            s.persistence = 0.3;
            Assert.AreEqual(0.3, TerraNoise.ResolvePersistence(s), 1e-12);
        }

        [Test]
        public void NoiseRemapStretchesTheMeasuredRange()
        {
            TerraNoiseSettings s = new TerraNoiseSettings();
            s.octaves = 5;
            TerraNoise raw = new TerraNoise(21, s);
            double low;
            double high;
            raw.MeasureRange(0.01, 0.99, 60000, out low, out high);
            // Adding octaves pulls the sum toward the middle: the practical range is well inside 0..1.
            Assert.IsTrue(low > 0.15 && low < 0.45, "low " + low);
            Assert.IsTrue(high > 0.55 && high < 0.85, "high " + high);
            s.remapMin = low;
            s.remapMax = high;
            TerraNoise stretched = new TerraNoise(21, s);
            int inside = 0;
            int total = 20000;
            double min = 1.0;
            double max = 0.0;
            for (int i = 0; i < total; i++)
            {
                double v = stretched.Sample01(i * 9.13 - 9000.0, i * 4.71 + 700.0);
                Assert.IsTrue(v >= 0.0 && v <= 1.0);
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                if (v > 0.0 && v < 1.0)
                {
                    inside++;
                }
            }
            Assert.Less(min, 0.02);
            Assert.Greater(max, 0.98);
            Assert.Greater(inside, total * 9 / 10);
        }

        [Test]
        public void OctaveCountIsClamped()
        {
            TerraNoiseSettings s = new TerraNoiseSettings();
            s.octaves = 500;
            TerraNoise n = new TerraNoise(1, s);
            double v = n.SampleRaw(10.0, 20.0);
            Assert.IsTrue(v >= 0.0 && v <= 1.0);
            s.octaves = -3;
            n = new TerraNoise(1, s);
            v = n.SampleRaw(10.0, 20.0);
            Assert.IsTrue(v >= 0.0 && v <= 1.0);
        }
    }
}
