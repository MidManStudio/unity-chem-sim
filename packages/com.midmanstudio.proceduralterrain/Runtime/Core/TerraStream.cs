// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraStream.cs"
// ============================================================================
using System;
using System.Collections.Generic;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>The address of a chunk of the lattice world.</summary>
    public struct TerraChunkKey : IEquatable<TerraChunkKey>
    {
        public long x;
        public long z;

        public TerraChunkKey(long x, long z)
        {
            this.x = x;
            this.z = z;
        }

        public bool Equals(TerraChunkKey other)
        {
            return x == other.x && z == other.z;
        }

        public override bool Equals(object obj)
        {
            return obj is TerraChunkKey && Equals((TerraChunkKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (int)(x * 73856093L) ^ (int)(z * 19349663L);
            }
        }

        public override string ToString()
        {
            return "(" + x + ", " + z + ")";
        }
    }

    /// <summary>How far a streamed world reaches and how steadily it changes.</summary>
    [Serializable]
    public class TerraStreamSettings
    {
        /// <summary>A chunk whose nearest point is within this many metres of the viewer is wanted.</summary>
        public double loadDistance = 900.0;

        /// <summary>A loaded chunk stays until it is this much farther than the load distance. Stops chunks at the edge flickering in and out.</summary>
        public double unloadMargin = 128.0;

        /// <summary>
        /// A chunk keeps its level of detail until the distance has passed the tier boundary by this many metres, so a viewer
        /// standing on a boundary does not rebuild the same chunk back and forth.
        /// </summary>
        public double lodMargin = 24.0;

        /// <summary>The most chunks held at once. The farthest new ones are dropped from a plan when the count would pass it.</summary>
        public int maxChunks = 1024;
    }

    /// <summary>One chunk to build, and the level of detail to build it at.</summary>
    public struct TerraChunkRequest
    {
        public TerraChunkKey key;
        public int step;

        /// <summary>The distance from the viewer to the nearest point of the chunk, in metres.</summary>
        public double distance;
    }

    /// <summary>What a streamer should do now: chunks to build (nearest first) and chunks to let go.</summary>
    public sealed class TerraStreamPlan
    {
        public readonly List<TerraChunkRequest> build = new List<TerraChunkRequest>();
        public readonly List<TerraChunkKey> unload = new List<TerraChunkKey>();

        public void Clear()
        {
            build.Clear();
            unload.Clear();
        }
    }

    /// <summary>
    /// Decides which chunks of a world should exist for a viewer at a true position, and at which level of detail. It knows
    /// nothing about meshes or threads: it is given what is held (a chunk and the step it is held or being built at) and
    /// returns what to change. The answer depends only on the viewer's position relative to the chunk grid, never on how
    /// far from the world origin the viewer is, and the same input always gives the same plan.
    /// </summary>
    public sealed class TerraStreamPlanner
    {
        private readonly TerraWorld world;
        private readonly TerraStreamSettings settings;

        public TerraStreamPlanner(TerraWorld world, TerraStreamSettings settings)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }
            this.world = world;
            this.settings = settings ?? new TerraStreamSettings();
        }

        public TerraStreamSettings Settings
        {
            get { return settings; }
        }

        /// <summary>The horizontal distance from a position to the nearest point of a chunk, 0 inside it.</summary>
        public double DistanceTo(long cx, long cz, double viewerX, double viewerZ)
        {
            double ox;
            double oz;
            world.Lattice.ChunkOrigin(cx, cz, out ox, out oz);
            double size = world.Lattice.ChunkSize;
            double dx = viewerX < ox ? ox - viewerX : (viewerX > ox + size ? viewerX - (ox + size) : 0.0);
            double dz = viewerZ < oz ? oz - viewerZ : (viewerZ > oz + size ? viewerZ - (oz + size) : 0.0);
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// The level of detail for a chunk at a distance, given the step it has now (0 when it has none). A chunk that has a
        /// step changes it only once the distance has gone past the tier boundary by the LOD margin, in either direction.
        /// </summary>
        public int StepFor(double distance, int currentStep)
        {
            int wanted = world.StepForDistance(distance);
            if (currentStep <= 0)
            {
                return wanted;
            }
            double margin = settings.lodMargin < 0.0 ? 0.0 : settings.lodMargin;
            int farther = world.StepForDistance(distance - margin);
            if (farther > currentStep)
            {
                return farther;
            }
            int nearer = world.StepForDistance(distance + margin);
            if (nearer < currentStep)
            {
                return nearer;
            }
            return currentStep;
        }

        /// <summary>
        /// Fills the plan. <paramref name="have"/> is every chunk that is held or already being built, with its step. A chunk in
        /// it is let go when it is beyond the load distance plus the margin, rebuilt when its step should change, and left alone
        /// otherwise. A chunk not in it is built when within the load distance. Builds come nearest first, ties by position.
        /// </summary>
        public void Plan(double viewerX, double viewerZ, Dictionary<TerraChunkKey, int> have, TerraStreamPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }
            plan.Clear();
            double load = settings.loadDistance < 0.0 ? 0.0 : settings.loadDistance;
            double keep = load + (settings.unloadMargin < 0.0 ? 0.0 : settings.unloadMargin);

            int held = 0;
            if (have != null)
            {
                foreach (KeyValuePair<TerraChunkKey, int> pair in have)
                {
                    double d = DistanceTo(pair.Key.x, pair.Key.z, viewerX, viewerZ);
                    if (d > keep)
                    {
                        plan.unload.Add(pair.Key);
                        continue;
                    }
                    held++;
                    int step = StepFor(d, pair.Value);
                    if (step != pair.Value)
                    {
                        TerraChunkRequest r;
                        r.key = pair.Key;
                        r.step = step;
                        r.distance = d;
                        plan.build.Add(r);
                    }
                }
            }

            long vx;
            long vz;
            world.Lattice.ChunkOf(viewerX, viewerZ, out vx, out vz);
            long reach = (long)Math.Ceiling(load / world.Lattice.ChunkSize) + 1;
            List<TerraChunkRequest> fresh = new List<TerraChunkRequest>();
            for (long cz = vz - reach; cz <= vz + reach; cz++)
            {
                for (long cx = vx - reach; cx <= vx + reach; cx++)
                {
                    TerraChunkKey key = new TerraChunkKey(cx, cz);
                    if (have != null && have.ContainsKey(key))
                    {
                        continue;
                    }
                    double d = DistanceTo(cx, cz, viewerX, viewerZ);
                    if (d > load)
                    {
                        continue;
                    }
                    TerraChunkRequest r;
                    r.key = key;
                    r.step = world.StepForDistance(d);
                    r.distance = d;
                    fresh.Add(r);
                }
            }
            fresh.Sort(Compare);

            // The cap counts what is held and stays, plus the new chunks, and drops the farthest new ones.
            int room = settings.maxChunks - held;
            if (room < 0)
            {
                room = 0;
            }
            int take = fresh.Count < room ? fresh.Count : room;
            for (int i = 0; i < take; i++)
            {
                plan.build.Add(fresh[i]);
            }
            plan.build.Sort(Compare);
            plan.unload.Sort(CompareKeys);
        }

        private static int CompareKeys(TerraChunkKey a, TerraChunkKey b)
        {
            int c = a.x.CompareTo(b.x);
            return c != 0 ? c : a.z.CompareTo(b.z);
        }

        private static int Compare(TerraChunkRequest a, TerraChunkRequest b)
        {
            int c = a.distance.CompareTo(b.distance);
            if (c != 0)
            {
                return c;
            }
            c = a.key.x.CompareTo(b.key.x);
            if (c != 0)
            {
                return c;
            }
            return a.key.z.CompareTo(b.key.z);
        }
    }
}
