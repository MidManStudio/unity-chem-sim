// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleImpulse.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Hits on one rig: a change of velocity written straight into the state of its particles. It is the same push as an
    /// impulse field of <see cref="JiggleConst.FieldImpulse"/> that covers the rig, scaled by the impulse response of each
    /// particle, but it reaches the one rig it is given and it is never lost: a frame that has no fixed step keeps the
    /// velocity for the next one, where a field that is only there for one frame would be gone.
    /// Call it before the frame runs, when nothing is running on the host. It has no Unity references.
    /// </summary>
    public static unsafe class JiggleImpulse
    {
        /// <summary>
        /// Changes the velocity of every simulated particle of a rig by <paramref name="velocityChange"/>, in meters per
        /// second, times the impulse response of the particle. A sleeping rig wakes. Returns false and changes nothing for a
        /// rig that is switched off or about to snap to its pose, because the hit would be lost, and for a velocity change
        /// that is not a finite vector.
        /// </summary>
        public static bool Hit(JiggleHost host, int rig, JVec3 velocityChange)
        {
            return Apply(host, rig, false, JVec3.Zero, 0.0f, 0u, velocityChange);
        }

        /// <summary>
        /// Like <see cref="Hit"/>, but the push fades with the distance of each particle from <paramref name="point"/>
        /// in world space: full at the point, none at <paramref name="radius"/>, by <paramref name="power"/> as the fields do
        /// (see <see cref="JiggleFields.Weight"/>). A radius that is not positive has no edge. Returns whether any particle
        /// was hit.
        /// </summary>
        public static bool HitAt(JiggleHost host, int rig, JVec3 point, float radius, uint power, JVec3 velocityChange)
        {
            return Apply(host, rig, true, point, radius, power, velocityChange);
        }

        private static bool Apply(
            JiggleHost host, int rig, bool local, JVec3 point, float radius, uint power, JVec3 dv)
        {
            if (host == null)
            {
                throw new ArgumentNullException("host");
            }
            if (rig < 0 || rig >= host.RigCount)
            {
                throw new ArgumentException("the rig does not exist");
            }
            if (!dv.IsFinite() || (local && !point.IsFinite()))
            {
                return false;
            }
            JiggleRig* r = host.Rigs + rig;
            if ((r->Flags & (JiggleConst.RigDisabled | JiggleConst.RigReset)) != 0)
            {
                return false;
            }
            int offset = (int)r->ParticleOffset;
            int count = (int)r->ParticleCount;
            JVec3 anchor = r->AnchorWorld;
            float dt = host.FixedDt;
            bool hit = false;
            for (int i = 1; i < count; i++)
            {
                JiggleParticleDef* def = host.Defs + offset + i;
                if ((def->Flags & JiggleConst.FlagKinematic) != 0)
                {
                    continue;
                }
                float w = 1.0f;
                if (local)
                {
                    JVec3 world = JVec3.From4(host.State[offset + i].Pos) + anchor;
                    w = JiggleFields.Weight((world - point).Length(), radius, power);
                }
                if (!(w > 0.0f))
                {
                    continue;
                }
                // Velocity is Pos - Prev, in meters per step, so a velocity change of dv moves Prev back by dv * dt.
                float scale = dt * w * def->ImpulseResponse;
                JVec3 prev = JVec3.From4(host.State[offset + i].Prev) - dv * scale;
                if (!prev.IsFinite())
                {
                    continue;
                }
                prev.Write4(ref host.State[offset + i].Prev);
                hit = true;
            }
            if (hit)
            {
                r->Flags &= ~JiggleConst.RigAsleep;
                r->SleepCounter = 0;
            }
            return hit;
        }
    }
}
