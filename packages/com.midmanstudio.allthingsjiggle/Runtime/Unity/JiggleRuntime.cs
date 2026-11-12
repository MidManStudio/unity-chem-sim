// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleRuntime.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Everything one build of the manager owns: a host with the rigs of all chains and lattices, the table of unique
    /// transforms for the transform jobs, and the per rig and per entry arrays the jobs read. It is built from a list of
    /// chains and a list of lattices, run once a frame and thrown away when they come or go, with the state of those that
    /// stay carried over to the next build. The rigs of the chains come first, then those of the lattices. It never changes
    /// while a frame runs.
    /// </summary>
    internal sealed unsafe class JiggleRuntime : IDisposable
    {
        /// <summary>The solver arrays.</summary>
        internal readonly JiggleHost Host;
        /// <summary>The chains by rig index.</summary>
        internal readonly List<JiggleChain> Chains = new List<JiggleChain>();
        /// <summary>The lattices by rig index minus the number of chains, because their rigs come after those of the chains.</summary>
        internal readonly List<JiggleLattice> Lattices = new List<JiggleLattice>();
        /// <summary>How many meshes the last frame uploaded. A lattice at rest, or asleep, uploads nothing.</summary>
        internal int LatticeUploads;

        private TransformAccessArray taa;
        private bool taaCreated;
        private JiggleSample* samples;
        private byte* readFlags;
        private int* writeSlot;
        private int* rigEntry;
        private int* rigLocal;
        private byte* rigMode;
        private int* entryTransform;
        private JVec3* bindPos;
        private JQuat* bindRot;
        private JQuat* locals;
        private int* status;
        private int statusCap;
        private JiggleLatticeBatch latticeBatch;
        private bool disposed;

        private JiggleRuntime(JiggleHost host, int rigs, int entries, int localCount)
        {
            Host = host;
            samples = JiggleMem.Alloc<JiggleSample>(entries);
            readFlags = JiggleMem.Alloc<byte>(entries);
            writeSlot = JiggleMem.Alloc<int>(entries);
            for (int i = 0; i < entries; i++)
            {
                writeSlot[i] = -1;
            }
            rigEntry = JiggleMem.Alloc<int>(rigs);
            rigLocal = JiggleMem.Alloc<int>(rigs);
            rigMode = JiggleMem.Alloc<byte>(rigs);
            entryTransform = JiggleMem.Alloc<int>(entries);
            bindPos = JiggleMem.Alloc<JVec3>(entries);
            bindRot = JiggleMem.Alloc<JQuat>(entries);
            locals = JiggleMem.Alloc<JQuat>(localCount);
        }

        /// <summary>The unique transforms of a build, in the order the transform jobs see them.</summary>
        private sealed class TransformTable
        {
            private readonly Dictionary<Transform, int> index = new Dictionary<Transform, int>();
            public readonly List<Transform> Transforms = new List<Transform>();
            public readonly List<byte> Flags = new List<byte>();
            public readonly List<int> Slots = new List<int>();

            public int Add(Transform t, byte flags, int slot)
            {
                int i;
                if (!index.TryGetValue(t, out i))
                {
                    i = Transforms.Count;
                    index.Add(t, i);
                    Transforms.Add(t);
                    Flags.Add(0);
                    Slots.Add(-1);
                }
                Flags[i] = (byte)(Flags[i] | flags);
                if (slot >= 0)
                {
                    Slots[i] = slot;
                }
                return i;
            }
        }

        /// <summary>
        /// Builds a runtime for the chains and lattices that can be simulated: one that cannot, or a chain that wants a bone
        /// another chain already drives, is logged and left out. A chain that was part of <paramref name="previous"/> keeps
        /// what it had simulated as long as its particle count did not change, a lattice as long as its mesh, box, grid and
        /// anchor did not change, and the clock carries over.
        /// </summary>
        internal static JiggleRuntime Build(List<JiggleChain> candidates, List<JiggleLattice> latticeCandidates, JiggleRuntime previous)
        {
            List<JiggleChain> ok = new List<JiggleChain>(candidates.Count);
            Dictionary<Transform, JiggleChain> driven = new Dictionary<Transform, JiggleChain>();
            int entries = 0;
            int localCount = 0;
            int particles = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                JiggleChain c = candidates[i];
                if (c == null)
                {
                    continue;
                }
                string problem;
                if (!c.Prepare(out problem))
                {
                    Debug.LogWarning("Jiggle chain '" + c.name + "' is not simulated: " + problem, c);
                    c.Detach();
                    continue;
                }
                Transform[] bones = c.BonesInternal;
                JiggleChain other = null;
                Transform clash = null;
                for (int b = 0; b < bones.Length && clash == null; b++)
                {
                    if (driven.TryGetValue(bones[b], out other))
                    {
                        clash = bones[b];
                    }
                }
                if (clash != null)
                {
                    Debug.LogWarning(
                        "Jiggle chain '" + c.name + "' is not simulated: the bone '" + clash.name + "' is already driven by '" + other.name + "'.", c);
                    c.Detach();
                    continue;
                }
                for (int b = 0; b < bones.Length; b++)
                {
                    driven.Add(bones[b], c);
                }
                ok.Add(c);
                entries += bones.Length + 1;
                localCount += bones.Length;
                particles += bones.Length + 2;
            }

            List<JiggleLattice> okLattices = new List<JiggleLattice>(latticeCandidates.Count);
            for (int i = 0; i < latticeCandidates.Count; i++)
            {
                JiggleLattice l = latticeCandidates[i];
                if (l == null)
                {
                    continue;
                }
                string problem;
                if (!l.Prepare(out problem))
                {
                    Debug.LogWarning("Jiggle lattice '" + l.name + "' is not simulated: " + problem, l);
                    l.Detach();
                    continue;
                }
                okLattices.Add(l);
                entries += l.Tree.Count;
                particles += l.Tree.Count;
            }

            int rigCount = ok.Count + okLattices.Count;
            JiggleHost host = new JiggleHost(rigCount < 4 ? 4 : rigCount, particles < 16 ? 16 : particles);
            if (previous != null)
            {
                host.CopyClock(previous.Host);
            }
            JiggleRuntime rt = new JiggleRuntime(host, rigCount, entries, localCount);
            TransformTable table = new TransformTable();
            int entryCursor = 0;
            int localCursor = 0;
            for (int r = 0; r < ok.Count; r++)
            {
                JiggleChain c = ok[r];
                Transform[] bones = c.BonesInternal;
                int n = bones.Length;
                bool bind = c.poseSource == JigglePoseSource.Bind;
                int rig = host.AddChain(c.RestWorld(), c.settings.ToParams(n), c.tipScale);
                int old = c.RigIndex;
                if (previous != null && object.ReferenceEquals(c.Runtime, previous) && old >= 0 && old < previous.Host.RigCount
                    && previous.Host.Rigs[old].ParticleCount == host.Rigs[rig].ParticleCount)
                {
                    host.CopyRigRuntime(previous.Host, old, rig);
                }
                c.Attach(rt, rig);
                c.BuiltSource = c.poseSource;
                rt.Chains.Add(c);
                rt.rigEntry[rig] = entryCursor;
                rt.rigLocal[rig] = localCursor;
                rt.rigMode[rig] = bind ? (byte)0 : (byte)1;
                byte anchorFlags = bind ? (byte)(JiggleReadJob.NeedPose | JiggleReadJob.NeedMatrix) : JiggleReadJob.NeedPose;
                rt.entryTransform[entryCursor] = table.Add(c.Anchor, anchorFlags, -1);
                rt.bindPos[entryCursor] = JVec3.Zero;
                rt.bindRot[entryCursor] = JQuat.Identity;
                for (int k = 1; k <= n; k++)
                {
                    rt.entryTransform[entryCursor + k] = table.Add(bones[k - 1], bind ? (byte)0 : JiggleReadJob.NeedPose, localCursor + k - 1);
                    if (bind)
                    {
                        Vector3 p = c.BindPos[k];
                        Quaternion q = c.BindRot[k];
                        rt.bindPos[entryCursor + k] = new JVec3(p.x, p.y, p.z);
                        rt.bindRot[entryCursor + k] = new JQuat(q.x, q.y, q.z, q.w);
                    }
                }
                entryCursor += n + 1;
                localCursor += n;
            }

            List<int> latticeRigs = new List<int>(okLattices.Count);
            List<int> latticeEntries = new List<int>(okLattices.Count);
            for (int i = 0; i < okLattices.Count; i++)
            {
                JiggleLattice l = okLattices[i];
                JiggleLatticeTree tree = l.Tree;
                int n = tree.Count;
                int rig = host.AddTree(l.RestWorld(), tree.ParentsOfParticles(), l.settings.ToParams(n - 1), false);
                int old = l.RigIndex;
                if (previous != null && object.ReferenceEquals(l.Runtime, previous) && l.AttachedStamp == l.BuildStamp
                    && old >= 0 && old < previous.Host.RigCount
                    && previous.Host.Rigs[old].ParticleCount == host.Rigs[rig].ParticleCount)
                {
                    host.CopyRigRuntime(previous.Host, old, rig);
                }
                l.Attach(rt, rig);
                rt.Lattices.Add(l);
                rt.rigEntry[rig] = entryCursor;
                rt.rigLocal[rig] = localCursor;
                rt.rigMode[rig] = JigglePoseJob.RigModeLattice;
                // Every control point is an entry on the one transform of the object, and its bind position is its rest place in
                // the space of the mesh. The transform is read for its matrix and never written.
                int transformIndex = table.Add(l.transform, (byte)(JiggleReadJob.NeedPose | JiggleReadJob.NeedMatrix), -1);
                for (int k = 0; k < n; k++)
                {
                    rt.entryTransform[entryCursor + k] = transformIndex;
                    rt.bindPos[entryCursor + k] = l.RestLocal[k];
                    rt.bindRot[entryCursor + k] = JQuat.Identity;
                }
                latticeRigs.Add(rig);
                latticeEntries.Add(entryCursor);
                entryCursor += n;
            }
            if (okLattices.Count > 0)
            {
                rt.latticeBatch = new JiggleLatticeBatch(okLattices, latticeRigs, latticeEntries);
            }

            for (int i = 0; i < table.Transforms.Count; i++)
            {
                rt.readFlags[i] = table.Flags[i];
                rt.writeSlot[i] = table.Slots[i];
            }
            if (table.Transforms.Count > 0)
            {
                rt.taa = new TransformAccessArray(table.Transforms.ToArray(), -1);
                rt.taaCreated = true;
            }
            return rt;
        }

        private void EnsureStatus(int batches)
        {
            if (batches > statusCap)
            {
                JiggleMem.Free(ref status);
                statusCap = batches < 16 ? 16 : batches;
                status = JiggleMem.Alloc<int>(statusCap);
            }
        }

        /// <summary>
        /// Runs one frame: reads the transforms, writes the pose, runs the solver on the chosen backend, writes the bone
        /// rotations and deforms the meshes of the lattices. The settings in the host must be set before. Returns 0 or the
        /// status of the solver.
        /// </summary>
        internal int Frame(float frameDt, JiggleRuntimeBackend backend, int rigsPerBatch)
        {
            JiggleHost h = Host;
            int rigs = h.RigCount;
            if (rigs == 0 || !taaCreated)
            {
                return JiggleConst.Ok;
            }
            h.BeginFrame(frameDt);
            h.RefreshArgs();

            JiggleReadJob read = new JiggleReadJob();
            read.Samples = samples;
            read.Flags = readFlags;
            JobHandle jh = read.ScheduleReadOnly(taa, 64, default(JobHandle));

            JigglePoseJob pose = new JigglePoseJob();
            pose.Rigs = h.Rigs;
            pose.PosePrev = h.PosePrev;
            pose.PoseCurr = h.PoseCurr;
            pose.PoseRot = h.PoseRot;
            pose.TipScale = h.TipScale;
            pose.Samples = samples;
            pose.RigEntry = rigEntry;
            pose.EntryTransform = entryTransform;
            pose.RigMode = rigMode;
            pose.BindPos = bindPos;
            pose.BindRot = bindRot;
            jh = pose.Schedule(rigs, 16, jh);

            int code = JiggleConst.Ok;
            if (backend == JiggleRuntimeBackend.Burst)
            {
                int batches = JiggleBurstSolver.BatchCount(rigs, rigsPerBatch);
                EnsureStatus(batches);
                jh = JiggleBurstSolver.Schedule(h, rigsPerBatch, status, JiggleBurstSolver.PhaseBoth, jh);
                jh = ScheduleOutput(jh);
                jh.Complete();
                code = JiggleBurstSolver.Worst(status, batches);
            }
            else
            {
                jh.Complete();
                JiggleBackend b = backend == JiggleRuntimeBackend.Native ? JiggleBackend.Native : JiggleBackend.Managed;
                code = h.Run(b, 0, (uint)rigs, false);
                if (code == JiggleConst.Ok)
                {
                    code = h.Run(b, 0, (uint)rigs, true);
                }
                ScheduleOutput(default(JobHandle)).Complete();
            }
            LatticeUploads = latticeBatch != null ? latticeBatch.Apply() : 0;
            h.EndFrame();
            return code;
        }

        // The jobs that hand the result to the scene: the bone rotations of the chains, and the control displacements and moved
        // vertices of the lattices. They only read what the solver wrote and write different things, so they run side by side.
        private JobHandle ScheduleOutput(JobHandle dependsOn)
        {
            JobHandle bones = ScheduleBones(dependsOn);
            if (latticeBatch == null)
            {
                return bones;
            }
            JobHandle lattices = latticeBatch.Schedule(Host, samples, entryTransform, dependsOn);
            return JobHandle.CombineDependencies(bones, lattices);
        }

        private JobHandle ScheduleBones(JobHandle dependsOn)
        {
            JiggleHost h = Host;
            JiggleLocalJob local = new JiggleLocalJob();
            local.Rigs = h.Rigs;
            local.PoseRot = h.PoseRot;
            local.Out = h.Out;
            local.RigLocal = rigLocal;
            local.Locals = locals;
            local.RigMode = rigMode;
            JobHandle jh = local.Schedule(h.RigCount, 16, dependsOn);
            JiggleWriteJob write = new JiggleWriteJob();
            write.Locals = locals;
            write.WriteSlot = writeSlot;
            return write.Schedule(taa, jh);
        }

        /// <summary>Frees everything. The chains that pointed here are not told, the manager rebuilds them.</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (taaCreated)
            {
                taa.Dispose();
                taaCreated = false;
            }
            JiggleMem.Free(ref samples);
            JiggleMem.Free(ref readFlags);
            JiggleMem.Free(ref writeSlot);
            JiggleMem.Free(ref rigEntry);
            JiggleMem.Free(ref rigLocal);
            JiggleMem.Free(ref rigMode);
            JiggleMem.Free(ref entryTransform);
            JiggleMem.Free(ref bindPos);
            JiggleMem.Free(ref bindRot);
            JiggleMem.Free(ref locals);
            JiggleMem.Free(ref status);
            if (latticeBatch != null)
            {
                latticeBatch.Dispose();
                latticeBatch = null;
            }
            Host.Dispose();
        }
    }
}
