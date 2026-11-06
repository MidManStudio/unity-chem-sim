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
//!
//! A link between a particle and its parent is one way unless both have a mass:
//! then the correction of the link is shared by mass, the lighter side moving
//! more, and the parent takes its share in the opposite direction. See
//! `link_shares` and the coupling passes in `step_rig`.

use crate::collide;
use crate::field;
use crate::math::{perpendicular, Q, V3};
use crate::types::*;

#[inline]
fn is_kinematic(d: &ParticleDef) -> bool {
    d.flags & FLAG_KINEMATIC != 0
}

/// How a correction of the link between particle `i` and its parent is shared, as (own share, parent share),
/// or `None` when the link is one way.
///
/// A link is coupled when both particles have a mass above 0, neither is kinematic and `i` is not a root
/// particle (a root is held to its pose by the root spring, not by a link). The heavier side moves less.
fn link_shares(defs: &[ParticleDef], i: usize) -> Option<(f32, f32)> {
    let d = &defs[i];
    let pd = &defs[d.parent as usize];
    if is_kinematic(d) || is_kinematic(pd) || d.flags & FLAG_ROOT != 0 {
        return None;
    }
    if d.mass > 0.0 && pd.mass > 0.0 {
        // A ratio beyond `MAX_MASS_RATIO` counts as exactly that ratio.
        let mut own_mass = d.mass;
        let mut parent_mass = pd.mass;
        if own_mass > parent_mass * MAX_MASS_RATIO {
            own_mass = parent_mass * MAX_MASS_RATIO;
        } else if parent_mass > own_mass * MAX_MASS_RATIO {
            parent_mass = own_mass * MAX_MASS_RATIO;
        }
        let sum = own_mass + parent_mass;
        if sum.is_finite() {
            return Some((parent_mass / sum, own_mass / sum));
        }
    }
    None
}

/// Restores the pose length of the bone from `xp` to `x` and keeps it inside its angle limit. Returns the new `x`.
fn hold_link(d: &ParticleDef, xp: V3, rest: V3, target: V3, x: V3) -> V3 {
    // Length: restore the pose length of the bone.
    let dd = x - xp;
    let l = dd.length();
    let l0 = rest.length();
    let dir = if l > 1e-8 { dd * (1.0 / l) } else { (target - xp).normalize_safe(V3::FORWARD) };
    let mut x = xp + V3::lerp(dd, dir * l0, d.length_elasticity);

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
    x
}

/// Pushes the particle at `x` out of the colliders of its rig. `xp` is its parent, for the segment test.
#[allow(clippy::too_many_arguments)]
fn collide_particle(
    d: &ParticleDef,
    colliders: &[Collider],
    refs: &[u32],
    ref_count: usize,
    a_t: V3,
    xp: V3,
    x: V3,
) -> V3 {
    let mut x = x;
    for k in 0..ref_count {
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
    x
}

/// How far the direction of particle `i` from its parent is from its pose direction, for its children.
fn aim_rot(defs: &[ParticleDef], q: &[V3; MAX_PARTICLES], work: &[V3; MAX_PARTICLES], i: usize) -> Q {
    let p = defs[i].parent as usize;
    let pose_aim = (q[i] - q[p]).normalize_safe(V3::FORWARD);
    let sim_aim = (work[i] - work[p]).normalize_safe(pose_aim);
    Q::from_to(pose_aim, sim_aim)
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

    let mut passes = cfg.coupling_passes;
    if passes < 1 {
        passes = 1;
    }
    if passes > MAX_COUPLING_PASSES {
        passes = MAX_COUPLING_PASSES;
    }
    let mut coupled_any = false;
    for i in 1..n {
        if link_shares(defs, i).is_some() {
            coupled_any = true;
            break;
        }
    }

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

        // 3. Constrain, parent first. This is the first pass of the coupling.
        rot[0] = Q::IDENTITY;
        work[0] = V3::from4(&state[0].pos);
        for i in 1..n {
            let d = &defs[i];
            let p = d.parent as usize;
            let mut x = work[i];
            let mut xp = work[p];

            if !is_kinematic(d) {
                if d.flags & FLAG_ROOT != 0 {
                    let k = d.root_elasticity * d.root_elasticity;
                    x = V3::lerp(x, q[i], k);
                } else {
                    let x0 = x;

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

                    // Length and angle limit.
                    x = hold_link(d, xp, rest, target, x);

                    // Two way coupling: the parent takes its share of the correction in the opposite direction.
                    if let Some((own, back)) = link_shares(defs, i) {
                        let delta = x - x0;
                        x = x0 + delta * own;
                        work[p] = work[p] - delta * back;
                        xp = work[p];
                        rot[p] = aim_rot(defs, &q, &work, p);
                    }
                }

                // Collision last, so the other constraints cannot push the particle back in.
                if d.flags & FLAG_COLLIDE != 0 && d.collision_radius > 0.0 {
                    x = collide_particle(d, colliders, refs, rig.ref_count as usize, a_t, xp, x);
                }
            }
            work[i] = x;

            // Remember how far this bone's direction is from its pose direction, for its children.
            rot[i] = aim_rot(defs, &q, &work, i);
        }

        // 3b. Passes 2 and up of the coupling, then a last collision sweep so a parent that took a share of a
        // correction cannot end up inside a collider. Rigs without coupled links skip all of it.
        if coupled_any {
            for _ in 1..passes {
                for i in 1..n {
                    if let Some((own, back)) = link_shares(defs, i) {
                        let d = &defs[i];
                        let p = d.parent as usize;
                        let xp = work[p];
                        let x = work[i];
                        let rest = q[i] - q[p];
                        let target = xp + rot[p].rotate(rest);
                        let held = hold_link(d, xp, rest, target, x);
                        let delta = held - x;
                        work[i] = x + delta * own;
                        work[p] = work[p] - delta * back;
                        rot[p] = aim_rot(defs, &q, &work, p);
                        rot[i] = aim_rot(defs, &q, &work, i);
                    }
                }
            }
            for i in 1..n {
                let d = &defs[i];
                if !is_kinematic(d) && d.flags & FLAG_COLLIDE != 0 && d.collision_radius > 0.0 {
                    let p = d.parent as usize;
                    work[i] = collide_particle(d, colliders, refs, rig.ref_count as usize, a_t, work[p], work[i]);
                }
            }
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

#[cfg(test)]
mod tests {
    use super::*;

    fn def(parent: i32, flags: u32, mass: f32) -> ParticleDef {
        ParticleDef { parent, flags, mass, ..ParticleDef::default() }
    }

    #[test]
    fn a_link_is_coupled_only_when_both_particles_are_free_and_have_a_mass() {
        let anchor = def(-1, FLAG_KINEMATIC, 5.0);
        let root = def(0, FLAG_ROOT, 2.0);
        let a = def(1, 0, 1.0);
        let b = def(2, 0, 3.0);
        let defs = [anchor, root, a, b];
        // Next to the anchor and under a root there is nothing to couple: the anchor is kinematic, and a root
        // particle is held by the root spring.
        assert_eq!(link_shares(&defs, 1), None);
        // A free particle under a root particle with masses is coupled, and the shares add up to 1.
        let (own, back) = link_shares(&defs, 2).unwrap();
        assert_eq!(own, 2.0 / 3.0);
        assert_eq!(back, 1.0 / 3.0);
        let (own, back) = link_shares(&defs, 3).unwrap();
        assert_eq!(own, 0.25);
        assert_eq!(back, 0.75);

        // No mass on either side keeps it one way.
        let mut d = defs;
        d[3].mass = 0.0;
        assert_eq!(link_shares(&d, 3), None);
        let mut d = defs;
        d[2].mass = 0.0;
        assert_eq!(link_shares(&d, 3), None);
        assert_eq!(link_shares(&d, 2), None);
        // Negative and not a number masses are not coupled.
        for bad in [-1.0f32, f32::NAN, f32::NEG_INFINITY] {
            let mut d = defs;
            d[3].mass = bad;
            assert_eq!(link_shares(&d, 3), None, "mass {}", bad);
        }
        // An infinite mass is very heavy, so it is split as the cap. Two infinite masses have no ratio to cap.
        let mut d = defs;
        d[2].mass = 1.0;
        d[3].mass = f32::INFINITY;
        assert_eq!(link_shares(&d, 3), Some((1.0 / 17.0, 16.0 / 17.0)));
        d[2].mass = f32::INFINITY;
        assert_eq!(link_shares(&d, 3), None);
        // A sum that overflows is not coupled either.
        let mut d = defs;
        d[2].mass = f32::MAX;
        d[3].mass = f32::MAX;
        assert_eq!(link_shares(&d, 3), None);
        // A ratio beyond the cap is split as the cap, from either side.
        let mut d = defs;
        d[2].mass = 1.0;
        d[3].mass = 1000.0;
        assert_eq!(link_shares(&d, 3), Some((1.0 / 17.0, 16.0 / 17.0)));
        d[3].mass = 1.0;
        d[2].mass = 1000.0;
        assert_eq!(link_shares(&d, 3), Some((16.0 / 17.0, 1.0 / 17.0)));
        d[2].mass = 1.0;
        d[3].mass = 16.0;
        assert_eq!(link_shares(&d, 3), Some((1.0 / 17.0, 16.0 / 17.0)));
        // A kinematic parent or child is never coupled.
        let mut d = defs;
        d[2].flags |= FLAG_KINEMATIC;
        assert_eq!(link_shares(&d, 3), None);
        assert_eq!(link_shares(&d, 2), None);
    }
}
