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
    /// Everything one build of the manager owns: a host with the rigs of all chains, the table of unique transforms
    /// for the transform jobs, and the per rig and per entry arrays the jobs read. It is built from a list of
    /// chains, run once a frame and thrown away when chains come or go, with the state of the chains that stay carried
    /// over to the next build. It never changes while a frame runs.
    /// </summary>
    internal sealed unsafe class JiggleRuntime : IDisposable
    {
        /// <summary>The solver arrays.</summary>
        internal readonly JiggleHost Host;
        /// <summary>The chains by rig index.</summary>
        internal readonly List<JiggleChain> Chains = new List<JiggleChain>();

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
        /// Builds a runtime for the chains that can be simulated: a chain that cannot, or that wants a bone another
        /// chain already drives, is logged and left out. A chain that was part of <paramref name="previous"/> keeps
        /// what it had simulated as long as its particle count did not change, and the clock carries over.
        /// </summary>
        internal static JiggleRuntime Build(List<JiggleChain> candidates, JiggleRuntime previous)
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

            JiggleHost host = new JiggleHost(ok.Count < 4 ? 4 : ok.Count, particles < 16 ? 16 : particles);
            if (previous != null)
            {
                host.CopyClock(previous.Host);
            }
            JiggleRuntime rt = new JiggleRuntime(host, ok.Count, entries, localCount);
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
        /// Runs one frame: reads the transforms, writes the pose, runs the solver on the chosen backend and writes the
        /// bone rotations. The settings in the host must be set before. Returns 0 or the status of the solver.
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
            h.EndFrame();
            return code;
        }

        private JobHandle ScheduleOutput(JobHandle dependsOn)
        {
            JiggleHost h = Host;
            JiggleLocalJob local = new JiggleLocalJob();
            local.Rigs = h.Rigs;
            local.PoseRot = h.PoseRot;
            local.Out = h.Out;
            local.RigLocal = rigLocal;
            local.Locals = locals;
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
            Host.Dispose();
        }
    }
}
