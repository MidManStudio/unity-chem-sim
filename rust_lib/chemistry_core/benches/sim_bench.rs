use criterion::{black_box, criterion_group, criterion_main, Criterion, Throughput};
use chemistry_core::AtomState;

/// Build a grid of hydrogen atoms spaced 3 Angstroms apart.
fn hydrogen_grid(n: usize) -> Vec<AtomState> {
    let side = (n as f32).cbrt().ceil() as usize;
    (0..n).map(|i| {
        let x = (i % side) as f32 * 3.0;
        let y = ((i / side) % side) as f32 * 3.0;
        let z = (i / (side * side)) as f32 * 3.0;
        AtomState {
            position:      [x, y, z],
            velocity:      [0.0; 3],
            force:         [0.0; 3],
            mass:          1.008,
            radius:        120.0,
            atomic_number: 1,
        }
    }).collect()
}

fn bench_step(c: &mut Criterion) {
    let mut group = c.benchmark_group("chem_step");
    for &n in &[64usize, 256usize, 1024usize] {
        group.throughput(Throughput::Elements(n as u64));
        let mut atoms = hydrogen_grid(n);

        // One context per size, created once and reused for every sample —
        // matches real usage (create in Awake, reuse every frame).
        let ctx = chemistry_core::chem_context_create(10.0);

        group.bench_function(format!("n={n}_cutoff10"), |b| {
            b.iter(|| unsafe {
                chemistry_core::chem_step(
                    black_box(ctx),
                    black_box(atoms.as_mut_ptr()),
                    black_box(n as i32),
                    black_box(0.001_f32),
                    black_box(10.0_f32),
                )
            })
        });

        unsafe { chemistry_core::chem_context_destroy(ctx); }
    }
    group.finish();
}

/// Isolates just the force kernel from the rest of chem_step (position and
/// velocity Verlet updates are cheap O(n) work, not the bottleneck either
/// version is trying to fix) — scalar vs SIMD, head to head, same N values
/// as chem_step above, so this bench's numbers tell us directly whether
/// SIMD batching is actually winning on the math itself, separate from
/// whatever chem_step's end-to-end regression is coming from.
fn bench_lj_kernel(c: &mut Criterion) {
    let mut group = c.benchmark_group("lj_kernel");
    for &n in &[64usize, 256usize, 1024usize] {
        group.throughput(Throughput::Elements(n as u64));

        let mut atoms_scalar = hydrogen_grid(n);
        let mut ctx_scalar = chemistry_core::SimContext::new(10.0);
        group.bench_function(format!("scalar_n={n}"), |b| {
            b.iter(|| {
                chemistry_core::compute_forces_scalar(
                    black_box(&mut ctx_scalar),
                    black_box(&mut atoms_scalar),
                    black_box(10.0_f32),
                )
            })
        });

        let mut atoms_simd = hydrogen_grid(n);
        let mut ctx_simd = chemistry_core::SimContext::new(10.0);
        group.bench_function(format!("simd_n={n}"), |b| {
            b.iter(|| {
                chemistry_core::compute_forces_simd(
                    black_box(&mut ctx_simd),
                    black_box(&mut atoms_simd),
                    black_box(10.0_f32),
                )
            })
        });
    }
    group.finish();
}

criterion_group!(benches, bench_step, bench_lj_kernel);
criterion_main!(benches);
