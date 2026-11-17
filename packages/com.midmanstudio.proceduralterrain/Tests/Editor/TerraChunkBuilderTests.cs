// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/Editor/TerraChunkBuilderTests.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace MidManStudio.ProceduralTerrain.Tests
{
    [TestFixture]
    public class TerraChunkBuilderTests
    {
        private static TerraWorld MakeWorld()
        {
            TerraWorldSettings s = new TerraWorldSettings();
            s.seed = 5150;
            s.height.noise.scale = 120.0;
            s.height.noise.remapMin = 0.29;
            s.height.noise.remapMax = 0.71;
            s.lattice.spacing = 4.0;
            s.lattice.cellsPerChunk = 16;
            s.scatter = new TerraScatterLayer[] { TerraPresets.Trees(0.2f, 0.9f) };
            return new TerraWorld(s);
        }

        private static bool WaitFor(Func<bool> condition, int milliseconds)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.ElapsedMilliseconds > milliseconds)
                {
                    return false;
                }
                Thread.Sleep(2);
            }
            return true;
        }

        private static void SameMesh(TerraMeshData a, TerraMeshData b)
        {
            Assert.AreEqual(a.vertexCount, b.vertexCount);
            Assert.AreEqual(a.positions.Length, b.positions.Length);
            Assert.AreEqual(a.indices.Length, b.indices.Length);
            for (int i = 0; i < a.positions.Length; i++)
            {
                Assert.IsTrue(a.positions[i] == b.positions[i] && a.normals[i] == b.normals[i], "a vertex differs at " + i);
            }
            for (int i = 0; i < a.colors.Length; i++)
            {
                Assert.IsTrue(a.colors[i] == b.colors[i], "a colour differs at " + i);
            }
            for (int i = 0; i < a.indices.Length; i++)
            {
                Assert.IsTrue(a.indices[i] == b.indices[i], "an index differs at " + i);
            }
        }

        [Test]
        public void Workers_BuildTheSameChunksAsDirectCalls()
        {
            TerraWorld world = MakeWorld();
            int[] steps = { 1, 2, 4, 8, 1, 2, 1, 4, 1, 2, 8, 1 };
            using (TerraChunkBuilder builder = new TerraChunkBuilder(world, 3))
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    builder.Enqueue(new TerraChunkKey(i - 5, 7 - i), steps[i], true, 100 + i);
                }
                Assert.IsTrue(WaitFor(() => builder.FinishedCount == steps.Length, 60000), "the workers did not finish");
                Assert.AreEqual(0, builder.Pending);
                int taken = 0;
                TerraChunkJob job;
                while (builder.TryTake(out job))
                {
                    taken++;
                    Assert.IsTrue(job.error == null, "a build threw: " + job.error);
                    TerraChunkResult direct = world.BuildChunk(job.key.x, job.key.z, job.step);
                    SameMesh(direct.mesh, job.chunk.mesh);
                    TerraScatterPoint[] points = world.ScatterChunk(job.key.x, job.key.z);
                    Assert.AreEqual(points.Length, job.scatter.Length);
                    for (int i = 0; i < points.Length; i++)
                    {
                        Assert.IsTrue(points[i].x == job.scatter[i].x && points[i].z == job.scatter[i].z
                            && points[i].y == job.scatter[i].y && points[i].layer == job.scatter[i].layer
                            && points[i].variant == job.scatter[i].variant && points[i].scale == job.scatter[i].scale,
                            "a scatter point differs");
                    }
                    Assert.IsTrue(job.ticket >= 100 && job.ticket < 100 + steps.Length);
                }
                Assert.AreEqual(steps.Length, taken);
            }
        }

        [Test]
        public void WithoutWorkers_RunSomeBuildsOnlyAsManyAsAsked()
        {
            TerraWorld world = MakeWorld();
            using (TerraChunkBuilder builder = new TerraChunkBuilder(world, 0))
            {
                for (int i = 0; i < 5; i++)
                {
                    builder.Enqueue(new TerraChunkKey(i, 0), 1, false, i);
                }
                Assert.AreEqual(5, builder.Pending);
                Assert.AreEqual(2, builder.RunSome(2));
                Assert.AreEqual(3, builder.Pending);
                Assert.AreEqual(2, builder.FinishedCount);
                Assert.AreEqual(3, builder.RunSome(10));
                Assert.AreEqual(0, builder.Pending);
                TerraChunkJob job;
                Assert.IsTrue(builder.TryTake(out job));
                Assert.AreEqual(0L, job.key.x, "jobs come out in the order they went in");
                Assert.IsTrue(job.scatter == null, "scatter was not asked for");
                Assert.AreEqual(0, job.ticket);
            }
        }

        [Test]
        public void Cancel_RemovesOnlyRequestsNotYetStarted()
        {
            TerraWorld world = MakeWorld();
            using (TerraChunkBuilder builder = new TerraChunkBuilder(world, 0))
            {
                builder.Enqueue(new TerraChunkKey(1, 1), 1, false, 1);
                builder.Enqueue(new TerraChunkKey(2, 2), 1, false, 2);
                builder.Enqueue(new TerraChunkKey(1, 1), 2, false, 3);
                Assert.AreEqual(2, builder.Cancel(new TerraChunkKey(1, 1)));
                Assert.AreEqual(0, builder.Cancel(new TerraChunkKey(9, 9)));
                Assert.AreEqual(1, builder.RunSome(10));
                TerraChunkJob job;
                Assert.IsTrue(builder.TryTake(out job));
                Assert.AreEqual(2L, job.key.x);
                Assert.IsFalse(builder.TryTake(out job));
            }
        }

        [Test]
        public void OnBuilt_RunsOnTheWorkerAndItsResultTravelsWithTheJob()
        {
            TerraWorld world = MakeWorld();
            int caller = Thread.CurrentThread.ManagedThreadId;
            int callbackThread = -1;
            using (TerraChunkBuilder builder = new TerraChunkBuilder(world, 2))
            {
                builder.OnBuilt = delegate(TerraChunkJob j)
                {
                    callbackThread = Thread.CurrentThread.ManagedThreadId;
                    j.payload = j.chunk.mesh.vertexCount;
                };
                builder.Enqueue(new TerraChunkKey(0, 0), 2, false, 1);
                Assert.IsTrue(WaitFor(() => builder.FinishedCount == 1, 60000));
                TerraChunkJob job;
                Assert.IsTrue(builder.TryTake(out job));
                Assert.AreEqual(job.chunk.mesh.vertexCount, (int)job.payload);
                Assert.AreNotEqual(caller, callbackThread, "the callback ran on the calling thread");
            }
        }

        [Test]
        public void AThrowingCallbackBecomesAnErrorOnTheJob()
        {
            TerraWorld world = MakeWorld();
            using (TerraChunkBuilder builder = new TerraChunkBuilder(world, 1))
            {
                builder.OnBuilt = delegate(TerraChunkJob j) { throw new InvalidOperationException("boom"); };
                builder.Enqueue(new TerraChunkKey(0, 0), 1, true, 1);
                builder.Enqueue(new TerraChunkKey(1, 0), 1, true, 2);
                Assert.IsTrue(WaitFor(() => builder.FinishedCount == 2, 60000), "a throwing callback stopped the worker");
                TerraChunkJob job;
                Assert.IsTrue(builder.TryTake(out job));
                Assert.IsTrue(job.error is InvalidOperationException);
                Assert.IsTrue(job.chunk == null && job.scatter == null && job.payload == null);
            }
        }

        [Test]
        public void Dispose_StopsTheWorkersAndRefusesNewRequests()
        {
            TerraWorld world = MakeWorld();
            TerraChunkBuilder builder = new TerraChunkBuilder(world, 3);
            Assert.AreEqual(3, builder.WorkerCount);
            Assert.IsTrue(WaitFor(() => builder.LiveWorkers == 3, 5000));
            builder.Dispose();
            Assert.IsTrue(WaitFor(() => builder.LiveWorkers == 0, 10000), "the workers did not exit");
            Assert.Throws<ObjectDisposedException>(delegate { builder.Enqueue(new TerraChunkKey(0, 0), 1, false, 1); });
        }

        [Test]
        public void ManyJobsFromSeveralWorkersAllArriveOnce()
        {
            TerraWorld world = MakeWorld();
            using (TerraChunkBuilder builder = new TerraChunkBuilder(world, 4))
            {
                int count = 40;
                for (int i = 0; i < count; i++)
                {
                    builder.Enqueue(new TerraChunkKey(i % 8, i / 8), 1 << (i % 3), true, i);
                }
                Assert.IsTrue(WaitFor(() => builder.FinishedCount == count, 120000));
                HashSet<int> tickets = new HashSet<int>();
                TerraChunkJob job;
                while (builder.TryTake(out job))
                {
                    Assert.IsTrue(job.error == null);
                    Assert.IsTrue(tickets.Add(job.ticket), "a job arrived twice");
                }
                Assert.AreEqual(count, tickets.Count);
            }
        }
    }
}
