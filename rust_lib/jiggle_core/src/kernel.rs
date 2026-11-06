// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "kernel.rs"
// ============================================================================
//! The scalar solver for one rig. This is the reference every other backend is
//! checked against, so it favors being plain and exact over being fast.
//!
//! One frame is `cfg.steps` fixed steps. In each step the kernel moves the
//! frame origin to the anchor, integrates every particle with verlet, then walks
//! the particles parent first and pulls each toward the pose of its parent's
//! current shape, restores its length, limits its angle and pushes it out of
//! colliders. `resolve_rig` then turns the state into positions and rotations.

use crate::collide;
use crate::field;
use crate::math::{perpendicular, Q, V3};
use crate::types::*;

#[inline]
fn is_kinematic(d: &ParticleDef) -> bool {
    d.flags & FLAG_KINEMATIC != 0
}

fn snap(state: &mut [ParticleState], pose_curr: &[[f32; 4]], n: usize) {
    for i in 0..n {
        state[i].pos = pose_curr[i];
        state[i].prev = pose_curr[i];
        state[i].pos[3] = 0.0;
        state[i].prev[3] = 0.0;
    }
}

fn wake_needed(
    cfg: &StepConfig,
    rig: &Rig,
    n: usize,
    pose_prev: &[[f32; 4]],
    pose_curr: &[[f32; 4]],
    a_delta: V3,
    a_world: V3,
    fields: &[Field],
) -> bool {
    let wake2 = cfg.wake_motion * cfg.wake_motion;
    if a_delta.length_sq() > wake2 {
        return true;
    }
    for i in 0..n {
        let m = V3::from4(&pose_curr[i]) - V3::from4(&pose_prev[i]);
        if m.length_sq() > wake2 {
            return true;
        }
    }
    for f in fields {
        if field::reaches(f, a_world, rig.extent) {
            return true;
        }
    }
    false
}

/// Runs one frame of fixed steps for one rig.
///
/// `defs`, `state`, `pose_prev` and `pose_curr` are the slices of this rig and
/// must hold at least `rig.particle_count` entries. `refs` is the rig's own
/// collider list. `pose_prev` is relative to the anchor of the last frame and
/// `pose_curr` to the anchor of this frame.
#[allow(clippy::too_many_arguments)]
pub fn step_rig(
    cfg: &StepConfig,
    rig: &mut Rig,
    defs: &[ParticleDef],
    state: &mut [ParticleState],
    pose_prev: &[[f32; 4]],
    pose_curr: &[[f32; 4]],
    colliders: &[Collider],
    refs: &[u32],
    fields: &[Field],
) {
    let n = rig.particle_count as usize;
    if n < 2 || n > MAX_PARTICLES || rig.flags & RIG_DISABLED != 0 {
        return;
    }
    let a_delta = V3::from3(&rig.anchor_delta);
    let a_world = V3::from3(&rig.anchor_world);

    let tele = cfg.teleport_distance > 0.0 && a_delta.length_sq() > cfg.teleport_distance * cfg.teleport_distance;
    if rig.flags & RIG_RESET != 0 || tele || !a_delta.is_finite() || !a_world.is_finite() {
        snap(state, pose_curr, n);
        rig.flags &= !(RIG_RESET | RIG_ASLEEP);
        rig.sleep_counter = 0;
        return;
    }

    if rig.flags & RIG_ASLEEP != 0 {
        if !wake_needed(cfg, rig, n, pose_prev, pose_curr, a_delta, a_world, fields) {
            snap(state, pose_curr, n);
            return;
        }
        rig.flags &= !RIG_ASLEEP;
        rig.sleep_counter = 0;
    }

    let steps = cfg.steps;
    if steps == 0 {
        // No fixed step fits in this frame. Only move the frame origin to the new anchor, so the motion of the
        // anchor is not lost, and hold the kinematic particles at the pose.
        for i in 0..n {
            let d = &defs[i];
            let shift = a_delta * (1.0 - d.ignore_root_motion);
            let pos = V3::from4(&state[i].pos) - shift;
            let prev = V3::from4(&state[i].prev) - shift;
            if is_kinematic(d) {
                V3::from4(&pose_curr[i]).write4(&mut state[i].pos);
            } else {
                pos.write4(&mut state[i].pos);
            }
            prev.write4(&mut state[i].prev);
        }
        return;
    }
    let inv_steps = 1.0 / steps as f32;
    let sub_delta = a_delta * inv_steps;
    let dt = cfg.dt;
    let dt2 = dt * dt;
    let gravity = V3::from3(&cfg.gravity);

    let mut q = [V3::ZERO; MAX_PARTICLES];
    let mut work = [V3::ZERO; MAX_PARTICLES];
    let mut vel = [V3::ZERO; MAX_PARTICLES];
    let mut rot = [Q::IDENTITY; MAX_PARTICLES];

    for s in 0..steps {
        let t = (s + 1) as f32 * inv_steps;
        let a_t = a_world - a_delta * (1.0 - t);
        let time = cfg.time + s as f32 * dt;

        // 1. Move the frame to the anchor of this step and set the kinematic particles to the pose.
        for i in 0..n {
            let d = &defs[i];
            let shift = sub_delta * (1.0 - d.ignore_root_motion);
            let mut pos = V3::from4(&state[i].pos) - shift;
            let mut prev = V3::from4(&state[i].prev) - shift;
            let qi = V3::lerp(V3::from4(&pose_prev[i]), V3::from4(&pose_curr[i]), t);
            q[i] = qi;
            if is_kinematic(d) {
                prev = pos;
                pos = qi;
            }
            pos.write4(&mut state[i].pos);
            prev.write4(&mut state[i].prev);
            vel[i] = pos - prev;
        }

        // 2. Integrate. Every particle reads the velocity of its parent from before this step.
        for i in 0..n {
            let d = &defs[i];
            let pos = V3::from4(&state[i].pos);
            if is_kinematic(d) {
                work[i] = pos;
                continue;
            }
            let p = d.parent as usize;
            let v_i = vel[i];
            let v_p = vel[p];
            let mut x = pos + v_p * (1.0 - d.air_drag) + (v_i - v_p) * (1.0 - d.drag);

            let mut facc = V3::ZERO;
            let mut fimp = V3::ZERO;
            if !fields.is_empty() {
                let p_world = pos + a_t;
                for f in fields {
                    let center_rel = V3::from4(&f.center) - a_t;
                    if f.mode == FIELD_IMPULSE {
                        if s == 0 {
                            fimp += field::push(f, center_rel, pos, p_world, time);
                        }
                    } else {
                        facc += field::push(f, center_rel, pos, p_world, time);
                    }
                }
            }
            let a = gravity * d.gravity + facc * d.wind_response;
            x = x + a * dt2;
            x = x + fimp * (dt * d.impulse_response);
            work[i] = x;
        }

        // 3. Constrain, parent first.
        rot[0] = Q::IDENTITY;
        work[0] = V3::from4(&state[0].pos);
        for i in 1..n {
            let d = &defs[i];
            let p = d.parent as usize;
            let mut x = work[i];
            let xp = work[p];

            if !is_kinematic(d) {
                if d.flags & FLAG_ROOT != 0 {
                    let k = d.root_elasticity * d.root_elasticity;
                    x = V3::lerp(x, q[i], k);
                } else {
                    // Shape: pull toward the pose of this bone seen from the parent's current orientation.
                    let rest = q[i] - q[p];
                    let target = xp + rot[p].rotate(rest);
                    let to = target - x;
                    let dist = to.length();
                    let cur = (x - xp).length();
                    let mut err = dist / if cur > 1e-6 { cur } else { 1e-6 };
                    if err > 1.0 {
                        err = 1.0;
                    }
                    let k = d.angle_elasticity * (1.0 - d.soften * (1.0 - err));
                    x = x + to * k;

                    // Length: restore the pose length of the bone.
                    let dd = x - xp;
                    let l = dd.length();
                    let l0 = rest.length();
                    let dir = if l > 1e-8 { dd * (1.0 / l) } else { (target - xp).normalize_safe(V3::FORWARD) };
                    x = xp + V3::lerp(dd, dir * l0, d.length_elasticity);

                    // Angle limit: keep the bone inside a cone around the target direction.
                    if d.flags & FLAG_ANGLE_LIMIT != 0 {
                        let b = (target - xp).normalize_safe(V3::FORWARD);
                        let a = (x - xp).normalize_safe(b);
                        let c = a.dot(b);
                        if c < d.limit_cos {
                            let perp = a - b * c;
                            let pn = perp.normalize_safe(perpendicular(b));
                            let a2 = b * d.limit_cos + pn * d.limit_sin;
                            let len = (x - xp).length();
                            let x2 = xp + a2 * len;
                            x = V3::lerp(x, x2, 1.0 - d.limit_soften * 0.5);
                        }
                    }
                }

                // Collision last, so the other constraints cannot push the particle back in.
                if d.flags & FLAG_COLLIDE != 0 && d.collision_radius > 0.0 {
                    let rc = rig.ref_count as usize;
                    for k in 0..rc {
                        let ci = refs[k] as usize;
                        if ci >= colliders.len() {
                            continue;
                        }
                        let c = &colliders[ci];
                        let center = V3::from4(&c.center) - a_t;
                        x = collide::push_out(c, center, x, d.collision_radius);
                        if d.flags & FLAG_SEGMENT != 0 {
                            let m = (xp + x) * 0.5;
                            let m2 = collide::push_out(c, center, m, d.collision_radius);
                            x = x + (m2 - m) * 2.0;
                        }
                    }
                }
            }
            work[i] = x;

            // Remember how far this bone's direction is from its pose direction, for its children.
            let pose_aim = (q[i] - q[p]).normalize_safe(V3::FORWARD);
            let sim_aim = (x - work[p]).normalize_safe(pose_aim);
            rot[i] = Q::from_to(pose_aim, sim_aim);
        }

        // 4. Finish the step.
        for i in 0..n {
            if is_kinematic(&defs[i]) {
                continue;
            }
            let pos = V3::from4(&state[i].pos);
            let prev = if pos.is_finite() { pos } else { q[i] };
            let next = if work[i].is_finite() { work[i] } else { q[i] };
            prev.write4(&mut state[i].prev);
            next.write4(&mut state[i].pos);
        }
    }

    // Sleep.
    if cfg.sleep_frames > 0 {
        let mut max_dev = 0.0f32;
        let mut max_vel = 0.0f32;
        for i in 0..n {
            if is_kinematic(&defs[i]) {
                continue;
            }
            let pos = V3::from4(&state[i].pos);
            let prev = V3::from4(&state[i].prev);
            let dev = (pos - V3::from4(&pose_curr[i])).length_sq();
            let vl = ((pos - prev) - sub_delta).length_sq();
            if dev > max_dev {
                max_dev = dev;
            }
            if vl > max_vel {
                max_vel = vl;
            }
        }
        if max_dev < cfg.sleep_deviation * cfg.sleep_deviation && max_vel < cfg.sleep_speed * cfg.sleep_speed {
            rig.sleep_counter = rig.sleep_counter.saturating_add(1);
        } else {
            rig.sleep_counter = 0;
        }
        if rig.sleep_counter >= cfg.sleep_frames {
            rig.flags |= RIG_ASLEEP;
            snap(state, pose_curr, n);
        }
    }
}

/// Turns the state of one rig into a position and a rotation for every particle.
///
/// Positions are interpolated between the last two steps by `cfg.alpha`. The
/// rotation of a particle is its pose rotation turned by the swing from the
/// direction of its children in the pose to their direction in the simulation.
/// Kinematic particles and sleeping or disabled rigs give the pose.
pub fn resolve_rig(
    cfg: &StepConfig,
    rig: &Rig,
    defs: &[ParticleDef],
    state: &[ParticleState],
    pose_curr: &[[f32; 4]],
    pose_rot: &[[f32; 4]],
    out: &mut [ParticleOut],
) {
    let n = rig.particle_count as usize;
    if n < 2 || n > MAX_PARTICLES {
        return;
    }
    let still = rig.flags & (RIG_DISABLED | RIG_ASLEEP) != 0;
    if still {
        for i in 0..n {
            out[i].pos = pose_curr[i];
            out[i].pos[3] = 0.0;
            out[i].rot = pose_rot[i];
        }
        return;
    }

    let mut alpha = cfg.alpha;
    if alpha < 0.0 {
        alpha = 0.0;
    }
    if alpha > 1.0 {
        alpha = 1.0;
    }

    let mut x = [V3::ZERO; MAX_PARTICLES];
    let mut q = [V3::ZERO; MAX_PARTICLES];
    let mut acc_pose = [V3::ZERO; MAX_PARTICLES];
    let mut acc_sim = [V3::ZERO; MAX_PARTICLES];
    let mut has_child = [false; MAX_PARTICLES];

    for i in 0..n {
        q[i] = V3::from4(&pose_curr[i]);
        x[i] = if is_kinematic(&defs[i]) {
            q[i]
        } else {
            V3::lerp(V3::from4(&state[i].prev), V3::from4(&state[i].pos), alpha)
        };
    }
    for j in 1..n {
        let p = defs[j].parent as usize;
        let pd = (q[j] - q[p]).normalize_safe(V3::FORWARD);
        let sd = (x[j] - x[p]).normalize_safe(pd);
        acc_pose[p] += pd;
        acc_sim[p] += sd;
        has_child[p] = true;
    }

    for i in 0..n {
        x[i].write4(&mut out[i].pos);
        out[i].pos[3] = 0.0;
        let base = Q::from4(&pose_rot[i]);
        if is_kinematic(&defs[i]) || !has_child[i] {
            out[i].rot = pose_rot[i];
            continue;
        }
        let pose_dir = acc_pose[i].normalize_safe(V3::FORWARD);
        let sim_dir = acc_sim[i].normalize_safe(pose_dir);
        let swing = Q::from_to(pose_dir, sim_dir).scaled_toward_identity(defs[i].blend);
        out[i].rot = swing.mul(base).normalized().to4();
    }
}
