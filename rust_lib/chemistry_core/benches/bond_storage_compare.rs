//! Direct MidVec-vs-Vec comparison for `ctx.bonds`'s storage, isolated
//! from physics entirely.
//!
//! ## Why this is a separate bench target from `sim_bench.rs`
//!
//! `compute_bonds` (the real thing) is inseparable from real position
//! data, the spatial hash, and per-element reactivity -- duplicating all
//! of that just to swap one container type would mean copying ~150 lines
//! of physics that has nothing to do with the actual question (container
//! choice) and would cost identically either way, diluting the one
//! signal actually worth measuring. This harness drives the exact same
//! `SparseSet<GenerationalIndex, C>` + push/retain/iterate operations
//! `push_bond_edge`/`break_one_bond`/`compute_bonds`'s Pass 1 loop
//! perform on the container, generically over `C`, with synthetic
//! (ring-topology) neighbors instead of real 3D proximity. This isn't
//! testing "is bonding correct" (bond_kernel in sim_bench.rs, against
//! real physics, already covers that) -- it's testing "which container
//! is cheaper to hold N atoms' bond lists", nothing else.
//!
//! ## Why cross-run bench comparisons don't work here
//!
//! Two runs of the *identical* commit on this repo's CI (bench #16 vs
//! #17, 2026-08-27/28) showed every benchmark -- including lj_kernel,
//! which touches nothing bond-related at all -- move ~25-38% in the same
//! direction between runs. That's GitHub's shared runner pool handing
//! back a different host machine, not a real signal. A same-process,
//! same-run, same-host comparison is the only way to get a number worth
//! trusting here -- which is what this file is for: MidVec and Vec are
//! benched back-to-back, in the same `cargo bench` invocation, so
//! whatever the runner's mood is that day, it's identical for both
//! sides.
//!
//! ## Scale
//!
//! n = 1,000 (sanity-check magnitude against sim_bench.rs's own
//! bond_kernel numbers), 100,000 (the stated long-term entity target),
//! and 1,000,000 (well past it, to see whether either container's cost
//! curve bends). Tractable at these sizes specifically *because* there's
//! no spatial hash or O(n) neighbor search involved -- just container
//! operations, which is the whole point of not reusing real compute_bonds
//! for this.
//!
//! k = 4 (realistic ceiling per grand-theft-grimoire-gameplay-reference.md
//! #1.3 -- nothing there exceeds 4 bonds on one atom) and k = 8 (past
//! MAX_INLINE_BONDS=6, so MidVec is guaranteed to have spilled to the
//! heap for every atom by the time these bench functions run) -- brackets
//! the design point from both sides: the common case, and the stress
//! case chemistry_core's own module docs already flagged as atypical but
//! possible (an atom deep in a nucleating ore/salt lattice).
//!
//! Large-n groups (100k, 1M) use a reduced sample count (`sample_size`
//! floor of 10, criterion's minimum) -- the default ~100 samples would
//! mean rebuilding an 8M-edge fixture ~100 times per sample point under
//! `iter_batched`, which is real CI minutes for very little extra
//! statistical value at this scale.

use criterion::{black_box, criterion_group, criterion_main, BatchSize, Criterion, Throughput};
use chemistry_core::BondInfo;
use mid_collections::{GenerationalIndex, GenerationalIndexAllocator, SparseSet};
use mid_math::MidVec;

/// Inline capacity chemistry_core actually ships with -- see
/// `MAX_INLINE_BONDS`'s own doc comment in simulation.rs for the N=6
/// reasoning. Not re-derived here; if that constant ever changes, this
/// bench should be updated to match, not silently compare against a
/// stale assumption.
const MAX_INLINE_BONDS: usize = 6;

/// Mirrors chemistry_core's own (private) `push_bond_edge` and
/// `retain_bond` closely enough to give a fair comparison, without being
/// coupled to their exact private signatures. `Vec<BondInfo>` and
/// `MidVec<BondInfo, N>` both already have every operation needed
/// natively (`push`, `len`, `Deref<[T]>` for iteration) except a
/// consistent `retain` -- `MidVec` doesn't have one, same gap
/// `retain_bond` in simulation.rs already had to cover.
trait BondList: Default {
    fn push_edge(&mut self, info: BondInfo);
    fn retain_edges(&mut self, keep: impl FnMut(&BondInfo) -> bool);
    fn edges(&self) -> &[BondInfo];
}

impl BondList for Vec<BondInfo> {
    fn push_edge(&mut self, info: BondInfo) { self.push(info); }
    fn retain_edges(&mut self, keep: impl FnMut(&BondInfo) -> bool) { self.retain(keep); }
    fn edges(&self) -> &[BondInfo] { self }
}

impl BondList for MidVec<BondInfo, MAX_INLINE_BONDS> {
    fn push_edge(&mut self, info: BondInfo) { self.push(info); }
    fn retain_edges(&mut self, mut keep: impl FnMut(&BondInfo) -> bool) {
        let mut i = 0;
        while i < self.len() {
            if keep(&self[i]) { i += 1; } else { self.remove(i); }
        }
    }
    fn edges(&self) -> &[BondInfo] { self }
}

/// Allocates `n` synthetic atom identities. Setup-only, identical cost
/// for both container types -- deliberately kept out of the timed region
/// in `bench_push` below, so that benchmark measures container-building
/// cost specifically, not identity-allocation cost diluting it.
fn allocate_ids(n: usize) -> Vec<GenerationalIndex> {
    let mut alloc = GenerationalIndexAllocator::with_capacity(n);
    (0..n).map(|_| alloc.allocate()).collect()
}

/// Builds a fresh, empty bond map and gives every one of the `ids.len()`
/// atoms exactly `k` symmetric bonds (k must be even), in a ring
/// topology: atom `i` bonds to `i+1 .. i+k/2` (mod n). Not spatially
/// meaningful -- doesn't need to be, this harness never reads a position
/// -- just a cheap, deterministic way to hand every atom the same bond
/// count without a real candidate search. Mirrors `push_bond_edge`'s
/// symmetric-insertion contract: one call touches both sides' lists.
fn build_saturated<C: BondList>(ids: &[GenerationalIndex], k: usize) -> SparseSet<GenerationalIndex, C> {
    let n = ids.len();
    assert!(k % 2 == 0, "k must be even for the symmetric ring construction to land exactly k per atom");
    assert!(n > k, "ring topology needs more atoms than bonds-per-atom or it wraps onto itself");

    let mut bonds: SparseSet<GenerationalIndex, C> = SparseSet::with_capacity(n);
    let half = k / 2;
    for i in 0..n {
        for d in 1..=half {
            let j = (i + d) % n;
            let (a, b) = (ids[i], ids[j]);
            let equilibrium_length = 3.29_f32; // representative H-H r_min; value doesn't matter to this harness, only that it's touched

            match bonds.get_mut(a) {
                Some(list) => list.push_edge(BondInfo { partner: b, equilibrium_length }),
                None => { let mut l = C::default(); l.push_edge(BondInfo { partner: b, equilibrium_length }); bonds.insert(a, l); }
            }
            match bonds.get_mut(b) {
                Some(list) => list.push_edge(BondInfo { partner: a, equilibrium_length }),
                None => { let mut l = C::default(); l.push_edge(BondInfo { partner: a, equilibrium_length }); bonds.insert(b, l); }
            }
        }
    }
    bonds
}

/// Times building a fully-saturated bond map from nothing, given
/// already-allocated identities -- the allocation-heavy side of the
/// comparison, isolated from identity-allocation cost (identical either
/// way, so kept out of the timed region rather than diluting the
/// signal). `MidVec`'s hypothesis: for k <= MAX_INLINE_BONDS, zero heap
/// allocations happen here at all; `Vec` always allocates on first push,
/// then again on regrowth past its starting capacity.
fn bench_push<C: BondList>(c: &mut Criterion, group_name: &str) {
    let mut group = c.benchmark_group(group_name);
    for &(n, k) in &[(1_000usize, 4usize), (1_000, 8), (100_000, 4), (100_000, 8), (1_000_000, 4), (1_000_000, 8)] {
        group.throughput(Throughput::Elements(n as u64));
        if n >= 100_000 {
            group.sample_size(10);
        }
        let ids = allocate_ids(n);
        group.bench_function(format!("n={n}_k={k}"), |b| {
            b.iter_batched(
                || (),
                |_| black_box(build_saturated::<C>(black_box(&ids), black_box(k))),
                BatchSize::LargeInput,
            )
        });
    }
    group.finish();
}

/// Times a full sweep over an already-built, already-saturated bond map
/// -- summing every edge's `equilibrium_length` so the compiler can't
/// optimize the traversal away. The cache-pressure side of the
/// comparison: `MidVec<BondInfo,6>` is 80 bytes/entry vs. `Vec<BondInfo>`'s
/// 24 -- this is where that 3.3x weight either shows up as a real cost
/// or doesn't.
fn bench_iterate<C: BondList>(c: &mut Criterion, group_name: &str) {
    let mut group = c.benchmark_group(group_name);
    for &(n, k) in &[(1_000usize, 4usize), (1_000, 8), (100_000, 4), (100_000, 8), (1_000_000, 4), (1_000_000, 8)] {
        group.throughput(Throughput::Elements(n as u64));
        if n >= 100_000 {
            group.sample_size(10);
        }
        let ids = allocate_ids(n);
        group.bench_function(format!("n={n}_k={k}"), |b| {
            b.iter_batched(
                || build_saturated::<C>(&ids, k),
                |bonds| {
                    let mut sum = 0.0_f32;
                    for (_owner, list) in bonds.iter() {
                        for info in list.edges() {
                            sum += info.equilibrium_length;
                        }
                    }
                    black_box(sum)
                },
                BatchSize::LargeInput,
            )
        });
    }
    group.finish();
}

fn bench_push_midvec(c: &mut Criterion) { bench_push::<MidVec<BondInfo, MAX_INLINE_BONDS>>(c, "bond_storage_push/midvec"); }
fn bench_push_vec(c: &mut Criterion)    { bench_push::<Vec<BondInfo>>(c, "bond_storage_push/vec"); }
fn bench_iterate_midvec(c: &mut Criterion) { bench_iterate::<MidVec<BondInfo, MAX_INLINE_BONDS>>(c, "bond_storage_iterate/midvec"); }
fn bench_iterate_vec(c: &mut Criterion)    { bench_iterate::<Vec<BondInfo>>(c, "bond_storage_iterate/vec"); }

criterion_group!(benches, bench_push_midvec, bench_push_vec, bench_iterate_midvec, bench_iterate_vec);
criterion_main!(benches);
