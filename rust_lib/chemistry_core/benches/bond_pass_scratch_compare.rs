//! Compares scratch-allocation strategies for the *transient, per-call*
//! `Vec`s `compute_bonds` and `form_bond` build fresh every time they run
//! -- isolated from real physics, same reasoning `bond_storage_compare.rs`
//! already gives for why that isolation is the right call here too.
//!
//! ## Why this is a different question from `bond_storage_compare.rs`
//!
//! That file benches `ctx.bonds`'s storage: built once, read across many
//! *future* steps, genuinely persistent for the atom's life. The three
//! real sites this file models are the opposite shape -- built, consumed,
//! and dropped within *one* call, every single call, never read again:
//!
//! - `compute_bonds` Pass 1's `broken: Vec<(GenerationalIndex,
//!   GenerationalIndex)>` -- fresh every `compute_bonds` call (i.e. every
//!   `step()`), regardless of whether any bond actually breaks that step.
//! - `compute_bonds` Pass 2's `new_bonds: Vec<(usize, usize,
//!   GenerationalIndex, GenerationalIndex, f32)>` -- same "fresh every
//!   call" lifecycle, populated once per reactive atom that finds a
//!   bonding candidate this pass.
//! - `form_bond`'s two `neighbors_of(...)` snapshots -- fresh `Vec`,
//!   smaller and rarer (once per newly formed bond, not once per step),
//!   but the same "build, consume immediately, drop" shape.
//!
//! All three share one lifecycle, so this file models that lifecycle
//! generically (`ScratchBuffer<T>`) rather than three separate harnesses
//! -- the two payload shapes benched below (`broken`'s 2-tuple,
//! `new_bonds`'s 5-tuple) stand in for all three real sites; `form_bond`'s
//! payload is the same 2-tuple shape as `broken`'s, just a different call
//! site.
//!
//! ## Why build+consume are timed together here, unlike
//! `bond_storage_compare.rs`'s split `bench_push`/`bench_iterate`
//!
//! That split makes sense there because `ctx.bonds` really does get built
//! once and read many separate times across many future steps -- build
//! cost and steady-state read cost are two genuinely different questions.
//! Here, build and consume happen together, once, inside the same
//! function call, every time -- splitting them would answer a question
//! nothing in the real code ever asks in isolation.
//!
//! ## The real constraint this bench exists to make visible:
//! `mid_arena::BumpArena<T>` has no reset/clear method
//!
//! Checked directly against its real source (`bump_arena.rs`), not
//! assumed: the only way to reclaim a `BumpArena`'s memory is to drop the
//! whole arena -- there is no `.reset()`/`.clear()` that keeps the
//! backing regions and empties them for reuse, the way `ctx.candidates`/
//! `ctx.bonded_this_pass` already reuse a persistent `Vec` via `.clear()`
//! every step. That rules out a genuinely "persistent, reused-every-step
//! arena" strategy for now -- it isn't a thing this crate supports yet,
//! so this file doesn't fake one. `FreshBumpArena` below means
//! "construct, populate, drop -- every single call," the *same* lifecycle
//! `Vec::new()` already has today, which is the only strategy it's fair
//! to compare it against without inventing a variant that doesn't exist.
//!
//! `ReusedVec` -- a persistent `Vec` living outside the timed region,
//! `.clear()`'d at the top of every call, exactly the pattern
//! `ctx.candidates`/`ctx.bonded_this_pass` already use for real in
//! `simulation.rs` -- is included specifically because it's the *proven*
//! alternative already sitting in this codebase, not a new idea. The
//! hypothesis worth stating plainly before the numbers come in: `Vec::new()`
//! allocates nothing until the first push (so `m=0` -- the common case for
//! `broken`, which rarely has anything in it -- should cost ~nothing for
//! `FreshVec` too), while `BumpArena::new()` eagerly allocates a
//! `DEFAULT_FIRST_REGION_CAPACITY=32` first region in its constructor
//! (`bump_arena.rs`, `with_capacity`) -- unconditionally, even for `m=0`.
//! If that hypothesis holds, `FreshBumpArena` should lose outright at
//! `m=0`, and `ReusedVec` should win outright everywhere. A losing result
//! for arena here is a real, useful answer -- exactly the same spirit as
//! this crate's own SIMD kernel being kept and benched even after losing
//! to scalar (`Cargo.toml`'s `scalar-math` feature comment).
//!
//! ## `m` -- population count, not atom count
//!
//! Deliberately not `n` the way `sim_bench.rs`/`bond_storage_compare.rs`
//! use it: what actually drives cost here is how many entries land in the
//! scratch buffer *this call*, not how many atoms exist in the
//! simulation. `m=0` models `broken`'s typical steady-state (bonds rarely
//! snap most steps). `m=8`/`m=64` model a handful to a moderate burst of
//! simultaneous new-bond candidates in `new_bonds` on a small-to-mid grid
//! (`sim_bench.rs`'s own n=64..1024 range). `m=512` is a deliberate stress
//! case past what any of `sim_bench.rs`'s grids should realistically
//! produce in one call, to see whether either strategy's cost curve bends.

use criterion::{black_box, criterion_group, criterion_main, Criterion, Throughput};
use mid_arena::BumpArena;
use mid_collections::{GenerationalIndex, GenerationalIndexAllocator};

const POPULATION_COUNTS: [usize; 4] = [0, 8, 64, 512];

/// One `broken`/`form_bond`-shaped payload: two atom handles, 8 bytes.
type BrokenEdge = (GenerationalIndex, GenerationalIndex);

/// One `new_bonds`-shaped payload: the real 5-tuple `compute_bonds`
/// Pass 2 actually builds, unchanged -- not a stand-in struct, so this
/// bench can't quietly drift from the real payload's size if that tuple
/// ever grows a field.
type NewBondCandidate = (usize, usize, GenerationalIndex, GenerationalIndex, f32);

/// Shared shape all three per-call scratch sites in `simulation.rs`
/// already follow: build from nothing, push zero or more items, visit
/// every item once, then (implicitly, via `Drop`) discard the whole
/// thing. `&mut self` throughout, not `&self` -- `BumpArena::iter_mut`
/// needs exclusive access (see its own doc comment), and matching that
/// stricter real requirement on every implementor keeps `bench_scratch`
/// below identical for all of them instead of special-casing one.
trait ScratchBuffer<T: Copy>: Default {
    fn push_item(&mut self, item: T);
    fn for_each(&mut self, visit: impl FnMut(T));
}

/// Today's actual strategy: `Vec::new()` fresh, every call.
struct FreshVec<T>(Vec<T>);
impl<T> Default for FreshVec<T> {
    fn default() -> Self { Self(Vec::new()) }
}
impl<T: Copy> ScratchBuffer<T> for FreshVec<T> {
    fn push_item(&mut self, item: T) { self.0.push(item); }
    fn for_each(&mut self, mut visit: impl FnMut(T)) {
        for &x in self.0.iter() { visit(x); }
    }
}

/// The naive direct swap: `BumpArena::new()` fresh, every call -- same
/// "construct, populate, drop" lifecycle as `FreshVec` above, the only
/// fair comparison given `BumpArena` has no reset (see module doc).
struct FreshBumpArena<T>(BumpArena<T>);
impl<T> Default for FreshBumpArena<T> {
    fn default() -> Self { Self(BumpArena::new()) }
}
impl<T: Copy> ScratchBuffer<T> for FreshBumpArena<T> {
    fn push_item(&mut self, item: T) { self.0.alloc(item); }
    fn for_each(&mut self, mut visit: impl FnMut(T)) {
        // NOTE for anyone extending this file: BumpArena::len() walks
        // the whole region chain every call (see its own source) -- O(1)
        // for Vec, O(regions) here. Fine to call once outside a timed
        // region (as the correctness check that validated this trait
        // did); never call it inside a b.iter closure, which is exactly
        // why this bench uses for_each's visit count instead of len().
        for &mut x in self.0.iter_mut() { visit(x); }
    }
}

/// Pre-allocates a pool of real atom handles once, outside every timed
/// region -- identity allocation cost is identical for every strategy
/// benched below, so keeping it out of the loop isolates the one thing
/// actually in question (container choice), same reasoning
/// `bond_storage_compare.rs`'s own `allocate_ids` already documents.
/// Sized past the largest `POPULATION_COUNTS` entry plus the `+1`
/// wraparound offset `make_*` below uses.
fn build_handle_pool() -> Vec<GenerationalIndex> {
    let mut alloc = GenerationalIndexAllocator::with_capacity(600);
    (0..600).map(|_| alloc.allocate()).collect()
}

fn make_broken_edge(pool: &[GenerationalIndex], i: usize) -> BrokenEdge {
    (pool[i % pool.len()], pool[(i + 1) % pool.len()])
}

fn make_new_bond(pool: &[GenerationalIndex], i: usize) -> NewBondCandidate {
    (i, i + 1, pool[i % pool.len()], pool[(i + 1) % pool.len()], 3.29_f32)
}

/// `FreshVec`/`FreshBumpArena` timed together: build from `C::default()`,
/// push `m` items, visit every item once, all inside the timed closure --
/// this *is* the cost being measured, not overhead to exclude (unlike
/// `bond_storage_compare.rs`'s `allocate_ids`, which times identity
/// allocation separately because that part genuinely isn't the question).
fn bench_scratch<T: Copy, C: ScratchBuffer<T>>(
    c: &mut Criterion,
    group_name: &str,
    pool: &[GenerationalIndex],
    make_item: impl Fn(&[GenerationalIndex], usize) -> T,
    // Reduces each payload to one comparable f32 so both the 2-tuple and
    // 5-tuple shapes can share this function -- matches
    // `bond_storage_compare.rs`'s own `bench_iterate`, which sums
    // `equilibrium_length` for the same "touch every item for real, don't
    // just count them" reason.
    touch: impl Fn(T) -> f32,
) {
    let mut group = c.benchmark_group(group_name);
    for &m in &POPULATION_COUNTS {
        if m > 0 {
            group.throughput(Throughput::Elements(m as u64));
        }
        group.bench_function(format!("m={m}"), |b| {
            b.iter(|| {
                let mut buf = C::default();
                for i in 0..m {
                    buf.push_item(black_box(make_item(pool, i)));
                }
                let mut sum = 0.0_f32;
                buf.for_each(|item| sum += touch(item));
                black_box(sum)
            })
        });
    }
    group.finish();
}

/// `ReusedVec`: not generic over `ScratchBuffer` on purpose -- the whole
/// point is a `Vec` that lives *outside* this function's per-sample
/// closure and gets `.clear()`'d at the top of every call, exactly
/// `ctx.candidates`/`ctx.bonded_this_pass`'s real pattern in
/// `simulation.rs`. That's a fundamentally different harness shape from
/// "construct fresh from `Default`", not a type-parameter swap, so it
/// doesn't fit through `bench_scratch` above. After the first sample,
/// `buf`'s capacity has already grown to cover the largest `m` seen and
/// `.clear()` never shrinks it -- the same steady-state this pattern
/// reaches for real after a simulation's first few steps.
fn bench_scratch_reused_vec<T: Copy>(
    c: &mut Criterion,
    group_name: &str,
    pool: &[GenerationalIndex],
    make_item: impl Fn(&[GenerationalIndex], usize) -> T,
    touch: impl Fn(T) -> f32,
) {
    let mut group = c.benchmark_group(group_name);
    for &m in &POPULATION_COUNTS {
        if m > 0 {
            group.throughput(Throughput::Elements(m as u64));
        }
        let mut buf: Vec<T> = Vec::new();
        group.bench_function(format!("m={m}"), |b| {
            b.iter(|| {
                buf.clear();
                for i in 0..m {
                    buf.push(black_box(make_item(pool, i)));
                }
                let mut sum = 0.0_f32;
                for &item in buf.iter() {
                    sum += touch(item);
                }
                black_box(sum)
            })
        });
    }
    group.finish();
}

// --- broken-edge shape (also stands in for form_bond's neighbors_of) ---

fn bench_broken_fresh_vec(c: &mut Criterion) {
    let pool = build_handle_pool();
    bench_scratch::<BrokenEdge, FreshVec<BrokenEdge>>(
        c, "scratch_broken/fresh_vec", &pool, make_broken_edge,
        |(a, _b)| a.index() as f32,
    );
}
fn bench_broken_reused_vec(c: &mut Criterion) {
    let pool = build_handle_pool();
    bench_scratch_reused_vec::<BrokenEdge>(
        c, "scratch_broken/reused_vec", &pool, make_broken_edge,
        |(a, _b)| a.index() as f32,
    );
}
fn bench_broken_fresh_bump_arena(c: &mut Criterion) {
    let pool = build_handle_pool();
    bench_scratch::<BrokenEdge, FreshBumpArena<BrokenEdge>>(
        c, "scratch_broken/fresh_bump_arena", &pool, make_broken_edge,
        |(a, _b)| a.index() as f32,
    );
}

// --- new_bonds shape ---

fn bench_new_bond_fresh_vec(c: &mut Criterion) {
    let pool = build_handle_pool();
    bench_scratch::<NewBondCandidate, FreshVec<NewBondCandidate>>(
        c, "scratch_new_bond/fresh_vec", &pool, make_new_bond,
        |(_i, _j, _a, _b, r_min)| r_min,
    );
}
fn bench_new_bond_reused_vec(c: &mut Criterion) {
    let pool = build_handle_pool();
    bench_scratch_reused_vec::<NewBondCandidate>(
        c, "scratch_new_bond/reused_vec", &pool, make_new_bond,
        |(_i, _j, _a, _b, r_min)| r_min,
    );
}
fn bench_new_bond_fresh_bump_arena(c: &mut Criterion) {
    let pool = build_handle_pool();
    bench_scratch::<NewBondCandidate, FreshBumpArena<NewBondCandidate>>(
        c, "scratch_new_bond/fresh_bump_arena", &pool, make_new_bond,
        |(_i, _j, _a, _b, r_min)| r_min,
    );
}

criterion_group!(
    benches,
    bench_broken_fresh_vec,
    bench_broken_reused_vec,
    bench_broken_fresh_bump_arena,
    bench_new_bond_fresh_vec,
    bench_new_bond_reused_vec,
    bench_new_bond_fresh_bump_arena,
);
criterion_main!(benches);
