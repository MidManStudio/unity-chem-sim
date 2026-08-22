use criterion::{black_box, criterion_group, criterion_main, Criterion, Throughput};
use chemistry_core::SimContext;

/// Spawn a grid of hydrogen atoms spaced 3 Angstroms apart into `ctx`.
///
/// Worth knowing: 3.0A happens to sit close to H-H's own LJ equilibrium
/// distance (r_min = sigma * 2^(1/6) ~= 3.29A for sigma=2.928A), which is
/// also within `BondParams::default()`'s bonding range (1.15 * r_min ~=
/// 3.78A). So as of the bonding system landing, this grid's nearest
/// neighbors will actually pair off and bond during chem_step/lj_kernel
/// below -- not a bug, this is what real H atoms placed near their
/// natural bond distance are supposed to do. It does mean chem_step's
/// numbers now include real bonding cost as a side effect, which
/// wasn't true in earlier bench runs -- worth remembering when comparing
/// across the bonding-system commit.
fn spawn_hydrogen_grid(ctx: &mut SimContext, n: usize) {
    let side = (n as f32).cbrt().ceil() as usize;
    for i in 0..n {
        let x = (i % side) as f32 * 3.0;
        let y = ((i / side) % side) as f32 * 3.0;
        let z = (i / (side * side)) as f32 * 3.0;
        chemistry_core::spawn_atom(ctx, 1, [x, y, z]);
    }
}

/// Round-robin through all 5 currently-known elements (H, He, Li, Be, B)
/// instead of pure hydrogen -- a much closer approximation of "mixed
/// reagents in a beaker" than a single-element grid, and exercises
/// element_data lookups across different atomic numbers (different rows
/// of the real table) instead of always hitting the same one. Different
/// element pairs combine to different sigma/epsilon, so bonding range and
/// LJ behavior genuinely varies pair to pair here, same as it would in
/// an actual mixed reaction.
fn spawn_mixed_element_grid(ctx: &mut SimContext, n: usize) {
    const ELEMENTS: [i32; 5] = [1, 2, 3, 4, 5]; // H, He, Li, Be, B
    let side = (n as f32).cbrt().ceil() as usize;
    for i in 0..n {
        let x = (i % side) as f32 * 3.0;
        let y = ((i / side) % side) as f32 * 3.0;
        let z = (i / (side * side)) as f32 * 3.0;
        let z_num = ELEMENTS[i % ELEMENTS.len()];
        chemistry_core::spawn_atom(ctx, z_num, [x, y, z]);
    }
}

fn bench_step(c: &mut Criterion) {
    let mut group = c.benchmark_group("chem_step");
    for &n in &[64usize, 256usize, 1024usize] {
        group.throughput(Throughput::Elements(n as u64));

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

/// Isolates just the force kernel from the rest of chem_step (position/
/// velocity Verlet updates and bond formation/breaking are separate
/// costs, not what either kernel is trying to optimize) -- scalar vs
/// SIMD, head to head, same N values as chem_step above.
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

/// A more realistic "mixed reagents" scenario: 5 different elements
/// instead of one, full chem_step (LJ + bonding + integration together --
/// this is what an actual gameplay tick looks like, not an isolated
/// kernel). This is the number to look at for "does this feel like real
/// usage," not chem_step/lj_kernel above, which both intentionally
/// isolate one thing at a time.
fn bench_mixed_elements(c: &mut Criterion) {
    let mut group = c.benchmark_group("chem_step_mixed");
    for &n in &[64usize, 256usize, 1024usize] {
        group.throughput(Throughput::Elements(n as u64));

        let mut ctx = SimContext::new(10.0);
        spawn_mixed_element_grid(&mut ctx, n);

        group.bench_function(format!("n={n}_cutoff10"), |b| {
            b.iter(|| {
                chemistry_core::step(black_box(&mut ctx), black_box(0.001_f32), black_box(10.0_f32))
            })
        });
    }
    group.finish();
}

criterion_group!(benches, bench_step, bench_lj_kernel, bench_mixed_elements);
criterion_main!(benches);
