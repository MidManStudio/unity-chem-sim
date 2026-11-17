// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/Editor/TerraStreamTests.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MidManStudio.ProceduralTerrain.Tests
{
    [TestFixture]
    public class TerraStreamTests
    {
        // Chunks of 64 m (16 cells of 4 m), tiers at 100, 250 and 500 m, load 400 m, unload margin 100 m.
        private static TerraWorld MakeWorld()
        {
            TerraWorldSettings s = new TerraWorldSettings();
            s.seed = 77;
            s.height.noise.scale = 120.0;
            s.height.noise.remapMin = 0.29;
            s.height.noise.remapMax = 0.71;
            s.lattice.spacing = 4.0;
            s.lattice.cellsPerChunk = 16;
            s.lod.distances = new double[] { 100.0, 250.0, 500.0 };
            return new TerraWorld(s);
        }

        private static TerraStreamSettings Stream()
        {
            TerraStreamSettings t = new TerraStreamSettings();
            t.loadDistance = 400.0;
            t.unloadMargin = 100.0;
            t.lodMargin = 24.0;
            t.maxChunks = 10000;
            return t;
        }

        private static Dictionary<TerraChunkKey, int> Held(TerraStreamPlan plan)
        {
            Dictionary<TerraChunkKey, int> have = new Dictionary<TerraChunkKey, int>();
            for (int i = 0; i < plan.build.Count; i++)
            {
                have[plan.build[i].key] = plan.build[i].step;
            }
            return have;
        }

        [Test]
        public void Plan_AsksForExactlyTheChunksWithinLoadDistanceNearestFirst()
        {
            TerraWorld world = MakeWorld();
            TerraStreamPlanner planner = new TerraStreamPlanner(world, Stream());
            TerraStreamPlan plan = new TerraStreamPlan();
            planner.Plan(32.0, 32.0, null, plan);

            int expected = 0;
            for (long cz = -20; cz <= 20; cz++)
            {
                for (long cx = -20; cx <= 20; cx++)
                {
                    if (planner.DistanceTo(cx, cz, 32.0, 32.0) <= 400.0)
                    {
                        expected++;
                    }
                }
            }
            Assert.AreEqual(expected, plan.build.Count);
            Assert.AreEqual(0, plan.unload.Count);
            Assert.AreEqual(0.0, plan.build[0].distance, 1e-9);
            Assert.AreEqual(0L, plan.build[0].key.x);
            Assert.AreEqual(0L, plan.build[0].key.z);
            for (int i = 0; i < plan.build.Count; i++)
            {
                Assert.IsTrue(plan.build[i].distance <= 400.0 + 1e-9, "a chunk beyond the load distance was asked for");
                if (i > 0)
                {
                    Assert.IsTrue(plan.build[i - 1].distance <= plan.build[i].distance, "the plan is not ordered nearest first");
                }
            }
        }

        [Test]
        public void Plan_StepsFollowTheTiersOfTheWorld()
        {
            TerraWorld world = MakeWorld();
            TerraStreamPlanner planner = new TerraStreamPlanner(world, Stream());
            TerraStreamPlan plan = new TerraStreamPlan();
            planner.Plan(32.0, 32.0, null, plan);
            HashSet<int> steps = new HashSet<int>();
            for (int i = 0; i < plan.build.Count; i++)
            {
                Assert.AreEqual(world.StepForDistance(plan.build[i].distance), plan.build[i].step);
                steps.Add(plan.build[i].step);
            }
            Assert.IsTrue(steps.Contains(1) && steps.Contains(2) && steps.Contains(4), "expected steps 1, 2 and 4 within 400 m");
        }

        [Test]
        public void Plan_HasNothingToDoOnceItsAnswerIsHeld()
        {
            TerraWorld world = MakeWorld();
            TerraStreamPlanner planner = new TerraStreamPlanner(world, Stream());
            TerraStreamPlan plan = new TerraStreamPlan();
            planner.Plan(100.0, -40.0, null, plan);
            Dictionary<TerraChunkKey, int> have = Held(plan);
            planner.Plan(100.0, -40.0, have, plan);
            Assert.AreEqual(0, plan.build.Count);
            Assert.AreEqual(0, plan.unload.Count);
        }

        [Test]
        public void Plan_LetsGoOnlyBeyondLoadDistancePlusMargin()
        {
            TerraWorld world = MakeWorld();
            TerraStreamPlanner planner = new TerraStreamPlanner(world, Stream());
            // Viewer at (32, 32): chunk (cx, 0) with cx >= 1 is 64 * cx - 32 metres away. Chunk 8 is at 480 (kept), chunk 9 at 544 (let go).
            Assert.AreEqual(480.0, planner.DistanceTo(8, 0, 32.0, 32.0), 1e-9);
            Assert.AreEqual(544.0, planner.DistanceTo(9, 0, 32.0, 32.0), 1e-9);
            Dictionary<TerraChunkKey, int> have = new Dictionary<TerraChunkKey, int>();
            have[new TerraChunkKey(8, 0)] = 8;
            have[new TerraChunkKey(9, 0)] = 8;
            TerraStreamPlan plan = new TerraStreamPlan();
            planner.Plan(32.0, 32.0, have, plan);
            Assert.AreEqual(1, plan.unload.Count);
            Assert.AreEqual(9L, plan.unload[0].x);
            for (int i = 0; i < plan.build.Count; i++)
            {
                Assert.IsTrue(plan.build[i].key.x != 8 || plan.build[i].key.z != 0, "a held chunk at the right step was asked for again");
                Assert.IsTrue(plan.build[i].key.x != 9 || plan.build[i].key.z != 0, "a chunk beyond the margin was asked for");
            }
        }

        [Test]
        public void Plan_AsksForAnotherStepWhenAHeldChunkIsAtTheWrongOne()
        {
            TerraWorld world = MakeWorld();
            TerraStreamPlanner planner = new TerraStreamPlanner(world, Stream());
            // Chunk 7 is 416 m away: step 4 is wanted, and a held step of 1 is far from it.
            Dictionary<TerraChunkKey, int> have = new Dictionary<TerraChunkKey, int>();
            have[new TerraChunkKey(7, 0)] = 1;
            TerraStreamPlan plan = new TerraStreamPlan();
            planner.Plan(32.0, 32.0, have, plan);
            bool found = false;
            for (int i = 0; i < plan.build.Count; i++)
            {
                if (plan.build[i].key.x == 7 && plan.build[i].key.z == 0)
                {
                    found = true;
                    Assert.AreEqual(4, plan.build[i].step);
                }
            }
            Assert.IsTrue(found, "the chunk at the wrong step was not asked for");
        }

        [Test]
        public void StepFor_KeepsTheStepUntilThePastTheBoundaryByTheMargin()
        {
            TerraStreamPlanner planner = new TerraStreamPlanner(MakeWorld(), Stream());
            // Boundary at 100 m, margin 24.
            Assert.AreEqual(2, planner.StepFor(105.0, 0), "a chunk with no step gets the exact tier");
            Assert.AreEqual(1, planner.StepFor(105.0, 1), "just past the boundary, a fine chunk stays fine");
            Assert.AreEqual(1, planner.StepFor(120.0, 1));
            Assert.AreEqual(2, planner.StepFor(125.0, 1), "past the boundary by the margin it changes");
            Assert.AreEqual(2, planner.StepFor(95.0, 2), "just inside, a coarse chunk stays coarse");
            Assert.AreEqual(2, planner.StepFor(76.0, 2));
            Assert.AreEqual(1, planner.StepFor(70.0, 2), "inside by the margin it changes");
            Assert.AreEqual(1, planner.StepFor(0.0, 4), "a jump of tiers is allowed");
            Assert.AreEqual(8, planner.StepFor(5000.0, 1));
        }

        [Test]
        public void Plan_DoesNotDependOnHowFarFromTheOriginTheViewerIs()
        {
            TerraWorld world = MakeWorld();
            TerraStreamPlanner planner = new TerraStreamPlanner(world, Stream());
            long shift = 1000000L;
            double size = world.Lattice.ChunkSize;
            TerraStreamPlan near = new TerraStreamPlan();
            TerraStreamPlan far = new TerraStreamPlan();
            planner.Plan(50.0, -70.0, null, near);
            planner.Plan(50.0 + shift * size, -70.0 - shift * size, null, far);
            Assert.AreEqual(near.build.Count, far.build.Count);
            for (int i = 0; i < near.build.Count; i++)
            {
                Assert.AreEqual(near.build[i].key.x + shift, far.build[i].key.x);
                Assert.AreEqual(near.build[i].key.z - shift, far.build[i].key.z);
                Assert.AreEqual(near.build[i].step, far.build[i].step);
                Assert.AreEqual(near.build[i].distance, far.build[i].distance, 1e-6);
            }
        }

        [Test]
        public void Plan_WorksForNegativeCoordinates()
        {
            TerraWorld world = MakeWorld();
            TerraStreamPlanner planner = new TerraStreamPlanner(world, Stream());
            TerraStreamPlan plan = new TerraStreamPlan();
            planner.Plan(-100.0, -100.0, null, plan);
            Assert.AreEqual(0.0, plan.build[0].distance, 1e-9);
            Assert.AreEqual(-2L, plan.build[0].key.x);
            Assert.AreEqual(-2L, plan.build[0].key.z);
        }

        [Test]
        public void Plan_CapDropsTheFarthestNewChunks()
        {
            TerraWorld world = MakeWorld();
            TerraStreamSettings capped = Stream();
            capped.maxChunks = 10;
            TerraStreamPlan all = new TerraStreamPlan();
            TerraStreamPlan few = new TerraStreamPlan();
            new TerraStreamPlanner(world, Stream()).Plan(32.0, 32.0, null, all);
            new TerraStreamPlanner(world, capped).Plan(32.0, 32.0, null, few);
            Assert.AreEqual(10, few.build.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(all.build[i].key, few.build[i].key);
            }
        }

        [Test]
        public void Plan_IsTheSameEveryTime()
        {
            TerraWorld world = MakeWorld();
            TerraStreamPlanner planner = new TerraStreamPlanner(world, Stream());
            TerraStreamPlan a = new TerraStreamPlan();
            TerraStreamPlan b = new TerraStreamPlan();
            planner.Plan(12.5, 300.25, null, a);
            planner.Plan(12.5, 300.25, null, b);
            Assert.AreEqual(a.build.Count, b.build.Count);
            for (int i = 0; i < a.build.Count; i++)
            {
                Assert.AreEqual(a.build[i].key, b.build[i].key);
                Assert.AreEqual(a.build[i].step, b.build[i].step);
            }
        }

        [Test]
        public void ChunkKey_EqualityAndHashing()
        {
            TerraChunkKey a = new TerraChunkKey(3, -4);
            TerraChunkKey b = new TerraChunkKey(3, -4);
            TerraChunkKey c = new TerraChunkKey(-4, 3);
            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.IsFalse(a.Equals(c));
            Dictionary<TerraChunkKey, int> d = new Dictionary<TerraChunkKey, int>();
            d[a] = 1;
            Assert.AreEqual(1, d[b]);
            Assert.IsFalse(d.ContainsKey(c));
        }
    }
}
