// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "benches/scene_bench.rs"
// ============================================================================
//! The comparison suite. Run with `cargo bench -p jiggle_core --bench scene_bench`.
//!
//! It runs a fixed table of scenes at 1 to N threads and prints one table plus one `CSV,` line per row. The
//! same scene table, the same geometry and the same motion are built by the Unity lab
//! (`Editor/Lab/JiggleBenchScenes.cs` and `JiggleLabRunner.cs`), which runs them on the Burst, native (this crate) and managed backends on
//! one machine. That run is the like for like comparison, because it uses one machine and one build of this
//! crate. A run of this file on its own gives the scaling over threads and a number to watch for regressions.
//!
//! Each frame has three phases: the pose write (what the host does), the step and the resolve (what the
//! backends do). A worker owns a window of whole rigs and does all three for them, with a barrier between
//! frames, the way a job system splits a frame. The columns `pose`, `step` and `resolve` are CPU microseconds
//! per frame summed over the workers, and `wall` is the time of one whole frame.
//!
//! Environment: `JIGGLE_BENCH_FILTER` keeps the scenes whose name contains the text, `JIGGLE_BENCH_THREADS`
//! is a comma separated list of thread counts (default 1, 2, 4 and all cores, up to the cores available) and
//! `JIGGLE_BENCH_SCALE` multiplies every frame count (for example 0.2 for a quick run).
#![allow(unsafe_code)] // Only the call overhead section uses the C interface.

use std::sync::Barrier;
use std::time::Instant;

use jiggle_core::ffi::{jgl_step_rigs, SolverArgs};
use jiggle_core::math::{Q, V3};
use jiggle_core::params::AuthoringParams;
use jiggle_core::solver::{resolve_range, step_range, Buffers};
use jiggle_core::types::*;
use jiggle_core::world::World;

/// One scene of the table. The table must stay equal to `JiggleBenchScenes.cs` in the Unity package.
#[derive(Clone, Copy)]
struct Spec {
    name: &'static str,
    rigs: usize,
    bones: usize,
    /// Four colliders per rig: a shared ground plane and a sphere, a capsule and a box of the rig.
    colliders: bool,
    /// A directional field with gusts and a vortex over the whole grid.
    fields: bool,
    /// 0 keeps every link one way. Otherwise every bone has a mass and this is the number of coupling passes.
    passes: u32,
    /// Sleeping on, at 30 frames.
    sleep: bool,
    /// The anchors do not move.
    still: bool,
    frames: usize,
    /// Frames that run before the timing starts.
    warmup: usize,
}

const fn spec(name: &'static str, rigs: usize, bones: usize, frames: usize) -> Spec {
    Spec { name, rigs, bones, colliders: false, fields: false, passes: 0, sleep: false, still: false, frames, warmup: 5 }
}

fn scenes() -> Vec<Spec> {
    let mut v = vec![
        spec("tiny", 1, 8, 20000),
        spec("hair-100", 100, 8, 2000),
        spec("hair-1k", 1000, 8, 300),
        spec("hair-10k", 10000, 8, 40),
        spec("long-64", 100, 64, 200),
        spec("max-126", 20, 126, 200),
    ];
    v.push(Spec { colliders: true, ..spec("collide-1k", 1000, 8, 200) });
    v.push(Spec { fields: true, ..spec("fields-1k", 1000, 8, 300) });
    v.push(Spec { passes: 1, ..spec("coupled-1k-p1", 1000, 8, 300) });
    v.push(Spec { passes: 4, ..spec("coupled-1k-p4", 1000, 8, 300) });
    v.push(Spec { sleep: true, still: true, warmup: 90, ..spec("asleep-1k", 1000, 8, 300) });
    v.push(Spec { colliders: true, fields: true, passes: 3, sleep: true, ..spec("full-1k", 1000, 8, 200) });
    v
}

// The anchors sit on a grid of 100 by N, half a meter apart. The chain hangs straight down, a tenth of a meter per bone.
fn base_of(r: usize) -> V3 {
    V3::new((r % 100) as f32 * 0.5, 0.0, (r / 100) as f32 * 0.5)
}

fn rest_point(r: usize, i: usize) -> V3 {
    base_of(r) + V3::new(0.0, -(i as f32) * 0.1, 0.0)
}

// The whole chain moves by this much in frame f. It differs per rig so no two rigs do the same work.
fn sway(f: usize, r: usize) -> V3 {
    let ff = f as f32;
    let rr = r as f32;
    V3::new((ff * 0.5 + rr * 0.37).sin() * 0.05, 0.0, (ff * 0.35 + rr * 0.11).cos() * 0.03)
}

fn build_world(s: &Spec) -> World {
    let mut w = World::new();
    w.cfg.coupling_passes = if s.passes > 0 { s.passes } else { 1 };
    if s.sleep {
        w.cfg.sleep_frames = 30;
    }
    if s.colliders {
        // The shared ground plane is collider 0, the rest follow in groups of three per rig.
        w.colliders.push(Collider {
            kind: COLLIDER_PLANE,
            center: [0.0, -0.7, 0.0, 0.0],
            axis: [0.0, 1.0, 0.0, 0.0],
            ..Collider::default()
        });
    }
    for r in 0..s.rigs {
        let rest: Vec<V3> = (0..=s.bones).map(|i| rest_point(r, i)).collect();
        let params = vec![
            AuthoringParams {
                mass: if s.passes > 0 { 1.0 } else { 0.0 },
                collide: s.colliders,
                segment_collision: s.colliders,
                collision_radius: 0.04,
                ..AuthoringParams::default()
            };
            s.bones
        ];
        let rig = w.add_chain_with_tip(&rest, &params, 1.0);
        if s.colliders {
            let b = base_of(r);
            let first = w.colliders.len() as u32;
            w.colliders.push(Collider {
                kind: COLLIDER_SPHERE,
                radius: 0.1,
                center: [b.x, b.y - 0.25, b.z + 0.06, 0.0],
                ..Collider::default()
            });
            w.colliders.push(Collider {
                kind: COLLIDER_CAPSULE,
                radius: 0.04,
                half_height: 0.2,
                center: [b.x + 0.1, b.y - 0.3, b.z, 0.0],
                axis: [0.0, 1.0, 0.0, 0.0],
                ..Collider::default()
            });
            w.colliders.push(Collider {
                kind: COLLIDER_BOX,
                center: [b.x - 0.08, b.y - 0.5, b.z, 0.0],
                extents: [0.03, 0.15, 0.1, 0.0],
                rot: [0.0, 0.0, 0.0, 1.0],
                ..Collider::default()
            });
            w.set_collider_list(rig, &[0, first, first + 1, first + 2]);
        }
    }
    if s.fields {
        w.fields.push(Field {
            kind: FIELD_DIRECTIONAL,
            mode: FIELD_ACCELERATION,
            dir: [1.0, 0.0, 0.3, 0.7],
            strength: 4.0,
            turbulence: 0.8,
            frequency: 1.3,
            ..Field::default()
        });
        w.fields.push(Field {
            kind: FIELD_VORTEX,
            mode: FIELD_ACCELERATION,
            center: [25.0, 0.0, 2.5, 0.0],
            dir: [0.0, 1.0, 0.0, 0.0],
            radius: 30.0,
            strength: 6.0,
            power: 1,
            ..Field::default()
        });
    }
    w
}

/// A window of whole rigs: the slices a worker owns, with the global index of the first rig and particle.
struct Window<'a> {
    first_rig: usize,
    p0: usize,
    rigs: &'a mut [Rig],
    defs: &'a [ParticleDef],
    state: &'a mut [ParticleState],
    pose_prev: &'a mut [[f32; 4]],
    pose_curr: &'a mut [[f32; 4]],
    pose_rot: &'a mut [[f32; 4]],
    out: &'a mut [ParticleOut],
}

fn split_windows(w: &mut World, threads: usize) -> Vec<Window<'_>> {
    let total = w.rigs.len();
    let per = total.div_ceil(threads);
    let mut rig_rest: &mut [Rig] = &mut w.rigs[..];
    let mut st: &mut [ParticleState] = &mut w.state[..];
    let mut pp: &mut [[f32; 4]] = &mut w.pose_prev[..];
    let mut pc: &mut [[f32; 4]] = &mut w.pose_curr[..];
    let mut pr: &mut [[f32; 4]] = &mut w.pose_rot[..];
    let mut out: &mut [ParticleOut] = &mut w.out[..];
    let defs: &[ParticleDef] = &w.defs[..];
    let mut windows = Vec::new();
    let mut first_rig = 0usize;
    let mut p_base = 0usize;
    while !rig_rest.is_empty() {
        let take = per.min(rig_rest.len());
        let (rw, rr) = std::mem::take(&mut rig_rest).split_at_mut(take);
        rig_rest = rr;
        let last = &rw[take - 1];
        let p_end = last.particle_offset as usize + last.particle_count as usize;
        assert_eq!(rw[0].particle_offset as usize, p_base, "the rigs of a window must be gapless");
        let len = p_end - p_base;
        let (stw, st_rest) = std::mem::take(&mut st).split_at_mut(len);
        st = st_rest;
        let (ppw, pp_rest) = std::mem::take(&mut pp).split_at_mut(len);
        pp = pp_rest;
        let (pcw, pc_rest) = std::mem::take(&mut pc).split_at_mut(len);
        pc = pc_rest;
        let (prw, pr_rest) = std::mem::take(&mut pr).split_at_mut(len);
        pr = pr_rest;
        let (ow, o_rest) = std::mem::take(&mut out).split_at_mut(len);
        out = o_rest;
        windows.push(Window {
            first_rig,
            p0: p_base,
            rigs: rw,
            defs: &defs[p_base..p_end],
            state: stw,
            pose_prev: ppw,
            pose_curr: pcw,
            pose_rot: prw,
            out: ow,
        });
        first_rig += take;
        p_base = p_end;
    }
    windows
}

// The same thing `World::set_pose` does, for one rig of a window, with the pose of frame f.
fn write_pose(s: &Spec, f: usize, local: usize, w: &mut Window) {
    let g = w.first_rig + local;
    let off = w.rigs[local].particle_offset as usize - w.p0;
    let n = w.rigs[local].particle_count as usize;
    let bones = n - 2;
    let sw = if s.still { V3::new(0.0, 0.0, 0.0) } else { sway(f, g) };
    let anchor = base_of(g) + sw;
    let mut world = [V3::new(0.0, 0.0, 0.0); MAX_PARTICLES];
    for (i, p) in world.iter_mut().enumerate().take(bones + 1) {
        *p = rest_point(g, i) + sw;
    }
    // The virtual end continues the last bone, one bone length away.
    world[bones + 1] = world[bones] + (world[bones] - world[bones - 1]) * 1.0;
    for (i, p) in world.iter().enumerate().take(n) {
        w.pose_prev[off + i] = w.pose_curr[off + i];
        let rel = *p - anchor;
        rel.write4(&mut w.pose_curr[off + i]);
        w.pose_curr[off + i][3] = 0.0;
        w.pose_rot[off + i] = Q::IDENTITY.to4();
    }
    let old = V3::from3(&w.rigs[local].anchor_world);
    (anchor - old).write3(&mut w.rigs[local].anchor_delta);
    anchor.write3(&mut w.rigs[local].anchor_world);
}

#[derive(Default, Clone, Copy)]
struct Phases {
    pose_ns: u128,
    step_ns: u128,
    resolve_ns: u128,
    wall_ns: u128,
    checksum: u64,
    asleep: usize,
    finite: bool,
}

#[allow(clippy::too_many_arguments)]
fn worker(
    s: &Spec,
    cfg: StepConfig,
    mut w: Window,
    colliders: &[Collider],
    refs: &[u32],
    fields: &[Field],
    barrier: &Barrier,
    frames: usize,
) -> Phases {
    let count = w.rigs.len();
    let mut ph = Phases::default();
    let mut wall_start = Instant::now();
    for f in 0..(s.warmup + frames) {
        barrier.wait();
        if f == s.warmup {
            wall_start = Instant::now();
        }
        let timed = f >= s.warmup;
        let t0 = Instant::now();
        for local in 0..count {
            write_pose(s, f, local, &mut w);
        }
        let t1 = Instant::now();
        let mut cfg = cfg;
        cfg.time = f as f32 / 60.0;
        {
            let mut b = Buffers {
                particle_base: w.p0,
                rigs: &mut *w.rigs,
                defs: w.defs,
                state: &mut *w.state,
                pose_prev: &*w.pose_prev,
                pose_curr: &*w.pose_curr,
                pose_rot: &*w.pose_rot,
                out: &mut *w.out,
                colliders,
                refs,
                fields,
            };
            step_range(&cfg, &mut b, 0, count).unwrap();
            let t2 = Instant::now();
            resolve_range(&cfg, &mut b, 0, count).unwrap();
            let t3 = Instant::now();
            if timed {
                ph.pose_ns += (t1 - t0).as_nanos();
                ph.step_ns += (t2 - t1).as_nanos();
                ph.resolve_ns += (t3 - t2).as_nanos();
            }
        }
    }
    barrier.wait();
    ph.wall_ns = wall_start.elapsed().as_nanos();
    ph.finite = true;
    for (i, o) in w.out.iter().enumerate() {
        let gi = (w.p0 + i) as u64;
        for v in o.pos.iter().chain(o.rot.iter()) {
            ph.finite &= v.is_finite();
            // Summed, so the result does not depend on how the rigs were split into windows.
            ph.checksum = ph.checksum.wrapping_add((v.to_bits() as u64 ^ gi.wrapping_mul(0x9e37_79b9_7f4a_7c15)).wrapping_mul(0x0100_0000_01b3));
        }
    }
    ph.asleep = w.rigs.iter().filter(|r| r.flags & RIG_ASLEEP != 0).count();
    ph
}

struct Row {
    wall_us: f64,
    pose_us: f64,
    step_us: f64,
    resolve_us: f64,
    checksum: u64,
    asleep: usize,
    finite: bool,
}

fn run_scene(s: &Spec, threads: usize, frames: usize) -> Row {
    let mut world = build_world(s);
    let mut cfg = world.cfg;
    cfg.dt = world.fixed_dt;
    cfg.steps = 1;
    cfg.alpha = 1.0;
    let World { colliders, refs, fields, .. } = &world;
    let (colliders, refs, fields) = (colliders.clone(), refs.clone(), fields.clone());
    let windows = split_windows(&mut world, threads);
    let n = windows.len();
    let barrier = Barrier::new(n);
    let results: Vec<Phases> = std::thread::scope(|scope| {
        let handles: Vec<_> = windows
            .into_iter()
            .map(|w| {
                let (c, r, f, b) = (&colliders[..], &refs[..], &fields[..], &barrier);
                scope.spawn(move || worker(s, cfg, w, c, r, f, b, frames))
            })
            .collect();
        handles.into_iter().map(|h| h.join().unwrap()).collect()
    });
    let fr = frames as f64;
    let sum = |f: fn(&Phases) -> u128| results.iter().map(f).sum::<u128>() as f64 / fr / 1000.0;
    Row {
        wall_us: results[0].wall_ns as f64 / fr / 1000.0,
        pose_us: sum(|p| p.pose_ns),
        step_us: sum(|p| p.step_ns),
        resolve_us: sum(|p| p.resolve_ns),
        checksum: results.iter().fold(0u64, |a, p| a.wrapping_add(p.checksum)),
        asleep: results.iter().map(|p| p.asleep).sum(),
        finite: results.iter().all(|p| p.finite),
    }
}

fn thread_counts() -> Vec<usize> {
    let cores = std::thread::available_parallelism().map(|n| n.get()).unwrap_or(1);
    if let Ok(text) = std::env::var("JIGGLE_BENCH_THREADS") {
        let v: Vec<usize> = text.split(',').filter_map(|t| t.trim().parse().ok()).filter(|&t| t >= 1).collect();
        if !v.is_empty() {
            return v;
        }
    }
    let mut v: Vec<usize> = [1, 2, 4].iter().copied().filter(|&t| t <= cores).collect();
    if !v.contains(&cores) {
        v.push(cores);
    }
    v
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

// What a call through the C interface costs, which is what the native backend pays for every batch it sends.
fn call_overhead(scale: f64) {
    let s = scenes().into_iter().find(|s| s.name == "hair-1k").unwrap();
    let mut world = build_world(&s);
    {
        let mut windows = split_windows(&mut world, 1);
        for local in 0..s.rigs {
            write_pose(&s, 0, local, &mut windows[0]);
        }
    }
    let mut cfg = world.cfg;
    cfg.steps = 1;
    cfg.alpha = 1.0;
    let args = args_of(&mut world);
    let frames = ((200.0 * scale) as usize).max(5);
    println!();
    println!("Call overhead of the C interface, {} rigs x {} bones, step only, {} frames", s.rigs, s.bones, frames);
    println!("{:>28} {:>12} {:>12}", "calls per frame", "us per frame", "us per call");
    let mut base_us = 0.0f64;
    for &batch in &[s.rigs, 250, 64, 16, 1] {
        let calls = s.rigs.div_ceil(batch);
        let start = Instant::now();
        for _ in 0..frames {
            let mut first = 0usize;
            while first < s.rigs {
                let n = batch.min(s.rigs - first);
                // SAFETY: cfg and args live for the whole call, and the arrays they point to are not touched meanwhile.
                let code = unsafe { jgl_step_rigs(&cfg, &args, first as u32, n as u32) };
                assert_eq!(code, 0);
                first += n;
            }
        }
        let us = start.elapsed().as_secs_f64() * 1e6 / frames as f64;
        if batch == s.rigs {
            base_us = us;
        }
        println!(
            "{:>28} {:>12.1} {:>12.3}",
            format!("{} (batches of {})", calls, batch),
            us,
            if calls > 1 { (us - base_us) / (calls as f64 - 1.0) } else { 0.0 }
        );
    }
    println!("us per call is the extra time over the single call, divided by the extra calls.");
}

fn main() {
    let filter = std::env::var("JIGGLE_BENCH_FILTER").unwrap_or_default();
    let scale: f64 = std::env::var("JIGGLE_BENCH_SCALE").ok().and_then(|t| t.parse().ok()).unwrap_or(1.0);
    let threads = thread_counts();
    println!("jiggle_core scene bench, {} cores, threads {:?}, frame scale {}", std::thread::available_parallelism().map(|n| n.get()).unwrap_or(1), threads, scale);
    println!("Times are microseconds per frame. pose, step and resolve are CPU time summed over the workers, wall is one frame.");
    println!(
        "{:<15} {:>3} {:>6} {:>5} {:>8} {:>9} {:>9} {:>9} {:>9} {:>9} {:>9} {:>6}",
        "scene", "thr", "rigs", "bones", "frames", "wall", "pose", "step", "resolve", "ns/part", "x thr1", "asleep"
    );
    let mut csv = Vec::new();
    for s in scenes().into_iter().filter(|s| s.name.contains(&filter)) {
        let frames = ((s.frames as f64 * scale) as usize).max(3);
        let particles = s.rigs * (s.bones + 2);
        let mut first_wall = 0.0f64;
        let mut first_sum = None;
        // A scene with fewer rigs than threads runs on as many threads as it has rigs.
        let mut counts: Vec<usize> = threads.iter().map(|&t| t.min(s.rigs)).collect();
        counts.dedup();
        for t in counts {
            let row = run_scene(&s, t, frames);
            if first_sum.is_none() {
                first_wall = row.wall_us;
                first_sum = Some(row.checksum);
            }
            let same = first_sum == Some(row.checksum);
            // Cost of the kernel per particle and step, over the CPU time of the step and the resolve.
            let ns_part = (row.step_us + row.resolve_us) * 1000.0 / particles as f64;
            println!(
                "{:<15} {:>3} {:>6} {:>5} {:>8} {:>9.1} {:>9.1} {:>9.1} {:>9.1} {:>9.1} {:>9.2} {:>6}{}{}",
                s.name,
                t,
                s.rigs,
                s.bones,
                frames,
                row.wall_us,
                row.pose_us,
                row.step_us,
                row.resolve_us,
                ns_part,
                first_wall / row.wall_us,
                row.asleep,
                if row.finite { "" } else { "  NOT FINITE" },
                if same { "" } else { "  RESULT DIFFERS FROM 1 THREAD" }
            );
            csv.push(format!(
                "CSV,v1,rust,{},{},{},{},{},{},{:.2},{:.2},{:.2},{:.2},{:.2},{:016x}",
                s.name, t, s.rigs, s.bones, particles, frames, row.wall_us, row.pose_us, row.step_us, row.resolve_us, ns_part, row.checksum
            ));
        }
    }
    println!();
    println!("CSV,v1,backend,scene,threads,rigs,bones,particles,frames,wall_us,pose_us,step_us,resolve_us,ns_per_particle,checksum");
    for l in &csv {
        println!("{}", l);
    }
    if filter.is_empty() || "hair-1k".contains(&filter) {
        call_overhead(scale);
    }
}
