// crates/chemistry_core/src/simulation.rs
//! Core physics: Lennard-Jones pairwise forces (neighbor-limited via
//! `spatial_hash`) + velocity Verlet integration + pairwise bonding.
//!
//! `SimContext` owns the atom array itself (see `lib.rs` module docs for
//! why). Atom identity is a `mid_collections::GenerationalIndex`
//! internally, `crate::AtomHandle` at the FFI boundary — `slot_of` is
//! keyed by the *raw* index (`u32`) rather than the full
//! `GenerationalIndex`, specifically so nothing here ever needs to
//! construct a `GenerationalIndex` from FFI-crossed data — see
//! `resolve()` below.
//!
//! Two force-kernel implementations, both `pub` so they're directly
//! bench-able and test-able against each other:
//!
//! - `compute_forces_scalar` — one pair at a time, early-`continue`s past
//!   out-of-cutoff / unparameterized candidates before paying for any
//!   sqrt/recip/power math on them.
//! - `compute_forces_simd` — batches 4 candidates at a time via mid_math's
//!   `Vec3x4`/`f32x4`. Benched as a regression vs scalar at every tested
//!   size except n=64, where it narrowly wins (bench #10, after the
//!   atom-ownership restructure) — see `Cargo.toml`'s `[features]`
//!   comment for the numbers and the standing recommendation.
//!
//! `step()` calls whichever is selected by the `scalar-math` feature
//! (default: on, i.e. scalar), then runs `compute_bonds` on top.
//!
//! ## Bonds: unbounded per atom, one *new* edge per atom per pass
//!
//! Bonds were a one-per-atom MVP restriction through bench #11; that's
//! gone now — an atom can hold as many simultaneous bonds as geometry and
//! reactivity allow (needed for anything beyond a diatomic pair: water,
//! saltpeter, sulfuric acid, all of it need atoms with 2-4 bonds at once).
//! `ctx.bonds` went from `SparseSet<GenerationalIndex, BondInfo>` (one
//! slot per atom) to `SparseSet<GenerationalIndex, MidVec<BondInfo, N>>`
//! (a small-vec per atom, `N` = `MAX_INLINE_BONDS` living inline before
//! spilling to the heap) to make that possible — see `BondInfo` docs for
//! why each edge still gets stored on both sides, and `MAX_INLINE_BONDS`'s
//! own doc for why `N` is what it is. `MidVec` has no `retain` (unlike
//! `Vec`) — see `retain_bond` below for the one bit of glue that needed
//! writing by hand.
//!
//! `compute_bonds`'s bond-formation pass (Pass 2) still caps an atom to
//! **one new edge per call**, deliberately — not a leftover of the old
//! restriction, a genuine engineering choice: without it, an atom that
//! suddenly finds itself surrounded by several in-range reactive
//! neighbors in one frame would bond to all of them simultaneously,
//! which reads as instant supersaturation rather than a molecule actually
//! assembling. One new edge per call, called once per `step()`, means a
//! multi-atom compound visibly forms over a few frames instead of
//! popping into existence — total bonds held is still unbounded, only
//! the *growth rate* is capped.

use crate::{AtomState, AtomHandle, BondGeometry, AngleGeometry, element_data};
use crate::spatial_hash::SpatialHash;
use mid_math::{Vec3, Vec3x4, f32x4, Xorshift64, MidVec};
use mid_collections::{GenerationalIndex, GenerationalIndexAllocator, SparseSet};

/// Boltzmann constant, eV/K.
const K_B_EV_PER_K: f32 = 8.617_333e-5;
/// amu -> eV*fs^2/A^2 (see module docs for the derivation).
const AMU_TO_EFF_MASS: f32 = 103.642_69;

#[inline]
fn eff_mass(mass_amu: f32) -> f32 {
    mass_amu.max(1e-6) * AMU_TO_EFF_MASS
}

/// Box-Muller transform for one standard-normal sample. `Xorshift64::f32()`
/// returns `[0, 1)`; nudged away from exact 0 since `ln(0)` is undefined.
#[inline]
fn gaussian(rng: &mut Xorshift64) -> f32 {
    let u1 = rng.f32().max(1e-7);
    let u2 = rng.f32();
    (-2.0 * u1.ln()).sqrt() * (core::f32::consts::TAU * u2).cos()
}

/// Tunable bond-formation/breaking parameters. `Default` gives reasonable
/// starting values — these are genuinely game-tuning knobs, not derived
/// physical constants, and worth exposing via FFI once the Unity side
/// actually wants to tune them (not done yet — nothing external consumes
/// this today).
#[derive(Clone, Copy, Debug)]
pub struct BondParams {
    /// Bond forms when candidates are within `range_factor * r_min` of
    /// each other, where `r_min = sigma * 2^(1/6)` is the LJ potential's
    /// own equilibrium separation for that pair — reuses already-
    /// computed sigma rather than introducing a new arbitrary distance.
    pub range_factor: f32,
    /// Minimum `sqrt(reactivity_i * reactivity_j)` required to bond.
    /// Geometric mean, same combining shape as LJ epsilon — if either
    /// side has zero reactivity (noble gases), the pair never bonds,
    /// with no special-casing needed.
    pub min_reactivity: f32,
    /// Bond breaks once stretched beyond `break_factor * equilibrium_length`.
    pub break_factor: f32,
    /// Harmonic spring constant, eV/A^2. A genuine engineering choice,
    /// not derived from `bond_strength()` — kept separate on purpose so
    /// tuning one doesn't silently retune the other.
    pub spring_k: f32,
}

impl Default for BondParams {
    fn default() -> Self {
        Self {
            range_factor: 1.15,
            // Comfortably below every nonzero reactivity_index in the
            // current 5-element table (0.0035-0.0053 for H/Li/Be/B,
            // checked in element_data.rs's own test) and above He's
            // exact 0.0.
            min_reactivity: 0.001,
            break_factor: 1.8,
            spring_k: 50.0,
        }
    }
}

/// One edge of a bond, from one side's perspective. Stored symmetrically
/// — if A is bonded to B, both `bonds[A]` contains an entry with
/// `partner: B` *and* `bonds[B]` contains one with `partner: A` — so
/// "what is this atom bonded to" is O(1) lookup + O(bonds held) scan from
/// either side, never a search over every other atom.
///
/// 12 bytes (`GenerationalIndex` is 8, `f32` is 4, no padding — both
/// fields are already 4-byte aligned) — the number `MAX_INLINE_BONDS`
/// below is sized against.
#[derive(Clone, Copy, Debug)]
pub struct BondInfo {
    pub partner: GenerationalIndex,
    pub equilibrium_length: f32,
}

/// Inline capacity for `MidVec<BondInfo, N>` before an atom's bond list
/// spills to the heap. Not a hard cap — `MidVec` grows past this exactly
/// like `Vec` does, just with a real allocation once it does — this only
/// picks how many bonds an atom can hold for free.
///
/// `6`, not `4`: every predefined compound in
/// `grand-theft-grimoire-gameplay-reference.md` §1.3 tops out at 4 bonds
/// on any one atom (H2SO4/Oil of Vitriol's sulfur, KAl(SO4)2's aluminum
/// as modeled here — tetrahedral, not the 6-coordinate hydrated form real
/// inorganic chemistry sometimes uses). `6` gives that documented ceiling
/// real headroom rather than sitting exactly on it, and isn't picked
/// arbitrarily above it either: it's the coordination number of the
/// single most common real ionic-solid packing (octahedral, NaCl-type) —
/// directly relevant here since several §1.3 entries (Litharge/PbO,
/// Cinnabar/HgS, Crocus of Iron/Fe2O3, Natron) are solid-state ores/salts
/// where an atom deep in a nucleating lattice, not just a small
/// molecule's central atom, is the realistic worst case for this sim's
/// actual proximity-based bonding. (Bench #15's cubic hydrogen-grid test
/// separately happened to produce exactly 6 neighbors per interior atom —
/// noted only because it's a striking coincidence with the number landed
/// on here for entirely different reasons; that bench artifact was
/// explicitly flagged as the wrong justification on its own, and isn't
/// being used as one now.)
///
/// A tuning knob, not a physical constant — revisit once real C#-side
/// atom density data exists (see the open Pass-2-saturation question in
/// `compute_bonds`' own docs, which this doesn't attempt to resolve).
const MAX_INLINE_BONDS: usize = 6;

/// One angle triple, `(arm_a, vertex, arm_b)`, stored keyed by `vertex` —
/// unlike `BondInfo`, this is **not** stored symmetrically on all three
/// atoms. An angle is a property of the vertex atom specifically (the
/// bend at that one atom, between two of *its* bonds); `arm_a`/`arm_b`
/// only need to be looked up as arms of *this* vertex's own triples, not
/// as vertices of their own — an arm atom with only one bond total
/// participates in zero angle triples of its own, and one with several
/// bonds already has its own entry in `ctx.angles` for whichever pairs of
/// *its* bonds form angles at *it*. So `ctx.angles.get(x)` answers "what
/// angles is `x` the vertex of", not "what angles is `x` involved in at
/// all" — the latter would need a reverse index this sim has no use for
/// (force application, the only consumer, only ever needs to walk
/// vertices — see `compute_angles`).
///
/// 20 bytes (`GenerationalIndex` is 8 × 2, `f32` is 4, no padding — all
/// three fields already 4-byte aligned).
#[derive(Clone, Copy, Debug)]
pub struct AngleInfo {
    pub arm_a: GenerationalIndex,
    pub arm_b: GenerationalIndex,
    pub equilibrium_angle: f32,
}

/// Inline capacity for `MidVec<AngleInfo, N>` before a vertex's angle
/// list spills to the heap — same "not a hard cap, just how many are
/// free" role `MAX_INLINE_BONDS` plays for bonds.
///
/// `6`, matching `MAX_INLINE_BONDS` rather than picked independently: a
/// vertex with `k` simultaneous bonds holds `C(k, 2)` angle triples (every
/// *pair* of its bonds forms one angle), and the same documented-compound
/// ceiling `MAX_INLINE_BONDS`'s own doc cites — 4 bonds on any one atom —
/// gives `C(4, 2) = 6` triples. So `6` here means the realistic ceiling
/// fits inline exactly, same relationship `MAX_INLINE_BONDS = 6` has to
/// its own documented 4-bond ceiling. It is *not* sized against
/// `MAX_INLINE_BONDS`'s own upper bound (a fully-spilled 7+-bond hub
/// would need `C(7,2) = 21`+ inline slots to never spill on the angle
/// side too) — that hub case is the solid-state-lattice scenario
/// `MAX_INLINE_BONDS`'s own doc already flags as not really a covalent-
/// angle situation in the first place (ionic packing, not a molecule with
/// well-defined bond angles), so paying heap-spill cost there instead of
/// bloating every vertex's inline storage for a case angle-bending
/// physics doesn't meaningfully apply to anyway is the right tradeoff.
/// A tuning knob, not a physical constant, like `MAX_INLINE_BONDS` itself.
const MAX_INLINE_ANGLES: usize = 6;

/// Tunable angle-bend parameters — same "genuine engineering choice, not
/// derived" status `BondParams` already has, and same reasoning for
/// keeping this a separate struct/knob-set rather than folding into
/// `BondParams`: bending an angle and stretching a bond are different
/// stiffnesses in every real force field, so tuning one shouldn't
/// silently retune the other.
#[derive(Clone, Copy, Debug)]
pub struct AngleParams {
    /// Equilibrium angle assigned to every *newly formed* angle triple,
    /// radians. A single global default rather than varying by
    /// hybridization state (real force fields do vary it — sp3 vs sp2 vs
    /// sp — this sim doesn't model hybridization at all) — 109.5°
    /// (tetrahedral, `2f32.acos()` isn't how this constant is normally
    /// expressed but is worth knowing as a sanity-check identity: the
    /// tetrahedral angle is `acos(-1/3)`) is the single most common real
    /// bond angle and a reasonable default for anything not modeled more
    /// specifically. Stored per-`AngleInfo` at formation time (not
    /// re-read from here every step) — same "baked in once, tunable
    /// knob only affects what's formed *after* you change it" relationship
    /// `BondParams::range_factor`/`min_reactivity` have to already-formed
    /// bonds' own `equilibrium_length`.
    pub equilibrium_angle: f32,
    /// Harmonic angle-bend spring constant, eV/rad². Unlike
    /// `equilibrium_angle`, this one genuinely is re-read from here every
    /// `compute_angles` call, applied uniformly to every triple that
    /// exists right now — mirrors `BondParams::spring_k`'s own "global,
    /// not stored per-edge" role exactly.
    pub k_theta: f32,
}

impl Default for AngleParams {
    fn default() -> Self {
        Self {
            // 109.5 degrees — tetrahedral. `Self::default()` isn't a
            // `const fn`, so `.to_radians()` is directly usable here
            // (no need for a hand-computed literal, and no risk of
            // silently getting one wrong).
            equilibrium_angle: 109.5_f32.to_radians(),
            // Untuned starting value, same status BondParams::spring_k's
            // own doc gives its 50.0 — revisit once real gameplay
            // compounds exist to tune against, not derived from anything
            // physical.
            k_theta: 5.0,
        }
    }
}

/// `Vec::retain`-equivalent for `MidVec`, which doesn't have one (see
/// `mid_vec/mod.rs`'s own doc comment for its full API — this genuinely
/// isn't in it, not an oversight on my part). Keeps only entries for
/// which `keep` returns `true`, preserving the relative order of
/// survivors, matching what `Vec::retain` already guaranteed and what
/// `break_one_bond`/`break_all_bonds` were already relying on being true
/// (see e.g. `breaking_one_edge_leaves_others_intact`'s index-0 check).
/// O(removed × remaining) via repeated `MidVec::remove` — the same shift-
/// left-by-one-per-removal cost `Vec::remove` has; fine at `MAX_INLINE_BONDS`/
/// `MAX_INLINE_ANGLES` scale, not something to reach for on a large
/// collection.
///
/// Generic over the element type on purpose — originally `BondInfo`-only
/// (`retain_bond`), widened when angle-triple cleanup showed up needing
/// the exact same "filter a `MidVec` in place" logic for `AngleInfo`.
/// Same function, two independent lists.
fn retain_midvec<T, const N: usize>(list: &mut MidVec<T, N>, mut keep: impl FnMut(&T) -> bool) {
    let mut i = 0;
    while i < list.len() {
        if keep(&list[i]) {
            i += 1;
        } else {
            list.remove(i);
        }
    }
}

/// Owns the atom array and everything the force kernels need to reuse
/// across calls: the spatial hash grid and scratch buffers sized to the
/// current atom count. Create once (`chem_context_create`), spawn/despawn
/// atoms and step through every call on the same context, free once
/// (`chem_context_destroy`) when done.
pub struct SimContext {
    /// Dense, swap-remove-compacted atom storage — this is the hot-loop
    /// data every force kernel and integrator walks. Order is not stable
    /// across despawns; identity lives in `handles`/`slot_of`, not array
    /// position.
    atoms: Vec<AtomState>,
    /// handles[i] = the real GenerationalIndex identity of atoms[i].
    handles: Vec<GenerationalIndex>,
    /// Issues and tracks liveness of GenerationalIndex handles.
    allocator: GenerationalIndexAllocator,
    /// raw_index -> current position in `atoms`/`handles`. Keyed by the
    /// raw `u32` index portion only — see module docs.
    slot_of: SparseSet<u32, u32>,
    /// Active bonds, keyed by the real GenerationalIndex (formed and
    /// checked entirely internally — no FFI round-trip involved in the
    /// decision to bond, so no need for the raw-index-only trick `slot_of`
    /// uses). One atom can hold several bonds at once now — see module
    /// docs — so each entry is a small-vec of edges (`MAX_INLINE_BONDS`
    /// inline, spills to the heap past that), not a single one.
    bonds: SparseSet<GenerationalIndex, MidVec<BondInfo, MAX_INLINE_BONDS>>,
    /// Active angle triples, keyed by the vertex atom's real
    /// `GenerationalIndex` — same "formed and checked entirely
    /// internally, no FFI round-trip in the decision" reasoning `bonds`
    /// already uses. See `AngleInfo`'s own doc for why this is keyed by
    /// vertex only (not stored on the arm atoms' own entries the way a
    /// bond edge is stored on both sides).
    angles: SparseSet<GenerationalIndex, MidVec<AngleInfo, MAX_INLINE_ANGLES>>,

    grid: SpatialHash,
    positions: Vec<Vec3>,
    forces: Vec<Vec3>,
    old_accel: Vec<Vec3>,
    /// Scratch buffer for one atom's j>i candidate indices, gathered from
    /// the spatial hash before either force kernel processes them.
    candidates: Vec<u32>,
    /// Scratch buffer for `compute_bonds`' Pass 2: which atom positions
    /// already picked up a *new* edge this call, so a second proposal
    /// touching an already-claimed atom waits for the next call instead
    /// of forming immediately — see module docs on the one-new-edge-per-
    /// call rule.
    bonded_this_pass: Vec<bool>,
    /// FFI-safe mirror of `handles`, refreshed on demand by
    /// `refresh_handles_scratch` — `handles: Vec<GenerationalIndex>`
    /// can't be exposed to C# as a raw pointer directly the way `atoms`
    /// can: `GenerationalIndex` isn't `#[repr(C)]`, so even though its
    /// two-u32 layout happens to match `AtomHandle`'s today, pointer-
    /// casting between them without that guarantee would be relying on
    /// undefined behavior that happens to work, not something actually
    /// sound. This buffer is the real, correct fix — same "cache,
    /// refresh on demand" shape `positions` already uses for a different
    /// reason (see `refresh_grid`).
    handles_ffi_scratch: Vec<AtomHandle>,
}

impl SimContext {
    /// `cutoff_hint` just sizes the initial grid — force kernels
    /// transparently rebuild it if a later call's `cutoff` ever actually
    /// differs from what the grid was built with, so passing 0.0 here and
    /// relying on the 10.0 default is fine too.
    pub fn new(cutoff_hint: f32) -> Self {
        let cutoff = if cutoff_hint > 0.0 { cutoff_hint } else { 10.0 };
        Self {
            atoms: Vec::new(),
            handles: Vec::new(),
            allocator: GenerationalIndexAllocator::new(),
            slot_of: SparseSet::new(),
            bonds: SparseSet::new(),
            angles: SparseSet::new(),
            grid: SpatialHash::new(cutoff),
            positions: Vec::new(),
            forces: Vec::new(),
            old_accel: Vec::new(),
            candidates: Vec::new(),
            bonded_this_pass: Vec::new(),
            handles_ffi_scratch: Vec::new(),
        }
    }
}

/// Resolves an FFI `AtomHandle` to its current array position, verifying
/// it's still alive along the way. `None` for a stale handle — never
/// panics on bad input, since this is the first thing every externally-
/// driven call does with caller-supplied data.
fn resolve(ctx: &SimContext, h: AtomHandle) -> Option<usize> {
    let &pos = ctx.slot_of.get(h.index)?;
    let real = ctx.handles[pos as usize];
    if real.generation() == h.generation {
        Some(pos as usize)
    } else {
        None // slot was reused by a different atom since this handle was issued
    }
}

/// Remove one specific bond edge between `owner` and `partner` from both
/// sides' lists — used when a single stretched edge breaks (Pass 1) and
/// an atom mid-multi-bond needs only that one edge gone, not its whole
/// bond list. Empties and removes an atom's list entry entirely once its
/// last edge is gone, so `ctx.bonds.contains(x)` keeps meaning "has at
/// least one bond" rather than "has a list sitting in the set, possibly
/// empty."
fn break_one_bond(ctx: &mut SimContext, owner: GenerationalIndex, partner: GenerationalIndex) {
    if let Some(list) = ctx.bonds.get_mut(owner) {
        retain_midvec(list, |b| b.partner != partner);
        if list.is_empty() {
            ctx.bonds.remove(owner);
        }
    }
    if let Some(list) = ctx.bonds.get_mut(partner) {
        retain_midvec(list, |b| b.partner != owner);
        if list.is_empty() {
            ctx.bonds.remove(partner);
        }
    }

    // Any angle triple centered at `owner` or `partner` that used the
    // OTHER atom as one of its two arms just lost the bond that made it
    // a valid arm — an angle's two arms must both currently be bonded to
    // its vertex (see AngleInfo's own doc), and that's no longer true for
    // this one. Angles centered at some unrelated third atom are
    // untouched: only a triple whose vertex is `owner` or `partner`
    // could possibly reference this specific edge at all.
    if let Some(list) = ctx.angles.get_mut(owner) {
        retain_midvec(list, |a| a.arm_a != partner && a.arm_b != partner);
        if list.is_empty() {
            ctx.angles.remove(owner);
        }
    }
    if let Some(list) = ctx.angles.get_mut(partner) {
        retain_midvec(list, |a| a.arm_a != owner && a.arm_b != owner);
        if list.is_empty() {
            ctx.angles.remove(partner);
        }
    }
}

/// Remove every bond `owner` currently holds — used on despawn, where the
/// whole atom (and everything it was bonded to) needs cleaning up, not
/// just one edge. Removes only the matching single edge from each
/// partner's own list, not the partner's entire list: a partner bonded to
/// several other atoms keeps its other bonds. (Getting this wrong — e.g.
/// wiping the partner's whole entry the way a naive one-bond-per-atom
/// port of the old code would — is exactly the kind of bug that would
/// only show up once something actually had more than one bond to lose;
/// see `despawn_does_not_wipe_a_survivors_other_bonds` in the tests.)
fn break_all_bonds(ctx: &mut SimContext, owner: GenerationalIndex) {
    let Some(partners) = ctx.bonds.remove(owner) else { return; };
    // Every triple centered on `owner` itself goes with it — no-op if
    // `owner` was never a vertex of anything.
    ctx.angles.remove(owner);
    for info in partners {
        if let Some(list) = ctx.bonds.get_mut(info.partner) {
            retain_midvec(list, |b| b.partner != owner);
            if list.is_empty() {
                ctx.bonds.remove(info.partner);
            }
        }
        // `owner` was an arm of any triple centered at this partner —
        // same per-edge cleanup break_one_bond does, just looped over
        // every edge `owner` held instead of one specific edge.
        if let Some(list) = ctx.angles.get_mut(info.partner) {
            retain_midvec(list, |a| a.arm_a != owner && a.arm_b != owner);
            if list.is_empty() {
                ctx.angles.remove(info.partner);
            }
        }
    }
}

/// Add one bond edge from `owner`'s side (owner -> partner) — caller
/// calls this twice, once per direction, to keep the symmetric-storage
/// invariant (see `BondInfo` docs). Appends to an existing list or starts
/// a new one; either way `owner` keeps every bond it already held.
fn push_bond_edge(ctx: &mut SimContext, owner: GenerationalIndex, partner: GenerationalIndex, equilibrium_length: f32) {
    match ctx.bonds.get_mut(owner) {
        Some(list) => list.push(BondInfo { partner, equilibrium_length }),
        None => { ctx.bonds.insert(owner, MidVec::from([BondInfo { partner, equilibrium_length }])); }
    }
}

/// Add one angle triple centered at `vertex`, arms `arm_a`/`arm_b` — the
/// `push_bond_edge` of angle triples, same "append to an existing list or
/// start a new one" shape. Only ever called from `form_bond` below; not
/// meant to be called with arms that aren't both currently bonded to
/// `vertex` (nothing here checks that — the caller is the one place that
/// already knows it's true by construction).
fn push_angle_triple(ctx: &mut SimContext, vertex: GenerationalIndex, arm_a: GenerationalIndex, arm_b: GenerationalIndex, equilibrium_angle: f32) {
    match ctx.angles.get_mut(vertex) {
        Some(list) => list.push(AngleInfo { arm_a, arm_b, equilibrium_angle }),
        None => { ctx.angles.insert(vertex, MidVec::from([AngleInfo { arm_a, arm_b, equilibrium_angle }])); }
    }
}

/// This atom's current bond partners, as a plain `Vec` snapshot — used by
/// `form_bond` to capture "who was `a`/`b` already bonded to" *before*
/// the new edge lands in either list. A transient scratch collection, not
/// something living in `SimContext`: `MidVec`'s whole point is avoiding
/// allocation for the *persistent*, per-atom-for-the-simulation's-life
/// bond/angle lists, which this isn't — it's a one-off copy taken once
/// per new bond, immediately consumed and dropped. Plain `Vec` is the
/// right tool for that, not a second inline-capacity type to reason
/// about for a buffer that never outlives one function call.
fn neighbors_of(ctx: &SimContext, atom: GenerationalIndex) -> Vec<GenerationalIndex> {
    ctx.bonds.get(atom).map_or_else(Vec::new, |list| list.iter().map(|info| info.partner).collect())
}

/// Form a new bond `a <-> b` (both directions of `BondInfo`, same as two
/// raw `push_bond_edge` calls) **and** every angle triple that bond
/// creates: for each atom `a`/`b` already had bonded before this edge, a
/// new triple appears with the new bond as one arm and that pre-existing
/// neighbor as the other, centered on whichever of `a`/`b` that neighbor
/// belongs to. This is the only place `ctx.angles` gains new entries —
/// angle-triple creation is entirely a side effect of bond-topology
/// change, not a separate discovery pass the way bond formation itself
/// (Pass 2, spatial-hash-driven) is.
///
/// Neighbor snapshots MUST be taken before either `push_bond_edge` call —
/// otherwise the new partner would already be in its own snapshot,
/// producing a degenerate zero-length-arm "triple" against itself.
fn form_bond(ctx: &mut SimContext, a: GenerationalIndex, b: GenerationalIndex, eq_len: f32, angle_params: &AngleParams) {
    let a_neighbors = neighbors_of(ctx, a);
    let b_neighbors = neighbors_of(ctx, b);

    push_bond_edge(ctx, a, b, eq_len);
    push_bond_edge(ctx, b, a, eq_len);

    for i in a_neighbors {
        push_angle_triple(ctx, a, i, b, angle_params.equilibrium_angle);
    }
    for k in b_neighbors {
        push_angle_triple(ctx, b, k, a, angle_params.equilibrium_angle);
    }
}

/// Spawn one atom of element `atomic_number` at `position`. Mass and
/// radius sourced from `element_data::make_atom`. Returns a handle valid
/// until despawned.
pub fn spawn_atom(ctx: &mut SimContext, atomic_number: i32, position: [f32; 3]) -> AtomHandle {
    let real = ctx.allocator.allocate();
    let atom = element_data::make_atom(atomic_number, position);
    let pos = ctx.atoms.len() as u32;

    ctx.atoms.push(atom);
    ctx.handles.push(real);
    ctx.slot_of.insert(real.index(), pos);

    AtomHandle { index: real.index(), generation: real.generation() }
}

/// Despawn an atom by handle. `false` for a stale handle. Cleans up any
/// bond involving this atom *before* freeing the handle — the allocator
/// doesn't do that automatically, has to be deliberate at the call site
/// (same discipline `mid_collections`' own generational-index tests call
/// out explicitly). Swap-remove keeps `atoms`/`handles` dense: the last
/// element moves into the vacated slot, and that moved atom's `slot_of`
/// entry is updated to its new position.
pub fn despawn_atom(ctx: &mut SimContext, h: AtomHandle) -> bool {
    let Some(pos) = resolve(ctx, h) else { return false; };
    let real = ctx.handles[pos];

    break_all_bonds(ctx, real);

    let last = ctx.atoms.len() - 1;
    if pos != last {
        ctx.atoms.swap(pos, last);
        ctx.handles.swap(pos, last);
        let moved = ctx.handles[pos];
        ctx.slot_of.insert(moved.index(), pos as u32);
    }
    ctx.atoms.pop();
    ctx.handles.pop();
    ctx.slot_of.remove(h.index);
    ctx.allocator.deallocate(real);
    true
}

/// Current number of live atoms.
pub fn atom_count(ctx: &SimContext) -> usize {
    ctx.atoms.len()
}

/// Copy one atom's current state out by handle. `None` for a stale handle.
pub fn get_atom(ctx: &SimContext, h: AtomHandle) -> Option<AtomState> {
    resolve(ctx, h).map(|pos| ctx.atoms[pos])
}

/// Read-only pointer into the dense atom array, for zero-copy rendering.
/// See `lib.rs` module docs for the "re-fetch every frame" caveat.
pub fn atoms_ptr(ctx: &SimContext) -> *const AtomState {
    ctx.atoms.as_ptr()
}

/// Is this atom currently bonded to anything? `false` for a stale handle.
pub fn is_bonded(ctx: &SimContext, h: AtomHandle) -> bool {
    let Some(pos) = resolve(ctx, h) else { return false; };
    ctx.bonds.contains(ctx.handles[pos])
}

/// Rebuilds `ctx.handles_ffi_scratch` from `ctx.handles` and returns a
/// pointer to it — same dense order as `chem_atoms_ptr`'s array (index
/// `i` here is the same atom as index `i` there), same "re-fetch every
/// frame, don't cache across a spawn/despawn/step" contract, same
/// "order not stable across despawns" caveat. `&mut` (unlike
/// `chem_atoms_ptr`'s `&SimContext`) because this genuinely mutates the
/// scratch buffer to refresh it — seeing why in the field's own doc
/// comment above.
pub fn refresh_handles_scratch(ctx: &mut SimContext) -> *const AtomHandle {
    ctx.handles_ffi_scratch.clear();
    ctx.handles_ffi_scratch.extend(
        ctx.handles.iter().map(|h| AtomHandle { index: h.index(), generation: h.generation() })
    );
    ctx.handles_ffi_scratch.as_ptr()
}

/// How many bonds this atom currently holds. 0 for a stale handle or an
/// unbonded atom — not distinguished, matching `is_bonded`'s existing
/// "false either way" contract for stale handles.
pub fn bond_count(ctx: &SimContext, h: AtomHandle) -> usize {
    let Some(pos) = resolve(ctx, h) else { return 0; };
    ctx.bonds.get(ctx.handles[pos]).map_or(0, |list| list.len())
}

/// This atom's `index`-th bond partner, if it exists. `None` for a stale
/// handle, an unbonded atom, or an out-of-range index — all the same
/// "nothing there" case from the caller's side, deliberately not
/// distinguished. Iterate `0..bond_count(ctx, h)` to walk every partner.
pub fn bond_partner_at(ctx: &SimContext, h: AtomHandle, index: usize) -> Option<AtomHandle> {
    let pos = resolve(ctx, h)?;
    let list = ctx.bonds.get(ctx.handles[pos])?;
    let info = list.get(index)?;
    Some(AtomHandle { index: info.partner.index(), generation: info.partner.generation() })
}

/// Rest length and current live separation of this atom's `index`-th bond
/// edge. `None` for a stale handle, an unbonded atom, an out-of-range
/// index, or — defensively, shouldn't happen in practice, since
/// `break_one_bond`/`break_all_bonds` keep both sides of an edge in sync
/// with liveness — an unresolvable partner.
///
/// Reads position straight from `ctx.atoms`, not the `ctx.positions`
/// scratch buffer: that buffer is only populated once a force kernel has
/// run at least once (see `refresh_grid`), so using it here would make
/// this accessor's correctness depend on step-ordering a caller shouldn't
/// have to know about. `ctx.atoms[..].position` is always current the
/// moment this is called — the same field `get_atom`/`atoms_ptr` expose.
pub fn bond_geometry_at(ctx: &SimContext, h: AtomHandle, index: usize) -> Option<BondGeometry> {
    let pos = resolve(ctx, h)?;
    let list = ctx.bonds.get(ctx.handles[pos])?;
    let info = list.get(index)?;

    let &partner_pos = ctx.slot_of.get(info.partner.index())?;
    let partner_pos = partner_pos as usize;
    if ctx.handles[partner_pos] != info.partner {
        return None;
    }

    let pi = ctx.atoms[pos].position;
    let pj = ctx.atoms[partner_pos].position;
    let dx = pj[0] - pi[0];
    let dy = pj[1] - pi[1];
    let dz = pj[2] - pi[2];
    let current_length = (dx * dx + dy * dy + dz * dz).sqrt();

    Some(BondGeometry {
        equilibrium_length: info.equilibrium_length,
        current_length,
    })
}

/// Initialise every currently-live atom's velocity from a Maxwell-
/// Boltzmann distribution at `temperature_k`, zero their force
/// accumulators.
pub fn init(ctx: &mut SimContext, temperature_k: f32, seed: u64) {
    let mut rng = Xorshift64::new_safe(seed);
    let t = temperature_k.max(0.0);
    for a in ctx.atoms.iter_mut() {
        let sigma_v = (K_B_EV_PER_K * t / eff_mass(a.mass)).sqrt();
        a.velocity = [
            gaussian(&mut rng) * sigma_v,
            gaussian(&mut rng) * sigma_v,
            gaussian(&mut rng) * sigma_v,
        ];
        a.force = [0.0; 3];
    }
}

/// Advance the simulation by `dt` femtoseconds using velocity Verlet:
/// position update from the old force, force recompute at the new
/// positions (LJ, then bonds on top), then a velocity half-step blending
/// old and new acceleration.
pub fn step(ctx: &mut SimContext, dt: f32, cutoff: f32) {
    let n = ctx.atoms.len();
    if n == 0 || dt <= 0.0 {
        return;
    }
    let cutoff = if cutoff > 0.0 { cutoff } else { 10.0 };

    ctx.old_accel.resize(n, Vec3::ZERO);

    {
        let atoms = &mut ctx.atoms;
        let old_accel = &mut ctx.old_accel;
        for (i, a) in atoms.iter_mut().enumerate() {
            let inv_m = 1.0 / eff_mass(a.mass);
            let f = Vec3::new(a.force[0], a.force[1], a.force[2]);
            let accel = f * inv_m;
            old_accel[i] = accel;

            let v = Vec3::new(a.velocity[0], a.velocity[1], a.velocity[2]);
            let p = Vec3::new(a.position[0], a.position[1], a.position[2]);
            let new_p = p + v * dt + accel * (0.5 * dt * dt);
            a.position = [new_p.x, new_p.y, new_p.z];
        }
    }

    #[cfg(feature = "scalar-math")]
    compute_forces_scalar(ctx, cutoff);
    #[cfg(not(feature = "scalar-math"))]
    compute_forces_simd(ctx, cutoff);

    compute_bonds(ctx, &BondParams::default(), &AngleParams::default());
    compute_angles(ctx, &AngleParams::default());

    {
        let atoms = &mut ctx.atoms;
        let old_accel = &ctx.old_accel;
        for (i, a) in atoms.iter_mut().enumerate() {
            let inv_m = 1.0 / eff_mass(a.mass);
            let f = Vec3::new(a.force[0], a.force[1], a.force[2]);
            let new_accel = f * inv_m;
            let v = Vec3::new(a.velocity[0], a.velocity[1], a.velocity[2]);
            let new_v = v + (old_accel[i] + new_accel) * (0.5 * dt);
            a.velocity = [new_v.x, new_v.y, new_v.z];
        }
    }
}

/// Rebuild `ctx.positions` from the current atom array and rebuild the
/// spatial hash grid against them. Shared by both force kernels.
fn refresh_grid(ctx: &mut SimContext, cutoff: f32) {
    let n = ctx.atoms.len();
    if (ctx.grid.cell_size() - cutoff).abs() > 1e-6 {
        ctx.grid = SpatialHash::new(cutoff);
    }
    ctx.positions.resize(n, Vec3::ZERO);
    for i in 0..n {
        let p = ctx.atoms[i].position;
        ctx.positions[i] = Vec3::new(p[0], p[1], p[2]);
    }
    ctx.grid.rebuild(&ctx.positions[..n]);
}

/// One pair at a time. Early-`continue`s past out-of-cutoff and
/// unparameterized-element candidates before paying for any sqrt/recip/
/// power math on them.
pub fn compute_forces_scalar(ctx: &mut SimContext, cutoff: f32) {
    let n = ctx.atoms.len();
    refresh_grid(ctx, cutoff);

    ctx.forces.resize(n, Vec3::ZERO);
    for f in ctx.forces[..n].iter_mut() {
        *f = Vec3::ZERO;
    }

    let cutoff_sq = cutoff * cutoff;

    for i in 0..n {
        let pi = ctx.positions[i];
        let pi_params = element_data::params(ctx.atoms[i].atomic_number);

        let grid = &ctx.grid;
        let positions = &ctx.positions;
        let atoms = &ctx.atoms;
        let forces = &mut ctx.forces;

        grid.for_each_candidate(pi, |j| {
            let j = j as usize;
            if j <= i {
                return;
            }
            let pj = positions[j];
            let d = pj - pi;
            let r2 = d.length_sq();
            if r2 < 1e-8 || r2 > cutoff_sq {
                return;
            }
            let pj_params = element_data::params(atoms[j].atomic_number);
            let (sigma, eps) = element_data::combine(pi_params, pj_params);
            if eps <= 0.0 || sigma <= 0.0 {
                return;
            }
            let r = r2.sqrt();
            let sr6 = (sigma / r).powi(6);
            let sr12 = sr6 * sr6;
            let f_mag = 24.0 * eps / r * (2.0 * sr12 - sr6);
            let dir = d * (1.0 / r);
            forces[i] -= dir * f_mag;
            forces[j] += dir * f_mag;
        });
    }

    for i in 0..n {
        ctx.atoms[i].force = [ctx.forces[i].x, ctx.forces[i].y, ctx.forces[i].z];
    }
}

/// 4 candidates at a time via `Vec3x4`/`f32x4`, gather-then-batch, with a
/// scalar remainder for whatever doesn't fill a full chunk.
pub fn compute_forces_simd(ctx: &mut SimContext, cutoff: f32) {
    let n = ctx.atoms.len();
    refresh_grid(ctx, cutoff);

    ctx.forces.resize(n, Vec3::ZERO);
    for f in ctx.forces[..n].iter_mut() {
        *f = Vec3::ZERO;
    }

    let cutoff_sq = cutoff * cutoff;
    let min_r2_4 = f32x4::splat(1e-8);
    let cutoff_sq_4 = f32x4::splat(cutoff_sq);
    let zero4 = f32x4::ZERO;

    for i in 0..n {
        let pi = ctx.positions[i];
        let pi_params = element_data::params(ctx.atoms[i].atomic_number);

        ctx.candidates.clear();
        {
            let grid = &ctx.grid;
            let candidates = &mut ctx.candidates;
            grid.for_each_candidate(pi, |j| {
                if j as usize > i {
                    candidates.push(j);
                }
            });
        }

        let cand_count = ctx.candidates.len();
        let chunk_count = cand_count / 4;
        let pi4 = Vec3x4::splat(pi);

        for c in 0..chunk_count {
            let base = c * 4;
            let j0 = ctx.candidates[base] as usize;
            let j1 = ctx.candidates[base + 1] as usize;
            let j2 = ctx.candidates[base + 2] as usize;
            let j3 = ctx.candidates[base + 3] as usize;

            let pj4 = Vec3x4::from_vec3s(
                ctx.positions[j0], ctx.positions[j1], ctx.positions[j2], ctx.positions[j3],
            );

            let (sigma0, eps0) = element_data::combine(pi_params, element_data::params(ctx.atoms[j0].atomic_number));
            let (sigma1, eps1) = element_data::combine(pi_params, element_data::params(ctx.atoms[j1].atomic_number));
            let (sigma2, eps2) = element_data::combine(pi_params, element_data::params(ctx.atoms[j2].atomic_number));
            let (sigma3, eps3) = element_data::combine(pi_params, element_data::params(ctx.atoms[j3].atomic_number));
            let sigma4 = f32x4::new(sigma0, sigma1, sigma2, sigma3);
            let eps4 = f32x4::new(eps0, eps1, eps2, eps3);

            let d4 = pj4 - pi4;
            let r2_4 = d4.length_sq();

            let mask = r2_4.cmpge(min_r2_4)
                & r2_4.cmple(cutoff_sq_4)
                & eps4.cmpgt(zero4)
                & sigma4.cmpgt(zero4);

            let r2_safe = r2_4.max(min_r2_4);
            let r4 = r2_safe.sqrt();
            let inv_r4 = r4.recip();

            let sr4 = sigma4 * inv_r4;
            let sr2_4 = sr4 * sr4;
            let sr6_4 = sr2_4 * sr2_4 * sr2_4;
            let sr12_4 = sr6_4 * sr6_4;
            let f_mag4 = f32x4::splat(24.0) * eps4 * inv_r4 * (f32x4::splat(2.0) * sr12_4 - sr6_4);

            let dir4 = d4 * inv_r4;
            let contrib4 = dir4 * f_mag4;
            let contrib4 = Vec3x4::select(mask, contrib4, Vec3x4::ZERO);

            let arr = contrib4.to_array();
            ctx.forces[i] -= arr[0] + arr[1] + arr[2] + arr[3];
            ctx.forces[j0] += arr[0];
            ctx.forces[j1] += arr[1];
            ctx.forces[j2] += arr[2];
            ctx.forces[j3] += arr[3];
        }

        for k in (chunk_count * 4)..cand_count {
            let j = ctx.candidates[k] as usize;
            let pj = ctx.positions[j];
            let d = pj - pi;
            let r2 = d.length_sq();
            if r2 < 1e-8 || r2 > cutoff_sq {
                continue;
            }
            let pj_params = element_data::params(ctx.atoms[j].atomic_number);
            let (sigma, eps) = element_data::combine(pi_params, pj_params);
            if eps <= 0.0 || sigma <= 0.0 {
                continue;
            }
            let r = r2.sqrt();
            let sr6 = (sigma / r).powi(6);
            let sr12 = sr6 * sr6;
            let f_mag = 24.0 * eps / r * (2.0 * sr12 - sr6);
            let dir = d * (1.0 / r);
            ctx.forces[i] -= dir * f_mag;
            ctx.forces[j] += dir * f_mag;
        }
    }

    for i in 0..n {
        ctx.atoms[i].force = [ctx.forces[i].x, ctx.forces[i].y, ctx.forces[i].z];
    }
}

/// Bond formation and breaking, plus harmonic spring forces for bonds
/// already formed. Additive on top of whatever `compute_forces_scalar`/
/// `compute_forces_simd` already wrote into each atom's `.force` — call
/// this *after* one of those, not instead of. Deliberately doesn't touch
/// or exclude anything in the LJ kernels: near the equilibrium distance
/// LJ's own force is already ~zero (that's what "equilibrium" means), so
/// the spring mainly matters for holding a compound together *beyond*
/// where LJ would still be acting at all.
///
/// Bonds are unbounded per atom — see module docs — with new-edge growth
/// capped to one per atom per call (also module docs) rather than total
/// bonds held.
///
/// `angle_params` is only consulted here for `equilibrium_angle`, baked
/// into any *new* angle triple Pass 2's bond formation creates (see
/// `form_bond`) — it plays no role in Pass 1's existing-bond spring force
/// or breaking check. Angle-bend *force* on triples that already exist is
/// a separate call, `compute_angles` — not folded into this function,
/// even though both ultimately read from the same `AngleParams` struct,
/// because "did any new bond topology change this step" and "apply force
/// to whatever topology exists right now" are genuinely different
/// concerns that happen to share a params type, not one operation.
pub fn compute_bonds(ctx: &mut SimContext, params: &BondParams, angle_params: &AngleParams) {
    // --- Pass 1: existing bonds — spring force, break check ---
    // One entry in `broken` per *edge* (owner, partner) now, not per
    // atom — an atom mid-multi-bond only loses the specific edge that
    // stretched too far, not every bond it holds.
    let mut broken: Vec<(GenerationalIndex, GenerationalIndex)> = Vec::new();
    {
        let bonds = &ctx.bonds;
        let slot_of = &ctx.slot_of;
        let handles = &ctx.handles;
        let positions = &ctx.positions;
        let atoms = &mut ctx.atoms;

        for (owner, list) in bonds.iter() {
            let Some(&owner_pos) = slot_of.get(owner.index()) else { continue; };
            if handles[owner_pos as usize] != owner {
                continue;
            }
            for info in list {
                let Some(&partner_pos) = slot_of.get(info.partner.index()) else { continue; };
                if handles[partner_pos as usize] != info.partner {
                    continue;
                }
                // Stored symmetrically (see BondInfo docs) — process each
                // edge once via an arbitrary but consistent tie-break.
                if owner_pos >= partner_pos {
                    continue;
                }

                let pi = positions[owner_pos as usize];
                let pj = positions[partner_pos as usize];
                let d = pj - pi;
                let r = d.length_sq().sqrt().max(1e-6);

                if r > info.equilibrium_length * params.break_factor {
                    broken.push((owner, info.partner));
                    continue;
                }

                let stretch = r - info.equilibrium_length;
                let f_mag = -params.spring_k * stretch;
                let dir = d * (1.0 / r);
                let contrib = dir * f_mag;

                let fi = Vec3::new(
                    atoms[owner_pos as usize].force[0],
                    atoms[owner_pos as usize].force[1],
                    atoms[owner_pos as usize].force[2],
                ) - contrib;
                atoms[owner_pos as usize].force = [fi.x, fi.y, fi.z];

                let fj = Vec3::new(
                    atoms[partner_pos as usize].force[0],
                    atoms[partner_pos as usize].force[1],
                    atoms[partner_pos as usize].force[2],
                ) + contrib;
                atoms[partner_pos as usize].force = [fj.x, fj.y, fj.z];
            }
        }
    }
    for (owner, partner) in broken {
        break_one_bond(ctx, owner, partner);
    }

    // --- Pass 2: form new bonds among in-range, reactive pairs ---
    // No longer skips atoms that already have a bond (that was the
    // one-bond-per-atom restriction) — every atom gets to seek its best
    // candidate every call, whether or not it's already bonded to
    // something else. Two things still guard against nonsense:
    //  - a candidate this atom is *already* bonded to specifically is
    //    excluded, so this can't propose a duplicate edge;
    //  - `bonded_this_pass` (keyed by array position, not handle — cheap
    //    to index, no hashing) caps each atom to at most one *new* edge
    //    per call, so two simultaneous proposals both touching the same
    //    atom don't both land in one pass. See module docs for why that
    //    cap exists.
    let n = ctx.atoms.len();
    let mut new_bonds: Vec<(usize, usize, GenerationalIndex, GenerationalIndex, f32)> = Vec::new();

    for i in 0..n {
        let handle_i = ctx.handles[i];
        let pi = ctx.positions[i];
        let pi_params = element_data::params(ctx.atoms[i].atomic_number);
        let react_i = element_data::reactivity_index(pi_params);
        if react_i <= 0.0 {
            continue;
        }

        let mut best: Option<(usize, f32, f32)> = None; // (j, r, r_min)
        {
            let grid = &ctx.grid;
            let positions = &ctx.positions;
            let atoms = &ctx.atoms;
            let handles = &ctx.handles;
            let bonds = &ctx.bonds;

            grid.for_each_candidate(pi, |j| {
                let j = j as usize;
                if j == i {
                    return;
                }
                // Already bonded to this specific candidate? Skip it —
                // being bonded to *other* atoms is fine now, only an
                // exact duplicate edge is excluded.
                if let Some(list) = bonds.get(handles[j]) {
                    if list.iter().any(|b| b.partner == handle_i) {
                        return;
                    }
                }
                let pj = positions[j];
                let d = pj - pi;
                let r2 = d.length_sq();
                if r2 < 1e-8 {
                    return;
                }
                let pj_params = element_data::params(atoms[j].atomic_number);
                let (sigma, eps) = element_data::combine(pi_params, pj_params);
                if sigma <= 0.0 || eps <= 0.0 {
                    return;
                }
                let r_min = sigma * 2f32.powf(1.0 / 6.0);
                let bond_range = r_min * params.range_factor;
                let r = r2.sqrt();
                if r > bond_range {
                    return;
                }
                let react_j = element_data::reactivity_index(pj_params);
                let combined = (react_i * react_j).max(0.0).sqrt();
                if combined < params.min_reactivity {
                    return;
                }
                if best.map_or(true, |(_, best_r, _)| r < best_r) {
                    best = Some((j, r, r_min));
                }
            });
        }

        if let Some((j, _r, r_min)) = best {
            new_bonds.push((i, j, handle_i, ctx.handles[j], r_min));
        }
    }

    ctx.bonded_this_pass.clear();
    ctx.bonded_this_pass.resize(n, false);
    for (i_pos, j_pos, a, b, eq_len) in new_bonds {
        if ctx.bonded_this_pass[i_pos] || ctx.bonded_this_pass[j_pos] {
            continue; // one side already claimed by an earlier pair this pass
        }
        ctx.bonded_this_pass[i_pos] = true;
        ctx.bonded_this_pass[j_pos] = true;
        form_bond(ctx, a, b, eq_len, angle_params);
    }
}

/// Harmonic angle-bend force for every currently-tracked angle triple —
/// additive on top of whatever `compute_forces_*`/`compute_bonds` already
/// wrote into each atom's `.force` this step, same "call after, not
/// instead of" contract `compute_bonds` itself uses relative to the LJ
/// kernels. Doesn't form or remove any triples — that's driven entirely
/// by bond-topology changes (`form_bond` on formation, `break_one_bond`/
/// `break_all_bonds` on breaking/despawn) — this only ever applies force
/// to whatever triples already exist right now.
///
/// Standard harmonic angle-bend gradient — the same shape used by every
/// real molecular-mechanics force field's "harmonic angle" term (AMBER,
/// CHARMM, GROMACS, LAMMPS `angle_style harmonic`): `E = 0.5 * k_theta *
/// (theta - theta_eq)^2`, force derived via `d(theta)/d(cos_theta) =
/// -1/sin(theta)`. Reads `ctx.positions`, not `ctx.atoms[..].position` —
/// safe here (unlike the FFI-facing `angle_geometry_at` below, which
/// deliberately uses the latter) because this only ever runs from inside
/// `step()`, strictly after `compute_forces_scalar`/`compute_forces_simd`
/// has already called `refresh_grid` and populated it — same reasoning
/// `compute_bonds`' own Pass 1 already relies on for the same buffer.
///
/// Skips a triple entirely if it's gone (nearly) collinear this step —
/// `sin(theta)` approaching zero makes the gradient *direction* itself
/// ill-defined, not just numerically large; applying nothing for one step
/// is the honest answer, not a force pointing in a near-arbitrary
/// direction blown up by dividing by almost-zero.
pub fn compute_angles(ctx: &mut SimContext, params: &AngleParams) {
    let angles = &ctx.angles;
    let slot_of = &ctx.slot_of;
    let handles = &ctx.handles;
    let positions = &ctx.positions;
    let atoms = &mut ctx.atoms;

    for (vertex, list) in angles.iter() {
        let Some(&vertex_pos) = slot_of.get(vertex.index()) else { continue; };
        let vertex_pos = vertex_pos as usize;
        if handles[vertex_pos] != vertex {
            continue;
        }

        for info in list {
            let Some(&a_pos) = slot_of.get(info.arm_a.index()) else { continue; };
            let Some(&b_pos) = slot_of.get(info.arm_b.index()) else { continue; };
            let a_pos = a_pos as usize;
            let b_pos = b_pos as usize;
            if handles[a_pos] != info.arm_a || handles[b_pos] != info.arm_b {
                continue;
            }

            let pv = positions[vertex_pos];
            let pa = positions[a_pos];
            let pb = positions[b_pos];

            // r1/r2: vectors from the vertex out to each arm — matches
            // the standard MM-textbook "rij"/"rkj" convention with the
            // vertex playing the role of the shared index `j`.
            let r1 = pa - pv;
            let r2 = pb - pv;
            let d1 = r1.length();
            let d2 = r2.length();
            if d1 < 1e-6 || d2 < 1e-6 {
                continue; // coincident atoms — nothing meaningful to bend
            }

            let cos_theta = (r1.dot(r2) / (d1 * d2)).clamp(-1.0, 1.0);
            let sin_theta = (1.0 - cos_theta * cos_theta).max(1e-8).sqrt();
            if sin_theta < 1e-2 {
                continue; // near-collinear: gradient direction ill-defined
            }

            let theta = cos_theta.acos();
            let coef = params.k_theta * (theta - info.equilibrium_angle) / sin_theta;

            // F_arm_a = coef * (r2/(d1*d2) - r1*cos_theta/d1^2)
            // F_arm_b = coef * (r1/(d1*d2) - r2*cos_theta/d2^2)
            // F_vertex = -(F_arm_a + F_arm_b)   — Newton's third law; the
            // vertex absorbs whatever the two arms are given so the net
            // force this triple contributes sums to zero.
            let f_a = r2 * (coef / (d1 * d2)) - r1 * (coef * cos_theta / (d1 * d1));
            let f_b = r1 * (coef / (d1 * d2)) - r2 * (coef * cos_theta / (d2 * d2));
            let f_v = -(f_a + f_b);

            let fa = Vec3::new(atoms[a_pos].force[0], atoms[a_pos].force[1], atoms[a_pos].force[2]) + f_a;
            atoms[a_pos].force = [fa.x, fa.y, fa.z];
            let fb = Vec3::new(atoms[b_pos].force[0], atoms[b_pos].force[1], atoms[b_pos].force[2]) + f_b;
            atoms[b_pos].force = [fb.x, fb.y, fb.z];
            let fv = Vec3::new(atoms[vertex_pos].force[0], atoms[vertex_pos].force[1], atoms[vertex_pos].force[2]) + f_v;
            atoms[vertex_pos].force = [fv.x, fv.y, fv.z];
        }
    }
}

/// How many angle triples this atom is currently the **vertex** of (see
/// `AngleInfo`'s own doc — this does not count triples where the atom is
/// only an arm). 0 for a stale handle or an atom that isn't a vertex
/// right now (needs >= 2 simultaneous bonds to be one) — same "nothing
/// there" contract `bond_count` already uses for stale handles.
pub fn angle_count(ctx: &SimContext, h: AtomHandle) -> usize {
    let Some(pos) = resolve(ctx, h) else { return 0; };
    ctx.angles.get(ctx.handles[pos]).map_or(0, |list| list.len())
}

/// This atom's `index`-th angle triple's two arm atoms — `h` itself is
/// always the vertex. `None` for a stale handle, an atom that isn't a
/// vertex right now, or an out-of-range index — same "nothing there"
/// case, deliberately not distinguished, matching `bond_partner_at`.
/// Iterate `0..angle_count(ctx, h)` to walk every triple centered here.
pub fn angle_arms_at(ctx: &SimContext, h: AtomHandle, index: usize) -> Option<(AtomHandle, AtomHandle)> {
    let pos = resolve(ctx, h)?;
    let list = ctx.angles.get(ctx.handles[pos])?;
    let info = list.get(index)?;
    Some((
        AtomHandle { index: info.arm_a.index(), generation: info.arm_a.generation() },
        AtomHandle { index: info.arm_b.index(), generation: info.arm_b.generation() },
    ))
}

/// Rest angle and current live angle of this atom's `index`-th angle
/// triple — same indexing `angle_arms_at` uses. `None` for a stale
/// handle, a non-vertex atom, an out-of-range index, or — defensively,
/// shouldn't happen in practice since `break_one_bond`/`break_all_bonds`
/// keep every triple's arms in sync with actual bond liveness — an
/// unresolvable arm.
///
/// Reads positions straight from `ctx.atoms`, not the `ctx.positions`
/// scratch buffer — same reasoning `bond_geometry_at` already documents:
/// that buffer only exists once a force kernel has run at least once, so
/// using it here would make this accessor's correctness depend on
/// step-ordering a caller shouldn't have to know about.
pub fn angle_geometry_at(ctx: &SimContext, h: AtomHandle, index: usize) -> Option<AngleGeometry> {
    let pos = resolve(ctx, h)?;
    let list = ctx.angles.get(ctx.handles[pos])?;
    let info = list.get(index)?;

    let &a_pos = ctx.slot_of.get(info.arm_a.index())?;
    let &b_pos = ctx.slot_of.get(info.arm_b.index())?;
    let a_pos = a_pos as usize;
    let b_pos = b_pos as usize;
    if ctx.handles[a_pos] != info.arm_a || ctx.handles[b_pos] != info.arm_b {
        return None;
    }

    let pv = ctx.atoms[pos].position;
    let pa = ctx.atoms[a_pos].position;
    let pb = ctx.atoms[b_pos].position;

    let r1 = Vec3::new(pa[0] - pv[0], pa[1] - pv[1], pa[2] - pv[2]);
    let r2 = Vec3::new(pb[0] - pv[0], pb[1] - pv[1], pb[2] - pv[2]);
    let d1 = r1.length();
    let d2 = r2.length();
    let current_angle = if d1 < 1e-6 || d2 < 1e-6 {
        0.0 // degenerate — coincident atoms, nothing meaningful to report
    } else {
        (r1.dot(r2) / (d1 * d2)).clamp(-1.0, 1.0).acos()
    };

    Some(AngleGeometry {
        equilibrium_angle: info.equilibrium_angle,
        current_angle,
    })
}

/// Total kinetic energy of all currently-live atoms, in eV.
pub fn kinetic_energy(ctx: &SimContext) -> f32 {
    ctx.atoms
        .iter()
        .map(|a| {
            let m = eff_mass(a.mass);
            let v2 = a.velocity[0] * a.velocity[0]
                + a.velocity[1] * a.velocity[1]
                + a.velocity[2] * a.velocity[2];
            0.5 * m * v2
        })
        .sum()
}

/// Current temperature estimate in Kelvin, from equipartition
/// (3 translational degrees of freedom per atom): `KE = 1.5 * N * k_B * T`.
pub fn temperature(ctx: &SimContext) -> f32 {
    let n = ctx.atoms.len();
    if n == 0 {
        return 0.0;
    }
    2.0 * kinetic_energy(ctx) / (3.0 * n as f32 * K_B_EV_PER_K)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn spawn_hydrogen_grid(ctx: &mut SimContext, n: usize, spacing: f32) {
        let side = (n as f32).cbrt().ceil() as usize;
        for i in 0..n {
            let x = (i % side) as f32 * spacing;
            let y = ((i / side) % side) as f32 * spacing;
            let z = (i / (side * side)) as f32 * spacing;
            spawn_atom(ctx, 1, [x, y, z]);
        }
    }

    /// Calls the real `compute_forces_scalar`/`compute_forces_simd`
    /// directly — validates the exact functions everything else uses.
    #[test]
    fn simd_batched_forces_match_scalar_reference() {
        for &n in &[5usize, 13, 37, 101] {
            let mut ctx_scalar = SimContext::new(10.0);
            spawn_hydrogen_grid(&mut ctx_scalar, n, 3.0);
            compute_forces_scalar(&mut ctx_scalar, 10.0);

            let mut ctx_simd = SimContext::new(10.0);
            spawn_hydrogen_grid(&mut ctx_simd, n, 3.0);
            compute_forces_simd(&mut ctx_simd, 10.0);

            for i in 0..n {
                for k in 0..3 {
                    let got = ctx_simd.atoms[i].force[k];
                    let want = ctx_scalar.atoms[i].force[k];
                    assert!(
                        (got - want).abs() < 1e-4,
                        "atom {i} component {k}: simd={got} scalar={want} (n={n})"
                    );
                }
            }
        }
    }

    #[test]
    fn despawn_is_safe_and_keeps_remaining_atoms_correct() {
        let mut ctx = SimContext::new(10.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 2, [1.0, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 3, [2.0, 0.0, 0.0]);
        assert_eq!(atom_count(&ctx), 3);

        assert!(despawn_atom(&mut ctx, b));
        assert_eq!(atom_count(&ctx), 2);

        assert!(!despawn_atom(&mut ctx, b));
        assert!(get_atom(&ctx, b).is_none());

        let got_a = get_atom(&ctx, a).expect("a should still be alive");
        assert_eq!(got_a.atomic_number, 1);
        let got_c = get_atom(&ctx, c).expect("c should still be alive");
        assert_eq!(got_c.atomic_number, 3);
    }

    #[test]
    fn bonds_form_between_reactive_atoms_at_equilibrium_distance() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);

        compute_forces_scalar(&mut ctx, 10.0);
        compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());

        assert!(is_bonded(&ctx, a));
        assert!(is_bonded(&ctx, b));
        assert_eq!(bond_count(&ctx, a), 1);
        assert_eq!(bond_count(&ctx, b), 1);
        assert_eq!(bond_partner_at(&ctx, a, 0), Some(b));
        assert_eq!(bond_partner_at(&ctx, b, 0), Some(a));
    }

    #[test]
    fn bonds_do_not_form_for_inert_helium() {
        let mut ctx = SimContext::new(10.0);
        let sigma_he = 2.551_f32;
        let r_min = sigma_he * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 2, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 2, [r_min, 0.0, 0.0]);

        compute_forces_scalar(&mut ctx, 10.0);
        compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());

        assert!(!is_bonded(&ctx, a));
        assert!(!is_bonded(&ctx, b));
    }

    #[test]
    fn bonds_break_when_stretched_too_far() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        compute_forces_scalar(&mut ctx, 10.0);
        compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        assert!(is_bonded(&ctx, a));

        // Nothing's been despawned, so spawn order still matches array
        // order here — direct position writes (not a real Verlet step,
        // this test only cares about the break condition itself).
        ctx.atoms[0].position = [0.0, 0.0, 0.0];
        ctx.atoms[1].position = [100.0, 0.0, 0.0];

        compute_forces_scalar(&mut ctx, 10.0); // refreshes ctx.positions
        compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());

        assert!(!is_bonded(&ctx, a));
        assert!(!is_bonded(&ctx, b));
    }

    #[test]
    fn despawn_cleans_up_bond() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        compute_forces_scalar(&mut ctx, 10.0);
        compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        assert!(is_bonded(&ctx, a));

        despawn_atom(&mut ctx, a);
        assert!(!is_bonded(&ctx, b));
    }

    /// A at the origin, B and C both within bond range of A but 2*r_min
    /// apart from *each other* (outside bond range), so only A-B and A-C
    /// are ever candidates. Pass 2 caps new edges to one per atom per
    /// call (see `compute_bonds` docs) — call it a few times so A picks
    /// up both, the way it actually would across a few real `step()`s.
    #[test]
    fn atom_can_hold_multiple_simultaneous_bonds() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 1, [-r_min, 0.0, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }

        assert_eq!(bond_count(&ctx, a), 2, "A should end up bonded to both B and C");
        assert!(is_bonded(&ctx, b));
        assert!(is_bonded(&ctx, c));
    }

    #[test]
    fn breaking_one_edge_leaves_others_intact() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 1, [-r_min, 0.0, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }
        assert_eq!(bond_count(&ctx, a), 2);

        // Nothing's been despawned, so spawn order still matches array
        // order (same convention `bonds_break_when_stretched_too_far`
        // relies on) — stretch only the A-B edge, leave C untouched.
        ctx.atoms[0].position = [0.0, 0.0, 0.0];   // A
        ctx.atoms[1].position = [500.0, 0.0, 0.0]; // B — far out of range
        // C (index 2) stays where it was.

        compute_forces_scalar(&mut ctx, 10.0); // refreshes ctx.positions
        compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());

        assert!(!is_bonded(&ctx, b));
        assert_eq!(bond_count(&ctx, a), 1, "A should still hold its bond to C");
        assert_eq!(bond_partner_at(&ctx, a, 0), Some(c));
    }

    /// The specific bug a naive one-bond-per-atom port of the old
    /// despawn cleanup would reintroduce: D bonded to both A and E,
    /// despawning A must remove only the D<->A edge, not D's whole bond
    /// list (which is what blindly reusing the old `ctx.bonds.remove(x)`-
    /// wipes-the-whole-entry approach would do to D's *surviving* bond).
    #[test]
    fn despawn_does_not_wipe_a_survivors_other_bonds() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let d = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let a = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        let e = spawn_atom(&mut ctx, 1, [-r_min, 0.0, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }
        assert_eq!(bond_count(&ctx, d), 2);

        despawn_atom(&mut ctx, a);

        assert_eq!(bond_count(&ctx, d), 1, "D's bond to E must survive A despawning");
        assert_eq!(bond_partner_at(&ctx, d, 0), Some(e));
    }

    #[test]
    fn bond_geometry_reports_equilibrium_and_tracks_live_length() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);

        compute_forces_scalar(&mut ctx, 10.0);
        compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        assert!(is_bonded(&ctx, a));

        let geo = bond_geometry_at(&ctx, a, 0).expect("a should have a bond at index 0");
        assert!((geo.equilibrium_length - r_min).abs() < 1e-3);
        assert!(
            (geo.current_length - r_min).abs() < 1e-3,
            "current_length should match the spawn separation before anything moves"
        );

        // Move B directly (no re-step needed — this reads ctx.atoms, not
        // the ctx.positions scratch buffer, so it doesn't depend on a
        // force kernel having run since the move) and confirm
        // current_length tracks the live separation while
        // equilibrium_length — a property of the bond, not the atoms'
        // current positions — stays exactly what it was at formation.
        ctx.atoms[1].position = [r_min * 1.3, 0.0, 0.0];
        let geo2 = bond_geometry_at(&ctx, a, 0).expect("still bonded, just stretched");
        assert!(
            (geo2.equilibrium_length - r_min).abs() < 1e-3,
            "equilibrium_length shouldn't change just because the atom moved"
        );
        assert!((geo2.current_length - r_min * 1.3).abs() < 1e-3);

        // Symmetric from B's side too — same edge, same numbers.
        let geo_from_b = bond_geometry_at(&ctx, b, 0).expect("b's side of the same edge");
        assert!((geo_from_b.equilibrium_length - geo2.equilibrium_length).abs() < 1e-6);
        assert!((geo_from_b.current_length - geo2.current_length).abs() < 1e-6);
    }

    #[test]
    fn bond_geometry_is_none_for_stale_unbonded_or_out_of_range() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        compute_forces_scalar(&mut ctx, 10.0);
        compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        assert!(is_bonded(&ctx, a));

        // Out-of-range index on a real, bonded atom.
        assert!(bond_geometry_at(&ctx, a, 1).is_none());

        // Unbonded atom, spawned far enough away to never be a bond
        // candidate for anything above.
        let c = spawn_atom(&mut ctx, 1, [1000.0, 0.0, 0.0]);
        assert!(bond_geometry_at(&ctx, c, 0).is_none());

        // Stale handle: despawn b, then query the handle we held for it.
        despawn_atom(&mut ctx, b);
        assert!(bond_geometry_at(&ctx, b, 0).is_none());
    }

    /// Exercises `MidVec`'s heap-spill path directly, not just its inline
    /// one: a hub atom picks up `MAX_INLINE_BONDS + 1` simultaneous edges,
    /// one more than fits inline, so `push_bond_edge`'s
    /// `MidVec::get_mut(owner).push(...)` on the 7th edge must trigger a
    /// real reallocation. Satellites sit on a pentagonal-bipyramid
    /// arrangement at `1.1 * r_min` from the hub (inside `bond_range =
    /// 1.15 * r_min`) with every satellite-satellite pair at least ~1.29
    /// (adjacent equatorial) to 2.2 (poles) times `r_min` apart — safely
    /// outside `bond_range`, so the only edges that can ever form are
    /// hub-satellite ones, never satellite-satellite. Pass 2 caps the hub
    /// to one *new* edge per `compute_bonds` call (see module docs), so
    /// forming all 7 needs at least 7 calls; run well past that for
    /// margin, matching `atom_can_hold_multiple_simultaneous_bonds`'s own
    /// over-iterate-for-safety approach.
    #[test]
    fn bond_list_spills_to_heap_past_max_inline_bonds() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let radius = r_min * 1.1;

        let hub = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let mut satellites = Vec::new();
        satellites.push(spawn_atom(&mut ctx, 1, [0.0, 0.0, radius]));
        satellites.push(spawn_atom(&mut ctx, 1, [0.0, 0.0, -radius]));
        for k in 0..5 {
            let theta = (k as f32) * core::f32::consts::TAU / 5.0;
            satellites.push(spawn_atom(&mut ctx, 1, [
                radius * theta.cos(),
                radius * theta.sin(),
                0.0,
            ]));
        }
        assert_eq!(satellites.len(), MAX_INLINE_BONDS + 1, "test setup: exactly one more satellite than fits inline");

        for _ in 0..(MAX_INLINE_BONDS + 1) * 2 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }

        assert_eq!(
            bond_count(&ctx, hub),
            MAX_INLINE_BONDS + 1,
            "hub should have bonded to every satellite, one past inline capacity"
        );

        // Reach past the public API to confirm this genuinely exercised
        // the heap path, not just that the count happens to be right —
        // `tests` is a submodule of `simulation`, so it can see `bonds`
        // (private to this module) same as the rest of this file already
        // does via `resolve`/etc.
        let hub_pos = resolve(&ctx, hub).expect("hub is live");
        let hub_list = ctx.bonds.get(ctx.handles[hub_pos]).expect("hub has bonds");
        assert!(hub_list.spilled(), "7 bonds on a 6-inline MidVec must have spilled to the heap");

        // Every satellite still resolves correctly post-spill — the
        // reallocation must not have corrupted or dropped any edge.
        for i in 0..bond_count(&ctx, hub) {
            let partner = bond_partner_at(&ctx, hub, i).expect("index within bond_count must resolve");
            assert!(satellites.contains(&partner), "every partner must be one of the satellites we actually spawned");
            let geo = bond_geometry_at(&ctx, hub, i).expect("geometry must resolve for every valid index");
            assert!((geo.current_length - radius).abs() < 1e-2, "nothing moved, so current_length should still match spawn radius");
        }

        // Satellite-satellite edges never formed — confirms the geometry
        // this test relies on actually held.
        for &s in &satellites {
            assert_eq!(bond_count(&ctx, s), 1, "each satellite should be bonded only to the hub, never to another satellite");
        }
    }

    #[test]
    fn handles_scratch_matches_atoms_ptr_order_and_length() {
        let mut ctx = SimContext::new(10.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 6, [10.0, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 8, [20.0, 0.0, 0.0]);

        // Despawn the middle one via swap-remove, same as any other test in
        // this file relies on — the real question here is whether the
        // handles scratch buffer tracks that reordering correctly, not just
        // whether it's right for a fresh, never-despawned set.
        despawn_atom(&mut ctx, b);

        let handles_ptr = refresh_handles_scratch(&mut ctx);
        let count = ctx.atoms.len();
        assert_eq!(count, 2, "a and c should remain live after b's despawn");

        let handles: &[AtomHandle] = unsafe { core::slice::from_raw_parts(handles_ptr, count) };
        let atoms_ptr = atoms_ptr(&ctx);
        let atoms: &[AtomState] = unsafe { core::slice::from_raw_parts(atoms_ptr, count) };

        assert_eq!(handles.len(), atoms.len(), "same dense array length as atoms_ptr");

        // Every index i's handle must correspond to the SAME atom as index i
        // in the atoms array — resolve each handle independently via
        // TryGetAtom-equivalent logic and confirm the position/atomic_number
        // match what's sitting at that same index in the atoms array.
        for i in 0..count {
            let resolved_pos = resolve(&ctx, handles[i]).expect("every handle in the scratch buffer must resolve");
            assert_eq!(resolved_pos, i, "handles[i] must resolve back to position i — same dense order as atoms_ptr");
            assert_eq!(ctx.atoms[resolved_pos].atomic_number, atoms[i].atomic_number);
        }

        // The despawned handle must not appear anywhere in the refreshed buffer.
        assert!(!handles.contains(&b), "a despawned atom's stale handle must not appear in the refreshed scratch buffer");
        assert!(handles.contains(&a) && handles.contains(&c), "both surviving atoms' handles must be present");
    }

    // ── Angular bonding ─────────────────────────────────────────────────

    /// A at the origin, B on +x, C on +y — both within bond range of A,
    /// and (at distance r_min*sqrt(2), well past BondParams::default()'s
    /// 1.15*r_min range) permanently outside bond range of *each other*.
    /// So A is the only atom that can ever end up with >= 2 bonds, and
    /// therefore the only possible vertex — same "cap candidates to a
    /// known small set" reasoning atom_can_hold_multiple_simultaneous_bonds
    /// already uses, just at 90 degrees apart instead of 180 so the
    /// resulting triple isn't the degenerate collinear case.
    #[test]
    fn angle_triple_forms_when_second_bond_completes() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 1, [0.0, r_min, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }

        assert_eq!(bond_count(&ctx, a), 2, "A should end up bonded to both B and C");
        assert!(is_bonded(&ctx, b));
        assert!(is_bonded(&ctx, c));

        assert_eq!(angle_count(&ctx, a), 1, "A should be the vertex of exactly one triple: B-A-C");
        assert_eq!(angle_count(&ctx, b), 0, "B only has one bond — can't be a vertex of anything");
        assert_eq!(angle_count(&ctx, c), 0, "C only has one bond — can't be a vertex of anything");

        let (arm_a, arm_b) = angle_arms_at(&ctx, a, 0).expect("a is the vertex of one triple");
        let arms = [arm_a, arm_b];
        assert!(
            arms.contains(&b) && arms.contains(&c),
            "the triple's two arms must be exactly {{B, C}} in either order, got {arms:?}"
        );
    }

    #[test]
    fn angle_geometry_reports_equilibrium_and_tracks_live_angle() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        spawn_atom(&mut ctx, 1, [0.0, r_min, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }
        assert_eq!(angle_count(&ctx, a), 1);

        let geo = angle_geometry_at(&ctx, a, 0).expect("a has one triple at index 0");
        assert!(
            (geo.equilibrium_angle - AngleParams::default().equilibrium_angle).abs() < 1e-6,
            "equilibrium_angle should be exactly whatever AngleParams::default() baked in at formation time"
        );
        assert!(
            (geo.current_angle - core::f32::consts::FRAC_PI_2).abs() < 1e-3,
            "B on +x and C on +y from A is a 90-degree angle, got {} rad",
            geo.current_angle
        );

        // Move C directly (reads ctx.atoms, not the ctx.positions scratch
        // buffer — same reasoning
        // bond_geometry_reports_equilibrium_and_tracks_live_length already
        // relies on for not needing a re-step first) to put B-A-C at 180
        // degrees and confirm current_angle tracks it while
        // equilibrium_angle — a property of the triple, not the atoms'
        // live positions — stays exactly what it was at formation. Still
        // a valid *query* at this angle: only compute_angles' *force*
        // application skips the near-collinear case, not this accessor.
        ctx.atoms[2].position = [-r_min, 0.0, 0.0]; // C, spawn index 2
        let geo2 = angle_geometry_at(&ctx, a, 0).expect("still a vertex, just moved");
        assert!(
            (geo2.equilibrium_angle - geo.equilibrium_angle).abs() < 1e-6,
            "equilibrium_angle shouldn't change just because an arm moved"
        );
        assert!(
            (geo2.current_angle - core::f32::consts::PI).abs() < 1e-3,
            "B on +x and C on -x from A is a 180-degree angle, got {} rad",
            geo2.current_angle
        );
    }

    /// Physical correctness check for `compute_angles`' force direction: a
    /// 90-degree B-A-C angle sits *below* `AngleParams::default()`'s
    /// 109.5-degree equilibrium, so the restoring force must widen it —
    /// push B and C further apart around A, not closer together. Checked
    /// via each arm's actual force components (not via which one landed
    /// as `AngleInfo::arm_a`/`arm_b` — that assignment order isn't
    /// something a caller should have to predict, see
    /// `angle_triple_forms_when_second_bond_completes`), plus a Newton's-
    /// third-law sanity check that the vertex absorbs exactly the
    /// negative sum of what the two arms are given.
    #[test]
    fn angle_force_widens_an_angle_below_equilibrium() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 1, [0.0, r_min, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }
        assert_eq!(angle_count(&ctx, a), 1, "test setup: A must be the one vertex");

        // Zero out whatever LJ/bond-spring force the loop above already
        // left in .force — compute_angles is additive on top of that by
        // design (matches compute_bonds' own relationship to the LJ
        // kernels), but this test wants to isolate the angle term alone.
        for atom in ctx.atoms.iter_mut() {
            atom.force = [0.0; 3];
        }

        compute_angles(&mut ctx, &AngleParams::default());

        let a_pos = resolve(&ctx, a).expect("a is live");
        let b_pos = resolve(&ctx, b).expect("b is live");
        let c_pos = resolve(&ctx, c).expect("c is live");

        let f_a = ctx.atoms[a_pos].force;
        let f_b = ctx.atoms[b_pos].force;
        let f_c = ctx.atoms[c_pos].force;

        // 90 degrees < 109.5-degree equilibrium — the angle must widen,
        // so B (sitting on +x from A) must be pushed toward -y (away
        // from C, which sits on +y from A) and C must be pushed toward
        // -x (away from B).
        assert!(f_b[1] < -1e-4, "B should be pushed toward -y (away from C), got force {f_b:?}");
        assert!(f_c[0] < -1e-4, "C should be pushed toward -x (away from B), got force {f_c:?}");

        // Newton's third law: the vertex absorbs exactly the negative sum
        // of what the two arms were given — true by construction
        // (f_v = -(f_a+f_b) in compute_angles), asserted directly here as
        // a conservation sanity check independent of the direction check
        // above.
        for k in 0..3 {
            assert!(
                (f_a[k] + f_b[k] + f_c[k]).abs() < 1e-4,
                "component {k}: forces on the vertex and both arms must sum to ~zero"
            );
        }
    }

    /// The angle-bend gradient's genuine singularity: at (near-)exactly
    /// 180 degrees, `sin(theta) -> 0` makes the force *direction* itself
    /// ill-defined, not just numerically large. `compute_angles` must
    /// skip the triple entirely rather than emit a force blown up by
    /// dividing by almost-zero.
    #[test]
    fn angle_force_skips_near_collinear_triples() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 1, [0.0, r_min, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }
        assert_eq!(angle_count(&ctx, a), 1);

        // Move C to exactly opposite B across A — B-A-C now sits at 180
        // degrees. Unlike angle_geometry_at (which reads ctx.atoms and so
        // sees a direct position write immediately), compute_angles reads
        // ctx.positions by design (see its own doc) — a re-step is needed
        // to refresh it, same "compute_forces_scalar refreshes
        // ctx.positions" convention bonds_break_when_stretched_too_far
        // already relies on.
        ctx.atoms[2].position = [-r_min, 0.0, 0.0]; // C, spawn index 2
        compute_forces_scalar(&mut ctx, 10.0); // refreshes ctx.positions

        for atom in ctx.atoms.iter_mut() {
            atom.force = [0.0; 3];
        }
        compute_angles(&mut ctx, &AngleParams::default());

        let a_pos = resolve(&ctx, a).expect("a is live");
        let b_pos = resolve(&ctx, b).expect("b is live");
        let c_pos = resolve(&ctx, c).expect("c is live");
        assert_eq!(ctx.atoms[a_pos].force, [0.0; 3], "near-collinear triple must contribute zero force, not a blown-up one");
        assert_eq!(ctx.atoms[b_pos].force, [0.0; 3]);
        assert_eq!(ctx.atoms[c_pos].force, [0.0; 3]);
    }

    #[test]
    fn angle_triple_is_removed_when_its_supporting_bond_breaks() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 1, [0.0, r_min, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }
        assert_eq!(angle_count(&ctx, a), 1, "test setup: one triple before breaking anything");

        // Stretch A-B (spawn order still matches array order — same
        // convention breaking_one_edge_leaves_others_intact already
        // relies on) far out of range; C stays put.
        ctx.atoms[1].position = [500.0, 0.0, 0.0]; // B, spawn index 1
        compute_forces_scalar(&mut ctx, 10.0); // refreshes ctx.positions
        compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());

        assert!(!is_bonded(&ctx, b), "test setup: A-B must actually have broken");
        assert_eq!(bond_count(&ctx, a), 1, "A should still hold its bond to C");
        assert_eq!(
            angle_count(&ctx, a),
            0,
            "the only triple A was a vertex of used B as an arm — it must be gone now that A-B broke"
        );
        assert_eq!(angle_count(&ctx, b), 0);
        assert_eq!(angle_count(&ctx, c), 0);
    }

    #[test]
    fn angle_triple_is_removed_when_the_vertex_despawns() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 1, [0.0, r_min, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }
        assert_eq!(angle_count(&ctx, a), 1);

        despawn_atom(&mut ctx, a);

        // B and C survive (A despawning only removes A's own bonds and
        // angles), but neither was ever a vertex of anything to begin
        // with (they only ever held one bond each), so there's nothing
        // left to report for either.
        assert_eq!(angle_count(&ctx, b), 0);
        assert_eq!(angle_count(&ctx, c), 0);
    }

    #[test]
    fn angle_triple_is_removed_when_an_arm_despawns() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 1, [0.0, r_min, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }
        assert_eq!(angle_count(&ctx, a), 1);

        despawn_atom(&mut ctx, b);

        assert!(is_bonded(&ctx, c), "A-C should survive B despawning");
        assert_eq!(
            angle_count(&ctx, a),
            0,
            "the triple's arm (B) is gone — A only has one bond left (to C), not enough to be a vertex of anything"
        );
    }

    #[test]
    fn angle_accessors_are_none_for_stale_non_vertex_or_out_of_range() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let a = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let b = spawn_atom(&mut ctx, 1, [r_min, 0.0, 0.0]);
        let c = spawn_atom(&mut ctx, 1, [0.0, r_min, 0.0]);

        for _ in 0..4 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }
        assert_eq!(angle_count(&ctx, a), 1);

        // Out-of-range index on a real vertex.
        assert!(angle_arms_at(&ctx, a, 1).is_none());
        assert!(angle_geometry_at(&ctx, a, 1).is_none());

        // A real, live atom that's simply never a vertex (only one bond).
        assert_eq!(angle_count(&ctx, b), 0);
        assert!(angle_arms_at(&ctx, b, 0).is_none());
        assert!(angle_geometry_at(&ctx, b, 0).is_none());

        // An atom with zero bonds at all, spawned far enough away to
        // never be a candidate for anything above.
        let d = spawn_atom(&mut ctx, 1, [1000.0, 0.0, 0.0]);
        assert_eq!(angle_count(&ctx, d), 0);
        assert!(angle_arms_at(&ctx, d, 0).is_none());
        assert!(angle_geometry_at(&ctx, d, 0).is_none());

        // Stale handle: despawn c, then query the handle we already held
        // for it.
        despawn_atom(&mut ctx, c);
        assert_eq!(angle_count(&ctx, c), 0);
        assert!(angle_arms_at(&ctx, c, 0).is_none());
        assert!(angle_geometry_at(&ctx, c, 0).is_none());
    }

    /// Exercises `MidVec`'s heap-spill path for angle triples specifically.
    /// `MAX_INLINE_ANGLES = 6` matches `MAX_INLINE_BONDS`'s own 4-bond
    /// documented ceiling — `C(4,2) = 6` fits inline exactly — so this
    /// needs a vertex holding *more* than 4 bonds to force an angle-side
    /// spill: 5 simultaneous bonds -> `C(5,2) = 10` triples, past the
    /// 6-inline cap. Reuses
    /// `bond_list_spills_to_heap_past_max_inline_bonds`'s own satellite
    /// geometry (already verified there that every satellite-satellite
    /// pair stays outside bond_range, so only hub-satellite edges can
    /// ever form) — just three of its five equatorial points plus both
    /// poles (five satellites total) instead of all seven, since five
    /// bonds is already enough to spill the *angle* list even though it
    /// isn't enough to spill the *bond* list (5 < `MAX_INLINE_BONDS`'s
    /// own 6).
    #[test]
    fn angle_list_spills_to_heap_past_max_inline_angles() {
        let mut ctx = SimContext::new(10.0);
        let sigma_h = 2.928_f32;
        let r_min = sigma_h * 2f32.powf(1.0 / 6.0);
        let radius = r_min * 1.1;

        let hub = spawn_atom(&mut ctx, 1, [0.0, 0.0, 0.0]);
        let mut satellites = Vec::new();
        satellites.push(spawn_atom(&mut ctx, 1, [0.0, 0.0, radius]));
        satellites.push(spawn_atom(&mut ctx, 1, [0.0, 0.0, -radius]));
        for k in 0..3 {
            let theta = (k as f32) * core::f32::consts::TAU / 5.0;
            satellites.push(spawn_atom(&mut ctx, 1, [radius * theta.cos(), radius * theta.sin(), 0.0]));
        }
        assert_eq!(satellites.len(), 5, "test setup: exactly enough bonds to spill C(5,2)=10 past the 6-inline angle cap");

        for _ in 0..10 {
            compute_forces_scalar(&mut ctx, 10.0);
            compute_bonds(&mut ctx, &BondParams::default(), &AngleParams::default());
        }

        assert_eq!(bond_count(&ctx, hub), 5, "hub should have bonded to all five satellites");
        assert_eq!(angle_count(&ctx, hub), 10, "C(5,2) = 10 angle triples, one per pair of the hub's five bonds");

        let hub_pos = resolve(&ctx, hub).expect("hub is live");
        let hub_angles = ctx.angles.get(ctx.handles[hub_pos]).expect("hub has angle triples");
        assert!(hub_angles.spilled(), "10 triples on a 6-inline MidVec must have spilled to the heap");

        // Every triple still resolves correctly post-spill, and both its
        // arms are always two of the five real satellites (never the hub
        // itself, never anything that isn't actually one of these five).
        for i in 0..angle_count(&ctx, hub) {
            let (arm_a, arm_b) = angle_arms_at(&ctx, hub, i).expect("index within angle_count must resolve");
            assert!(satellites.contains(&arm_a) && satellites.contains(&arm_b), "both arms must be real satellites");
            assert_ne!(arm_a, arm_b, "a triple's two arms must be distinct atoms");
        }
    }
}

