// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleHost.cs"
// ============================================================================
using System;
using System.Runtime.InteropServices;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// The solver arrays in unmanaged memory with room to grow, for a host that runs the solver from jobs. The
    /// addresses stay valid until the next call that adds something, so jobs may hold them while a frame runs
    /// and nothing may be added meanwhile. It builds rigs the way <see cref="JiggleWorld"/> does (the same code),
    /// and the backends read and write it through <see cref="Args"/> and <see cref="Cfg"/>. It has no Unity references.
    /// </summary>
    public sealed unsafe class JiggleHost : IDisposable
    {
        /// <summary>Rig headers.</summary>
        public JiggleRig* Rigs;
        /// <summary>Particle definitions.</summary>
        public JiggleParticleDef* Defs;
        /// <summary>Particle states.</summary>
        public JiggleParticleState* State;
        /// <summary>Pose positions of the last frame, relative to the anchor of the last frame.</summary>
        public JVec4* PosePrev;
        /// <summary>Pose positions of this frame, relative to the anchor of this frame.</summary>
        public JVec4* PoseCurr;
        /// <summary>Pose rotations of this frame.</summary>
        public JVec4* PoseRot;
        /// <summary>Output of the last resolve.</summary>
        public JiggleParticleOut* Out;
        /// <summary>Distance of the virtual end from the last bone per rig, as a multiple of the last bone's length.</summary>
        public float* TipScale;
        /// <summary>Colliders in world space. Valid for <see cref="ColliderCount"/> entries.</summary>
        public JiggleCollider* Colliders;
        /// <summary>The collider lists of all rigs, one after the other, as indices into <see cref="Colliders"/>.</summary>
        public uint* Refs;
        /// <summary>Force fields in world space. Valid for <see cref="FieldCount"/> entries.</summary>
        public JiggleField* Fields;
        /// <summary>The settings of the next call. One record at a fixed address.</summary>
        public JiggleStepConfig* Cfg;
        /// <summary>The argument block of the next call, filled by <see cref="RefreshArgs"/>. One record at a fixed address.</summary>
        public JiggleSolverArgs* Args;

        /// <summary>Number of rigs.</summary>
        public int RigCount;
        /// <summary>Number of particles over all rigs.</summary>
        public int ParticleCount;
        /// <summary>Number of colliders.</summary>
        public int ColliderCount;
        /// <summary>Number of collider indices over all rigs.</summary>
        public int RefCount;
        /// <summary>Number of fields.</summary>
        public int FieldCount;

        /// <summary>Fixed step length in seconds.</summary>
        public float FixedDt;
        /// <summary>Most fixed steps one <see cref="Step"/> call runs. Leftover time beyond it is dropped.</summary>
        public uint MaxSteps;

        private int rigCap;
        private int particleCap;
        private int colliderCap;
        private int refCap;
        private int fieldCap;
        private float accumulator;
        private bool disposed;

        /// <summary>An empty host with a 60 Hz step and the defaults of <see cref="JiggleStepConfig.Defaults"/>.</summary>
        public JiggleHost(int rigCapacity = 64, int particleCapacity = 1024)
        {
            FixedDt = 1.0f / 60.0f;
            MaxSteps = 8;
            Cfg = (JiggleStepConfig*)Marshal.AllocHGlobal(sizeof(JiggleStepConfig));
            *Cfg = JiggleStepConfig.Defaults(FixedDt);
            Args = (JiggleSolverArgs*)Marshal.AllocHGlobal(sizeof(JiggleSolverArgs));
            *Args = new JiggleSolverArgs();
            Reserve(rigCapacity < 4 ? 4 : rigCapacity, particleCapacity < 16 ? 16 : particleCapacity);
        }

        /// <summary>Frees the memory. The host cannot be used afterwards. Calling it twice does nothing.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Frees the memory if Dispose was not called.</summary>
        ~JiggleHost()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Free(ref Rigs);
            Free(ref Defs);
            Free(ref State);
            Free(ref PosePrev);
            Free(ref PoseCurr);
            Free(ref PoseRot);
            Free(ref Out);
            Free(ref TipScale);
            Free(ref Colliders);
            Free(ref Refs);
            Free(ref Fields);
            Free(ref Cfg);
            Free(ref Args);
            RigCount = 0;
            ParticleCount = 0;
            ColliderCount = 0;
            RefCount = 0;
            FieldCount = 0;
        }

        private static void Free<T>(ref T* p) where T : unmanaged
        {
            if (p != null)
            {
                Marshal.FreeHGlobal((IntPtr)p);
                p = null;
            }
        }

        private static void Grow<T>(ref T* p, int count, int oldCap, int newCap) where T : unmanaged
        {
            long bytes = (long)newCap * sizeof(T);
            T* np = (T*)Marshal.AllocHGlobal((IntPtr)bytes);
            if (p != null)
            {
                if (count > 0)
                {
                    Buffer.MemoryCopy(p, np, bytes, (long)count * sizeof(T));
                }
                Marshal.FreeHGlobal((IntPtr)p);
            }
            p = np;
        }

        // Makes room for at least the given numbers of rigs and particles, doubling so adding is cheap on average.
        private void Reserve(int rigs, int particles)
        {
            if (rigs > rigCap)
            {
                int cap = rigCap < 4 ? 4 : rigCap;
                while (cap < rigs)
                {
                    cap *= 2;
                }
                Grow(ref Rigs, RigCount, rigCap, cap);
                Grow(ref TipScale, RigCount, rigCap, cap);
                rigCap = cap;
            }
            if (particles > particleCap)
            {
                int cap = particleCap < 16 ? 16 : particleCap;
                while (cap < particles)
                {
                    cap *= 2;
                }
                Grow(ref Defs, ParticleCount, particleCap, cap);
                Grow(ref State, ParticleCount, particleCap, cap);
                Grow(ref PosePrev, ParticleCount, particleCap, cap);
                Grow(ref PoseCurr, ParticleCount, particleCap, cap);
                Grow(ref PoseRot, ParticleCount, particleCap, cap);
                Grow(ref Out, ParticleCount, particleCap, cap);
                particleCap = cap;
            }
        }

        /// <summary>
        /// Adds a rig from the rest pose of a chain in world space, exactly as <see cref="JiggleWorld.AddChain"/> does,
        /// and returns its index. Throws <see cref="ArgumentException"/> for fewer than two points or a parameter count
        /// that is not one per bone, and <see cref="ObjectDisposedException"/> after Dispose.
        /// </summary>
        public int AddChain(JVec3[] restWorld, JiggleParams[] p, float tipScale = 1.0f)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("JiggleHost");
            }
            JiggleChainBuild.Validate(restWorld, p);
            tipScale = JiggleChainBuild.CleanTipScale(tipScale);
            JiggleParticleDef[] defs = JiggleChainBuild.BakeDefs(p);
            JVec3[] worldPose = JiggleChainBuild.PoseWithTip(restWorld, tipScale);
            int n = defs.Length;
            Reserve(RigCount + 1, ParticleCount + n);

            JVec3 anchor = restWorld[0];
            int offset = ParticleCount;
            for (int i = 0; i < n; i++)
            {
                JVec4 a = new JVec4(0f, 0f, 0f, 0f);
                (worldPose[i] - anchor).Write4(ref a);
                JiggleParticleState s = new JiggleParticleState();
                s.Pos = a;
                s.Prev = a;
                Defs[offset + i] = defs[i];
                State[offset + i] = s;
                PosePrev[offset + i] = a;
                PoseCurr[offset + i] = a;
                PoseRot[offset + i] = JQuat.Identity.To4();
                Out[offset + i] = new JiggleParticleOut();
            }
            JiggleRig rig = new JiggleRig();
            rig.ParticleOffset = (uint)offset;
            rig.ParticleCount = (uint)n;
            rig.RefOffset = 0;
            rig.RefCount = 0;
            rig.Extent = JiggleChainBuild.Extent(worldPose);
            rig.AnchorWorld = anchor;
            Rigs[RigCount] = rig;
            TipScale[RigCount] = tipScale;
            ParticleCount += n;
            RigCount++;
            return RigCount - 1;
        }

        /// <summary>
        /// Sets the mass of particle <paramref name="particle"/> of a rig, counted in the rig: 1 is the first bone and
        /// the last index is the virtual end particle. 0 makes its links one way again.
        /// </summary>
        public void SetMass(int rig, int particle, float mass)
        {
            if (rig < 0 || rig >= RigCount || particle < 1 || particle >= (int)Rigs[rig].ParticleCount)
            {
                throw new ArgumentException("the particle is outside the rig");
            }
            Defs[(int)Rigs[rig].ParticleOffset + particle].Mass =
                mass > JiggleParams.MaxMass ? JiggleParams.MaxMass : (mass > 0.0f ? mass : 0.0f);
        }

        /// <summary>
        /// Bakes new parameters into a rig in place, one set per bone as in <see cref="AddChain"/>, without touching
        /// its state. The number of sets must be the number of bones the rig was built with. A mass given with
        /// <see cref="SetMass"/> to the virtual end particle is replaced by the mass of the last bone.
        /// </summary>
        public void SetParams(int rig, JiggleParams[] p)
        {
            CheckRig(rig);
            if (p == null || p.Length != (int)Rigs[rig].ParticleCount - 2)
            {
                throw new ArgumentException("one parameter set per bone is needed");
            }
            JiggleParticleDef[] defs = JiggleChainBuild.BakeDefs(p);
            int offset = (int)Rigs[rig].ParticleOffset;
            for (int i = 0; i < defs.Length; i++)
            {
                Defs[offset + i] = defs[i];
            }
        }

        /// <summary>
        /// Moves the virtual end of a rig to tipScale bone lengths past the last bone. The change shows from the
        /// next pose written with the host's tip scale, which <see cref="JigglePose.Write"/> reads from <see cref="TipScale"/>.
        /// </summary>
        public void SetTipScale(int rig, float tipScale)
        {
            CheckRig(rig);
            TipScale[rig] = JiggleChainBuild.CleanTipScale(tipScale);
        }

        /// <summary>Makes a rig snap to its pose on the next step: the solver clears the flag it sets here.</summary>
        public void Reset(int rig)
        {
            CheckRig(rig);
            Rigs[rig].Flags |= JiggleConst.RigReset;
        }

        private void CheckRig(int rig)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("JiggleHost");
            }
            if (rig < 0 || rig >= RigCount)
            {
                throw new ArgumentException("the rig does not exist");
            }
        }

        /// <summary>Adds a collider and returns its index.</summary>
        public int AddCollider(JiggleCollider c)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("JiggleHost");
            }
            ReserveColliders(ColliderCount + 1);
            Colliders[ColliderCount] = c;
            ColliderCount++;
            return ColliderCount - 1;
        }

        /// <summary>Adds a field and returns its index.</summary>
        public int AddField(JiggleField f)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("JiggleHost");
            }
            ReserveFields(FieldCount + 1);
            Fields[FieldCount] = f;
            FieldCount++;
            return FieldCount - 1;
        }

        /// <summary>
        /// Gives a rig the colliders it should test, by index into <see cref="Colliders"/>. Lists sit one after the
        /// other in <see cref="Refs"/>, so a rig can be given a list once, and the lists are kept when rigs are added.
        /// Giving a rig a second list leaves the first one in <see cref="Refs"/> unused, so a host that rewrites its
        /// lists calls <see cref="ClearColliderLists"/> first.
        /// </summary>
        public void SetColliderList(int rig, uint[] list)
        {
            if (list == null)
            {
                throw new ArgumentException("the list is missing");
            }
            fixed (uint* p = list)
            {
                SetColliderList(rig, p, list.Length);
            }
        }

        /// <summary>
        /// The same as <see cref="SetColliderList(int, uint[])"/> for a list in memory the caller owns, so a host that
        /// builds a list every frame does not allocate. The list is copied. A count of 0 gives the rig no colliders.
        /// </summary>
        public void SetColliderList(int rig, uint* list, int count)
        {
            CheckRig(rig);
            if (count < 0 || (count > 0 && list == null))
            {
                throw new ArgumentException("the list is missing");
            }
            ReserveRefs(RefCount + count);
            for (int i = 0; i < count; i++)
            {
                Refs[RefCount + i] = list[i];
            }
            Rigs[rig].RefOffset = (uint)RefCount;
            Rigs[rig].RefCount = (uint)count;
            RefCount += count;
        }

        /// <summary>
        /// Replaces collider <paramref name="index"/>, which must exist. The lists of the rigs are kept, so a collider
        /// that moves is written here and nothing else changes.
        /// </summary>
        public void SetCollider(int index, in JiggleCollider c)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("JiggleHost");
            }
            if (index < 0 || index >= ColliderCount)
            {
                throw new ArgumentException("the collider does not exist");
            }
            Colliders[index] = c;
        }

        /// <summary>
        /// Removes every collider and every rig's list of them, so the scene can write its colliders again. The memory
        /// is kept.
        /// </summary>
        public void ClearColliders()
        {
            ClearColliderLists();
            ColliderCount = 0;
        }

        /// <summary>
        /// Gives every rig an empty list again and forgets all the lists, without removing colliders. The memory is kept.
        /// </summary>
        public void ClearColliderLists()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("JiggleHost");
            }
            for (int r = 0; r < RigCount; r++)
            {
                Rigs[r].RefOffset = 0;
                Rigs[r].RefCount = 0;
            }
            RefCount = 0;
        }

        /// <summary>Removes every field, so the scene can write its fields again. The memory is kept.</summary>
        public void ClearFields()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("JiggleHost");
            }
            FieldCount = 0;
        }

        /// <summary>Whether the solver put the rig to sleep, so its output is the pose.</summary>
        public bool IsAsleep(int rig)
        {
            CheckRig(rig);
            return (Rigs[rig].Flags & JiggleConst.RigAsleep) != 0;
        }

        /// <summary>Whether the rig is switched off, so the solver skips it and its output is the pose.</summary>
        public bool IsDisabled(int rig)
        {
            CheckRig(rig);
            return (Rigs[rig].Flags & JiggleConst.RigDisabled) != 0;
        }

        /// <summary>
        /// Switches a rig off or on. A rig that is off costs nothing in the solver and outputs its pose. Switching it
        /// on again makes it snap to its pose on the next step, because its state is as old as the day it was
        /// switched off and would swing from there.
        /// </summary>
        public void SetDisabled(int rig, bool disabled)
        {
            CheckRig(rig);
            uint f = Rigs[rig].Flags;
            bool was = (f & JiggleConst.RigDisabled) != 0;
            if (disabled == was)
            {
                return;
            }
            if (disabled)
            {
                f |= JiggleConst.RigDisabled;
            }
            else
            {
                f &= ~JiggleConst.RigDisabled;
                f |= JiggleConst.RigReset;
            }
            Rigs[rig].Flags = f;
        }

        /// <summary>
        /// Wakes a sleeping rig, so the next step simulates it. The state of a sleeping rig is its pose, so it carries
        /// on from there. Does nothing to a rig that is awake, except to start its time at rest again.
        /// </summary>
        public void Wake(int rig)
        {
            CheckRig(rig);
            Rigs[rig].Flags &= ~JiggleConst.RigAsleep;
            Rigs[rig].SleepCounter = 0;
        }

        private void ReserveColliders(int count)
        {
            if (count > colliderCap)
            {
                int cap = NextCap(colliderCap, count, 8);
                Grow(ref Colliders, ColliderCount, colliderCap, cap);
                colliderCap = cap;
            }
        }

        private void ReserveRefs(int count)
        {
            if (count > refCap)
            {
                int cap = NextCap(refCap, count, 16);
                Grow(ref Refs, RefCount, refCap, cap);
                refCap = cap;
            }
        }

        private void ReserveFields(int count)
        {
            if (count > fieldCap)
            {
                int cap = NextCap(fieldCap, count, 4);
                Grow(ref Fields, FieldCount, fieldCap, cap);
                fieldCap = cap;
            }
        }

        private static int NextCap(int cap, int need, int min)
        {
            int c = cap < min ? min : cap;
            while (c < need)
            {
                c *= 2;
            }
            return c;
        }

        /// <summary>
        /// Copies what a rig has simulated so far, its state, its pose, its output and its header flags and anchor, from
        /// a rig of another host, so a rebuilt host carries on where the old one was. Both rigs must have the same
        /// number of particles.
        /// </summary>
        public void CopyRigRuntime(JiggleHost source, int sourceRig, int rig)
        {
            JiggleRig s = source.Rigs[sourceRig];
            JiggleRig d = Rigs[rig];
            if (s.ParticleCount != d.ParticleCount)
            {
                throw new ArgumentException("the rigs have different particle counts");
            }
            int n = (int)s.ParticleCount;
            int so = (int)s.ParticleOffset;
            int dof = (int)d.ParticleOffset;
            for (int i = 0; i < n; i++)
            {
                State[dof + i] = source.State[so + i];
                PosePrev[dof + i] = source.PosePrev[so + i];
                PoseCurr[dof + i] = source.PoseCurr[so + i];
                PoseRot[dof + i] = source.PoseRot[so + i];
                Out[dof + i] = source.Out[so + i];
            }
            d.Flags = s.Flags;
            d.SleepCounter = s.SleepCounter;
            d.AnchorWorld = s.AnchorWorld;
            d.AnchorDelta = s.AnchorDelta;
            Rigs[rig] = d;
        }

        /// <summary>The time carried over between frames that has not made a whole fixed step yet, in seconds.</summary>
        public float Accumulator
        {
            get { return accumulator; }
            set { accumulator = JiggleMath.IsFinite(value) && value > 0.0f ? value : 0.0f; }
        }

        /// <summary>
        /// Takes over the clock of another host: the step length, the step limit, the time of the world and the time
        /// left in the accumulator, so a rebuilt host does not lose a step or restart gusts.
        /// </summary>
        public void CopyClock(JiggleHost source)
        {
            FixedDt = source.FixedDt;
            MaxSteps = source.MaxSteps;
            Cfg->Time = source.Cfg->Time;
            accumulator = source.accumulator;
        }

        /// <summary>Fills <see cref="Args"/> from the current addresses and counts. Call it before every raw call.</summary>
        public void RefreshArgs()
        {
            FillArgs(Args);
        }

        /// <summary>
        /// Fills an argument block, which may live anywhere, from the current addresses and counts. A caller that
        /// runs several ranges at once gives each its own block, because the backends read the block of their call.
        /// </summary>
        public void FillArgs(JiggleSolverArgs* a)
        {
            *a = new JiggleSolverArgs();
            a->Rigs = (ulong)Rigs;
            a->Defs = (ulong)Defs;
            a->State = (ulong)State;
            a->PosePrev = (ulong)PosePrev;
            a->PoseCurr = (ulong)PoseCurr;
            a->PoseRot = (ulong)PoseRot;
            a->Out = (ulong)Out;
            a->Colliders = (ulong)Colliders;
            a->Refs = (ulong)Refs;
            a->Fields = (ulong)Fields;
            a->RigCount = (uint)RigCount;
            a->ParticleCount = (uint)ParticleCount;
            a->ColliderCount = (uint)ColliderCount;
            a->RefCount = (uint)RefCount;
            a->FieldCount = (uint)FieldCount;
        }

        /// <summary>
        /// Runs the fixed step accumulator for frameDt seconds into <see cref="Cfg"/>: Steps, Alpha and Dt. The caller
        /// then runs the backend. A frame time that is not finite or is negative counts as 0.
        /// </summary>
        public void BeginFrame(float frameDt)
        {
            uint steps;
            float alpha;
            JiggleClock.Advance(ref accumulator, frameDt, FixedDt, MaxSteps, out steps, out alpha);
            Cfg->Steps = steps;
            Cfg->Alpha = alpha;
            Cfg->Dt = FixedDt;
        }

        /// <summary>Sets exactly steps fixed steps with alpha 1 in <see cref="Cfg"/>, ignoring the accumulator.</summary>
        public void BeginFixed(uint steps)
        {
            Cfg->Steps = steps;
            Cfg->Alpha = 1.0f;
            Cfg->Dt = FixedDt;
        }

        /// <summary>Moves the time of the world on by the steps of the last frame. Call it after the backend has run.</summary>
        public void EndFrame()
        {
            Cfg->Time += (float)Cfg->Steps * FixedDt;
        }

        /// <summary>
        /// One call of the managed or the native backend over rigs first to first + count with the current arrays and
        /// settings, for the step or for the resolve. Returns 0 or a negative status code.
        /// </summary>
        public int Run(JiggleBackend backend, uint first, uint count, bool resolve)
        {
            RefreshArgs();
            return Call(backend, Cfg, Args, first, count, resolve);
        }

        /// <summary>
        /// One call of a backend with a settings record and an argument block of the caller's choosing. Ranges that
        /// do not share rigs may be called from several threads at once, each with its own argument block.
        /// </summary>
        public static int Call(
            JiggleBackend backend, JiggleStepConfig* cfg, JiggleSolverArgs* args, uint first, uint count, bool resolve)
        {
            if (backend == JiggleBackend.Native)
            {
                return resolve
                    ? JiggleNative.jgl_resolve_rigs(cfg, args, first, count)
                    : JiggleNative.jgl_step_rigs(cfg, args, first, count);
            }
            return resolve
                ? JiggleSolver.ResolveRigs(cfg, args, first, count)
                : JiggleSolver.StepRigs(cfg, args, first, count);
        }

        /// <summary>
        /// A whole frame on one backend: the accumulator for frameDt, the step and the resolve of every rig. It does
        /// what <see cref="JiggleWorld.Step"/> does. Returns 0 or a negative status code.
        /// </summary>
        public int Step(float frameDt, JiggleBackend backend)
        {
            BeginFrame(frameDt);
            return RunAll(backend);
        }

        /// <summary>Runs exactly steps fixed steps and resolves, ignoring the accumulator. Alpha is 1.</summary>
        public int StepFixed(uint steps, JiggleBackend backend)
        {
            BeginFixed(steps);
            return RunAll(backend);
        }

        private int RunAll(JiggleBackend backend)
        {
            uint count = (uint)RigCount;
            int code = Run(backend, 0, count, false);
            if (code == JiggleConst.Ok)
            {
                code = Run(backend, 0, count, true);
            }
            if (code == JiggleConst.Ok)
            {
                EndFrame();
            }
            return code;
        }

        /// <summary>World position of particle i of a rig after the last resolve.</summary>
        public JVec3 WorldPos(int rig, int i)
        {
            JiggleRig r = Rigs[rig];
            return JVec3.From4(Out[(int)r.ParticleOffset + i].Pos) + r.AnchorWorld;
        }

        /// <summary>World rotation of particle i of a rig after the last resolve.</summary>
        public JQuat WorldRot(int rig, int i)
        {
            JiggleRig r = Rigs[rig];
            return JQuat.From4(Out[(int)r.ParticleOffset + i].Rot);
        }
    }
}
