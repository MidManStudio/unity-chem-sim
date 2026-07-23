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
        group.bench_function(format!("n={n}_cutoff10"), |b| {
            b.iter(|| unsafe {
                chemistry_core::chem_step(
                    black_box(atoms.as_mut_ptr()),
                    black_box(n as i32),
                    black_box(0.001_f32),
                    black_box(10.0_f32),
                )
            })
        });
    }
    group.finish();
}

criterion_group!(benches, bench_step);
criterion_main!(benches);
