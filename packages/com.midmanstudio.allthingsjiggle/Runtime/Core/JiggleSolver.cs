// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleSolver.cs"
// ============================================================================
namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Runs the managed kernel over a range of rigs in shared arrays, with every offset checked first.
    /// It takes the same arguments and gives the same status codes as the native `jgl_step_rigs` and
    /// `jgl_resolve_rigs`, so the two backends are interchangeable and can be compared call for call.
    /// </summary>
    public static unsafe class JiggleSolver
    {
        /// <summary>Runs cfg.Steps fixed steps for rigs first to first + count.</summary>
        public static int StepRigs(JiggleStepConfig* cfg, JiggleSolverArgs* args, uint first, uint count)
        {
            return Run(cfg, args, first, count, false);
        }

        /// <summary>
        /// Writes the position and rotation of every particle of rigs first to first + count into the output
        /// array, interpolating by cfg.Alpha.
        /// </summary>
        public static int ResolveRigs(JiggleStepConfig* cfg, JiggleSolverArgs* args, uint first, uint count)
        {
            return Run(cfg, args, first, count, true);
        }

        private static int Run(JiggleStepConfig* cfgPtr, JiggleSolverArgs* argsPtr, uint first, uint count, bool resolve)
        {
            if (cfgPtr == null || argsPtr == null)
            {
                return JiggleConst.ErrNull;
            }
            JiggleStepConfig cfg = *cfgPtr;
            JiggleSolverArgs a = *argsPtr;

            ulong end = (ulong)first + (ulong)count;
            if (end > a.RigCount)
            {
                return JiggleConst.ErrRange;
            }
            if (count == 0)
            {
                return JiggleConst.Ok;
            }
            if (a.Rigs == 0)
            {
                return JiggleConst.ErrNull;
            }
            JiggleRig* rigs = (JiggleRig*)a.Rigs + first;

            // Find the window of particles the range covers and check the rigs do not overlap.
            ulong total = a.ParticleCount;
            ulong pmin = ulong.MaxValue;
            ulong pmax = 0;
            for (uint i = 0; i < count; i++)
            {
                ulong off = rigs[i].ParticleOffset;
                ulong e = off + rigs[i].ParticleCount;
                if (e > total)
                {
                    return JiggleConst.ErrBadRig;
                }
                if (i > 0 && off < pmax)
                {
                    return JiggleConst.ErrBadRig;
                }
                if (off < pmin)
                {
                    pmin = off;
                }
                if (e > pmax)
                {
                    pmax = e;
                }
            }
            ulong window = pmax - pmin;
            if (window > 0 && (a.Defs == 0 || a.State == 0 || a.PosePrev == 0 || a.PoseCurr == 0 || a.PoseRot == 0 || a.Out == 0))
            {
                return JiggleConst.ErrNull;
            }
            if ((a.ColliderCount > 0 && a.Colliders == 0) || (a.RefCount > 0 && a.Refs == 0) || (a.FieldCount > 0 && a.Fields == 0))
            {
                return JiggleConst.ErrNull;
            }

            JiggleParticleDef* defs = (JiggleParticleDef*)a.Defs;
            JiggleParticleState* state = (JiggleParticleState*)a.State;
            JVec4* posePrev = (JVec4*)a.PosePrev;
            JVec4* poseCurr = (JVec4*)a.PoseCurr;
            JVec4* poseRot = (JVec4*)a.PoseRot;
            JiggleParticleOut* output = (JiggleParticleOut*)a.Out;
            JiggleCollider* colliders = (JiggleCollider*)a.Colliders;
            uint* refs = (uint*)a.Refs;
            JiggleField* fields = (JiggleField*)a.Fields;

            // Every rig is checked before the first one runs, so a refused call has written nothing.
            for (uint i = 0; i < count; i++)
            {
                int code = CheckRig(rigs + i, defs, a.RefCount);
                if (code != JiggleConst.Ok)
                {
                    return code;
                }
            }

            for (uint i = 0; i < count; i++)
            {
                ulong off = rigs[i].ParticleOffset;
                if (resolve)
                {
                    JiggleKernel.ResolveRig(cfg, rigs[i], defs + off, state + off, poseCurr + off, poseRot + off, output + off);
                }
                else
                {
                    JiggleKernel.StepRig(
                        cfg, rigs + i, defs + off, state + off, posePrev + off, poseCurr + off,
                        colliders, (int)a.ColliderCount, refs + rigs[i].RefOffset, fields, (int)a.FieldCount);
                }
            }
            return JiggleConst.Ok;
        }

        // The particle range is already known to lie inside the arrays. This checks the count, the collider list
        // and the topology: particle 0 is kinematic with no parent and every other parent comes earlier.
        private static int CheckRig(JiggleRig* rig, JiggleParticleDef* defs, uint refTotal)
        {
            uint n = rig->ParticleCount;
            if (n < 2 || n > JiggleConst.MaxParticles)
            {
                return JiggleConst.ErrBadRig;
            }
            ulong rend = (ulong)rig->RefOffset + (ulong)rig->RefCount;
            if (rend > refTotal)
            {
                return JiggleConst.ErrBadRig;
            }
            JiggleParticleDef* d = defs + rig->ParticleOffset;
            if ((d[0].Flags & JiggleConst.FlagKinematic) == 0 || d[0].Parent != -1)
            {
                return JiggleConst.ErrBadRig;
            }
            for (int i = 1; i < (int)n; i++)
            {
                if (d[i].Parent < 0 || d[i].Parent >= i)
                {
                    return JiggleConst.ErrBadRig;
                }
            }
            return JiggleConst.Ok;
        }
    }
}
