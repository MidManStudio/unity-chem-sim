// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "benches/step_bench.rs"
// ============================================================================
//! A plain timing harness for the scalar kernel, no external crates. Run with
//! `cargo bench -p jiggle_core --bench step_bench`. It builds many chains,
//! steps them for a number of frames and prints microseconds per rig step.

use std::time::Instant;

use jiggle_core::math::V3;
use jiggle_core::params::AuthoringParams;
use jiggle_core::world::World;

fn build(rigs: usize, bones: usize) -> (World, Vec<Vec<V3>>) {
    let mut world = World::new();
    let mut poses = Vec::new();
    for r in 0..rigs {
        let base = V3::new((r % 50) as f32 * 0.7, 0.0, (r / 50) as f32 * 0.7);
        let rest: Vec<V3> = (0..=bones).map(|i| base + V3::new(0.0, -(i as f32) * 0.1, 0.0)).collect();
        let params = vec![AuthoringParams::default(); bones];
        world.add_chain(&rest, &params);
        poses.push(rest);
    }
    (world, poses)
}

fn main() {
    for &(rigs, bones) in &[(100usize, 6usize), (600, 6), (600, 12)] {
        let (mut world, poses) = build(rigs, bones);
        let frames = 200;
        let start = Instant::now();
        for f in 0..frames {
            let sway = (f as f32 * 0.1).sin() * 0.05;
            for (r, rest) in poses.iter().enumerate() {
                let moved: Vec<V3> = rest.iter().map(|p| *p + V3::new(sway, 0.0, 0.0)).collect();
                world.set_pose(r, &moved, None);
            }
            world.step_fixed(1).unwrap();
        }
        let total = start.elapsed().as_secs_f64();
        println!(
            "{:>4} rigs x {:>2} bones, {} frames: {:.1} us per rig step (includes pose upload)",
            rigs,
            bones,
            frames,
            total * 1e6 / (frames as f64 * rigs as f64)
        );
    }
}
