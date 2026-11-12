// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleWorld.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>Lets a test change the argument block of a raw call after it has been filled in.</summary>
    internal delegate void JiggleArgsHook(ref JiggleSolverArgs args);

    /// <summary>Which implementation of the solver runs a call.</summary>
    public enum JiggleBackend
    {
        /// <summary>The managed reference kernel in this package.</summary>
        Managed = 0,
        /// <summary>The Rust library jiggle_core through the C interface.</summary>
        Native = 1,
    }

    /// <summary>
    /// A self contained world of plain managed arrays: it builds rigs from chains of world positions,
    /// runs the fixed step accumulator and calls the chosen backend. It mirrors the Rust `World` and is used
    /// by the tests and by hosts that do not own their own buffers. It is a model of the call sequence, not
    /// the fast path: the Unity runtime keeps its arrays in native memory and calls the backends directly.
    /// </summary>
    public sealed unsafe class JiggleWorld
    {
        /// <summary>Rig headers.</summary>
        public JiggleRig[] Rigs = new JiggleRig[0];
        /// <summary>Particle definitions.</summary>
        public JiggleParticleDef[] Defs = new JiggleParticleDef[0];
        /// <summary>Particle states.</summary>
        public JiggleParticleState[] State = new JiggleParticleState[0];
        /// <summary>Pose positions of the last frame, relative to the anchor of the last frame.</summary>
        public JVec4[] PosePrev = new JVec4[0];
        /// <summary>Pose positions of this frame, relative to the anchor of this frame.</summary>
        public JVec4[] PoseCurr = new JVec4[0];
        /// <summary>Pose rotations of this frame.</summary>
        public JVec4[] PoseRot = new JVec4[0];
        /// <summary>Output of the last step.</summary>
        public JiggleParticleOut[] Out = new JiggleParticleOut[0];
        /// <summary>Colliders.</summary>
        public JiggleCollider[] Colliders = new JiggleCollider[0];
        /// <summary>Collider lists of all rigs.</summary>
        public uint[] Refs = new uint[0];
        /// <summary>Fields.</summary>
        public JiggleField[] Fields = new JiggleField[0];
        /// <summary>Per rig distance of the virtual end particle from the last bone, as a multiple of the last bone's length.</summary>
        public float[] TipScale = new float[0];
        /// <summary>Settings of the next step. Steps and Alpha are set by <see cref="Step"/>.</summary>
        public JiggleStepConfig Cfg;
        /// <summary>Fixed step length in seconds.</summary>
        public float FixedDt;
        /// <summary>Most fixed steps one <see cref="Step"/> call runs. Leftover time beyond it is dropped.</summary>
        public uint MaxSteps;

        private float accumulator;

        /// <summary>An empty world with a 60 Hz step and gravity of 9.81 meters per second squared downward.</summary>
        public JiggleWorld()
        {
            FixedDt = 1.0f / 60.0f;
            MaxSteps = 8;
            Cfg = JiggleStepConfig.Defaults(FixedDt);
        }

        private static void Append<T>(ref T[] array, T value)
        {
            Array.Resize(ref array, array.Length + 1);
            array[array.Length - 1] = value;
        }

        /// <summary>
        /// Adds a rig from the rest pose of a chain in world space. restWorld[0] is the anchor, the transform
        /// above the first bone. The others are the bones from the root down, each the child of the one before.
        /// p holds one entry per bone. A virtual end particle that continues the last bone is added, tipScale
        /// times the length of the last bone away from it. The first bone is a root particle. Returns the rig index.
        /// A link is one way unless both of its particles have a mass (see <see cref="JiggleParams.Mass"/>): then the
        /// child also pulls its parent back. With no masses the tip does not change how the real bones move. It only
        /// sets how far the last bone turns for a given sideways motion of its end: a shorter tip turns it more, a
        /// longer one less. The virtual end particle takes the mass of the last bone, and <see cref="SetMass"/> gives
        /// it a weight of its own, which is how a weight hung on a hair strand is made.
        /// </summary>
        public int AddChain(JVec3[] restWorld, JiggleParams[] p, float tipScale = 1.0f)
        {
            JiggleChainBuild.Validate(restWorld, p);
            int offset = Defs.Length;
            tipScale = JiggleChainBuild.CleanTipScale(tipScale);

            JiggleParticleDef[] defs = JiggleChainBuild.BakeDefs(p);
            for (int i = 0; i < defs.Length; i++)
            {
                Append(ref Defs, defs[i]);
            }

            int n = defs.Length;
            JVec3 anchor = restWorld[0];
            JVec3[] worldPose = JiggleChainBuild.PoseWithTip(restWorld, tipScale);
            float extent = JiggleChainBuild.Extent(worldPose);
            for (int i = 0; i < n; i++)
            {
                JVec3 rel = worldPose[i] - anchor;
                JVec4 a = new JVec4(0f, 0f, 0f, 0f);
                rel.Write4(ref a);
                JiggleParticleState s = new JiggleParticleState();
                s.Pos = a;
                s.Prev = a;
                Append(ref State, s);
                Append(ref PosePrev, a);
                Append(ref PoseCurr, a);
                Append(ref PoseRot, JQuat.Identity.To4());
                Append(ref Out, new JiggleParticleOut());
            }
            JiggleRig rig = new JiggleRig();
            rig.ParticleOffset = (uint)offset;
            rig.ParticleCount = (uint)n;
            rig.RefOffset = (uint)Refs.Length;
            rig.RefCount = 0;
            rig.Extent = extent;
            rig.AnchorWorld = anchor;
            Append(ref Rigs, rig);
            Append(ref TipScale, tipScale);
            return Rigs.Length - 1;
        }

        /// <summary>
        /// Adds a tree rig from the rest pose of its particles in world space, and returns its index. restWorld[0] is the
        /// anchor and the others are the particles in an order where every particle comes after its parent. parent holds, for
        /// each particle after the anchor, the index in the rig of its parent, 0 for the anchor, so parent[i] belongs to
        /// restWorld[i + 1] and is at most i. p holds one parameter set per particle after the anchor. With pinRoots
        /// a particle whose parent is the anchor is a root particle, held to its pose position as the first bone of a chain
        /// is; without it that particle hangs on the anchor like any other particle on its parent. A tree has no virtual end,
        /// so every particle is a real point and its pose is set with <see cref="SetTreePose"/>. Links are coupled as in a
        /// chain, between particles that both have a mass, and the kinematic anchor is never coupled. Throws
        /// <see cref="ArgumentException"/> when the arguments do not make a tree.
        /// </summary>
        public int AddTree(JVec3[] restWorld, int[] parent, JiggleParams[] p, bool pinRoots = false)
        {
            JiggleTreeBuild.Validate(restWorld, parent, p);
            int offset = Defs.Length;
            JiggleParticleDef[] defs = JiggleTreeBuild.BakeDefs(parent, p, pinRoots);
            for (int i = 0; i < defs.Length; i++)
            {
                Append(ref Defs, defs[i]);
            }

            int n = defs.Length;
            JVec3 anchor = restWorld[0];
            float extent = JiggleChainBuild.Extent(restWorld);
            for (int i = 0; i < n; i++)
            {
                JVec3 rel = restWorld[i] - anchor;
                JVec4 a = new JVec4(0f, 0f, 0f, 0f);
                rel.Write4(ref a);
                JiggleParticleState s = new JiggleParticleState();
                s.Pos = a;
                s.Prev = a;
                Append(ref State, s);
                Append(ref PosePrev, a);
                Append(ref PoseCurr, a);
                Append(ref PoseRot, JQuat.Identity.To4());
                Append(ref Out, new JiggleParticleOut());
            }
            JiggleRig rig = new JiggleRig();
            rig.ParticleOffset = (uint)offset;
            rig.ParticleCount = (uint)n;
            rig.RefOffset = (uint)Refs.Length;
            rig.RefCount = 0;
            rig.Extent = extent;
            rig.AnchorWorld = anchor;
            Append(ref Rigs, rig);
            Append(ref TipScale, 1.0f);
            return Rigs.Length - 1;
        }

        /// <summary>Whether a rig was added with <see cref="AddTree"/>: it has no virtual end particle.</summary>
        public bool IsTree(int rig)
        {
            JiggleRig r = Rigs[rig];
            return JiggleTreeBuild.IsTree(Defs[(int)r.ParticleOffset + (int)r.ParticleCount - 1]);
        }

        /// <summary>
        /// Sets the mass of particle <paramref name="particle"/> of a rig, counted in the rig: 1 is the first bone
        /// and the last index is the virtual end particle. 0 makes its links one way again.
        /// </summary>
        public void SetMass(int rig, int particle, float mass)
        {
            JiggleRig r = Rigs[rig];
            if (particle < 1 || particle >= (int)r.ParticleCount)
            {
                throw new ArgumentException("the particle is outside the rig");
            }
            Defs[(int)r.ParticleOffset + particle].Mass =
                mass > JiggleParams.MaxMass ? JiggleParams.MaxMass : (mass > 0.0f ? mass : 0.0f);
        }

        /// <summary>
        /// Gives a rig the colliders it should test, by index into <see cref="Colliders"/>. The list must be set
        /// before another rig is added or the arrays are rebuilt, because lists sit one after the other in Refs.
        /// </summary>
        public void SetColliderList(int rig, uint[] list)
        {
            uint off = (uint)Refs.Length;
            int old = Refs.Length;
            Array.Resize(ref Refs, old + list.Length);
            Array.Copy(list, 0, Refs, old, list.Length);
            Rigs[rig].RefOffset = off;
            Rigs[rig].RefCount = (uint)list.Length;
        }

        /// <summary>Adds a collider and returns its index.</summary>
        public int AddCollider(JiggleCollider c)
        {
            Append(ref Colliders, c);
            return Colliders.Length - 1;
        }

        /// <summary>Adds a field and returns its index.</summary>
        public int AddField(JiggleField f)
        {
            Append(ref Fields, f);
            return Fields.Length - 1;
        }

        /// <summary>
        /// Sets the pose of a rig for the coming frame from world positions, anchor first, bones after. The virtual
        /// end particle continues the last bone. Rotations default to the identity.
        /// </summary>
        public void SetPose(int rig, JVec3[] poseWorld, JQuat[] rots = null)
        {
            int off = (int)Rigs[rig].ParticleOffset;
            int n = (int)Rigs[rig].ParticleCount;
            if (IsTree(rig))
            {
                throw new ArgumentException("the rig is a tree, use SetTreePose");
            }
            if (poseWorld.Length != n - 1)
            {
                throw new ArgumentException("the pose needs the anchor and one position per bone");
            }
            // The same code the Unity jobs run, so the tests that use this method check it against the Rust kernel.
            fixed (JiggleRig* pRig = &Rigs[rig])
            fixed (JVec4* pPrev = &PosePrev[off])
            fixed (JVec4* pCurr = &PoseCurr[off])
            fixed (JVec4* pRot = &PoseRot[off])
            fixed (JVec3* pWorld = poseWorld)
            fixed (JQuat* pRots = rots)
            {
                JigglePose.Write(pRig, pPrev, pCurr, pRot, TipScale[rig], pWorld, pRots, rots == null ? 0 : rots.Length);
            }
        }

        /// <summary>
        /// Sets the pose of a tree rig for the coming frame from world positions, one for every particle, the anchor
        /// first. Rotations default to the identity. Throws <see cref="ArgumentException"/> for a chain or for the wrong
        /// number of positions.
        /// </summary>
        public void SetTreePose(int rig, JVec3[] poseWorld, JQuat[] rots = null)
        {
            int off = (int)Rigs[rig].ParticleOffset;
            int n = (int)Rigs[rig].ParticleCount;
            if (!IsTree(rig))
            {
                throw new ArgumentException("the rig is a chain, use SetPose");
            }
            if (poseWorld.Length != n)
            {
                throw new ArgumentException("the pose needs one position for every particle, the anchor included");
            }
            fixed (JiggleRig* pRig = &Rigs[rig])
            fixed (JVec4* pPrev = &PosePrev[off])
            fixed (JVec4* pCurr = &PoseCurr[off])
            fixed (JVec4* pRot = &PoseRot[off])
            fixed (JVec3* pWorld = poseWorld)
            fixed (JQuat* pRots = rots)
            {
                JigglePose.WriteTree(pRig, pPrev, pCurr, pRot, pWorld, pRots, rots == null ? 0 : rots.Length);
            }
        }

        /// <summary>
        /// Runs the fixed step accumulator for frameDt seconds and then resolves every rig. A frame time that is
        /// not finite or is negative counts as 0. Returns 0 or a negative status code.
        /// </summary>
        public int Step(float frameDt, JiggleBackend backend)
        {
            uint n;
            float alpha;
            JiggleClock.Advance(ref accumulator, frameDt, FixedDt, MaxSteps, out n, out alpha);
            Cfg.Steps = n;
            Cfg.Alpha = alpha;
            return Run(backend);
        }

        /// <summary>Runs exactly steps fixed steps and resolves, ignoring the accumulator. Alpha is 1.</summary>
        public int StepFixed(uint steps, JiggleBackend backend)
        {
            Cfg.Steps = steps;
            Cfg.Alpha = 1.0f;
            return Run(backend);
        }

        private int Run(JiggleBackend backend)
        {
            Cfg.Dt = FixedDt;
            uint count = (uint)Rigs.Length;
            int code = Invoke(backend, 0, count, false);
            if (code == JiggleConst.Ok)
            {
                code = Invoke(backend, 0, count, true);
            }
            if (code == JiggleConst.Ok)
            {
                Cfg.Time += (float)Cfg.Steps * FixedDt;
            }
            return code;
        }

        /// <summary>
        /// One raw call of the chosen backend over rigs first to first + count, with the current arrays, the
        /// current <see cref="Cfg"/> and nothing else done. The tests use it to send a corrupted argument block or a
        /// null pointer to both backends and compare what they answer.
        /// </summary>
        internal int Invoke(
            JiggleBackend backend, uint first, uint count, bool resolve,
            JiggleArgsHook hook = null, bool nullCfg = false, bool nullArgs = false)
        {
            JiggleStepConfig cfg = Cfg;
            int code;
            fixed (JiggleRig* pRigs = Rigs)
            fixed (JiggleParticleDef* pDefs = Defs)
            fixed (JiggleParticleState* pState = State)
            fixed (JVec4* pPrev = PosePrev)
            fixed (JVec4* pCurr = PoseCurr)
            fixed (JVec4* pRot = PoseRot)
            fixed (JiggleParticleOut* pOut = Out)
            fixed (JiggleCollider* pColl = Colliders)
            fixed (uint* pRefs = Refs)
            fixed (JiggleField* pFields = Fields)
            {
                JiggleSolverArgs args = new JiggleSolverArgs();
                args.Rigs = (ulong)pRigs;
                args.Defs = (ulong)pDefs;
                args.State = (ulong)pState;
                args.PosePrev = (ulong)pPrev;
                args.PoseCurr = (ulong)pCurr;
                args.PoseRot = (ulong)pRot;
                args.Out = (ulong)pOut;
                args.Colliders = (ulong)pColl;
                args.Refs = (ulong)pRefs;
                args.Fields = (ulong)pFields;
                args.RigCount = (uint)Rigs.Length;
                args.ParticleCount = (uint)Defs.Length;
                args.ColliderCount = (uint)Colliders.Length;
                args.RefCount = (uint)Refs.Length;
                args.FieldCount = (uint)Fields.Length;
                if (hook != null)
                {
                    hook(ref args);
                }
                JiggleStepConfig* pCfg = nullCfg ? null : &cfg;
                JiggleSolverArgs* pArgs = nullArgs ? null : &args;
                if (backend == JiggleBackend.Native)
                {
                    code = resolve
                        ? JiggleNative.jgl_resolve_rigs(pCfg, pArgs, first, count)
                        : JiggleNative.jgl_step_rigs(pCfg, pArgs, first, count);
                }
                else
                {
                    code = resolve
                        ? JiggleSolver.ResolveRigs(pCfg, pArgs, first, count)
                        : JiggleSolver.StepRigs(pCfg, pArgs, first, count);
                }
            }
            return code;
        }

        /// <summary>World position of particle i of a rig after the last step.</summary>
        public JVec3 WorldPos(int rig, int i)
        {
            JiggleRig r = Rigs[rig];
            return JVec3.From4(Out[(int)r.ParticleOffset + i].Pos) + r.AnchorWorld;
        }

        /// <summary>World rotation of particle i of a rig after the last step.</summary>
        public JQuat WorldRot(int rig, int i)
        {
            JiggleRig r = Rigs[rig];
            return JQuat.From4(Out[(int)r.ParticleOffset + i].Rot);
        }
    }
}
