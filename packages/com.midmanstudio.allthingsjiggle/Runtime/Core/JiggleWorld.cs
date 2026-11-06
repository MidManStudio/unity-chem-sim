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
            Cfg = new JiggleStepConfig();
            Cfg.Dt = FixedDt;
            Cfg.Steps = 1;
            Cfg.GravityY = -9.81f;
            Cfg.TeleportDistance = 5.0f;
            Cfg.SleepSpeed = 0.0005f;
            Cfg.SleepDeviation = 0.002f;
            Cfg.SleepFrames = 0;
            Cfg.WakeMotion = 0.002f;
        }

        private static void Append<T>(ref T[] array, T value)
        {
            Array.Resize(ref array, array.Length + 1);
            array[array.Length - 1] = value;
        }

        // Where the virtual end particle goes: past the last bone, along its direction, scale bone lengths away.
        private static JVec3 TipPosition(JVec3[] world, float scale)
        {
            int n = world.Length;
            JVec3 last = world[n - 1];
            JVec3 before = world[n - 2];
            return last + (last - before) * scale;
        }

        /// <summary>
        /// Adds a rig from the rest pose of a chain in world space. restWorld[0] is the anchor, the transform
        /// above the first bone. The others are the bones from the root down, each the child of the one before.
        /// p holds one entry per bone. A virtual end particle that continues the last bone is added, tipScale
        /// times the length of the last bone away from it. The first bone is a root particle. Returns the rig index.
        /// The solver is one way, so the tip does not change how the real bones move. It only sets how far the last
        /// bone turns for a given sideways motion of its end: a shorter tip turns it more, a longer one less.
        /// </summary>
        public int AddChain(JVec3[] restWorld, JiggleParams[] p, float tipScale = 1.0f)
        {
            if (restWorld == null || p == null || restWorld.Length < 2 || p.Length != restWorld.Length - 1)
            {
                throw new ArgumentException("a chain needs an anchor and one parameter set per bone");
            }
            int bones = restWorld.Length - 1;
            int offset = Defs.Length;
            if (!(JiggleMath.IsFinite(tipScale) && tipScale > 0.0f))
            {
                tipScale = 1.0f;
            }

            Append(ref Defs, JiggleParams.AnchorDef());
            for (int b = 0; b < bones; b++)
            {
                uint flags = b == 0 ? JiggleConst.FlagRoot : 0u;
                Append(ref Defs, p[b].Bake(b, flags));
            }
            JiggleParams tip = p[bones - 1];
            tip.Collide = false;
            Append(ref Defs, tip.Bake(bones, JiggleConst.FlagVirtual));

            int n = bones + 2;
            JVec3 anchor = restWorld[0];
            JVec3[] worldPose = new JVec3[n];
            Array.Copy(restWorld, worldPose, restWorld.Length);
            worldPose[n - 1] = TipPosition(restWorld, tipScale);

            float extent = 0.0f;
            for (int i = 0; i < n; i++)
            {
                float d = (worldPose[i] - anchor).Length();
                if (d > extent)
                {
                    extent = d;
                }
            }
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
            JiggleRig r = Rigs[rig];
            int off = (int)r.ParticleOffset;
            int n = (int)r.ParticleCount;
            if (poseWorld.Length != n - 1)
            {
                throw new ArgumentException("the pose needs the anchor and one position per bone");
            }
            JVec3 anchor = poseWorld[0];
            JVec3 oldAnchor = r.AnchorWorld;
            JVec3[] world = new JVec3[n];
            Array.Copy(poseWorld, world, poseWorld.Length);
            world[n - 1] = TipPosition(poseWorld, TipScale[rig]);

            for (int i = 0; i < n; i++)
            {
                PosePrev[off + i] = PoseCurr[off + i];
                JVec3 rel = world[i] - anchor;
                rel.Write4(ref PoseCurr[off + i]);
                PoseCurr[off + i].W = 0.0f;
                JQuat q = (rots != null && i < rots.Length) ? rots[i] : JQuat.Identity;
                PoseRot[off + i] = q.To4();
            }
            JVec3 delta = anchor - oldAnchor;
            Rigs[rig].AnchorDelta = delta;
            Rigs[rig].AnchorWorld = anchor;
        }

        // A float to uint conversion that follows Rust's `as u32`: NaN and negative values give 0, large ones stick.
        private static uint FloatToUint(float v)
        {
            if (float.IsNaN(v) || v <= 0.0f)
            {
                return 0u;
            }
            if (v >= 4294967296.0f)
            {
                return uint.MaxValue;
            }
            return (uint)v;
        }

        /// <summary>
        /// Runs the fixed step accumulator for frameDt seconds and then resolves every rig. A frame time that is
        /// not finite or is negative counts as 0. Returns 0 or a negative status code.
        /// </summary>
        public int Step(float frameDt, JiggleBackend backend)
        {
            if (JiggleMath.IsFinite(frameDt) && frameDt > 0.0f)
            {
                accumulator += frameDt;
            }
            uint n = FloatToUint(accumulator / FixedDt + 1e-4f);
            if (n > MaxSteps)
            {
                n = MaxSteps;
                accumulator = 0.0f;
            }
            else
            {
                float rest = accumulator - (float)n * FixedDt;
                accumulator = rest > 0.0f ? rest : 0.0f;
            }
            Cfg.Steps = n;
            float alpha = accumulator / FixedDt;
            Cfg.Alpha = alpha > 1.0f ? 1.0f : alpha;
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
