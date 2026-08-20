use criterion::{black_box, criterion_group, criterion_main, Criterion, Throughput};
use chemistry_core::SimContext;

/// Spawn a grid of hydrogen atoms spaced 3 Angstroms apart into `ctx`.
fn spawn_hydrogen_grid(ctx: &mut SimContext, n: usize) {
    let side = (n as f32).cbrt().ceil() as usize;
    for i in 0..n {
        let x = (i % side) as f32 * 3.0;
        let y = ((i / side) % side) as f32 * 3.0;
        let z = (i / (side * side)) as f32 * 3.0;
        chemistry_core::spawn_atom(ctx, 1, [x, y, z]);
    }
}

fn bench_step(c: &mut Criterion) {
    let mut group = c.benchmark_group("chem_step");
    for &n in &[64usize, 256usize, 1024usize] {
        group.throughput(Throughput::Elements(n as u64));

        // One context per size, created once, atoms spawned once, reused
        // for every sample — matches real usage (spawn in Awake, step
        // every frame).
        let mut ctx = SimContext::new(10.0);
        spawn_hydrogen_grid(&mut ctx, n);

        group.bench_function(format!("n={n}_cutoff10"), |b| {
            b.iter(|| {
                chemistry_core::step(black_box(&mut ctx), black_box(0.001_f32), black_box(10.0_f32))
            })
        });
    }
    group.finish();
}

/// Isolates just the force kernel from the rest of chem_step (position and
/// velocity Verlet updates are cheap O(n) work, not the bottleneck either
/// version is trying to fix) — scalar vs SIMD, head to head, same N values
/// as chem_step above.
fn bench_lj_kernel(c: &mut Criterion) {
    let mut group = c.benchmark_group("lj_kernel");
    for &n in &[64usize, 256usize, 1024usize] {
        group.throughput(Throughput::Elements(n as u64));

        let mut ctx_scalar = SimContext::new(10.0);
        spawn_hydrogen_grid(&mut ctx_scalar, n);
        group.bench_function(format!("scalar_n={n}"), |b| {
            b.iter(|| {
                chemistry_core::compute_forces_scalar(black_box(&mut ctx_scalar), black_box(10.0_f32))
            })
        });

        let mut ctx_simd = SimContext::new(10.0);
        spawn_hydrogen_grid(&mut ctx_simd, n);
        group.bench_function(format!("simd_n={n}"), |b| {
            b.iter(|| {
                chemistry_core::compute_forces_simd(black_box(&mut ctx_simd), black_box(10.0_f32))
            })
        });
    }
    group.finish();
}

criterion_group!(benches, bench_step, bench_lj_kernel);
criterion_main!(benches);
