use criterion::{black_box, criterion_group, criterion_main, BatchSize, Criterion, Throughput};
use chemistry_core::{BondParams, SimContext};

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

/// Round-robin through all 20 currently-known elements (the original H,
/// He, Li, Be, B plus C, N, O, P, S, As, Sb, Zn, Cu, Fe, Sn, Pb, Hg, Ag,
/// Au added alongside the bonding generalization) instead of the
/// original 5 -- a much closer approximation of "mixed reagents in a
/// beaker" than a single-element grid, and exercises element_data
/// lookups across a genuinely wide mass/radius/reactivity spread instead
/// of five light elements clustered at the top of the table. Different
/// element pairs combine to very different sigma/epsilon now (H-H vs.
/// Pb-Au, say), so bonding range and LJ behavior vary a lot more pair to
/// pair than the original 5-element version did.
///
/// Numbers from this bench are **not comparable to bench #11's**
/// chem_step_mixed results -- different element mix, different mass
/// distribution, different bonding topology (unbounded per atom now, not
/// one-per-atom) all change the workload's actual shape, not just its
/// cost. Same "treat every bench run as its own baseline" rule bench #11
/// itself called out.
fn spawn_mixed_element_grid(ctx: &mut SimContext, n: usize) {
    const ELEMENTS: [i32; 20] = [
        1, 2, 3, 4, 5,                          // H, He, Li, Be, B
        6, 7, 8, 15, 16,                        // C, N, O, P, S
        26, 29, 30, 33,                         // Fe, Cu, Zn, As
        47, 50, 51,                             // Ag, Sn, Sb
        79, 80, 82,                             // Au, Hg, Pb
    ];
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

/// Isolates `compute_bonds` from LJ/integration -- the piece that
/// actually changed shape with the unbounded-bonds generalization. Bench
/// #12 (chem_step regressed ~3.5-4x on the hydrogen grid below vs. bench
/// #11, while lj_kernel -- which never touches bonding -- didn't regress
/// at all) is exactly why this group exists: without it, "chem_step got
/// slower" and "compute_bonds got slower" were the same unverified claim,
/// the same gap lj_kernel already closed for the force math back when
/// SIMD was benched against scalar. This closes it for bonding.
///
/// Two states, because compute_bonds' own cost isn't one number anymore:
///
/// - **`cold`**: fresh grid, zero existing bonds. Almost pure Pass 2 --
///   every atom runs a full neighbor search; Pass 1 has nothing to do yet
///   since nothing's bonded.
/// - **`warm`**: same grid, pre-stepped until bonding saturates first (up
///   to 6 neighbors/atom on this grid's geometry, since a bond is
///   unbounded per atom now -- see `compute_bonds` docs). Pass 1's cost
///   scales with total bonds *held*, not bonds *formed this call*, so
///   this is the steady-state number that actually dominates a
///   long-running sim -- and it's *also* still paying full Pass 2 search
///   cost on every already-saturated atom, every single call, for zero
///   new bonds every time. That residual search is a known, not-yet-
///   addressed cost, not an oversight in this bench -- worth an
///   early-exit heuristic (e.g. skip the search once an atom's already
///   at some per-element bond cap) if `warm` ever needs to come down, but
///   that's a real design decision (what should that cap even be,
///   generically, without hardcoding real valence chemistry per element)
///   rather than something to sneak in as a bench-driven "optimization."
///
/// Deliberately uses `iter_batched`, not plain `iter` like every other
/// group here: `iter` reuses one `ctx` across every sample, so bonds
/// would keep accumulating sample to sample and `cold` would only be
/// honestly cold on the very first sample criterion runs. `iter_batched`'s
/// setup closure runs fresh before every timed sample instead, and its
/// input is moved into (and back out of) the timed routine specifically
/// so `ctx`'s own `Drop` -- deallocating every atom's `Vec<BondInfo>`,
/// not free once bonds are dense -- happens *outside* the timed region,
/// not counted as part of `compute_bonds`' own cost.
fn bench_bond_kernel(c: &mut Criterion) {
    let mut group = c.benchmark_group("bond_kernel");
    let params = BondParams::default();

    for &n in &[64usize, 256usize, 1024usize] {
        group.throughput(Throughput::Elements(n as u64));

                group.bench_function(format!("cold_n={n}"), |b| {
            b.iter_batched(
                || {
                    let mut ctx = SimContext::new(10.0);
                    spawn_hydrogen_grid(&mut ctx, n);
                    
                    // ADD THIS LINE: Run one force pass to populate ctx.positions 
                    // and build the spatial hash grid before the timer starts.
                    chemistry_core::compute_forces_scalar(&mut ctx, 10.0);
                    
                    ctx
                },
                |mut ctx| {
                    chemistry_core::compute_bonds(black_box(&mut ctx), black_box(&params));
                    ctx
                },
                BatchSize::SmallInput,
            )
        });


        // Setup does real work here (spawn + up to 8 rounds of forces +
        // bonds to reach saturation), unlike `cold`'s setup above --
        // LargeInput tells criterion not to over-batch it.
        group.bench_function(format!("warm_n={n}"), |b| {
            b.iter_batched(
                || {
                    let mut ctx = SimContext::new(10.0);
                    spawn_hydrogen_grid(&mut ctx, n);
                    // One new edge per atom per compute_bonds call (see
                    // its own docs) -- 8 rounds comfortably saturates
                    // this grid's 6-neighbor-per-atom geometric max.
                    for _ in 0..8 {
                        chemistry_core::compute_forces_scalar(&mut ctx, 10.0);
                        chemistry_core::compute_bonds(&mut ctx, &params);
                    }
                    ctx
                },
                |mut ctx| {
                    chemistry_core::compute_bonds(black_box(&mut ctx), black_box(&params));
                    ctx
                },
                BatchSize::LargeInput,
            )
        });
    }
    group.finish();
}

criterion_group!(benches, bench_step, bench_lj_kernel, bench_mixed_elements, bench_bond_kernel);
criterion_main!(benches);
