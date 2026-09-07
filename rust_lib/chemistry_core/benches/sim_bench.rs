use criterion::{black_box, criterion_group, criterion_main, BatchSize, Criterion, Throughput};
use chemistry_core::{AngleParams, BondParams, SimContext};

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
    // compute_bonds gained this third parameter alongside angular bonding
    // (forms one angle triple per pre-existing neighbor when a new bond
    // edge lands) -- bound once here and reused below, same as `params`.
    let angle_params = AngleParams::default();

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
                    chemistry_core::compute_bonds(black_box(&mut ctx), black_box(&params), black_box(&angle_params));
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
                        chemistry_core::compute_bonds(&mut ctx, &params, &angle_params);
                    }
                    ctx
                },
                |mut ctx| {
                    chemistry_core::compute_bonds(black_box(&mut ctx), black_box(&params), black_box(&angle_params));
                    ctx
                },
                BatchSize::LargeInput,
            )
        });
    }
    group.finish();
}

/// Isolates `compute_angles` -- the harmonic angle-bend force pass added
/// alongside angular bonding (commit 60f1d66) -- the same way
/// `bench_lj_kernel` isolates the LJ pass and `bench_bond_kernel` isolates
/// `compute_bonds`. It landed without its own isolated bench until now;
/// it was only ever exercised indirectly through `bench_step`/
/// `bench_mixed_elements`, which also pay for LJ and bonding every call --
/// the exact gap `bench_bond_kernel`'s own doc comment already called out
/// for `compute_bonds`, back when *that* only had `chem_step`'s combined
/// number to go on. A change to angle-force cost specifically had nothing
/// here that could have caught it on its own.
///
/// Two states, same reasoning `bench_bond_kernel` already established:
///
/// - **`cold`**: fresh grid, one force pass, zero bonds -- and therefore
///   zero angle triples, since triples only ever form as a side effect of
///   `form_bond` (`compute_angles`'s own doc comment: "doesn't form or
///   remove any triples ... driven entirely by bond-topology changes").
///   Expected to cost close to nothing; this row exists to make that
///   explicit and comparable across commits, not because it's expected to
///   be interesting on its own.
/// - **`warm`**: same 8-round saturation setup as `bench_bond_kernel`'s
///   `warm` state, so real angle triples exist by the time
///   `compute_angles` is what's actually timed -- the steady-state number
///   for a long-running sim, same role `bond_kernel`'s own `warm` row
///   plays for bonding.
///
/// `iter_batched` for the same reason `bench_bond_kernel` uses it over
/// plain `iter`: setup has to be genuinely fresh per sample so `cold`
/// stays honestly cold and `warm`'s saturation doesn't compound sample to
/// sample the way a shared, reused `ctx` would.
fn bench_angle_kernel(c: &mut Criterion) {
    let mut group = c.benchmark_group("angle_kernel");
    let bond_params = BondParams::default();
    let angle_params = AngleParams::default();

    for &n in &[64usize, 256usize, 1024usize] {
        group.throughput(Throughput::Elements(n as u64));

        group.bench_function(format!("cold_n={n}"), |b| {
            b.iter_batched(
                || {
                    let mut ctx = SimContext::new(10.0);
                    spawn_hydrogen_grid(&mut ctx, n);
                    chemistry_core::compute_forces_scalar(&mut ctx, 10.0);
                    ctx
                },
                |mut ctx| {
                    chemistry_core::compute_angles(black_box(&mut ctx), black_box(&angle_params));
                    ctx
                },
                BatchSize::SmallInput,
            )
        });

        // Same 8-round saturation setup as bench_bond_kernel's warm state
        // above -- deliberately not re-deriving a different saturation
        // point, so this row's atom/angle topology is directly comparable
        // to that one's, not just similarly named.
        group.bench_function(format!("warm_n={n}"), |b| {
            b.iter_batched(
                || {
                    let mut ctx = SimContext::new(10.0);
                    spawn_hydrogen_grid(&mut ctx, n);
                    for _ in 0..8 {
                        chemistry_core::compute_forces_scalar(&mut ctx, 10.0);
                        chemistry_core::compute_bonds(&mut ctx, &bond_params, &angle_params);
                    }
                    ctx
                },
                |mut ctx| {
                    chemistry_core::compute_angles(black_box(&mut ctx), black_box(&angle_params));
                    ctx
                },
                BatchSize::LargeInput,
            )
        });
    }
    group.finish();
}

criterion_group!(benches, bench_step, bench_lj_kernel, bench_mixed_elements, bench_bond_kernel, bench_angle_kernel);
criterion_main!(benches);
