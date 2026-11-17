// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraChunkBuilder.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using System.Threading;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>One request to the builder, and its answer.</summary>
    public sealed class TerraChunkJob
    {
        public TerraChunkKey key;
        public int step;

        /// <summary>Whether the scatter points of the chunk are wanted too. A change of level of detail alone does not need them again.</summary>
        public bool withScatter;

        /// <summary>A number the caller gave the request. A caller that asks for a chunk twice uses it to tell a stale answer from the current one.</summary>
        public int ticket;

        public TerraChunkResult chunk;
        public TerraScatterPoint[] scatter;

        /// <summary>Whatever <see cref="TerraChunkBuilder.OnBuilt"/> made of the result on the worker thread.</summary>
        public object payload;

        /// <summary>Set when the build, or the OnBuilt callback, threw. The other fields are then not filled.</summary>
        public Exception error;
    }

    /// <summary>
    /// Builds chunks of a world in the background. Requests go in a queue; worker threads take them in order, call
    /// <see cref="TerraWorld.BuildChunk"/> (and ScatterChunk when asked) and put the finished job in a second queue for the
    /// caller to take from its own thread. The world is immutable and every call on it is pure, so any number of workers can
    /// run at once and a chunk built here is vertex for vertex the chunk a direct call returns.
    ///
    /// With zero workers nothing runs in the background: the caller drives the queue with <see cref="RunSome"/>, for platforms
    /// without threads or for a streamer that wants to spend a fixed time per frame.
    /// </summary>
    public sealed class TerraChunkBuilder : IDisposable
    {
        private readonly TerraWorld world;
        private readonly object gate = new object();
        private readonly LinkedList<TerraChunkJob> waiting = new LinkedList<TerraChunkJob>();
        private readonly Queue<TerraChunkJob> finished = new Queue<TerraChunkJob>();
        private readonly Thread[] threads;
        private int running;
        private int liveWorkers;
        private bool disposed;

        /// <summary>
        /// Called on the thread that built a job, after the build, with the job. Use it to do work that needs no engine thread,
        /// such as turning the arrays into the types an engine wants, and leave the result in <see cref="TerraChunkJob.payload"/>.
        /// Set it before the first request.
        /// </summary>
        public Action<TerraChunkJob> OnBuilt;

        public TerraChunkBuilder(TerraWorld world, int workerThreads)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }
            this.world = world;
            int n = workerThreads < 0 ? 0 : (workerThreads > 64 ? 64 : workerThreads);
            threads = new Thread[n];
            liveWorkers = n;
            for (int i = 0; i < n; i++)
            {
                Thread t = new Thread(Work);
                t.IsBackground = true;
                t.Name = "TerraChunkBuilder " + i;
                threads[i] = t;
                t.Start();
            }
        }

        public int WorkerCount
        {
            get { return threads.Length; }
        }

        /// <summary>Workers that have not exited yet. Falls to zero some time after <see cref="Dispose"/>.</summary>
        public int LiveWorkers
        {
            get { lock (gate) { return liveWorkers; } }
        }

        /// <summary>Requests that are waiting or being built. Finished jobs not yet taken are not counted.</summary>
        public int Pending
        {
            get { lock (gate) { return waiting.Count + running; } }
        }

        /// <summary>Finished jobs ready to take.</summary>
        public int FinishedCount
        {
            get { lock (gate) { return finished.Count; } }
        }

        /// <summary>Puts a request in the queue. Requests are built in the order they arrive.</summary>
        public void Enqueue(TerraChunkKey key, int step, bool withScatter, int ticket)
        {
            TerraChunkJob job = new TerraChunkJob();
            job.key = key;
            job.step = step < 1 ? 1 : step;
            job.withScatter = withScatter;
            job.ticket = ticket;
            lock (gate)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException("TerraChunkBuilder");
                }
                waiting.AddLast(job);
                Monitor.Pulse(gate);
            }
        }

        /// <summary>
        /// Removes the requests for a chunk that no worker has started yet and returns how many it removed. A request already
        /// being built finishes; the caller drops its answer by the ticket.
        /// </summary>
        public int Cancel(TerraChunkKey key)
        {
            int removed = 0;
            lock (gate)
            {
                LinkedListNode<TerraChunkJob> node = waiting.First;
                while (node != null)
                {
                    LinkedListNode<TerraChunkJob> next = node.Next;
                    if (node.Value.key.Equals(key))
                    {
                        waiting.Remove(node);
                        removed++;
                    }
                    node = next;
                }
            }
            return removed;
        }

        /// <summary>Takes one finished job, if there is one. Safe to call from any thread.</summary>
        public bool TryTake(out TerraChunkJob job)
        {
            lock (gate)
            {
                if (finished.Count > 0)
                {
                    job = finished.Dequeue();
                    return true;
                }
            }
            job = null;
            return false;
        }

        /// <summary>
        /// Builds up to a number of waiting requests on the calling thread and returns how many it built. This is the way to
        /// drive a builder with no workers. With workers it works too, and simply helps them.
        /// </summary>
        public int RunSome(int maxJobs)
        {
            int done = 0;
            while (done < maxJobs)
            {
                TerraChunkJob job = TakeWaiting(false);
                if (job == null)
                {
                    break;
                }
                Execute(job);
                done++;
            }
            return done;
        }

        /// <summary>Stops the workers after the job each is on. Waiting requests are dropped; finished jobs can still be taken.</summary>
        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                waiting.Clear();
                Monitor.PulseAll(gate);
            }
        }

        // Takes the first waiting request. With block set the worker waits for one, and gets null only when disposed.
        private TerraChunkJob TakeWaiting(bool block)
        {
            lock (gate)
            {
                while (waiting.Count == 0)
                {
                    if (!block || disposed)
                    {
                        return null;
                    }
                    Monitor.Wait(gate);
                }
                if (disposed)
                {
                    return null;
                }
                TerraChunkJob job = waiting.First.Value;
                waiting.RemoveFirst();
                running++;
                return job;
            }
        }

        private void Work()
        {
            try
            {
                while (true)
                {
                    TerraChunkJob job = TakeWaiting(true);
                    if (job == null)
                    {
                        return;
                    }
                    Execute(job);
                }
            }
            finally
            {
                lock (gate)
                {
                    liveWorkers--;
                }
            }
        }

        private void Execute(TerraChunkJob job)
        {
            try
            {
                job.chunk = world.BuildChunk(job.key.x, job.key.z, job.step);
                if (job.withScatter)
                {
                    job.scatter = world.ScatterChunk(job.key.x, job.key.z);
                }
                Action<TerraChunkJob> callback = OnBuilt;
                if (callback != null)
                {
                    callback(job);
                }
            }
            catch (Exception e)
            {
                job.chunk = null;
                job.scatter = null;
                job.payload = null;
                job.error = e;
            }
            lock (gate)
            {
                running--;
                finished.Enqueue(job);
            }
        }
    }
}
