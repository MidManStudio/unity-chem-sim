// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/Editor/TerraHeightTests.cs"
// ============================================================================
using System;
using NUnit.Framework;

namespace MidManStudio.ProceduralTerrain.Tests
{
    [TestFixture]
    public class TerraHeightTests
    {
        private static TerraHeightSettings Hills()
        {
            TerraHeightSettings h = new TerraHeightSettings();
            h.noise.scale = 120.0;
            h.noise.octaves = 4;
            h.heightScale = 80.0;
            return h;
        }

        [Test]
        public void HeightIsAPureFunctionOfPosition()
        {
            TerraHeightField a = new TerraHeightField(5, Hills());
            TerraHeightField b = new TerraHeightField(5, Hills());
            for (int i = 0; i < 200; i++)
            {
                double x = i * 31.3 - 3000.0;
                double z = i * -17.9 + 1000.0;
                Assert.AreEqual(a.Height(x, z), b.Height(x, z));
                Assert.AreEqual(a.Height(x, z), a.Height(x, z));
            }
        }

        [Test]
        public void HeightFollowsCurveBaseAndScale()
        {
            TerraHeightSettings h = Hills();
            h.baseHeight = -10.0;
            h.heightScale = 50.0;
            h.curve = new TerraCurve(false, new TerraCurveKey(0f, 0f), new TerraCurveKey(1f, 0.5f));
            TerraHeightField f = new TerraHeightField(2, h);
            for (int i = 0; i < 200; i++)
            {
                double x = i * 13.7;
                double z = i * 9.1;
                double n = f.NoiseValue(x, z);
                Assert.AreEqual(-10.0 + 50.0 * (n * 0.5), f.Height(x, z), 1e-9);
            }
            Assert.AreEqual(0.0, f.ValueOfHeight(-10.0), 1e-12);
            Assert.AreEqual(1.0, f.ValueOfHeight(40.0), 1e-12);
            Assert.AreEqual(0.0, f.ValueOfHeight(-99.0), 1e-12);
            Assert.AreEqual(40.0, f.HeightOfValue(1.0), 1e-12);
        }

        [Test]
        public void PadFlattensToTheTerrainAtItsCentre()
        {
            TerraHeightSettings h = Hills();
            TerraHeightField plain = new TerraHeightField(8, h);
            double target = plain.Height(100.0, 50.0);
            TerraStamp pad = new TerraStamp();
            pad.kind = TerraStampKind.Pad;
            pad.x = 100.0;
            pad.z = 50.0;
            pad.radius = 30.0;
            pad.falloff = 20.0;
            h.stamps = new[] { pad };
            TerraHeightField f = new TerraHeightField(8, h);
            for (int i = 0; i < 40; i++)
            {
                double a = i * 0.7;
                double x = 100.0 + Math.Cos(a) * 25.0;
                double z = 50.0 + Math.Sin(a) * 25.0;
                Assert.AreEqual(target, f.Height(x, z), 1e-9, "inside the pad");
            }
            Assert.AreEqual(plain.Height(400.0, 400.0), f.Height(400.0, 400.0), 1e-12);
            // Beyond the pad the ground blends back: halfway through the falloff it lies between the two.
            double mx = 100.0 + 40.0;
            double mz = 50.0;
            double lo = Math.Min(target, plain.Height(mx, mz)) - 1e-9;
            double hi = Math.Max(target, plain.Height(mx, mz)) + 1e-9;
            Assert.IsTrue(f.Height(mx, mz) >= lo && f.Height(mx, mz) <= hi);
        }

        [Test]
        public void PadWithFixedHeightAndOffset()
        {
            TerraHeightSettings h = Hills();
            TerraStamp pad = new TerraStamp();
            pad.kind = TerraStampKind.Pad;
            pad.shape = TerraStampShape.Box;
            pad.halfX = 20.0;
            pad.halfZ = 10.0;
            pad.autoHeight = false;
            pad.targetHeight = 33.0;
            pad.heightOffset = 1.5;
            pad.falloff = 5.0;
            h.stamps = new[] { pad };
            TerraHeightField f = new TerraHeightField(8, h);
            Assert.AreEqual(34.5, f.Height(0.0, 0.0), 1e-9);
            Assert.AreEqual(34.5, f.Height(19.0, -9.0), 1e-9);
        }

        [Test]
        public void RoadIsLevelAcrossAndFollowsTheGroundAlong()
        {
            TerraHeightSettings h = Hills();
            TerraStamp road = new TerraStamp();
            road.kind = TerraStampKind.Road;
            road.points = new[] { new TerraPoint(-200.0, 0.0), new TerraPoint(200.0, 0.0) };
            road.width = 8.0;
            road.falloff = 6.0;
            h.stamps = new[] { road };
            TerraHeightField plain = new TerraHeightField(4, Hills());
            TerraHeightField f = new TerraHeightField(4, h);
            for (int i = -10; i <= 10; i++)
            {
                double x = i * 17.0;
                double centre = f.Height(x, 0.0);
                Assert.AreEqual(plain.Height(x, 0.0), centre, 1e-9, "the road follows the ground along its line");
                Assert.AreEqual(centre, f.Height(x, 3.9), 1e-9, "level across the road");
                Assert.AreEqual(centre, f.Height(x, -3.9), 1e-9, "level across the road");
            }
            Assert.AreEqual(plain.Height(0.0, 300.0), f.Height(0.0, 300.0), 1e-12);
        }

        [Test]
        public void CarveAndMoundMoveTheGroundByTheirAmount()
        {
            TerraHeightSettings h = Hills();
            TerraStamp pit = new TerraStamp();
            pit.kind = TerraStampKind.Carve;
            pit.x = 0.0;
            pit.z = 0.0;
            pit.radius = 20.0;
            pit.amount = 6.0;
            TerraStamp hill = new TerraStamp();
            hill.kind = TerraStampKind.Mound;
            hill.x = 500.0;
            hill.z = 0.0;
            hill.radius = 20.0;
            hill.amount = 12.0;
            hill.strength = 0.5f;
            TerraStamp river = new TerraStamp();
            river.kind = TerraStampKind.Carve;
            river.points = new[] { new TerraPoint(-300.0, 300.0), new TerraPoint(300.0, 300.0) };
            river.width = 10.0;
            river.amount = 4.0;
            h.stamps = new[] { pit, hill, river };
            TerraHeightField plain = new TerraHeightField(4, Hills());
            TerraHeightField f = new TerraHeightField(4, h);
            Assert.AreEqual(plain.Height(3.0, 4.0) - 6.0, f.Height(3.0, 4.0), 1e-9);
            Assert.AreEqual(plain.Height(500.0, 5.0) + 6.0, f.Height(500.0, 5.0), 1e-9);
            Assert.AreEqual(plain.Height(20.0, 300.0) - 4.0, f.Height(20.0, 300.0), 1e-9);
            Assert.AreEqual(plain.Height(20.0, 340.0), f.Height(20.0, 340.0), 1e-12);
        }

        [Test]
        public void StampsApplyInListOrderAndDisabledOnesAreSkipped()
        {
            TerraHeightSettings h = Hills();
            TerraStamp raise = new TerraStamp();
            raise.kind = TerraStampKind.Mound;
            raise.radius = 50.0;
            raise.amount = 10.0;
            TerraStamp pad = new TerraStamp();
            pad.kind = TerraStampKind.Pad;
            pad.radius = 30.0;
            pad.autoHeight = true;
            TerraStamp off = new TerraStamp();
            off.kind = TerraStampKind.Mound;
            off.radius = 500.0;
            off.amount = 1000.0;
            off.enabled = false;
            h.stamps = new[] { raise, pad, off };
            TerraHeightField plain = new TerraHeightField(1, Hills());
            TerraHeightField f = new TerraHeightField(1, h);
            // The pad reads the ground after the mound, at its centre, so it sits on top of the mound.
            double expected = plain.Height(0.0, 0.0) + 10.0;
            Assert.AreEqual(expected, f.Height(5.0, 5.0), 1e-9);
        }

        [Test]
        public void PatchReplacesAndAddsWithFeather()
        {
            TerraHeightSettings h = Hills();
            TerraPatch p = new TerraPatch();
            p.minX = 0.0;
            p.minZ = 0.0;
            p.sizeX = 100.0;
            p.sizeZ = 100.0;
            p.width = 2;
            p.height = 2;
            p.data = new[] { 0.0f, 1.0f, 0.0f, 1.0f };
            p.minHeight = 10.0f;
            p.maxHeight = 30.0f;
            p.feather = 10.0;
            p.mode = TerraPatchMode.Replace;
            h.patches = new[] { p };
            TerraHeightField f = new TerraHeightField(3, h);
            Assert.AreEqual(13.0, f.Height(15.0, 50.0), 1e-6);
            Assert.AreEqual(27.0, f.Height(85.0, 50.0), 1e-6);
            Assert.AreEqual(20.0, f.Height(50.0, 50.0), 1e-6);
            TerraHeightField plain = new TerraHeightField(3, Hills());
            Assert.AreEqual(plain.Height(-5.0, 50.0), f.Height(-5.0, 50.0), 1e-12);
            p.mode = TerraPatchMode.Add;
            TerraHeightField add = new TerraHeightField(3, h);
            Assert.AreEqual(plain.Height(50.0, 50.0) + 20.0, add.Height(50.0, 50.0), 1e-6);
            p.mode = TerraPatchMode.Max;
            TerraHeightField max = new TerraHeightField(3, h);
            Assert.IsTrue(max.Height(50.0, 50.0) >= Math.Max(plain.Height(50.0, 50.0), 20.0) - 1e-6);
        }

        [Test]
        public void LockKeepsTheLandWhenTheWorldSeedChanges()
        {
            TerraHeightSettings h = Hills();
            TerraLock l = new TerraLock();
            l.x = 0.0;
            l.z = 0.0;
            l.radius = 100.0;
            l.falloff = 40.0;
            l.seed = 77;
            h.locks = new[] { l };
            TerraHeightField a = new TerraHeightField(1, h);
            TerraHeightField b = new TerraHeightField(2, h);
            for (int i = 0; i < 30; i++)
            {
                double x = Math.Cos(i) * 90.0;
                double z = Math.Sin(i) * 90.0;
                Assert.AreEqual(a.Height(x, z), b.Height(x, z), 1e-9, "inside the lock the seed does not matter");
            }
            int different = 0;
            for (int i = 0; i < 30; i++)
            {
                if (Math.Abs(a.Height(600.0 + i * 11.0, 300.0) - b.Height(600.0 + i * 11.0, 300.0)) > 1e-6)
                {
                    different++;
                }
            }
            Assert.Greater(different, 25, "outside the lock the seed matters");
        }

        [Test]
        public void IslandFalloffPutsTheEdgeAtTheGround()
        {
            TerraHeightSettings h = Hills();
            h.falloff.mode = TerraFalloffMode.Radial;
            h.falloff.radius = 300.0;
            TerraHeightField f = new TerraHeightField(6, h);
            Assert.AreEqual(f.BaseHeight, f.Height(300.0, 0.0), 1e-9);
            Assert.AreEqual(f.BaseHeight, f.Height(-800.0, 123.0), 1e-9);
        }

        [Test]
        public void NormalPointsUpOnFlatGroundAndTiltsOnASlope()
        {
            TerraHeightSettings h = Hills();
            h.heightScale = 1e-9;
            TerraHeightField flat = new TerraHeightField(1, h);
            double nx;
            double ny;
            double nz;
            flat.Normal(5.0, 5.0, 1.0, out nx, out ny, out nz);
            Assert.AreEqual(1.0, ny, 1e-6);
            TerraHeightSettings s = Hills();
            TerraHeightField hills = new TerraHeightField(1, s);
            for (int i = 0; i < 50; i++)
            {
                hills.Normal(i * 23.1, i * -7.7, 1.0, out nx, out ny, out nz);
                Assert.AreEqual(1.0, nx * nx + ny * ny + nz * nz, 1e-9);
                Assert.IsTrue(ny > 0.0);
                double slope = hills.SlopeDegrees(i * 23.1, i * -7.7, 1.0);
                Assert.AreEqual(Math.Acos(ny) * 180.0 / Math.PI, slope, 1e-9);
            }
        }

        [Test]
        public void PaintAndScatterBlockingFollowTheStamp()
        {
            TerraHeightSettings h = Hills();
            TerraStamp pad = new TerraStamp();
            pad.kind = TerraStampKind.Pad;
            pad.radius = 20.0;
            pad.falloff = 10.0;
            pad.paint = true;
            pad.paintColor = new TerraColor(1f, 0f, 0f, 1f);
            pad.blocksScatter = true;
            h.stamps = new[] { pad };
            TerraHeightField f = new TerraHeightField(1, h);
            TerraColor grass = new TerraColor(0f, 1f, 0f, 1f);
            TerraColor inside = f.ApplyPaint(5.0, 5.0, grass);
            Assert.AreEqual(1.0, inside.r, 1e-6);
            Assert.AreEqual(0.0, inside.g, 1e-6);
            TerraColor outside = f.ApplyPaint(200.0, 0.0, grass);
            Assert.AreEqual(0.0, outside.r, 1e-6);
            Assert.AreEqual(1.0, outside.g, 1e-6);
            Assert.IsTrue(f.BlocksScatter(5.0, 5.0));
            Assert.IsFalse(f.BlocksScatter(200.0, 0.0));
        }

        [Test]
        public void StampBoundsSkipNothingThatReaches()
        {
            // The bounding box shortcut must give the same answer as evaluating every stamp everywhere.
            TerraHeightSettings h = Hills();
            TerraStamp a = new TerraStamp();
            a.kind = TerraStampKind.Road;
            a.points = new[] { new TerraPoint(-50.0, -50.0), new TerraPoint(60.0, 20.0), new TerraPoint(10.0, 90.0) };
            a.width = 6.0;
            a.falloff = 9.0;
            TerraStamp b = new TerraStamp();
            b.kind = TerraStampKind.Pad;
            b.shape = TerraStampShape.Box;
            b.x = 40.0;
            b.z = 40.0;
            b.halfX = 15.0;
            b.halfZ = 8.0;
            b.rotationDegrees = 33.0;
            b.falloff = 12.0;
            h.stamps = new[] { a, b };
            TerraHeightField f = new TerraHeightField(9, h);
            TerraStamp[] stamps = h.stamps;
            for (int i = 0; i < 4000; i++)
            {
                double x = -120.0 + (i % 80) * 3.0;
                double z = -120.0 + (i / 80) * 6.0;
                // Influence is zero where the box shortcut would skip, so the shortcut must not skip any influenced point.
                for (int s = 0; s < stamps.Length; s++)
                {
                    double nx;
                    double nz;
                    double w = stamps[s].Influence(x, z, out nx, out nz);
                    if (w > 0.0)
                    {
                        Assert.AreNotEqual(new TerraHeightField(9, Hills()).Height(x, z), f.Height(x, z), "a stamp with influence " + w + " at " + x + "," + z + " changed nothing");
                        break;
                    }
                }
            }
        }
    }
}
