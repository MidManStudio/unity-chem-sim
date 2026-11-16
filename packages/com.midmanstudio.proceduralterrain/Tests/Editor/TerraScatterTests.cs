// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/Editor/TerraScatterTests.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MidManStudio.ProceduralTerrain.Tests
{
    [TestFixture]
    public class TerraScatterTests
    {
        private static TerraWorldSettings WorldWith(TerraScatterLayer layer, int cells, double jitter)
        {
            TerraWorldSettings s = new TerraWorldSettings();
            s.seed = 31;
            s.height.noise.scale = 150.0;
            s.height.noise.remapMin = 0.29;
            s.height.noise.remapMax = 0.71;
            s.height.heightScale = 60.0;
            s.lattice.spacing = 4.0;
            s.lattice.cellsPerChunk = cells;
            s.lattice.jitter = jitter;
            s.scatter = new[] { layer };
            return s;
        }

        private static TerraScatterLayer Trees()
        {
            TerraScatterLayer l = new TerraScatterLayer();
            l.minValue = 0.0f;
            l.maxValue = 1.0f;
            l.maxSlopeDegrees = 60.0f;
            l.density = 0.8f;
            l.minDistance = 9.0;
            l.edgeMargin = 0.0;
            return l;
        }

        private static List<TerraScatterPoint> Block(TerraWorld w, int from, int to)
        {
            List<TerraScatterPoint> all = new List<TerraScatterPoint>();
            for (int cx = from; cx <= to; cx++)
            {
                for (int cz = from; cz <= to; cz++)
                {
                    all.AddRange(w.ScatterChunk(cx, cz));
                }
            }
            return all;
        }

        [Test]
        public void PointsKeepTheirMinimumDistanceAcrossChunkBorders()
        {
            TerraWorld w = new TerraWorld(WorldWith(Trees(), 16, 0.25));
            List<TerraScatterPoint> all = Block(w, -1, 1);
            Assert.Greater(all.Count, 100);
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    double dx = all[i].x - all[j].x;
                    double dz = all[i].z - all[j].z;
                    Assert.IsTrue(dx * dx + dz * dz >= 81.0 - 1e-9, "two points " + Math.Sqrt(dx * dx + dz * dz) + " m apart");
                }
            }
        }

        [Test]
        public void ChunkBordersDoNotChangeTheResult()
        {
            // Without jitter the nodes do not depend on where the chunk lines are, so four small chunks must hold exactly the points
            // one big chunk holds. If the rule leaned on chunk borders they would differ along the middle.
            TerraScatterLayer layer = Trees();
            TerraWorld small = new TerraWorld(WorldWith(layer, 16, 0.0));
            TerraWorld big = new TerraWorld(WorldWith(layer, 32, 0.0));
            HashSet<string> fromSmall = new HashSet<string>();
            for (int cx = 0; cx <= 1; cx++)
            {
                for (int cz = 0; cz <= 1; cz++)
                {
                    foreach (TerraScatterPoint p in small.ScatterChunk(cx, cz))
                    {
                        Assert.IsTrue(fromSmall.Add(p.x.ToString("F4") + "," + p.z.ToString("F4")), "a point was placed twice");
                    }
                }
            }
            HashSet<string> fromBig = new HashSet<string>();
            foreach (TerraScatterPoint p in big.ScatterChunk(0, 0))
            {
                fromBig.Add(p.x.ToString("F4") + "," + p.z.ToString("F4"));
            }
            Assert.Greater(fromBig.Count, 40);
            Assert.AreEqual(fromBig.Count, fromSmall.Count);
            foreach (string s in fromBig)
            {
                Assert.IsTrue(fromSmall.Contains(s), "missing " + s);
            }
        }

        [Test]
        public void PointsBelongToTheirChunkAndAreRepeatable()
        {
            TerraWorld w = new TerraWorld(WorldWith(Trees(), 16, 0.25));
            double size = w.Lattice.ChunkSize;
            TerraScatterPoint[] a = w.ScatterChunk(2, -3);
            TerraScatterPoint[] b = new TerraWorld(WorldWith(Trees(), 16, 0.25)).ScatterChunk(2, -3);
            Assert.AreEqual(a.Length, b.Length);
            Assert.Greater(a.Length, 5);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].x, b[i].x);
                Assert.AreEqual(a[i].z, b[i].z);
                Assert.AreEqual(a[i].yawDegrees, b[i].yawDegrees);
                Assert.AreEqual(a[i].scale, b[i].scale);
                Assert.IsTrue(a[i].x >= 2 * size - 4.0 && a[i].x < 3 * size + 4.0, "x " + a[i].x);
                Assert.IsTrue(a[i].z >= -3 * size - 4.0 && a[i].z < -2 * size + 4.0, "z " + a[i].z);
            }
        }

        [Test]
        public void LayerRulesAreRespected()
        {
            TerraScatterLayer layer = Trees();
            layer.minValue = 0.35f;
            layer.maxValue = 0.65f;
            layer.maxSlopeDegrees = 12.0f;
            layer.scaleMin = 0.5f;
            layer.scaleMax = 2.0f;
            layer.variants = 4;
            layer.yOffset = 0.25;
            TerraWorld w = new TerraWorld(WorldWith(layer, 16, 0.25));
            List<TerraScatterPoint> all = Block(w, -2, 2);
            Assert.Greater(all.Count, 20);
            int[] seen = new int[4];
            foreach (TerraScatterPoint p in all)
            {
                double h = w.HeightAt(p.x, p.z);
                double v = w.Field.ValueOfHeight(h);
                Assert.IsTrue(v >= 0.35 - 1e-9 && v <= 0.65 + 1e-9, "value " + v);
                Assert.IsTrue(w.Field.SlopeDegrees(p.x, p.z, 1.0) <= 12.0 + 1e-9);
                Assert.AreEqual(h + 0.25, p.y, 1e-9);
                Assert.IsTrue(p.scale >= 0.5f && p.scale <= 2.0f);
                Assert.IsTrue(p.variant >= 0 && p.variant < 4);
                Assert.IsTrue(p.yawDegrees >= 0.0f && p.yawDegrees < 360.0f);
                Assert.AreEqual(1.0, p.normalX * p.normalX + p.normalY * p.normalY + p.normalZ * p.normalZ, 1e-4);
                seen[p.variant]++;
            }
            foreach (int n in seen)
            {
                Assert.Greater(n, 0, "every variant is used");
            }
        }

        [Test]
        public void DensityZeroPlacesNothingAndDisabledLayersAreSkipped()
        {
            TerraScatterLayer none = Trees();
            none.density = 0.0f;
            Assert.AreEqual(0, new TerraWorld(WorldWith(none, 16, 0.25)).ScatterChunk(0, 0).Length);
            TerraScatterLayer off = Trees();
            off.enabled = false;
            Assert.AreEqual(0, new TerraWorld(WorldWith(off, 16, 0.25)).ScatterChunk(0, 0).Length);
            TerraScatterLayer dense = Trees();
            dense.density = 1.0f;
            dense.minDistance = 0.5;
            TerraScatterLayer sparse = Trees();
            sparse.density = 1.0f;
            sparse.minDistance = 20.0;
            int denseCount = new TerraWorld(WorldWith(dense, 16, 0.25)).ScatterChunk(0, 0).Length;
            int sparseCount = new TerraWorld(WorldWith(sparse, 16, 0.25)).ScatterChunk(0, 0).Length;
            Assert.Greater(denseCount, sparseCount * 3);
        }

        [Test]
        public void StampsKeepScatterOff()
        {
            TerraWorldSettings s = WorldWith(Trees(), 16, 0.25);
            TerraStamp pad = new TerraStamp();
            pad.kind = TerraStampKind.Pad;
            pad.x = 32.0;
            pad.z = 32.0;
            pad.radius = 24.0;
            pad.falloff = 8.0;
            pad.blocksScatter = true;
            s.height.stamps = new[] { pad };
            TerraWorld w = new TerraWorld(s);
            List<TerraScatterPoint> all = Block(w, -1, 1);
            Assert.Greater(all.Count, 50);
            foreach (TerraScatterPoint p in all)
            {
                double d = Math.Sqrt((p.x - 32.0) * (p.x - 32.0) + (p.z - 32.0) * (p.z - 32.0));
                Assert.IsTrue(d > 24.0, "a tree stands on the town pad at distance " + d);
            }
            pad.blocksScatter = false;
            int inside = 0;
            foreach (TerraScatterPoint p in Block(new TerraWorld(s), -1, 1))
            {
                if (Math.Sqrt((p.x - 32.0) * (p.x - 32.0) + (p.z - 32.0) * (p.z - 32.0)) < 20.0)
                {
                    inside++;
                }
            }
            Assert.Greater(inside, 0, "with blocking off trees do grow there");
        }

        [Test]
        public void BiomeEdgesAreSkippedWhenAskedTo()
        {
            TerraScatterLayer plain = Trees();
            plain.density = 1.0f;
            plain.minDistance = 2.0;
            TerraScatterLayer guarded = Trees();
            guarded.density = 1.0f;
            guarded.minDistance = 2.0;
            guarded.edgeMargin = 5.0;
            TerraWorld a = new TerraWorld(WorldWith(plain, 32, 0.25));
            TerraWorld b = new TerraWorld(WorldWith(guarded, 32, 0.25));
            int na = a.ScatterChunk(0, 0).Length;
            TerraScatterPoint[] kept = b.ScatterChunk(0, 0);
            Assert.Greater(na, kept.Length);
            foreach (TerraScatterPoint p in kept)
            {
                int band = b.Settings.biomes.IndexAt(b.Field.ValueOfHeight(b.HeightAt(p.x, p.z)));
                Assert.AreEqual(band, b.Settings.biomes.IndexAt(b.Field.ValueOfHeight(b.HeightAt(p.x + 5.0, p.z))));
                Assert.AreEqual(band, b.Settings.biomes.IndexAt(b.Field.ValueOfHeight(b.HeightAt(p.x, p.z - 5.0))));
            }
        }

        [Test]
        public void IslandScatterKeepsItsDistance()
        {
            TerraWorldSettings s = WorldWith(Trees(), 16, 0.25);
            s.island.minX = -150.0;
            s.island.maxX = 150.0;
            s.island.minZ = -150.0;
            s.island.maxZ = 150.0;
            s.island.radius = 5.0;
            TerraChunkResult r = new TerraWorld(s).BuildIsland();
            Assert.Greater(r.scatter.Length, 20);
            for (int i = 0; i < r.scatter.Length; i++)
            {
                Assert.IsTrue(Math.Abs(r.scatter[i].x) <= 150.0 && Math.Abs(r.scatter[i].z) <= 150.0);
                for (int j = i + 1; j < r.scatter.Length; j++)
                {
                    double dx = r.scatter[i].x - r.scatter[j].x;
                    double dz = r.scatter[i].z - r.scatter[j].z;
                    Assert.IsTrue(dx * dx + dz * dz >= 81.0 - 1e-9);
                }
            }
        }
    }
}
