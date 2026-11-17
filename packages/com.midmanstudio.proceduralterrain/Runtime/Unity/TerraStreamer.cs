// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Unity/TerraStreamer.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>
    /// Keeps the chunks around a viewer alive. Every so often it asks the planner what should change, sends the builds to
    /// worker threads, and each frame turns a few finished meshes into chunk objects. Chunks are positioned by their true
    /// corner minus a world origin that the streamer holds, so a game that shifts its origin tells the streamer with
    /// <see cref="SetWorldOrigin"/> and the chunks follow.
    /// </summary>
    [AddComponentMenu("MidManStudio/ProceduralTerrain/Terrain Streamer")]
    [DisallowMultipleComponent]
    public sealed class TerraStreamer : MonoBehaviour
    {
        private sealed class Entry
        {
            public TerraChunk chunk;
            public int loadedStep;
            public int requestedStep;
            public int ticket;
            public bool scatterDelivered;
            public bool failed;
        }

        [Tooltip("The world to stream. Changing it while running needs Restart.")]
        public TerraWorldProfile profile;

        [Tooltip("What the chunks are built around. When empty, the main camera.")]
        public Transform viewer;

        [Min(0)]
        [Tooltip("Background threads that build chunks. 0 builds on the main thread, a few at a time, for platforms without threads.")]
        public int workerThreads = 2;

        [Min(1)]
        [Tooltip("The most builds asked of the workers at once. A new plan is made from the viewer's place when one finishes, so a small number keeps the nearest chunks first.")]
        public int maxInFlight = 4;

        [Min(1)]
        [Tooltip("How many finished meshes are put into chunk objects per frame.")]
        public int applyPerFrame = 2;

        [Min(1)]
        [Tooltip("With no worker threads: how many chunks are built per frame.")]
        public int syncBuildsPerFrame = 1;

        [Min(0f)]
        [Tooltip("Seconds between plans.")]
        public float replanInterval = 0.25f;

        [Header("Scatter")]
        [Tooltip("Instantiate the prefabs of the profile's scatter bindings on the chunks. Turn off to place scatter yourself from ScatterReady.")]
        public bool spawnScatter = true;

        [Min(1)]
        [Tooltip("The most scatter prefabs instantiated per frame, over all chunks.")]
        public int scatterPerFrame = 64;

        /// <summary>A chunk has a mesh: the first one, or a new level of detail. The chunk's step says which.</summary>
        public event Action<TerraChunk> ChunkBuilt;

        /// <summary>A chunk is about to be destroyed.</summary>
        public event Action<TerraChunk> ChunkRemoved;

        /// <summary>The scatter points of a chunk have arrived. Raised once per chunk, whether or not the streamer places them.</summary>
        public event Action<TerraChunk> ScatterReady;

        private TerraWorld world;
        private TerraStreamPlanner planner;
        private TerraChunkBuilder builder;
        private readonly Dictionary<TerraChunkKey, Entry> entries = new Dictionary<TerraChunkKey, Entry>();
        private readonly Dictionary<TerraChunkKey, int> have = new Dictionary<TerraChunkKey, int>();
        private readonly TerraStreamPlan plan = new TerraStreamPlan();
        private readonly List<TerraChunkRequest> queued = new List<TerraChunkRequest>();
        private readonly List<TerraChunk> spawning = new List<TerraChunk>();
        private readonly Dictionary<string, GameObject[]> bindings = new Dictionary<string, GameObject[]>();
        private int queuedCursor;
        private int nextTicket;
        private float replanTimer;
        private double originX;
        private double originZ;
        private Material fallbackMaterial;

        /// <summary>The world being streamed, or null before <see cref="Restart"/> or while there is no profile.</summary>
        public TerraWorld World
        {
            get { return world; }
        }

        /// <summary>The true position, in metres, of the point that is (0, 0, 0) in the scene.</summary>
        public double WorldOriginX
        {
            get { return originX; }
        }

        public double WorldOriginZ
        {
            get { return originZ; }
        }

        public int LoadedChunkCount
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<TerraChunkKey, Entry> pair in entries)
                {
                    if (pair.Value.chunk != null)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        /// <summary>Builds asked for and not yet shown, running or waiting.</summary>
        public int PendingBuilds
        {
            get
            {
                if (builder == null)
                {
                    return 0;
                }
                return builder.Pending + builder.FinishedCount + (queued.Count - queuedCursor);
            }
        }

        /// <summary>True when the plan is empty and no build is running or waiting.</summary>
        public bool IsSettled
        {
            get { return world != null && PendingBuilds == 0; }
        }

        private void OnEnable()
        {
            Begin();
        }

        private void OnDisable()
        {
            Shutdown();
        }

        private void Update()
        {
            if (world == null)
            {
                return;
            }
            Transform v = viewer;
            if (v == null)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    v = cam.transform;
                }
            }
            if (v == null)
            {
                return;
            }
            Vector3 p = v.position;
            Tick(p.x + originX, p.z + originZ, Time.unscaledDeltaTime);
        }

        /// <summary>Builds the world from the profile again and drops every chunk. Call it after changing the profile.</summary>
        public void Restart()
        {
            Shutdown();
            Begin();
        }

        /// <summary>Drops every chunk and stops the workers. OnDisable does the same.</summary>
        public void Shutdown()
        {
            if (builder != null)
            {
                builder.Dispose();
                builder = null;
            }
            foreach (KeyValuePair<TerraChunkKey, Entry> pair in entries)
            {
                Entry e = pair.Value;
                if (e.chunk != null)
                {
                    Raise(ChunkRemoved, e.chunk);
                    e.chunk.Release();
                    TerraObjects.Discard(e.chunk.gameObject);
                }
            }
            entries.Clear();
            have.Clear();
            queued.Clear();
            queuedCursor = 0;
            spawning.Clear();
            bindings.Clear();
            if (fallbackMaterial != null)
            {
                TerraObjects.Discard(fallbackMaterial);
                fallbackMaterial = null;
            }
            world = null;
            planner = null;
        }

        private void Begin()
        {
            if (profile == null)
            {
                Debug.LogWarning("TerraStreamer: no profile is assigned, so no terrain is streamed.", this);
                return;
            }
            world = profile.CreateWorld();
            planner = new TerraStreamPlanner(world, profile.stream);
            builder = new TerraChunkBuilder(world, workerThreads);
            builder.OnBuilt = PrepareJob;
            bindings.Clear();
            TerraScatterBinding[] list = profile.scatterBindings;
            for (int i = 0; list != null && i < list.Length; i++)
            {
                TerraScatterBinding b = list[i];
                if (b != null && !string.IsNullOrEmpty(b.layer) && b.variants != null && b.variants.Length > 0 && !bindings.ContainsKey(b.layer))
                {
                    bindings[b.layer] = b.variants;
                }
            }
            replanTimer = 0f;
        }

        // Runs on a worker: the engine arrays of the mesh, so the main thread only copies them into a Mesh.
        private static void PrepareJob(TerraChunkJob job)
        {
            job.payload = TerraMeshConverter.Prepare(job.chunk.mesh);
        }

        /// <summary>
        /// One step of the streamer for a viewer at a true position: plan now and then, hand builds to the workers, show some
        /// finished meshes and place some scatter. Update calls it every frame with the viewer's position. Tests and tools
        /// can call it themselves.
        /// </summary>
        public void Tick(double viewerWorldX, double viewerWorldZ, float deltaTime)
        {
            if (world == null)
            {
                return;
            }
            replanTimer -= deltaTime;
            if (replanTimer <= 0f)
            {
                replanTimer = replanInterval;
                Replan(viewerWorldX, viewerWorldZ);
            }
            Submit();
            if (builder.WorkerCount == 0)
            {
                builder.RunSome(syncBuildsPerFrame);
            }
            Drain();
            if (spawnScatter)
            {
                SpawnScatter();
            }
        }

        private void Replan(double viewerWorldX, double viewerWorldZ)
        {
            have.Clear();
            foreach (KeyValuePair<TerraChunkKey, Entry> pair in entries)
            {
                have[pair.Key] = pair.Value.requestedStep != 0 ? pair.Value.requestedStep : pair.Value.loadedStep;
            }
            planner.Plan(viewerWorldX, viewerWorldZ, have, plan);
            for (int i = 0; i < plan.unload.Count; i++)
            {
                Remove(plan.unload[i]);
            }
            queued.Clear();
            queued.AddRange(plan.build);
            queuedCursor = 0;
        }

        private void Remove(TerraChunkKey key)
        {
            Entry e;
            if (!entries.TryGetValue(key, out e))
            {
                return;
            }
            builder.Cancel(key);
            if (e.chunk != null)
            {
                spawning.Remove(e.chunk);
                Raise(ChunkRemoved, e.chunk);
                e.chunk.Release();
                TerraObjects.Discard(e.chunk.gameObject);
            }
            entries.Remove(key);
        }

        private void Submit()
        {
            while (queuedCursor < queued.Count && builder.Pending < maxInFlight)
            {
                TerraChunkRequest r = queued[queuedCursor];
                queuedCursor++;
                Entry e;
                bool known = entries.TryGetValue(r.key, out e);
                if (!known)
                {
                    e = new Entry();
                    entries[r.key] = e;
                }
                else if (e.requestedStep == r.step || (e.requestedStep == 0 && e.loadedStep == r.step))
                {
                    continue;
                }
                nextTicket++;
                e.ticket = nextTicket;
                e.requestedStep = r.step;
                builder.Enqueue(r.key, r.step, !e.scatterDelivered, e.ticket);
            }
        }

        private void Drain()
        {
            int applied = 0;
            TerraChunkJob job;
            while (applied < applyPerFrame && builder.TryTake(out job))
            {
                Entry e;
                if (!entries.TryGetValue(job.key, out e) || e.ticket != job.ticket)
                {
                    // The chunk was let go, or asked for again at another step since this build began.
                    continue;
                }
                if (job.error != null)
                {
                    e.failed = true;
                    Debug.LogError("TerraStreamer: building chunk " + job.key + " failed: " + job.error, this);
                    continue;
                }
                Apply(job, e);
                applied++;
            }
        }

        private void Apply(TerraChunkJob job, Entry e)
        {
            TerraPreparedMesh prepared = job.payload as TerraPreparedMesh;
            if (prepared == null)
            {
                prepared = TerraMeshConverter.Prepare(job.chunk.mesh);
            }
            TerraChunk chunk = e.chunk;
            if (chunk == null)
            {
                GameObject go = new GameObject("Terra " + job.key.x + "," + job.key.z);
                go.transform.SetParent(transform, false);
                chunk = go.AddComponent<TerraChunk>();
                chunk.Initialize(job.key, job.chunk.originX, job.chunk.originZ, profile, ResolveMaterial());
                e.chunk = chunk;
            }
            bool collide = profile.colliders && job.step <= profile.colliderMaxStep;
            chunk.SetMesh(prepared, job.step, collide);
            chunk.Place(originX, originZ);
            e.loadedStep = job.step;
            e.requestedStep = 0;
            if (job.scatter != null)
            {
                chunk.scatterPoints = job.scatter;
                chunk.scatterCursor = 0;
                e.scatterDelivered = true;
                if (spawnScatter && !spawning.Contains(chunk))
                {
                    spawning.Add(chunk);
                }
                Raise(ScatterReady, chunk);
            }
            Raise(ChunkBuilt, chunk);
        }

        private Material ResolveMaterial()
        {
            if (profile.material != null)
            {
                return profile.material;
            }
            if (fallbackMaterial == null)
            {
                fallbackMaterial = TerraMaterials.Create();
                if (fallbackMaterial == null)
                {
                    Debug.LogWarning("TerraStreamer: the profile has no material and no shader was found, so chunks will not draw.", this);
                }
            }
            return fallbackMaterial;
        }

        private void SpawnScatter()
        {
            int budget = scatterPerFrame;
            int i = 0;
            while (i < spawning.Count && budget > 0)
            {
                TerraChunk chunk = spawning[i];
                TerraScatterPoint[] points = chunk.scatterPoints;
                while (budget > 0 && points != null && chunk.scatterCursor < points.Length)
                {
                    SpawnOne(chunk, points[chunk.scatterCursor]);
                    chunk.scatterCursor++;
                    budget--;
                }
                if (points == null || chunk.scatterCursor >= points.Length)
                {
                    spawning.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }
        }

        private void SpawnOne(TerraChunk chunk, TerraScatterPoint p)
        {
            TerraScatterLayer[] layers = world.Settings.scatter;
            if (layers == null || p.layer < 0 || p.layer >= layers.Length || layers[p.layer] == null)
            {
                return;
            }
            GameObject[] variants;
            if (!bindings.TryGetValue(layers[p.layer].name, out variants))
            {
                return;
            }
            GameObject prefab = variants[(p.variant & 0x7FFFFFFF) % variants.Length];
            if (prefab == null)
            {
                return;
            }
            GameObject go = UnityEngine.Object.Instantiate(prefab, chunk.transform);
            Transform t = go.transform;
            t.localPosition = new Vector3((float)(p.x - chunk.originX), (float)p.y, (float)(p.z - chunk.originZ));
            Quaternion yaw = Quaternion.Euler(0f, p.yawDegrees, 0f);
            t.localRotation = layers[p.layer].alignToNormal
                ? Quaternion.FromToRotation(Vector3.up, new Vector3(p.normalX, p.normalY, p.normalZ)) * yaw
                : yaw;
            t.localScale = prefab.transform.localScale * p.scale;
        }

        private static void Raise(Action<TerraChunk> handler, TerraChunk chunk)
        {
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(chunk);
            }
            catch (Exception e)
            {
                Debug.LogError("TerraStreamer: a handler threw: " + e);
            }
        }

        /// <summary>
        /// Tells the streamer the scene's origin is now a different place, and moves every chunk to match. The game must move
        /// everything else by the same amount in the other direction: a true position is the scene position plus the origin.
        /// </summary>
        public void SetWorldOrigin(double x, double z)
        {
            originX = x;
            originZ = z;
            foreach (KeyValuePair<TerraChunkKey, Entry> pair in entries)
            {
                if (pair.Value.chunk != null)
                {
                    pair.Value.chunk.Place(originX, originZ);
                }
            }
        }

        /// <summary>Moves the world origin by a distance. Everything in the scene moves by the negative of it.</summary>
        public void ShiftWorldOrigin(double dx, double dz)
        {
            SetWorldOrigin(originX + dx, originZ + dz);
        }

        /// <summary>The scene position of a true position.</summary>
        public Vector3 WorldToScene(double x, double y, double z)
        {
            return new Vector3((float)(x - originX), (float)y, (float)(z - originZ));
        }

        /// <summary>The true horizontal position of a scene position.</summary>
        public void SceneToWorld(Vector3 p, out double x, out double z)
        {
            x = p.x + originX;
            z = p.z + originZ;
        }

        /// <summary>
        /// The height of the ground in metres at a true position, from the height field. Exact at a vertex of a full detail
        /// chunk; inside a triangle the mesh is a straight line between its corners, so it can differ a little. For the surface
        /// a body touches, use physics.
        /// </summary>
        public double HeightAtWorld(double x, double z)
        {
            return world == null ? 0.0 : world.HeightAt(x, z);
        }

        public bool TryGetChunk(long cx, long cz, out TerraChunk chunk)
        {
            Entry e;
            if (entries.TryGetValue(new TerraChunkKey(cx, cz), out e) && e.chunk != null)
            {
                chunk = e.chunk;
                return true;
            }
            chunk = null;
            return false;
        }
    }
}
