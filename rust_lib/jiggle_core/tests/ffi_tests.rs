// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "tests/ffi_tests.rs"
// ============================================================================
//! Tests of the C interface: layout, error codes, parity with the safe world and threaded batches.
#![allow(unsafe_code)]

use jiggle_core::ffi::*;
use jiggle_core::math::V3;
use jiggle_core::params::AuthoringParams;
use jiggle_core::types::*;
use jiggle_core::world::World;

fn text_of(f: unsafe extern "C" fn(*mut u8, i32) -> i32) -> String {
    let mut buf = [0u8; 512];
    let len = unsafe { f(buf.as_mut_ptr(), buf.len() as i32) };
    assert!(len >= 0 && (len as usize) < buf.len());
    String::from_utf8(buf[..len as usize].to_vec()).unwrap()
}

fn probe_words(kind: i32) -> Vec<u32> {
    let size = jgl_struct_size(kind);
    assert!(size > 0 && size % 16 == 0, "kind {} size {}", kind, size);
    let mut buf = vec![0u8; size as usize];
    let written = unsafe { jgl_layout_probe(kind, buf.as_mut_ptr(), buf.len() as i32) };
    assert_eq!(written, size);
    buf.chunks_exact(4).map(|c| u32::from_le_bytes([c[0], c[1], c[2], c[3]])).collect()
}

fn fl(v: f32) -> u32 {
    v.to_bits()
}

#[test]
fn version_and_abi() {
    assert_eq!(jgl_abi_version(), ABI_VERSION);
    assert_eq!(ABI_VERSION, 1);
    let v = text_of(jgl_library_version);
    assert_eq!(v, env!("CARGO_PKG_VERSION"));
    // A short buffer truncates but still returns the full length.
    let mut small = [0xAAu8; 3];
    let full = unsafe { jgl_library_version(small.as_mut_ptr(), 3) };
    assert_eq!(full as usize, v.len());
    assert_eq!(small[2], 0);
    assert_eq!(&small[..2], &v.as_bytes()[..2]);
    // A null buffer or zero capacity only reports the length.
    assert_eq!(unsafe { jgl_library_version(std::ptr::null_mut(), 0) } as usize, v.len());
}

#[test]
fn struct_sizes_are_fixed() {
    let want = [(KIND_PARTICLE_DEF, 80), (KIND_PARTICLE_STATE, 32), (KIND_RIG, 64), (KIND_STEP_CONFIG, 64), (KIND_COLLIDER, 80), (KIND_FIELD, 64), (KIND_PARTICLE_OUT, 32), (KIND_SOLVER_ARGS, 112)];
    for (k, s) in want {
        assert_eq!(jgl_struct_size(k), s, "kind {}", k);
    }
    assert_eq!(jgl_struct_size(0), 0);
    assert_eq!(jgl_struct_size(99), 0);
    assert_eq!(jgl_struct_size(-1), 0);
    // None of them needs more than 8 byte alignment, so a C# struct with explicit size matches on every target.
    assert!(std::mem::align_of::<SolverArgs>() <= 8);
    assert!(std::mem::align_of::<ParticleDef>() == 4);
}

#[test]
fn layout_probe_matches_the_documented_field_order() {
    // Integer fields count 1, 2, 3 and so on, float fields 1.5, 2.5 and so on, in field order.
    let mut def = vec![1, 2, 3, 4];
    def.extend((1..=16).map(|i| fl(i as f32 + 0.5)));
    assert_eq!(probe_words(KIND_PARTICLE_DEF), def);

    let st: Vec<u32> = (1..=8).map(|i| fl(i as f32 + 0.5)).collect();
    assert_eq!(probe_words(KIND_PARTICLE_STATE), st);
    assert_eq!(probe_words(KIND_PARTICLE_OUT), st);

    let rig = vec![1, 2, 3, 4, 5, 6, fl(1.5), 7, fl(2.5), fl(3.5), fl(4.5), fl(5.5), fl(6.5), fl(7.5), fl(8.5), fl(9.5)];
    assert_eq!(probe_words(KIND_RIG), rig);

    let cfg = vec![fl(1.5), 1, fl(2.5), fl(3.5), fl(4.5), fl(5.5), fl(6.5), fl(7.5), fl(8.5), fl(9.5), 2, fl(10.5), fl(11.5), fl(12.5), fl(13.5), fl(14.5)];
    assert_eq!(probe_words(KIND_STEP_CONFIG), cfg);

    let mut col = vec![1, 2, fl(1.5), fl(2.5)];
    col.extend((3..=18).map(|i| fl(i as f32 + 0.5)));
    assert_eq!(probe_words(KIND_COLLIDER), col);

    let mut field = vec![1, 2, 3, 4];
    field.extend((1..=12).map(|i| fl(i as f32 + 0.5)));
    assert_eq!(probe_words(KIND_FIELD), field);

    // Ten 64 bit addresses 1 to 10 as low and high words, then eight 32 bit values 11 to 18.
    let mut args = Vec::new();
    for i in 1..=10u32 {
        args.push(i);
        args.push(0);
    }
    args.extend(11..=18u32);
    assert_eq!(probe_words(KIND_SOLVER_ARGS), args);
}

#[test]
fn layout_probe_refuses_bad_input() {
    let mut buf = [0u8; 256];
    assert_eq!(unsafe { jgl_layout_probe(99, buf.as_mut_ptr(), 256) }, JGL_ERR_ARG);
    assert_eq!(unsafe { jgl_layout_probe(KIND_RIG, std::ptr::null_mut(), 256) }, JGL_ERR_ARG);
    assert_eq!(unsafe { jgl_layout_probe(KIND_RIG, buf.as_mut_ptr(), 8) }, JGL_ERR_ARG);
    assert!(text_of(jgl_last_error).contains("too small"));
    unsafe { jgl_layout_probe(99, buf.as_mut_ptr(), 256) };
    assert!(text_of(jgl_last_error).contains("unknown struct kind"));
}

// ------------------------------------------------------------ a test scene

fn scene(rigs: usize) -> World {
    let mut w = World::new();
    w.fields.push(Field { kind: FIELD_DIRECTIONAL, dir: [0.3, 0.0, 1.0, 0.7], strength: 5.0, turbulence: 0.5, frequency: 1.1, ..Field::default() });
    w.colliders.push(Collider { kind: COLLIDER_SPHERE, radius: 0.2, center: [0.5, -0.3, 0.0, 0.0], ..Collider::default() });
    w.colliders.push(Collider { kind: COLLIDER_PLANE, center: [0.0, -0.6, 0.0, 0.0], axis: [0.0, 1.0, 0.0, 0.0], ..Collider::default() });
    for r in 0..rigs {
        let base = V3::new(r as f32 * 0.5, 0.0, 0.0);
        let bones = 3 + r % 4;
        let rest: Vec<V3> = (0..=bones).map(|i| base + V3::new(i as f32 * 0.15, 0.0, 0.0)).collect();
        let p = AuthoringParams { collide: true, collision_radius: 0.04, angle_limited: r % 2 == 0, angle_limit: 0.5, stiffness: 0.5 + (r % 3) as f32 * 0.15, ..AuthoringParams::default() };
        let rig = w.add_chain_with_tip(&rest, &vec![p; bones], 1.0 + (r % 3) as f32);
        w.set_collider_list(rig, &[0, 1]);
    }
    w
}

fn pose_world(w: &World, rig: usize, t: f32) -> Vec<V3> {
    // The rest pose of the rig plus a wobble, anchor first and bones after, without the virtual end.
    let n = w.rigs[rig].particle_count as usize;
    let anchor = V3::from3(&w.rigs[rig].anchor_world);
    let base = V3::new(rig as f32 * 0.5, 0.0, 0.0);
    let _ = anchor;
    (0..n - 1).map(|i| base + V3::new(i as f32 * 0.15, (t * 3.0).sin() * 0.1, (t * 2.0).cos() * 0.1)).collect()
}

fn args_of(w: &mut World) -> SolverArgs {
    SolverArgs {
        rigs: w.rigs.as_mut_ptr() as usize as u64,
        defs: w.defs.as_ptr() as usize as u64,
        state: w.state.as_mut_ptr() as usize as u64,
        pose_prev: w.pose_prev.as_ptr() as usize as u64,
        pose_curr: w.pose_curr.as_ptr() as usize as u64,
        pose_rot: w.pose_rot.as_ptr() as usize as u64,
        out: w.out.as_mut_ptr() as usize as u64,
        colliders: w.colliders.as_ptr() as usize as u64,
        refs: w.refs.as_ptr() as usize as u64,
        fields: w.fields.as_ptr() as usize as u64,
        rig_count: w.rigs.len() as u32,
        particle_count: w.defs.len() as u32,
        collider_count: w.colliders.len() as u32,
        ref_count: w.refs.len() as u32,
        field_count: w.fields.len() as u32,
        reserved: [0; 3],
    }
}

fn bits(w: &World) -> Vec<u32> {
    let mut v = Vec::new();
    for s in &w.state {
        v.extend(s.pos.iter().chain(s.prev.iter()).map(|x| x.to_bits()));
    }
    for o in &w.out {
        v.extend(o.pos.iter().chain(o.rot.iter()).map(|x| x.to_bits()));
    }
    for r in &w.rigs {
        v.push(r.flags);
        v.push(r.sleep_counter);
    }
    v
}

#[test]
fn ffi_matches_the_safe_world_bit_for_bit() {
    let mut a = scene(23);
    let mut b = scene(23);
    for f in 0..60 {
        let t = f as f32 / 60.0;
        for r in 0..a.rigs.len() {
            let pose = pose_world(&a, r, t);
            a.set_pose(r, &pose, None);
            b.set_pose(r, &pose, None);
        }
        a.cfg.time = t;
        b.cfg.time = t;
        a.cfg.steps = 1;
        a.cfg.alpha = 1.0;
        a.step_fixed(1).unwrap();
        // The same frame through the C interface.
        b.cfg.steps = 1;
        b.cfg.alpha = 1.0;
        b.cfg.dt = b.fixed_dt;
        let cfg = b.cfg;
        let args = args_of(&mut b);
        let n = b.rigs.len() as u32;
        assert_eq!(unsafe { jgl_step_rigs(&cfg, &args, 0, n) }, JGL_OK);
        assert_eq!(unsafe { jgl_resolve_rigs(&cfg, &args, 0, n) }, JGL_OK);
        a.cfg.time = t; // step_fixed advances time, so keep both on the same clock
    }
    assert_eq!(bits(&a), bits(&b));
}

#[test]
fn threaded_batches_match_one_call() {
    let mut one = scene(40);
    let mut many = scene(40);
    for f in 0..40 {
        let t = f as f32 / 60.0;
        for r in 0..one.rigs.len() {
            let pose = pose_world(&one, r, t);
            one.set_pose(r, &pose, None);
            many.set_pose(r, &pose, None);
        }
        for w in [&mut one, &mut many] {
            w.cfg.time = t;
            w.cfg.steps = 2;
            w.cfg.alpha = 0.5;
            w.cfg.dt = w.fixed_dt;
        }
        let cfg = one.cfg;
        let n = one.rigs.len() as u32;
        let args = args_of(&mut one);
        assert_eq!(unsafe { jgl_step_rigs(&cfg, &args, 0, n) }, JGL_OK);
        assert_eq!(unsafe { jgl_resolve_rigs(&cfg, &args, 0, n) }, JGL_OK);

        let cfg = many.cfg;
        let args = args_of(&mut many);
        // Four threads, each with its own batch of rigs. Odd batch sizes on purpose.
        let batches = [(0u32, 7u32), (7, 13), (20, 9), (29, 11)];
        std::thread::scope(|s| {
            for (first, count) in batches {
                s.spawn(move || {
                    assert_eq!(unsafe { jgl_step_rigs(&cfg, &args, first, count) }, JGL_OK);
                    assert_eq!(unsafe { jgl_resolve_rigs(&cfg, &args, first, count) }, JGL_OK);
                });
            }
        });
    }
    assert_eq!(bits(&one), bits(&many));
}

// ------------------------------------------------------------------ errors

fn cfg() -> StepConfig {
    StepConfig { dt: 1.0 / 60.0, steps: 1, alpha: 1.0, gravity: [0.0, -9.81, 0.0], ..StepConfig::default() }
}

#[test]
fn null_pointers_are_refused() {
    let c = cfg();
    let a = SolverArgs::default();
    assert_eq!(unsafe { jgl_step_rigs(std::ptr::null(), &a, 0, 0) }, JGL_ERR_NULL);
    assert_eq!(unsafe { jgl_step_rigs(&c, std::ptr::null(), 0, 0) }, JGL_ERR_NULL);
    assert_eq!(unsafe { jgl_resolve_rigs(std::ptr::null(), &a, 0, 0) }, JGL_ERR_NULL);
    assert!(text_of(jgl_last_error).contains("null"));
}

#[test]
fn empty_calls_are_fine() {
    let c = cfg();
    let a = SolverArgs::default();
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 0, 0) }, JGL_OK);
    assert_eq!(unsafe { jgl_resolve_rigs(&c, &a, 0, 0) }, JGL_OK);
}

#[test]
fn range_past_the_rig_count_is_refused() {
    let mut w = scene(3);
    let a = args_of(&mut w);
    let c = cfg();
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 0, 4) }, JGL_ERR_RANGE);
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 3, 1) }, JGL_ERR_RANGE);
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, u32::MAX, 2) }, JGL_ERR_RANGE);
    assert_eq!(unsafe { jgl_resolve_rigs(&c, &a, 2, 5) }, JGL_ERR_RANGE);
    assert!(text_of(jgl_last_error).contains("rig range"));
}

#[test]
fn a_null_array_is_refused_even_with_a_nonzero_rig_offset() {
    // The window of particles starts at the first rig's offset. A null address must be caught before that offset is added.
    let mut w = scene(4);
    let mut a = args_of(&mut w);
    a.defs = 0;
    let c = cfg();
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 2, 1) }, JGL_ERR_NULL);
    let mut a = args_of(&mut w);
    a.rigs = 0;
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 0, 1) }, JGL_ERR_NULL);
    let mut a = args_of(&mut w);
    a.out = 0;
    assert_eq!(unsafe { jgl_resolve_rigs(&c, &a, 3, 1) }, JGL_ERR_NULL);
}

#[test]
fn bad_rigs_are_refused_before_anything_is_written() {
    let c = cfg();
    // Particle range outside the arrays.
    let mut w = scene(3);
    w.rigs[1].particle_offset = 10_000;
    let a = args_of(&mut w);
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 1, 1) }, JGL_ERR_BAD_RIG);
    // Overlapping rigs inside one call.
    // Rig 1 is a copy of rig 0's header, so each rig is valid alone and only the overlap is wrong.
    let mut w = scene(3);
    w.rigs[1] = w.rigs[0];
    let before = w.state.clone();
    let a = args_of(&mut w);
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 0, 2) }, JGL_ERR_BAD_RIG);
    assert_eq!(before, w.state, "nothing may be stepped when the range is refused");
    // Rig 0 alone is fine.
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 0, 1) }, JGL_OK);
    // Topology.
    let mut w = scene(2);
    let off = w.rigs[1].particle_offset as usize;
    w.defs[off + 2].parent = 9;
    let before = w.state.clone();
    let a = args_of(&mut w);
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 1, 1) }, JGL_ERR_BAD_RIG);
    assert!(text_of(jgl_last_error).contains("topology"));
    assert_eq!(before, w.state);
    // A bad rig late in a range stops the whole call before any rig of it runs, for step and for resolve.
    let mut w = scene(3);
    let off = w.rigs[2].particle_offset as usize;
    w.defs[off + 2].parent = 9;
    let before_state = w.state.clone();
    let before_out = w.out.clone();
    let a = args_of(&mut w);
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 0, 3) }, JGL_ERR_BAD_RIG);
    assert_eq!(before_state, w.state, "rigs 0 and 1 must not have been stepped");
    assert_eq!(unsafe { jgl_resolve_rigs(&c, &a, 0, 3) }, JGL_ERR_BAD_RIG);
    assert_eq!(before_out, w.out, "rigs 0 and 1 must not have been resolved");
    // The good rigs of the same arrays still run when the call leaves the bad one out.
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 0, 2) }, JGL_OK);
    assert_ne!(before_state, w.state);
    // Collider list outside the reference array.
    let mut w = scene(2);
    w.rigs[0].ref_count = 99;
    let a = args_of(&mut w);
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 0, 1) }, JGL_ERR_BAD_RIG);
    assert!(text_of(jgl_last_error).contains("collider list"));
}

#[test]
fn success_leaves_the_last_error_alone() {
    let mut w = scene(2);
    let a = args_of(&mut w);
    let c = cfg();
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 5, 1) }, JGL_ERR_RANGE);
    let before = text_of(jgl_last_error);
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 0, 2) }, JGL_OK);
    assert_eq!(text_of(jgl_last_error), before);
}

#[test]
fn last_error_is_per_thread() {
    let mut w = scene(1);
    let a = args_of(&mut w);
    let c = cfg();
    assert_eq!(unsafe { jgl_step_rigs(&c, &a, 9, 1) }, JGL_ERR_RANGE);
    let other = std::thread::spawn(|| text_of(jgl_last_error)).join().unwrap();
    assert_eq!(other, "");
    assert!(!text_of(jgl_last_error).is_empty());
}

#[test]
fn the_rig_array_is_updated_in_place() {
    // The kernel writes the sleep state into the host's rig headers.
    let mut w = scene(1);
    w.fields.clear();
    w.cfg.gravity = [0.0; 3];
    w.cfg.sleep_frames = 3;
    w.cfg.dt = w.fixed_dt;
    w.cfg.steps = 1;
    let pose = pose_world(&w, 0, 0.0);
    for _ in 0..10 {
        // A still pose, so the rig can fall asleep.
        let still: Vec<V3> = pose.iter().enumerate().map(|(i, _)| V3::new(i as f32 * 0.15, 0.0, 0.0)).collect();
        w.set_pose(0, &still, None);
        let c = w.cfg;
        let a = args_of(&mut w);
        assert_eq!(unsafe { jgl_step_rigs(&c, &a, 0, 1) }, JGL_OK);
        assert_eq!(unsafe { jgl_resolve_rigs(&c, &a, 0, 1) }, JGL_OK);
    }
    assert!(w.rigs[0].flags & RIG_ASLEEP != 0);
}
