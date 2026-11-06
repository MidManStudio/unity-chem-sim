// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleKernel.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// The solver for one rig, a line by line port of `kernel.rs`. It is the managed reference
    /// that the Burst and Rust backends are checked against, so it keeps the exact operation order of
    /// the Rust code. It works on pointers into the shared arrays and uses only plain structs and
    /// static methods, so Burst can compile it.
    ///
    /// One frame is cfg.Steps fixed steps. In each step the kernel moves the frame origin to the anchor,
    /// integrates every particle with verlet, then walks the particles parent first and pulls each
    /// toward the pose of its parent's current shape, restores its length, limits its angle and pushes
    /// it out of colliders. <see cref="ResolveRig"/> then turns the state into positions and rotations.
    /// </summary>
    public static unsafe class JiggleKernel
    {
        private static bool IsKinematic(in JiggleParticleDef d)
        {
            return (d.Flags & JiggleConst.FlagKinematic) != 0;
        }

        private static void Snap(JiggleParticleState* state, JVec4* poseCurr, int n)
        {
            for (int i = 0; i < n; i++)
            {
                state[i].Pos = poseCurr[i];
                state[i].Prev = poseCurr[i];
                state[i].Pos.W = 0.0f;
                state[i].Prev.W = 0.0f;
            }
        }

        private static bool WakeNeeded(
            in JiggleStepConfig cfg, in JiggleRig rig, int n, JVec4* posePrev, JVec4* poseCurr,
            JVec3 aDelta, JVec3 aWorld, JiggleField* fields, int fieldCount)
        {
            float wake2 = cfg.WakeMotion * cfg.WakeMotion;
            if (aDelta.LengthSq() > wake2)
            {
                return true;
            }
            for (int i = 0; i < n; i++)
            {
                JVec3 m = JVec3.From4(poseCurr[i]) - JVec3.From4(posePrev[i]);
                if (m.LengthSq() > wake2)
                {
                    return true;
                }
            }
            for (int k = 0; k < fieldCount; k++)
            {
                if (JiggleFields.Reaches(fields[k], aWorld, rig.Extent))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Runs one frame of fixed steps for one rig. defs, state, posePrev and poseCurr point at the first
        /// particle of this rig and must hold at least rig.ParticleCount entries. refs points at the rig's own
        /// collider list, which holds rig.RefCount entries. posePrev is relative to the anchor of the last frame
        /// and poseCurr to the anchor of this frame.
        /// </summary>
        public static void StepRig(
            in JiggleStepConfig cfg, JiggleRig* rig, JiggleParticleDef* defs, JiggleParticleState* state,
            JVec4* posePrev, JVec4* poseCurr, JiggleCollider* colliders, int colliderCount, uint* refs,
            JiggleField* fields, int fieldCount)
        {
            int n = (int)rig->ParticleCount;
            if (n < 2 || n > JiggleConst.MaxParticles || (rig->Flags & JiggleConst.RigDisabled) != 0)
            {
                return;
            }
            JVec3 aDelta = rig->AnchorDelta;
            JVec3 aWorld = rig->AnchorWorld;

            bool tele = cfg.TeleportDistance > 0.0f && aDelta.LengthSq() > cfg.TeleportDistance * cfg.TeleportDistance;
            if ((rig->Flags & JiggleConst.RigReset) != 0 || tele || !aDelta.IsFinite() || !aWorld.IsFinite())
            {
                Snap(state, poseCurr, n);
                rig->Flags &= ~(JiggleConst.RigReset | JiggleConst.RigAsleep);
                rig->SleepCounter = 0;
                return;
            }

            if ((rig->Flags & JiggleConst.RigAsleep) != 0)
            {
                if (!WakeNeeded(cfg, *rig, n, posePrev, poseCurr, aDelta, aWorld, fields, fieldCount))
                {
                    Snap(state, poseCurr, n);
                    return;
                }
                rig->Flags &= ~JiggleConst.RigAsleep;
                rig->SleepCounter = 0;
            }

            uint steps = cfg.Steps;
            if (steps == 0)
            {
                // No fixed step fits in this frame. Only move the frame origin to the new anchor, so the motion of the
                // anchor is not lost, and hold the kinematic particles at the pose.
                for (int i = 0; i < n; i++)
                {
                    JVec3 shift0 = aDelta * (1.0f - defs[i].IgnoreRootMotion);
                    JVec3 pos0 = JVec3.From4(state[i].Pos) - shift0;
                    JVec3 prev0 = JVec3.From4(state[i].Prev) - shift0;
                    if (IsKinematic(defs[i]))
                    {
                        JVec3.From4(poseCurr[i]).Write4(ref state[i].Pos);
                    }
                    else
                    {
                        pos0.Write4(ref state[i].Pos);
                    }
                    prev0.Write4(ref state[i].Prev);
                }
                return;
            }
            float invSteps = 1.0f / (float)steps;
            JVec3 subDelta = aDelta * invSteps;
            float dt = cfg.Dt;
            float dt2 = dt * dt;
            JVec3 gravity = cfg.Gravity;

            JVec3* q = stackalloc JVec3[JiggleConst.MaxParticles];
            JVec3* work = stackalloc JVec3[JiggleConst.MaxParticles];
            JVec3* vel = stackalloc JVec3[JiggleConst.MaxParticles];
            JQuat* rot = stackalloc JQuat[JiggleConst.MaxParticles];
            for (int i = 0; i < JiggleConst.MaxParticles; i++)
            {
                q[i] = JVec3.Zero;
                work[i] = JVec3.Zero;
                vel[i] = JVec3.Zero;
                rot[i] = JQuat.Identity;
            }

            for (uint s = 0; s < steps; s++)
            {
                float t = (float)(s + 1) * invSteps;
                JVec3 aT = aWorld - aDelta * (1.0f - t);
                float time = cfg.Time + (float)s * dt;

                // 1. Move the frame to the anchor of this step and set the kinematic particles to the pose.
                for (int i = 0; i < n; i++)
                {
                    JVec3 shift = subDelta * (1.0f - defs[i].IgnoreRootMotion);
                    JVec3 pos = JVec3.From4(state[i].Pos) - shift;
                    JVec3 prev = JVec3.From4(state[i].Prev) - shift;
                    JVec3 qi = JVec3.Lerp(JVec3.From4(posePrev[i]), JVec3.From4(poseCurr[i]), t);
                    q[i] = qi;
                    if (IsKinematic(defs[i]))
                    {
                        prev = pos;
                        pos = qi;
                    }
                    pos.Write4(ref state[i].Pos);
                    prev.Write4(ref state[i].Prev);
                    vel[i] = pos - prev;
                }

                // 2. Integrate. Every particle reads the velocity of its parent from before this step.
                for (int i = 0; i < n; i++)
                {
                    JVec3 pos = JVec3.From4(state[i].Pos);
                    if (IsKinematic(defs[i]))
                    {
                        work[i] = pos;
                        continue;
                    }
                    int p = defs[i].Parent;
                    JVec3 vI = vel[i];
                    JVec3 vP = vel[p];
                    JVec3 x = pos + vP * (1.0f - defs[i].AirDrag) + (vI - vP) * (1.0f - defs[i].Drag);

                    JVec3 facc = JVec3.Zero;
                    JVec3 fimp = JVec3.Zero;
                    if (fieldCount != 0)
                    {
                        JVec3 pWorld = pos + aT;
                        for (int k = 0; k < fieldCount; k++)
                        {
                            JVec3 centerRel = JVec3.From4(fields[k].Center) - aT;
                            if (fields[k].Mode == JiggleConst.FieldImpulse)
                            {
                                if (s == 0)
                                {
                                    fimp = fimp + JiggleFields.Push(fields[k], centerRel, pos, pWorld, time);
                                }
                            }
                            else
                            {
                                facc = facc + JiggleFields.Push(fields[k], centerRel, pos, pWorld, time);
                            }
                        }
                    }
                    JVec3 a = gravity * defs[i].Gravity + facc * defs[i].WindResponse;
                    x = x + a * dt2;
                    x = x + fimp * (dt * defs[i].ImpulseResponse);
                    work[i] = x;
                }

                // 3. Constrain, parent first.
                rot[0] = JQuat.Identity;
                work[0] = JVec3.From4(state[0].Pos);
                for (int i = 1; i < n; i++)
                {
                    int p = defs[i].Parent;
                    JVec3 x = work[i];
                    JVec3 xp = work[p];

                    if (!IsKinematic(defs[i]))
                    {
                        if ((defs[i].Flags & JiggleConst.FlagRoot) != 0)
                        {
                            float k = defs[i].RootElasticity * defs[i].RootElasticity;
                            x = JVec3.Lerp(x, q[i], k);
                        }
                        else
                        {
                            // Shape: pull toward the pose of this bone seen from the parent's current orientation.
                            JVec3 rest = q[i] - q[p];
                            JVec3 target = xp + rot[p].Rotate(rest);
                            JVec3 to = target - x;
                            float dist = to.Length();
                            float cur = (x - xp).Length();
                            float err = dist / (cur > 1e-6f ? cur : 1e-6f);
                            if (err > 1.0f)
                            {
                                err = 1.0f;
                            }
                            float k = defs[i].AngleElasticity * (1.0f - defs[i].Soften * (1.0f - err));
                            x = x + to * k;

                            // Length: restore the pose length of the bone.
                            JVec3 dd = x - xp;
                            float l = dd.Length();
                            float l0 = rest.Length();
                            JVec3 dir = l > 1e-8f ? dd * (1.0f / l) : (target - xp).NormalizeSafe(JVec3.Forward);
                            x = xp + JVec3.Lerp(dd, dir * l0, defs[i].LengthElasticity);

                            // Angle limit: keep the bone inside a cone around the target direction.
                            if ((defs[i].Flags & JiggleConst.FlagAngleLimit) != 0)
                            {
                                JVec3 b = (target - xp).NormalizeSafe(JVec3.Forward);
                                JVec3 av = (x - xp).NormalizeSafe(b);
                                float c = JVec3.Dot(av, b);
                                if (c < defs[i].LimitCos)
                                {
                                    JVec3 perp = av - b * c;
                                    JVec3 pn = perp.NormalizeSafe(JiggleMath.Perpendicular(b));
                                    JVec3 a2 = b * defs[i].LimitCos + pn * defs[i].LimitSin;
                                    float len = (x - xp).Length();
                                    JVec3 x2 = xp + a2 * len;
                                    x = JVec3.Lerp(x, x2, 1.0f - defs[i].LimitSoften * 0.5f);
                                }
                            }
                        }

                        // Collision last, so the other constraints cannot push the particle back in.
                        if ((defs[i].Flags & JiggleConst.FlagCollide) != 0 && defs[i].CollisionRadius > 0.0f)
                        {
                            int rc = (int)rig->RefCount;
                            for (int k = 0; k < rc; k++)
                            {
                                int ci = (int)refs[k];
                                if (ci >= colliderCount)
                                {
                                    continue;
                                }
                                JVec3 center = JVec3.From4(colliders[ci].Center) - aT;
                                x = JiggleCollide.PushOut(colliders[ci], center, x, defs[i].CollisionRadius);
                                if ((defs[i].Flags & JiggleConst.FlagSegment) != 0)
                                {
                                    JVec3 m = (xp + x) * 0.5f;
                                    JVec3 m2 = JiggleCollide.PushOut(colliders[ci], center, m, defs[i].CollisionRadius);
                                    x = x + (m2 - m) * 2.0f;
                                }
                            }
                        }
                    }
                    work[i] = x;

                    // Remember how far this bone's direction is from its pose direction, for its children.
                    JVec3 poseAim = (q[i] - q[p]).NormalizeSafe(JVec3.Forward);
                    JVec3 simAim = (x - work[p]).NormalizeSafe(poseAim);
                    rot[i] = JQuat.FromTo(poseAim, simAim);
                }

                // 4. Finish the step.
                for (int i = 0; i < n; i++)
                {
                    if (IsKinematic(defs[i]))
                    {
                        continue;
                    }
                    JVec3 pos = JVec3.From4(state[i].Pos);
                    JVec3 prev = pos.IsFinite() ? pos : q[i];
                    JVec3 next = work[i].IsFinite() ? work[i] : q[i];
                    prev.Write4(ref state[i].Prev);
                    next.Write4(ref state[i].Pos);
                }
            }

            // Sleep.
            if (cfg.SleepFrames > 0)
            {
                float maxDev = 0.0f;
                float maxVel = 0.0f;
                for (int i = 0; i < n; i++)
                {
                    if (IsKinematic(defs[i]))
                    {
                        continue;
                    }
                    JVec3 pos = JVec3.From4(state[i].Pos);
                    JVec3 prev = JVec3.From4(state[i].Prev);
                    float dev = (pos - JVec3.From4(poseCurr[i])).LengthSq();
                    float vl = ((pos - prev) - subDelta).LengthSq();
                    if (dev > maxDev)
                    {
                        maxDev = dev;
                    }
                    if (vl > maxVel)
                    {
                        maxVel = vl;
                    }
                }
                if (maxDev < cfg.SleepDeviation * cfg.SleepDeviation && maxVel < cfg.SleepSpeed * cfg.SleepSpeed)
                {
                    rig->SleepCounter = rig->SleepCounter == uint.MaxValue ? uint.MaxValue : rig->SleepCounter + 1;
                }
                else
                {
                    rig->SleepCounter = 0;
                }
                if (rig->SleepCounter >= cfg.SleepFrames)
                {
                    rig->Flags |= JiggleConst.RigAsleep;
                    Snap(state, poseCurr, n);
                }
            }
        }

        /// <summary>
        /// Turns the state of one rig into a position and a rotation for every particle. Positions are
        /// interpolated between the last two steps by cfg.Alpha. The rotation of a particle is its pose
        /// rotation turned by the swing from the direction of its children in the pose to their direction in
        /// the simulation. Kinematic particles and sleeping or disabled rigs give the pose.
        /// </summary>
        public static void ResolveRig(
            in JiggleStepConfig cfg, in JiggleRig rig, JiggleParticleDef* defs, JiggleParticleState* state,
            JVec4* poseCurr, JVec4* poseRot, JiggleParticleOut* output)
        {
            int n = (int)rig.ParticleCount;
            if (n < 2 || n > JiggleConst.MaxParticles)
            {
                return;
            }
            bool still = (rig.Flags & (JiggleConst.RigDisabled | JiggleConst.RigAsleep)) != 0;
            if (still)
            {
                for (int i = 0; i < n; i++)
                {
                    output[i].Pos = poseCurr[i];
                    output[i].Pos.W = 0.0f;
                    output[i].Rot = poseRot[i];
                }
                return;
            }

            float alpha = cfg.Alpha;
            if (alpha < 0.0f)
            {
                alpha = 0.0f;
            }
            if (alpha > 1.0f)
            {
                alpha = 1.0f;
            }

            JVec3* x = stackalloc JVec3[JiggleConst.MaxParticles];
            JVec3* q = stackalloc JVec3[JiggleConst.MaxParticles];
            JVec3* accPose = stackalloc JVec3[JiggleConst.MaxParticles];
            JVec3* accSim = stackalloc JVec3[JiggleConst.MaxParticles];
            bool* hasChild = stackalloc bool[JiggleConst.MaxParticles];
            for (int i = 0; i < JiggleConst.MaxParticles; i++)
            {
                x[i] = JVec3.Zero;
                q[i] = JVec3.Zero;
                accPose[i] = JVec3.Zero;
                accSim[i] = JVec3.Zero;
                hasChild[i] = false;
            }

            for (int i = 0; i < n; i++)
            {
                q[i] = JVec3.From4(poseCurr[i]);
                x[i] = IsKinematic(defs[i])
                    ? q[i]
                    : JVec3.Lerp(JVec3.From4(state[i].Prev), JVec3.From4(state[i].Pos), alpha);
            }
            for (int j = 1; j < n; j++)
            {
                int p = defs[j].Parent;
                JVec3 pd = (q[j] - q[p]).NormalizeSafe(JVec3.Forward);
                JVec3 sd = (x[j] - x[p]).NormalizeSafe(pd);
                accPose[p] = accPose[p] + pd;
                accSim[p] = accSim[p] + sd;
                hasChild[p] = true;
            }

            for (int i = 0; i < n; i++)
            {
                x[i].Write4(ref output[i].Pos);
                output[i].Pos.W = 0.0f;
                JQuat baseRot = JQuat.From4(poseRot[i]);
                if (IsKinematic(defs[i]) || !hasChild[i])
                {
                    output[i].Rot = poseRot[i];
                    continue;
                }
                JVec3 poseDir = accPose[i].NormalizeSafe(JVec3.Forward);
                JVec3 simDir = accSim[i].NormalizeSafe(poseDir);
                JQuat swing = JQuat.FromTo(poseDir, simDir).ScaledTowardIdentity(defs[i].Blend);
                output[i].Rot = JQuat.Mul(swing, baseRot).Normalized().To4();
            }
        }
    }
}
