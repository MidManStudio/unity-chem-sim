// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "tests/kernel_tests.rs"
// ============================================================================
//! Behavior tests for the scalar kernel, through the safe `World`.
//!
//! Each test states the property it checks. Tolerances are loose enough for
//! f32 and tight enough to catch a wrong sign or a missing term.

#![allow(clippy::needless_range_loop)]

use jiggle_core::collide;
use jiggle_core::math::{Q, V3};
use jiggle_core::params::AuthoringParams;
use jiggle_core::solver::{self, Buffers, SolverError};
use jiggle_core::types::*;
use jiggle_core::world::World;

const BONE: f32 = 0.2;

/// Anchor at the origin and `bones` bones along +x, each `BONE` long.
fn chain_x(bones: usize) -> Vec<V3> {
    (0..=bones).map(|i| V3::new(i as f32 * BONE, 0.0, 0.0)).collect()
}

fn base_params() -> AuthoringParams {
    AuthoringParams { stiffness: 0.8, stretch: 0.0, drag: 0.1, ..AuthoringParams::default() }
}

fn world_with(bones: usize, p: AuthoringParams) -> (World, usize, Vec<V3>) {
    let rest = chain_x(bones);
    let mut w = World::new();
    let rig = w.add_chain(&rest, &vec![p; bones]);
    (w, rig, rest)
}

fn run_frames(w: &mut World, rig: usize, pose: &[V3], frames: usize) {
    for _ in 0..frames {
        w.set_pose(rig, pose, None);
        w.step_fixed(1).unwrap();
    }
}

fn state_pos(w: &World, rig: usize, i: usize) -> V3 {
    V3::from4(&w.state[w.rigs[rig].particle_offset as usize + i].pos)
}

fn state_vel(w: &World, rig: usize, i: usize) -> V3 {
    let s = &w.state[w.rigs[rig].particle_offset as usize + i];
    V3::from4(&s.pos) - V3::from4(&s.prev)
}

fn near(a: V3, b: V3, tol: f32) -> bool {
    (a - b).length() <= tol
}

#[test]
fn rest_pose_is_stable_without_gravity() {
    let (mut w, rig, rest) = world_with(4, base_params());
    w.cfg.gravity = [0.0; 3];
    run_frames(&mut w, rig, &rest, 300);
    let n = w.rigs[rig].particle_count as usize;
    for i in 0..n {
        let want = if i < rest.len() { rest[i] } else { rest[rest.len() - 1] + (rest[rest.len() - 1] - rest[rest.len() - 2]) };
        assert!(near(w.world_pos(rig, i), want, 1e-5), "particle {} drifted: {:?}", i, w.world_pos(rig, i));
    }
}

#[test]
fn gravity_pulls_below_pose_then_settles() {
    let (mut w, rig, rest) = world_with(4, base_params());
    run_frames(&mut w, rig, &rest, 600);
    let tip = w.world_pos(rig, 4);
    assert!(tip.y < -0.001, "the tip should sag, got y = {}", tip.y);
    assert!(tip.y > -0.5, "stiffness should limit the sag, got y = {}", tip.y);
    for i in 1..6 {
        let v = state_vel(&w, rig, i).length();
        assert!(v < 1e-4, "particle {} still moves at {} per step", i, v);
    }
}

#[test]
fn stiffer_chain_sags_less() {
    let mut sag = Vec::new();
    for stiffness in [0.4f32, 0.7, 0.95] {
        let (mut w, rig, rest) = world_with(4, AuthoringParams { stiffness, ..base_params() });
        run_frames(&mut w, rig, &rest, 600);
        sag.push(-w.world_pos(rig, 4).y);
    }
    assert!(sag[0] > sag[1] && sag[1] > sag[2], "sag by stiffness: {:?}", sag);
}

#[test]
fn bone_lengths_are_kept_when_stretch_is_zero() {
    let (mut w, rig, rest) = world_with(5, base_params());
    for f in 0..240 {
        let mut pose = rest.clone();
        let sway = (f as f32 * 0.2).sin() * 0.3;
        for p in pose.iter_mut() {
            p.z += sway;
        }
        w.set_pose(rig, &pose, None);
        w.step_fixed(1).unwrap();
        // Bones 2.. keep their length to their parent. Bone 1 is a root particle and is held by the root spring.
        for i in 2..6 {
            let l = (state_pos(&w, rig, i) - state_pos(&w, rig, i - 1)).length();
            assert!((l - BONE).abs() < 2e-4, "frame {} bone {} length {}", f, i, l);
        }
    }
}

#[test]
fn stretch_lets_the_length_change() {
    // Particle 1 is the root, held to its pose. Particle 2 hangs from it with no pull and no length restore, so it falls away.
    let (mut w, rig, rest) = world_with(3, AuthoringParams { stretch: 1.0, stiffness: 0.0, drag: 0.0, ..base_params() });
    w.cfg.gravity = [0.0, -30.0, 0.0];
    run_frames(&mut w, rig, &rest, 60);
    let l = (state_pos(&w, rig, 2) - state_pos(&w, rig, 1)).length();
    assert!(l > BONE + 0.01, "with stretch 1 the bone should be free to change length, got {}", l);
}

#[test]
fn motion_decays_after_a_jerk() {
    let (mut w, rig, rest) = world_with(4, base_params());
    w.cfg.gravity = [0.0; 3];
    // Jerk the anchor sideways once, then hold still.
    let mut pose = rest.clone();
    for p in pose.iter_mut() {
        p.z += 0.4;
    }
    w.set_pose(rig, &pose, None);
    w.step_fixed(1).unwrap();
    let mut energy = Vec::new();
    for _ in 0..240 {
        w.set_pose(rig, &pose, None);
        w.step_fixed(1).unwrap();
        let e: f32 = (1..6).map(|i| state_vel(&w, rig, i).length_sq()).sum();
        energy.push(e);
    }
    let early: f32 = energy[..30].iter().sum();
    let late: f32 = energy[210..].iter().sum();
    assert!(early > 0.0, "the jerk should have set the chain moving");
    assert!(late < early * 1e-3, "motion should die down: early {} late {}", early, late);
}

#[test]
fn drag_one_stops_motion_relative_to_parent() {
    let (mut w, rig, rest) = world_with(3, AuthoringParams { drag: 1.0, stiffness: 0.0, ..base_params() });
    w.cfg.gravity = [0.0; 3];
    run_frames(&mut w, rig, &rest, 5);
    // A particle with full drag keeps no velocity of its own: it only follows its parent's motion.
    for i in 2..5 {
        let v = state_vel(&w, rig, i).length();
        assert!(v < 1e-6, "particle {} velocity {}", i, v);
    }
}

#[test]
fn air_drag_one_ignores_the_parent_motion() {
    // With air drag 1 and drag 0 a particle keeps only its own velocity and takes none from its parent.
    let (mut w, rig, rest) = world_with(2, AuthoringParams { air_drag: 1.0, drag: 0.0, stiffness: 0.0, stretch: 1.0, ..base_params() });
    w.cfg.gravity = [0.0; 3];
    let off = w.rigs[rig].particle_offset as usize;
    // Give particle 2 a velocity along +z and none for the others.
    w.state[off + 2].pos[2] = 0.01;
    run_frames(&mut w, rig, &rest, 1);
    let z = state_pos(&w, rig, 2).z;
    assert!((z - 0.02).abs() < 1e-4, "own velocity should carry on, got z = {}", z);
}

#[test]
fn angle_limit_bounds_the_swing() {
    let params = AuthoringParams {
        stiffness: 0.3,
        angle_limit: 0.25,
        angle_limited: true,
        stretch: 0.0,
        drag: 0.0,
        ..AuthoringParams::default()
    };
    let (mut w, rig, rest) = world_with(4, params);
    let limit = 0.25 * std::f32::consts::FRAC_PI_2;
    let mut worst = 0.0f32;
    for f in 0..300 {
        let mut pose = rest.clone();
        let sway = (f as f32 * 0.35).sin() * 0.5;
        for p in pose.iter_mut() {
            p.y += sway;
        }
        w.set_pose(rig, &pose, None);
        w.step_fixed(1).unwrap();

        // Rebuild what the kernel used: the pose of this step is `pose` relative to its anchor, and the state is final.
        let off = w.rigs[rig].particle_offset as usize;
        let n = w.rigs[rig].particle_count as usize;
        let q: Vec<V3> = (0..n).map(|i| V3::from4(&w.pose_curr[off + i])).collect();
        let x: Vec<V3> = (0..n).map(|i| state_pos(&w, rig, i)).collect();
        let mut rot = vec![Q::IDENTITY; n];
        for i in 1..n {
            let p = w.defs[off + i].parent as usize;
            let pose_aim = (q[i] - q[p]).normalize_safe(V3::new(0.0, 0.0, 1.0));
            let sim_aim = (x[i] - x[p]).normalize_safe(pose_aim);
            rot[i] = Q::from_to(pose_aim, sim_aim);
        }
        // Bone 1 is a root particle, with no angle constraint. Check the others against their parent's swing.
        for i in 2..n {
            let p = w.defs[off + i].parent as usize;
            let target = x[p] + rot[p].rotate(q[i] - q[p]);
            let a = (x[i] - x[p]).normalize_safe(V3::new(1.0, 0.0, 0.0));
            let b = (target - x[p]).normalize_safe(V3::new(1.0, 0.0, 0.0));
            let angle = a.dot(b).clamp(-1.0, 1.0).acos();
            if angle > worst {
                worst = angle;
            }
        }
    }
    assert!(worst <= limit + 2e-3, "swing {} exceeds the limit {}", worst, limit);
    assert!(worst > limit * 0.5, "the test motion should reach the limit, worst {}", worst);
}

#[test]
fn runs_are_deterministic() {
    let build = || {
        let (mut w, rig, rest) = world_with(6, AuthoringParams { angle_limit: 0.4, angle_limited: true, ..base_params() });
        w.fields.push(Field {
            kind: FIELD_DIRECTIONAL,
            mode: FIELD_ACCELERATION,
            center: [0.0; 4],
            dir: [1.0, 0.0, 0.3, 0.7],
            strength: 4.0,
            turbulence: 0.8,
            frequency: 1.3,
            ..Field::default()
        });
        for f in 0..400 {
            let mut pose = rest.clone();
            let s = (f as f32 * 0.07).sin();
            for p in pose.iter_mut() {
                p.x += s * 0.3;
                p.z += (f as f32 * 0.11).cos() * 0.2;
            }
            w.set_pose(rig, &pose, None);
            w.step(1.0 / 50.0).unwrap();
        }
        w.out.iter().flat_map(|o| o.pos.iter().chain(o.rot.iter()).map(|v| v.to_bits())).collect::<Vec<u32>>()
    };
    assert_eq!(build(), build());
}

// ---------------------------------------------------------------- colliders

fn collider_world(c: Collider, start: V3) -> (World, usize) {
    // A short chain with every bone colliding, and a collider list holding `c`.
    let params = AuthoringParams { collide: true, collision_radius: 0.05, stiffness: 0.6, ..base_params() };
    let rest = chain_x(3);
    let mut w = World::new();
    let rig = w.add_chain(&rest, &[params; 3]);
    w.colliders.push(c);
    w.set_collider_list(rig, &[0]);
    // Start the last bone inside the collider.
    let off = w.rigs[rig].particle_offset as usize;
    start.write4(&mut w.state[off + 3].pos);
    start.write4(&mut w.state[off + 3].prev);
    (w, rig)
}

fn run_collider(c: Collider, start: V3) -> (World, usize) {
    let (mut w, rig) = collider_world(c, start);
    w.cfg.gravity = [0.0; 3];
    let rest = chain_x(3);
    run_frames(&mut w, rig, &rest, 30);
    (w, rig)
}

#[test]
fn sphere_collider_pushes_out() {
    let c = Collider { kind: COLLIDER_SPHERE, radius: 0.15, center: [0.6, 0.0, 0.0, 0.0], ..Collider::default() };
    let (w, rig) = run_collider(c, V3::new(0.6, 0.01, 0.0));
    for i in 1..4 {
        let d = (state_pos(&w, rig, i) - V3::new(0.6, 0.0, 0.0)).length();
        assert!(d >= 0.15 + 0.05 - 1e-4, "bone {} is {} from the center", i, d);
    }
}

#[test]
fn capsule_collider_pushes_out() {
    let c = Collider {
        kind: COLLIDER_CAPSULE,
        radius: 0.08,
        half_height: 0.3,
        center: [0.6, 0.0, 0.0, 0.0],
        axis: [0.0, 1.0, 0.0, 0.0],
        ..Collider::default()
    };
    let (w, rig) = run_collider(c, V3::new(0.6, 0.1, 0.0));
    let p = state_pos(&w, rig, 3);
    // Distance to the capsule's core segment along y from -0.3 to 0.3 at x = 0.6.
    let t = p.y.clamp(-0.3, 0.3);
    let d = (p - V3::new(0.6, t, 0.0)).length();
    assert!(d >= 0.08 + 0.05 - 1e-4, "distance to the core {}", d);
}

#[test]
fn plane_collider_holds_the_chain_above() {
    // Floor at y = -0.1 with its normal up. The chain hangs under gravity and must rest on the floor.
    let c = Collider { kind: COLLIDER_PLANE, center: [0.0, -0.1, 0.0, 0.0], axis: [0.0, 1.0, 0.0, 0.0], ..Collider::default() };
    let params = AuthoringParams { collide: true, collision_radius: 0.05, stiffness: 0.3, ..base_params() };
    let rest = chain_x(3);
    let mut w = World::new();
    let rig = w.add_chain(&rest, &[params; 3]);
    w.colliders.push(c);
    w.set_collider_list(rig, &[0]);
    run_frames(&mut w, rig, &rest, 400);
    for i in 1..4 {
        let y = state_pos(&w, rig, i).y;
        assert!(y >= -0.1 + 0.05 - 1e-4, "bone {} at y = {}", i, y);
    }
    // And it actually touched the floor: without it the tip would sag lower.
    let low = (1..4).map(|i| state_pos(&w, rig, i).y).fold(f32::MAX, f32::min);
    assert!(low < -0.1 + 0.05 + 0.02, "the chain never reached the floor, lowest y = {}", low);
}

#[test]
fn box_collider_pushes_out_from_inside_and_outside() {
    let q = Q::from_to(V3::new(1.0, 0.0, 0.0), V3::new(0.0, 1.0, 0.0)); // 90 degrees about z
    let c = Collider {
        kind: COLLIDER_BOX,
        radius: 0.0,
        center: [0.6, 0.0, 0.0, 0.0],
        extents: [0.1, 0.2, 0.1, 0.0],
        rot: q.to4(),
        ..Collider::default()
    };
    let (w, rig) = run_collider(c, V3::new(0.6, 0.0, 0.02));
    let p = state_pos(&w, rig, 3);
    let local = q.conjugate().rotate(p - V3::new(0.6, 0.0, 0.0));
    let half = V3::new(0.1, 0.2, 0.1);
    let clamped = V3::new(local.x.clamp(-half.x, half.x), local.y.clamp(-half.y, half.y), local.z.clamp(-half.z, half.z));
    let d = (local - clamped).length();
    assert!(d >= 0.05 - 1e-4, "distance to the box {}", d);
}

#[test]
fn inside_sphere_keeps_particles_in() {
    let c = Collider { kind: COLLIDER_INSIDE_SPHERE, radius: 0.35, center: [0.0, 0.0, 0.0, 0.0], ..Collider::default() };
    let params = AuthoringParams { collide: true, collision_radius: 0.05, stiffness: 0.2, ..base_params() };
    let rest = chain_x(3);
    let mut w = World::new();
    let rig = w.add_chain(&rest, &[params; 3]);
    w.colliders.push(c);
    w.set_collider_list(rig, &[0]);
    // The rest pose of the outer bones is 0.4 and 0.6 from the center, outside the sphere.
    run_frames(&mut w, rig, &rest, 100);
    for i in 1..4 {
        let d = state_pos(&w, rig, i).length();
        assert!(d <= 0.35 - 0.05 + 1e-4, "bone {} is {} from the center", i, d);
    }
}

#[test]
fn collider_untouched_particles_are_not_moved() {
    // Same kernel step with and without a far away collider gives the same bits.
    let far = Collider { kind: COLLIDER_SPHERE, radius: 0.1, center: [50.0, 50.0, 50.0, 0.0], ..Collider::default() };
    let (mut a, ra) = collider_world(far, V3::new(0.6, 0.0, 0.0));
    let (mut b, rb) = collider_world(far, V3::new(0.6, 0.0, 0.0));
    b.set_collider_list(rb, &[]);
    let rest = chain_x(3);
    run_frames(&mut a, ra, &rest, 50);
    run_frames(&mut b, rb, &rest, 50);
    assert_eq!(a.state, b.state);
}

#[test]
fn segment_collision_catches_a_bone_crossing_a_thin_collider() {
    // A small sphere sits at the middle of the last bone. Point collision on the end particle misses it,
    // segment collision pushes the segment away.
    let c = Collider { kind: COLLIDER_SPHERE, radius: 0.05, center: [0.5, 0.0, 0.0, 0.0], ..Collider::default() };
    let mk = |segment: bool| {
        let params = AuthoringParams { collide: true, collision_radius: 0.02, segment_collision: segment, stiffness: 1.0, ..base_params() };
        let rest = chain_x(3);
        let mut w = World::new();
        let rig = w.add_chain(&rest, &[params; 3]);
        w.colliders.push(c);
        w.set_collider_list(rig, &[0]);
        w.cfg.gravity = [0.0; 3];
        run_frames(&mut w, rig, &rest, 40);
        (w, rig)
    };
    let (plain, rp) = mk(false);
    let (seg, rs) = mk(true);
    let mid = |w: &World, r: usize| (state_pos(w, r, 2) + state_pos(w, r, 3)) * 0.5;
    let d_plain = (mid(&plain, rp) - V3::new(0.5, 0.0, 0.0)).length();
    let d_seg = (mid(&seg, rs) - V3::new(0.5, 0.0, 0.0)).length();
    assert!(d_seg > d_plain + 0.005, "segment {} should clear the sphere better than point {}", d_seg, d_plain);
}

#[test]
fn collide_functions_leave_clear_points_alone() {
    let sphere = Collider { kind: COLLIDER_SPHERE, radius: 0.1, ..Collider::default() };
    let x = V3::new(1.0, 2.0, 3.0);
    assert_eq!(collide::push_out(&sphere, V3::ZERO, x, 0.05), x);
    let unknown = Collider { kind: 99, ..Collider::default() };
    assert_eq!(collide::push_out(&unknown, V3::ZERO, x, 0.05), x);
}

// ------------------------------------------------------------------- fields

fn field_world(f: Field, response: f32) -> (World, usize, Vec<V3>) {
    let p = AuthoringParams { wind_response: response, impulse_response: response, stiffness: 0.5, ..base_params() };
    let (mut w, rig, rest) = world_with(3, p);
    w.cfg.gravity = [0.0; 3];
    w.fields.push(f);
    (w, rig, rest)
}

#[test]
fn directional_wind_pushes_along_its_direction() {
    let f = Field { kind: FIELD_DIRECTIONAL, dir: [0.0, 0.0, 1.0, 0.0], strength: 8.0, ..Field::default() };
    let (mut w, rig, rest) = field_world(f, 1.0);
    run_frames(&mut w, rig, &rest, 120);
    let tip = w.world_pos(rig, 3);
    assert!(tip.z > 0.01, "wind along +z should push the tip to +z, got {}", tip.z);
    assert!(tip.y.abs() < 1e-4 && tip.x > 0.3, "no sideways push expected: {:?}", tip);
}

#[test]
fn wind_response_zero_ignores_wind() {
    let f = Field { kind: FIELD_DIRECTIONAL, dir: [0.0, 0.0, 1.0, 0.0], strength: 8.0, ..Field::default() };
    let (mut w, rig, rest) = field_world(f, 0.0);
    run_frames(&mut w, rig, &rest, 60);
    assert!(w.world_pos(rig, 3).z.abs() < 1e-6);
}

#[test]
fn field_outside_its_radius_has_no_effect() {
    let f = Field { kind: FIELD_RADIAL, center: [10.0, 0.0, 0.0, 0.0], radius: 1.0, strength: 20.0, ..Field::default() };
    let (mut w, rig, rest) = field_world(f, 1.0);
    run_frames(&mut w, rig, &rest, 60);
    assert!(near(w.world_pos(rig, 3), rest[3], 1e-5));
}

#[test]
fn radial_field_pushes_away_and_negative_pulls() {
    let center = [0.3, -0.2, 0.0, 0.0];
    let run = |strength: f32| {
        let f = Field { kind: FIELD_RADIAL, center, radius: 2.0, strength, power: 1, ..Field::default() };
        let (mut w, rig, rest) = field_world(f, 1.0);
        run_frames(&mut w, rig, &rest, 30);
        w.world_pos(rig, 3).y - rest[3].y
    };
    // The center is below the chain, so pushing away moves the tip up and pulling moves it down.
    assert!(run(15.0) > 0.001);
    assert!(run(-15.0) < -0.001);
}

#[test]
fn vortex_field_pushes_around_its_axis() {
    // Axis +y through x = 0.6. The tip sits on the axis side at distance; its push is along axis x radial.
    let f = Field { kind: FIELD_VORTEX, center: [0.3, 0.0, 0.0, 0.0], dir: [0.0, 1.0, 0.0, 0.0], radius: 2.0, strength: 20.0, ..Field::default() };
    let (mut w, rig, rest) = field_world(f, 1.0);
    run_frames(&mut w, rig, &rest, 30);
    let tip = w.world_pos(rig, 3);
    // Radial vector from the axis to the tip is +x, axis is +y, y cross x = -z.
    assert!(tip.z < -0.002, "vortex should push to -z here, got {}", tip.z);
    assert!(tip.y.abs() < 1e-4);
}

#[test]
fn impulse_applies_once_per_frame_on_the_first_step() {
    // One free particle: no pull to the pose, no drag, no gravity. An impulse of 3 m/s gives it 3 / 60 meters per
    // step. Applied once in a frame of four steps it travels 4 * 0.05 = 0.2 m. Applied on every step it would
    // gain speed each time and travel 10 * 0.05 = 0.5 m.
    let f = Field { kind: FIELD_DIRECTIONAL, mode: FIELD_IMPULSE, dir: [0.0, 0.0, 1.0, 0.0], strength: 3.0, ..Field::default() };
    let free = AuthoringParams { stiffness: 0.0, stretch: 1.0, root_stretch: 1.0, drag: 0.0, ..base_params() };
    let (mut w, rig, rest) = world_with(1, free);
    w.cfg.gravity = [0.0; 3];
    w.fields.push(f);
    w.set_pose(rig, &rest, None);
    w.step_fixed(4).unwrap();
    let z = state_pos(&w, rig, 1).z;
    assert!((z - 0.2).abs() < 0.002, "expected 0.2 m from a single impulse, got {}", z);
    // The next frame carries the velocity on and adds one more impulse, so the speed is now two impulses, not eight.
    w.set_pose(rig, &rest, None);
    w.step_fixed(1).unwrap();
    let v = state_vel(&w, rig, 1).z;
    assert!((v - 0.1).abs() < 0.002, "expected 0.1 m per step after two frames, got {}", v);
}

#[test]
fn impulse_does_not_run_when_no_step_fits() {
    let f = Field { kind: FIELD_DIRECTIONAL, mode: FIELD_IMPULSE, dir: [0.0, 0.0, 1.0, 0.0], strength: 3.0, ..Field::default() };
    let (mut w, rig, rest) = field_world(f, 1.0);
    w.set_pose(rig, &rest, None);
    w.step_fixed(0).unwrap();
    assert!(near(state_pos(&w, rig, 3), rest[3], 1e-6));
}

// -------------------------------------------------------------------- sleep

#[test]
fn rig_falls_asleep_and_wakes_on_motion() {
    let (mut w, rig, rest) = world_with(3, base_params());
    w.cfg.gravity = [0.0; 3];
    w.cfg.sleep_frames = 10;
    // Disturb the tip a little, then hold still.
    let off = w.rigs[rig].particle_offset as usize;
    w.state[off + 3].pos[2] = 0.003;
    run_frames(&mut w, rig, &rest, 200);
    assert!(w.rigs[rig].flags & RIG_ASLEEP != 0, "the rig should be asleep, counter {}", w.rigs[rig].sleep_counter);
    // Asleep: the output is exactly the pose.
    for i in 0..4 {
        assert!(near(w.world_pos(rig, i), rest[i], 1e-6));
    }
    // A small anchor motion below the wake threshold keeps it asleep.
    let mut moved = rest.clone();
    for p in moved.iter_mut() {
        p.y += 0.0005;
    }
    w.set_pose(rig, &moved, None);
    w.step_fixed(1).unwrap();
    assert!(w.rigs[rig].flags & RIG_ASLEEP != 0);
    // A big one wakes it.
    let mut moved = rest.clone();
    for p in moved.iter_mut() {
        p.y += 0.1;
    }
    w.set_pose(rig, &moved, None);
    w.step_fixed(1).unwrap();
    assert!(w.rigs[rig].flags & RIG_ASLEEP == 0, "a 10 cm anchor move should wake the rig");
}

#[test]
fn a_reaching_field_wakes_a_sleeping_rig() {
    let (mut w, rig, rest) = world_with(3, base_params());
    w.cfg.gravity = [0.0; 3];
    w.cfg.sleep_frames = 5;
    run_frames(&mut w, rig, &rest, 30);
    assert!(w.rigs[rig].flags & RIG_ASLEEP != 0);
    w.fields.push(Field { kind: FIELD_DIRECTIONAL, dir: [0.0, 0.0, 1.0, 0.0], strength: 5.0, ..Field::default() });
    run_frames(&mut w, rig, &rest, 1);
    assert!(w.rigs[rig].flags & RIG_ASLEEP == 0);
}

#[test]
fn sleeping_never_starts_while_sleep_frames_is_zero() {
    let (mut w, rig, rest) = world_with(3, base_params());
    w.cfg.gravity = [0.0; 3];
    w.cfg.sleep_frames = 0;
    run_frames(&mut w, rig, &rest, 100);
    assert!(w.rigs[rig].flags & RIG_ASLEEP == 0);
}

// -------------------------------------------------------- teleport and reset

#[test]
fn teleport_snaps_to_the_pose_without_a_whip() {
    let (mut w, rig, rest) = world_with(4, base_params());
    run_frames(&mut w, rig, &rest, 60);
    let far: Vec<V3> = rest.iter().map(|p| *p + V3::new(50.0, 0.0, 0.0)).collect();
    w.set_pose(rig, &far, None);
    w.step_fixed(1).unwrap();
    let off = w.rigs[rig].particle_offset as usize;
    for i in 0..6 {
        assert_eq!(w.state[off + i].pos, w.pose_curr[off + i], "particle {} was not snapped", i);
        assert_eq!(w.state[off + i].prev, w.pose_curr[off + i]);
    }
    assert!(w.state.iter().all(|s| s.pos.iter().all(|v| v.is_finite())));
}

#[test]
fn reset_flag_snaps_once_and_clears() {
    let (mut w, rig, rest) = world_with(4, base_params());
    run_frames(&mut w, rig, &rest, 60);
    w.rigs[rig].flags |= RIG_RESET;
    w.set_pose(rig, &rest, None);
    w.step_fixed(1).unwrap();
    assert_eq!(w.rigs[rig].flags & RIG_RESET, 0);
    let off = w.rigs[rig].particle_offset as usize;
    assert_eq!(w.state[off + 4].pos, w.pose_curr[off + 4]);
}

#[test]
fn disabled_rig_is_not_stepped() {
    let (mut w, rig, rest) = world_with(3, base_params());
    w.rigs[rig].flags |= RIG_DISABLED;
    let before = w.state.clone();
    run_frames(&mut w, rig, &rest, 10);
    assert_eq!(before, w.state);
    // Its output is the pose.
    assert!(near(w.world_pos(rig, 3), rest[3], 1e-6));
}

// --------------------------------------------------------------- zero steps

#[test]
fn zero_steps_keep_world_positions_when_the_anchor_moves() {
    let (mut w, rig, rest) = world_with(3, base_params());
    run_frames(&mut w, rig, &rest, 60);
    let before: Vec<V3> = (1..5).map(|i| w.world_pos(rig, i)).collect();
    // Move the whole pose 0.3 along z and run no step.
    let moved: Vec<V3> = rest.iter().map(|p| *p + V3::new(0.0, 0.0, 0.3)).collect();
    w.set_pose(rig, &moved, None);
    w.step_fixed(0).unwrap();
    let off = w.rigs[rig].particle_offset as usize;
    let anchor = V3::from3(&w.rigs[rig].anchor_world);
    // The simulated world position of a loose bone has not changed: the state was rebased.
    for i in 2..5 {
        let world = V3::from4(&w.state[off + i].pos) + anchor;
        assert!(near(world, before[i - 1], 1e-5), "particle {} moved by the rebase", i);
    }
}

#[test]
fn ignore_root_motion_follows_the_anchor() {
    let mk = |ignore: f32| {
        let (mut w, rig, rest) = world_with(3, AuthoringParams { ignore_root_motion: ignore, ..base_params() });
        w.cfg.gravity = [0.0; 3];
        run_frames(&mut w, rig, &rest, 5);
        let moved: Vec<V3> = rest.iter().map(|p| *p + V3::new(0.0, 0.0, 0.5)).collect();
        w.set_pose(rig, &moved, None);
        w.step_fixed(1).unwrap();
        // Distance from the tip's state to its pose, in the anchor frame: the lag.
        (state_pos(&w, rig, 3) - V3::from4(&w.pose_curr[w.rigs[rig].particle_offset as usize + 3])).length()
    };
    let lag_free = mk(0.0);
    let lag_follow = mk(1.0);
    assert!(lag_free > 0.01, "a free chain should lag a 0.5 m jump, lag {}", lag_free);
    assert!(lag_follow < 1e-4, "ignore_root_motion 1 should follow the anchor, lag {}", lag_follow);
}

// ------------------------------------------------------------------ resolve

#[test]
fn resolve_at_rest_returns_the_pose_rotation() {
    let (mut w, rig, rest) = world_with(4, base_params());
    w.cfg.gravity = [0.0; 3];
    run_frames(&mut w, rig, &rest, 10);
    for i in 0..6 {
        let q = w.world_rot(rig, i);
        assert!((q.w.abs() - 1.0).abs() < 1e-5 && q.x.abs() < 1e-5 && q.y.abs() < 1e-5 && q.z.abs() < 1e-5, "particle {}: {:?}", i, q);
    }
}

#[test]
fn resolve_rotation_swings_with_the_bones() {
    let (mut w, rig, rest) = world_with(3, AuthoringParams { stiffness: 0.3, ..base_params() });
    run_frames(&mut w, rig, &rest, 300);
    // The chain points along +x in the pose and sags below it: bone 1 should be rotated so its +x points downward.
    let q = w.world_rot(rig, 1);
    let dir = q.rotate(V3::new(1.0, 0.0, 0.0));
    assert!(dir.y < -0.01, "bone 1 should point down, got {:?}", dir);
    // Its position agrees: the next particle is below it.
    assert!(w.world_pos(rig, 2).y < w.world_pos(rig, 1).y);
    // All outputs are unit quaternions.
    for i in 0..5 {
        let q = w.world_rot(rig, i);
        let l = (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w).sqrt();
        assert!((l - 1.0).abs() < 1e-4);
    }
}

#[test]
fn blend_zero_gives_the_pose_rotation() {
    let (mut w, rig, rest) = world_with(3, AuthoringParams { blend: 0.0, stiffness: 0.3, ..base_params() });
    run_frames(&mut w, rig, &rest, 200);
    for i in 0..5 {
        let q = w.world_rot(rig, i);
        assert!(q.x.abs() < 1e-5 && q.y.abs() < 1e-5 && q.z.abs() < 1e-5, "particle {}: {:?}", i, q);
    }
}

#[test]
fn alpha_blends_between_the_last_two_steps() {
    let (mut w, rig, rest) = world_with(3, base_params());
    run_frames(&mut w, rig, &rest, 3);
    let off = w.rigs[rig].particle_offset as usize;
    let s = w.state[off + 3];
    w.cfg.alpha = 0.0;
    w.cfg.steps = 0;
    // Resolve only: step_fixed(0) rebases with no anchor motion, then resolves with alpha 1, so call the solver directly.
    let cfg = w.cfg;
    let count = w.rigs.len();
    let mut b = Buffers {
        particle_base: 0,
        rigs: &mut w.rigs,
        defs: &w.defs,
        state: &mut w.state,
        pose_prev: &w.pose_prev,
        pose_curr: &w.pose_curr,
        pose_rot: &w.pose_rot,
        out: &mut w.out,
        colliders: &w.colliders,
        refs: &w.refs,
        fields: &w.fields,
    };
    solver::resolve_range(&cfg, &mut b, 0, count).unwrap();
    assert_eq!(w.out[off + 3].pos[..3], s.prev[..3]);
    let mut cfg2 = cfg;
    cfg2.alpha = 1.0;
    let mut b = Buffers {
        particle_base: 0,
        rigs: &mut w.rigs,
        defs: &w.defs,
        state: &mut w.state,
        pose_prev: &w.pose_prev,
        pose_curr: &w.pose_curr,
        pose_rot: &w.pose_rot,
        out: &mut w.out,
        colliders: &w.colliders,
        refs: &w.refs,
        fields: &w.fields,
    };
    solver::resolve_range(&cfg2, &mut b, 0, count).unwrap();
    assert_eq!(w.out[off + 3].pos[..3], s.pos[..3]);
}

// ------------------------------------------------------------- robustness

#[test]
fn nan_in_the_state_is_sanitized() {
    let (mut w, rig, rest) = world_with(4, base_params());
    run_frames(&mut w, rig, &rest, 10);
    let off = w.rigs[rig].particle_offset as usize;
    w.state[off + 3].pos = [f32::NAN, f32::NAN, f32::NAN, 0.0];
    run_frames(&mut w, rig, &rest, 2);
    for i in 0..6 {
        let s = w.state[off + i];
        assert!(s.pos.iter().chain(s.prev.iter()).all(|v| v.is_finite()), "particle {} still holds a NaN", i);
    }
    for o in &w.out {
        assert!(o.pos.iter().chain(o.rot.iter()).all(|v| v.is_finite()));
    }
}

#[test]
fn nan_in_the_anchor_snaps() {
    let (mut w, rig, rest) = world_with(3, base_params());
    run_frames(&mut w, rig, &rest, 10);
    w.rigs[rig].anchor_delta = [f32::NAN, 0.0, 0.0];
    w.step_fixed(1).unwrap();
    let off = w.rigs[rig].particle_offset as usize;
    assert!(w.state[off..off + 5].iter().all(|s| s.pos.iter().all(|v| v.is_finite())));
}

#[test]
fn largest_rig_works() {
    // 126 bones plus the anchor and the virtual end is exactly MAX_PARTICLES.
    let bones = MAX_PARTICLES - 2;
    let (mut w, rig, rest) = world_with(bones, base_params());
    run_frames(&mut w, rig, &rest, 30);
    assert_eq!(w.rigs[rig].particle_count as usize, MAX_PARTICLES);
    assert!(w.out.iter().all(|o| o.pos.iter().all(|v| v.is_finite())));
}

#[test]
fn many_steps_per_frame_stay_stable() {
    let (mut w, rig, rest) = world_with(4, base_params());
    for _ in 0..60 {
        w.set_pose(rig, &rest, None);
        w.step_fixed(8).unwrap();
    }
    assert!(w.out.iter().all(|o| o.pos.iter().all(|v| v.is_finite())));
    assert!(w.world_pos(rig, 4).y > -0.5);
}

#[test]
fn accumulator_runs_whole_steps_and_keeps_the_remainder() {
    let (mut w, rig, rest) = world_with(2, base_params());
    w.set_pose(rig, &rest, None);
    // 1.5 fixed steps: one step now, alpha 0.5, and the half step is kept for the next call.
    w.step(1.5 / 60.0).unwrap();
    assert_eq!(w.cfg.steps, 1);
    assert!((w.cfg.alpha - 0.5).abs() < 1e-4);
    w.set_pose(rig, &rest, None);
    w.step(0.5 / 60.0).unwrap();
    assert_eq!(w.cfg.steps, 1);
    assert!(w.cfg.alpha.abs() < 1e-3 || (w.cfg.alpha - 1.0).abs() < 1e-3);
    // A long hitch is clamped.
    w.set_pose(rig, &rest, None);
    w.step(5.0).unwrap();
    assert_eq!(w.cfg.steps, w.max_steps);
}

// -------------------------------------------------------------- solver errors

fn solver_call(w: &mut World, steps: u32) -> Result<(), SolverError> {
    let mut cfg = w.cfg;
    cfg.steps = steps;
    let count = w.rigs.len();
    let mut b = Buffers {
        particle_base: 0,
        rigs: &mut w.rigs,
        defs: &w.defs,
        state: &mut w.state,
        pose_prev: &w.pose_prev,
        pose_curr: &w.pose_curr,
        pose_rot: &w.pose_rot,
        out: &mut w.out,
        colliders: &w.colliders,
        refs: &w.refs,
        fields: &w.fields,
    };
    solver::step_range(&cfg, &mut b, 0, count)
}

#[test]
fn solver_refuses_bad_topology() {
    let (mut w, _rig, _rest) = world_with(3, base_params());
    // A parent that comes later than the particle.
    w.defs[2].parent = 4;
    assert_eq!(solver_call(&mut w, 1), Err(SolverError::BadTopology(0)));
    // Particle 0 not kinematic.
    let (mut w, _, _) = world_with(3, base_params());
    w.defs[0].flags = 0;
    assert_eq!(solver_call(&mut w, 1), Err(SolverError::BadTopology(0)));
    // A negative parent for a later particle.
    let (mut w, _, _) = world_with(3, base_params());
    w.defs[3].parent = -1;
    assert_eq!(solver_call(&mut w, 1), Err(SolverError::BadTopology(0)));
}

#[test]
fn solver_refuses_bad_ranges() {
    let (mut w, _, _) = world_with(3, base_params());
    w.rigs[0].particle_count = 500;
    assert_eq!(solver_call(&mut w, 1), Err(SolverError::BadRig(0)));
    let (mut w, _, _) = world_with(3, base_params());
    w.rigs[0].particle_count = 1;
    assert_eq!(solver_call(&mut w, 1), Err(SolverError::BadRig(0)));
    let (mut w, _, _) = world_with(3, base_params());
    w.rigs[0].particle_offset = 3;
    assert_eq!(solver_call(&mut w, 1), Err(SolverError::BadRig(0)));
    let (mut w, _, _) = world_with(3, base_params());
    w.rigs[0].ref_count = 4;
    assert_eq!(solver_call(&mut w, 1), Err(SolverError::BadRefs(0)));
    let (mut w, _, _) = world_with(3, base_params());
    let count = w.rigs.len() + 1;
    let cfg = w.cfg;
    let mut b = Buffers {
        particle_base: 0,
        rigs: &mut w.rigs,
        defs: &w.defs,
        state: &mut w.state,
        pose_prev: &w.pose_prev,
        pose_curr: &w.pose_curr,
        pose_rot: &w.pose_rot,
        out: &mut w.out,
        colliders: &w.colliders,
        refs: &w.refs,
        fields: &w.fields,
    };
    assert_eq!(solver::step_range(&cfg, &mut b, 0, count), Err(SolverError::RigRange));
}

#[test]
fn a_bad_collider_index_is_skipped() {
    let (mut w, rig, rest) = world_with(3, AuthoringParams { collide: true, ..base_params() });
    w.set_collider_list(rig, &[7]);
    run_frames(&mut w, rig, &rest, 5);
    assert!(w.out.iter().all(|o| o.pos.iter().all(|v| v.is_finite())));
}

#[test]
fn rigs_do_not_affect_each_other() {
    // Two rigs in one world give the same result as each alone.
    let rest_a = chain_x(3);
    let rest_b: Vec<V3> = rest_a.iter().map(|p| *p + V3::new(0.0, 2.0, 0.0)).collect();
    let mut both = World::new();
    let ra = both.add_chain(&rest_a, &[base_params(); 3]);
    let rb = both.add_chain(&rest_b, &[AuthoringParams { stiffness: 0.3, ..base_params() }; 3]);
    let mut only_b = World::new();
    let rb1 = only_b.add_chain(&rest_b, &[AuthoringParams { stiffness: 0.3, ..base_params() }; 3]);
    for f in 0..100 {
        let s = (f as f32 * 0.2).sin() * 0.2;
        let pa: Vec<V3> = rest_a.iter().map(|p| *p + V3::new(0.0, s, 0.0)).collect();
        let pb: Vec<V3> = rest_b.iter().map(|p| *p + V3::new(0.0, 0.0, s)).collect();
        both.set_pose(ra, &pa, None);
        both.set_pose(rb, &pb, None);
        only_b.set_pose(rb1, &pb, None);
        both.step_fixed(1).unwrap();
        only_b.step_fixed(1).unwrap();
    }
    let ob = both.rigs[rb].particle_offset as usize;
    assert_eq!(&both.out[ob..ob + 5], &only_b.out[..5]);
}

// ---------------------------------------------------------------- tip scale

#[test]
fn tip_scale_sets_the_virtual_tip_distance_and_only_the_last_bones_turn() {
    let hang: Vec<V3> = (0..=2).map(|i| V3::new(0.0, -(i as f32) * 0.1, 0.0)).collect();
    let p = AuthoringParams { stiffness: 0.15, stretch: 0.0, drag: 0.1, ..AuthoringParams::default() };
    let run = |scale: f32| {
        let mut w = World::new();
        let rig = w.add_chain_with_tip(&hang, &[p; 2], scale);
        // The virtual particle sits `scale` bone lengths past the last bone.
        let off = w.rigs[rig].particle_offset as usize;
        let tip = V3::from4(&w.pose_curr[off + 3]);
        let last = V3::from4(&w.pose_curr[off + 2]);
        assert!(((tip - last).length() - 0.1 * scale).abs() < 1e-6);
        let mut peak_turn = 0.0f32;
        let mut positions = Vec::new();
        for f in 0..120 {
            let dz = if f < 10 { f as f32 * 0.02 } else { 0.2 };
            let pose: Vec<V3> = hang.iter().map(|q| *q + V3::new(0.0, 0.0, dz)).collect();
            w.set_pose(rig, &pose, None);
            w.step_fixed(1).unwrap();
            let q = w.world_rot(rig, 2);
            let turn = 2.0 * (q.x * q.x + q.y * q.y + q.z * q.z).sqrt().atan2(q.w.abs());
            peak_turn = peak_turn.max(turn);
            positions.push(w.world_pos(rig, 2));
        }
        (peak_turn, positions)
    };
    let (short_turn, short_pos) = run(0.5);
    let (long_turn, long_pos) = run(4.0);
    // Real bone positions do not depend on the tip at all.
    assert_eq!(short_pos, long_pos);
    // The last bone turns less for a longer tip.
    assert!(short_turn > long_turn * 1.5, "short {} long {}", short_turn, long_turn);
    // A bad scale falls back to 1.
    let mut w = World::new();
    let rig = w.add_chain_with_tip(&hang, &[p; 2], f32::NAN);
    assert_eq!(w.tip_scale[rig], 1.0);
}
