//! chemistry_core — Unity FFI simulation library.
//!
//! ## Rust owns the atom array
//!
//! `SimContext` owns the atom storage itself now — atoms are spawned and
//! despawned through the FFI surface below, not passed in as a pointer
//! each call. This is deliberate: for Rust to actually be the source of
//! truth for atom identity (not just bookkeeping, but *enforcing* it),
//! and for this crate to work unmodified in a non-DOTS engine with zero
//! concept of a stable entity ID, the array has to live here.
//!
//! ## AtomState layout (C-compatible, 48 bytes)
//!
//! ```text
//!  offset  field           type
//!  ------  -----           ----
//!   0      position        [f32; 3]   world-space (Angstroms)
//!  12      velocity        [f32; 3]
//!  24      force           [f32; 3]   accumulated this frame, cleared per step
//!  36      mass            f32        atomic mass (amu)
//!  40      radius          f32        van der Waals radius (pm)
//!  44      atomic_number   i32        element Z — LJ param lookup key
//! ```
//!
//! ## AtomHandle layout (C-compatible, 8 bytes)
//!
//! ```text
//!  offset  field        type
//!  ------  -----        ----
//!   0      index        u32
//!   4      generation   u32
//! ```
//!
//! Chemistry_core's own type, not `mid_collections::GenerationalIndex`
//! directly — that type isn't `#[repr(C)]` (no layout guarantee across
//! FFI) and deliberately has no public constructor from raw parts (it can
//! only ever be *held*, obtained from `GenerationalIndexAllocator::allocate`,
//! never synthesized) — by design, so nothing can forge a handle. `AtomHandle`
//! is what actually crosses the FFI boundary; converted to/from the real
//! `GenerationalIndex` only inside Rust, by comparing against a handle this
//! crate already legitimately obtained, never by constructing one from the
//! raw ints C# sends back. Same reasoning `bevy_ecs::Entity` uses for being
//! `#[repr(C, align(8))]` in the first place — checked against the real
//! source, not assumed.
//!
//! ## Usage from C#
//!
//! ```csharp
//! FFIBridge.ValidateStructSizes();               // call once in Awake
//! IntPtr ctx = FFIBridge.chem_context_create(10.0f);
//! AtomHandle h = FFIBridge.chem_spawn_atom(ctx, 1, 0f, 0f, 0f); // Z=1 (hydrogen)
//! FFIBridge.chem_init(ctx, 300.0f, 42UL);
//! // per frame:
//! FFIBridge.chem_step(ctx, Time.deltaTime, 10.0f); // dt is femtoseconds, not
//!                                                   // seconds — see chem_step's
//!                                                   // own doc before wiring this
//!                                                   // straight to Time.deltaTime
//! IntPtr atoms = FFIBridge.chem_atoms_ptr(ctx);   // re-fetch every frame, don't cache
//! int n = FFIBridge.chem_atom_count(ctx);
//! // ... draw n AtomState structs starting at `atoms` ...
//! FFIBridge.chem_despawn_atom(ctx, h);
//! // on teardown (e.g. OnDestroy):
//! FFIBridge.chem_context_destroy(ctx);
//! ```
//!
//! `chem_atoms_ptr`'s array order is **not** stable across despawns
//! (swap-remove keeps the live set dense) — fine for "draw N points
//! somewhere" (this is a `GraphicsBuffer` + `DrawMesh` billboard renderer,
//! no per-atom GameObjects, so nothing needs a stable array slot to track
//! a specific atom across frames), not fine for "this array position is
//! always atom #47." Use `AtomHandle` + `chem_get_atom` for that.

mod simulation;
mod spatial_hash;
mod element_data;
mod fx_hash;

pub use simulation::*;
pub use element_data::{ElementParams, params, reactivity_index, bond_strength, make_atom};

/// FFI-safe atom handle. See module docs for why this exists instead of
/// passing `mid_collections::GenerationalIndex` directly.
#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct AtomHandle {
    pub index: u32,
    pub generation: u32,
}

/// C-compatible atom state. Must match C# `AtomData` exactly.
/// Verified at compile time (48 bytes) and at runtime via `chem_struct_size()`.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default)]
pub struct AtomState {
    pub position:      [f32; 3],   //  0
    pub velocity:      [f32; 3],   // 12
    pub force:         [f32; 3],   // 24
    pub mass:          f32,         // 36
    pub radius:        f32,         // 40
    pub atomic_number: i32,         // 44
}

const _: () = assert!(core::mem::size_of::<AtomState>() == 48);
const _: () = assert!(core::mem::size_of::<AtomHandle>() == 8);

/// Rest length and current live separation of one bond edge — what C#
/// needs to draw a stick between two bonded atoms, or show one straining
/// before it snaps. See `chem_bond_geometry_at`.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default)]
pub struct BondGeometry {
    pub equilibrium_length: f32,   //  0
    pub current_length:     f32,   //  4
}

const _: () = assert!(core::mem::size_of::<BondGeometry>() == 8);

// ── FFI surface ───────────────────────────────────────────────────────────────

/// Create a persistent simulation context: owns the atom array, the
/// spatial hash grid, and the scratch buffers the force kernels need.
/// `cutoff_hint` just sizes the initial grid — passing 0.0 is fine,
/// `chem_step` falls back to 10.0 either way.
///
/// Create once (e.g. in Awake), reuse for the lifetime of the simulation,
/// free exactly once with `chem_context_destroy` when done (e.g. OnDestroy).
#[no_mangle]
pub extern "C" fn chem_context_create(cutoff_hint: f32) -> *mut SimContext {
    Box::into_raw(Box::new(SimContext::new(cutoff_hint)))
}

/// Frees a context created by `chem_context_create`, and every atom still
/// alive in it. Passing null is a no-op. Never call this twice on the same
/// pointer, and never touch the pointer again afterward — same rules as
/// any C `free()`.
#[no_mangle]
pub unsafe extern "C" fn chem_context_destroy(ctx: *mut SimContext) {
    if !ctx.is_null() {
        drop(Box::from_raw(ctx));
    }
}

/// Spawn one atom of element `atomic_number` at `(x, y, z)`. Mass and
/// (rendering) radius are sourced from `element_data`'s table
/// automatically — same `make_atom` builder used internally. Returns a
/// handle valid until the atom is despawned.
#[no_mangle]
pub unsafe extern "C" fn chem_spawn_atom(
    ctx:           *mut SimContext,
    atomic_number: i32,
    x: f32, y: f32, z: f32,
) -> AtomHandle {
    let ctx = &mut *ctx;
    simulation::spawn_atom(ctx, atomic_number, [x, y, z])
}

/// Despawn an atom by handle. Returns `false` if the handle is stale
/// (already despawned, or from a different context) rather than crashing
/// — always check the return value if it matters to the caller.
#[no_mangle]
pub unsafe extern "C" fn chem_despawn_atom(ctx: *mut SimContext, handle: AtomHandle) -> bool {
    let ctx = &mut *ctx;
    simulation::despawn_atom(ctx, handle)
}

/// Current number of live atoms in the context.
#[no_mangle]
pub unsafe extern "C" fn chem_atom_count(ctx: *const SimContext) -> i32 {
    let ctx = &*ctx;
    simulation::atom_count(ctx) as i32
}

/// Copy one atom's current state into `out` by handle. Returns `false`
/// (and leaves `out` untouched) if the handle is stale.
#[no_mangle]
pub unsafe extern "C" fn chem_get_atom(
    ctx:    *const SimContext,
    handle: AtomHandle,
    out:    *mut AtomState,
) -> bool {
    let ctx = &*ctx;
    match simulation::get_atom(ctx, handle) {
        Some(a) => { *out = a; true }
        None => false,
    }
}

/// Read-only pointer into Rust's own dense atom array — for zero-copy
/// rendering. Valid only until the next `chem_spawn_atom`/
/// `chem_despawn_atom`/`chem_step` call on this context (a spawn can
/// reallocate, a despawn reorders via swap-remove) — re-fetch every
/// frame, never cache across a frame boundary.
#[no_mangle]
pub unsafe extern "C" fn chem_atoms_ptr(ctx: *const SimContext) -> *const AtomState {
    let ctx = &*ctx;
    simulation::atoms_ptr(ctx)
}

/// Initialise every currently-live atom's velocity from a Maxwell-
/// Boltzmann distribution at `temperature_k`, zero their force
/// accumulators. Call once after spawning your initial atoms.
#[no_mangle]
pub unsafe extern "C" fn chem_init(ctx: *mut SimContext, temperature_k: f32, seed: u64) {
    let ctx = &mut *ctx;
    simulation::init(ctx, temperature_k, seed);
}

/// Advance simulation by `dt` **femtoseconds**, not seconds — matches
/// `simulation::step`'s own doc comment and what `AMU_TO_EFF_MASS`
/// (amu -> eV*fs^2/A^2) requires dimensionally for `F = ma` to balance.
/// An earlier version of this comment said "seconds"; that was wrong.
/// `cutoff` = 0.0 uses 10.0 Angstroms.
#[no_mangle]
pub unsafe extern "C" fn chem_step(ctx: *mut SimContext, dt: f32, cutoff: f32) {
    let ctx = &mut *ctx;
    simulation::step(ctx, dt, cutoff);
}

/// Total kinetic energy of all currently-live atoms, in eV.
#[no_mangle]
pub unsafe extern "C" fn chem_kinetic_energy(ctx: *const SimContext) -> f32 {
    let ctx = &*ctx;
    simulation::kinetic_energy(ctx)
}

/// Current temperature estimate in Kelvin (from equipartition).
#[no_mangle]
pub unsafe extern "C" fn chem_temperature(ctx: *const SimContext) -> f32 {
    let ctx = &*ctx;
    simulation::temperature(ctx)
}

/// Is this atom currently bonded to anything? `false` for a stale handle,
/// not a crash.
#[no_mangle]
pub unsafe extern "C" fn chem_is_bonded(ctx: *const SimContext, handle: AtomHandle) -> bool {
    let ctx = &*ctx;
    simulation::is_bonded(ctx, handle)
}

/// How many bonds this atom currently holds. 0 for a stale handle or an
/// unbonded atom — same "nothing there" value either way, deliberately
/// not distinguished (matches `chem_is_bonded`'s existing "false either
/// way" contract for stale handles). Bonds are unbounded per atom now
/// (see `simulation` module docs) — this replaces the old single-partner
/// `chem_bond_partner`, which could only ever express "bonded to one
/// thing or not." Safe to change out from under nothing: `Runtime/Core`
/// hasn't been written against the old shape yet.
#[no_mangle]
pub unsafe extern "C" fn chem_bond_count(ctx: *const SimContext, handle: AtomHandle) -> i32 {
    let ctx = &*ctx;
    simulation::bond_count(ctx, handle) as i32
}

/// This atom's `index`-th bond partner (`0..chem_bond_count(...)`).
/// Returns `false` (and leaves `out` untouched) for a stale handle, an
/// unbonded atom, or `index >= chem_bond_count(...)` — all the same
/// "nothing there" case from the caller's side, deliberately not
/// distinguished. Iterate `0..chem_bond_count(ctx, handle)` to walk every
/// partner; order isn't semantically meaningful (formation order, not
/// distance or anything else), just stable within a single frame.
#[no_mangle]
pub unsafe extern "C" fn chem_bond_partner_at(
    ctx:    *const SimContext,
    handle: AtomHandle,
    index:  i32,
    out:    *mut AtomHandle,
) -> bool {
    let ctx = &*ctx;
    if index < 0 {
        return false;
    }
    match simulation::bond_partner_at(ctx, handle, index as usize) {
        Some(partner) => { *out = partner; true }
        None => false,
    }
}

/// Rest length and current live separation of this atom's `index`-th bond
/// edge (`0..chem_bond_count(...)`) — same indexing `chem_bond_partner_at`
/// already uses. Returns `false` (and leaves `out` untouched) for a stale
/// handle, an unbonded atom, or an out-of-range index — same "nothing
/// there" contract every other bond accessor here uses.
///
/// `current_length` is read straight from each atom's live
/// `AtomState.position` — the same field `chem_get_atom`/`chem_atoms_ptr`
/// already expose — so it's accurate as of whatever the caller's last
/// `chem_step` left it at, with no extra call needed just to refresh it.
///
/// Strain isn't computed here on purpose: `(current_length -
/// equilibrium_length) / equilibrium_length` is one line on the C# side,
/// and leaving it there keeps this accessor from baking in a "how should
/// a straining bond be visualized/thresholded" opinion the game side
/// hasn't settled yet.
#[no_mangle]
pub unsafe extern "C" fn chem_bond_geometry_at(
    ctx:    *const SimContext,
    handle: AtomHandle,
    index:  i32,
    out:    *mut BondGeometry,
) -> bool {
    let ctx = &*ctx;
    if index < 0 {
        return false;
    }
    match simulation::bond_geometry_at(ctx, handle, index as usize) {
        Some(geo) => { *out = geo; true }
        None => false,
    }
}

/// `AtomState` size validation. Call from C# `ValidateStructSizes()`.
/// If this returns != 48, the struct layout is mismatched — fix before proceeding.
#[no_mangle]
pub extern "C" fn chem_struct_size() -> i32 {
    core::mem::size_of::<AtomState>() as i32
}

/// `AtomHandle` size validation, same idea as `chem_struct_size`. Should
/// always return 8.
#[no_mangle]
pub extern "C" fn chem_handle_size() -> i32 {
    core::mem::size_of::<AtomHandle>() as i32
}

/// `BondGeometry` size validation, same idea as `chem_struct_size`. Should
/// always return 8.
#[no_mangle]
pub extern "C" fn chem_bond_geometry_size() -> i32 {
    core::mem::size_of::<BondGeometry>() as i32
}
