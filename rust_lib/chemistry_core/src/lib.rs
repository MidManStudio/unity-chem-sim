//! chemistry_core — Unity FFI simulation library.
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
//! ## Usage from C#
//!
//! ```csharp
//! FFIBridge.ValidateStructSizes();               // call once in Awake
//! IntPtr ctx = FFIBridge.chem_context_create(10.0f);
//! FFIBridge.chem_init(ptr, count, 300.0f, 42UL);
//! // per frame:
//! FFIBridge.chem_step(ctx, ptr, count, Time.deltaTime, 10.0f);
//! // on teardown (e.g. OnDestroy):
//! FFIBridge.chem_context_destroy(ctx);
//! ```
//!
//! `ctx` is created once and reused for the lifetime of the simulation —
//! `chem_step` no longer allocates internally, `SimContext` owns the
//! spatial hash grid and scratch buffers across calls instead.

mod simulation;
mod spatial_hash;
mod element_data;
mod fx_hash;

pub use simulation::*;

use std::slice;

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

// ── FFI surface ───────────────────────────────────────────────────────────────

/// Create a persistent simulation context: owns the spatial hash grid and
/// the scratch buffers `chem_step` needs, so `chem_step` doesn't allocate
/// per call. `cutoff_hint` just sizes the initial grid — passing 0.0 is
/// fine, `step` falls back to 10.0 either way and rebuilds the grid
/// transparently if a later `cutoff` ever actually differs.
///
/// Create once (e.g. in Awake), reuse for every `chem_step` call, free
/// exactly once with `chem_context_destroy` when done (e.g. OnDestroy).
#[no_mangle]
pub extern "C" fn chem_context_create(cutoff_hint: f32) -> *mut SimContext {
    Box::into_raw(Box::new(SimContext::new(cutoff_hint)))
}

/// Frees a context created by `chem_context_create`. Passing null is a
/// no-op. Never call this twice on the same pointer, and never touch the
/// pointer again afterward — same rules as any C `free()`.
#[no_mangle]
pub unsafe extern "C" fn chem_context_destroy(ctx: *mut SimContext) {
    if !ctx.is_null() {
        drop(Box::from_raw(ctx));
    }
}

/// Initialise atom velocities from Maxwell-Boltzmann distribution at
/// `temperature_k`. Call once after allocating the NativeArray. Doesn't
/// need a `SimContext` — nothing here touches the spatial hash.
#[no_mangle]
pub unsafe extern "C" fn chem_init(
    atoms:         *mut AtomState,
    count:         i32,
    temperature_k: f32,
    seed:          u64,
) {
    if atoms.is_null() || count <= 0 { return; }
    let s = slice::from_raw_parts_mut(atoms, count as usize);
    simulation::init(s, temperature_k, seed);
}

/// Advance simulation by `dt` seconds. `cutoff` = 0.0 uses 10.0 Angstroms.
/// `ctx` must be a live pointer from `chem_context_create` — this is where
/// the spatial hash and scratch buffers actually live now, reused across
/// calls instead of allocated fresh each time.
#[no_mangle]
pub unsafe extern "C" fn chem_step(
    ctx:    *mut SimContext,
    atoms:  *mut AtomState,
    count:  i32,
    dt:     f32,
    cutoff: f32,
) {
    if ctx.is_null() || atoms.is_null() || count <= 0 { return; }
    let ctx = &mut *ctx;
    let s = slice::from_raw_parts_mut(atoms, count as usize);
    simulation::step(ctx, s, dt, cutoff);
}

/// Returns total kinetic energy of all atoms in eV.
#[no_mangle]
pub unsafe extern "C" fn chem_kinetic_energy(
    atoms: *const AtomState,
    count: i32,
) -> f32 {
    if atoms.is_null() || count <= 0 { return 0.0; }
    let s = slice::from_raw_parts(atoms, count as usize);
    simulation::kinetic_energy(s)
}

/// Returns current temperature estimate in Kelvin (from equipartition).
#[no_mangle]
pub unsafe extern "C" fn chem_temperature(
    atoms: *const AtomState,
    count: i32,
) -> f32 {
    if atoms.is_null() || count <= 0 { return 0.0; }
    let s = slice::from_raw_parts(atoms, count as usize);
    simulation::temperature(s)
}

/// Struct size validation. Call from C# `ValidateStructSizes()`.
/// If this returns != 48, the struct layout is mismatched — fix before proceeding.
#[no_mangle]
pub extern "C" fn chem_struct_size() -> i32 {
    core::mem::size_of::<AtomState>() as i32
}
