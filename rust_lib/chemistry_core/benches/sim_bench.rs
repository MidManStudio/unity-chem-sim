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
        // matches real usage (create in Awake, reuse every frame) and now
        // measures steady-state chem_step cost instead of also paying
        // scratch-buffer + spatial-hash allocation on every single
        // iteration. Numbers from this bench are not directly comparable
        // to earlier runs for that reason — this is a methodology change,
        // not (only) an algorithmic one; expect a further drop that isn't
        // "the sim got faster," it's "we stopped benchmarking malloc."
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

criterion_group!(benches, bench_step);
criterion_main!(benches);
