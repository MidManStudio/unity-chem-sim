// crates/chemistry_core/src/simulation.rs
//! Core physics: Lennard-Jones pairwise forces (neighbor-limited via
//! `spatial_hash`) + velocity Verlet integration.
//!
//! `SimContext` owns the atom array itself now (see `lib.rs` module docs
//! for why). Atom identity is a `mid_collections::GenerationalIndex`
//! internally, `crate::AtomHandle` at the FFI boundary — `slot_of` is
//! keyed by the *raw* index (`u32`, which `mid_collections::SparseSet`
//! already supports directly) rather than the full `GenerationalIndex`,
//! specifically so nothing here ever needs to construct a
//! `GenerationalIndex` from FFI-crossed data — see `resolve()` below.
//!
//! Two force-kernel implementations, both `pub` so they're directly
//! bench-able and test-able against each other:
//!
//! - `compute_forces_scalar` — one pair at a time, early-`continue`s past
//!   out-of-cutoff / unparameterized candidates before paying for any
//!   sqrt/recip/power math on them.
//! - `compute_forces_simd` — batches 4 candidates at a time via mid_math's
//!   `Vec3x4`/`f32x4`. Benched (see `lj_kernel` group in `sim_bench.rs`)
//!   as a net *regression* vs scalar (3.5-38% slower depending on N) —
//!   plausibly because the coarse 3x3x3 cell search over-fetches
//!   candidates past the true cutoff, and SIMD pays full transcendental-
//!   math cost on those (no per-lane branching exists) where scalar just
//!   skips them, plus `combine()` runs on all 4 lanes unconditionally
//!   since the mask needs sigma/eps before it can check them.
//!
//! `step()` calls whichever is selected by the `scalar-math` feature
//! (default: on, i.e. scalar) — flip it in `Cargo.toml` to compare
//! without code changes.

use crate::{AtomState, AtomHandle, element_data};
use crate::spatial_hash::SpatialHash;
use mid_math::{Vec3, Vec3x4, f32x4, Xorshift64};
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
    /// Parallel array, always the same length as `atoms`.
    handles: Vec<GenerationalIndex>,
    /// Issues and tracks liveness of GenerationalIndex handles. Real,
    /// tested free-list allocator from mid_collections — not reimplemented
    /// here.
    allocator: GenerationalIndexAllocator,
    /// raw_index -> current position in `atoms`/`handles`. Keyed by the
    /// raw `u32` index portion only (not the full GenerationalIndex) —
    /// generation is checked separately in `resolve()` via the stored
    /// real handle's `.generation()`, since constructing a
    /// GenerationalIndex from raw FFI-crossed parts isn't possible (see
    /// lib.rs module docs) and isn't needed for this lookup anyway.
    slot_of: SparseSet<u32, u32>,

    grid: SpatialHash,
    positions: Vec<Vec3>,
    forces: Vec<Vec3>,
    old_accel: Vec<Vec3>,
    /// Scratch buffer for one atom's j>i candidate indices, gathered from
    /// the spatial hash before either force kernel processes them.
    candidates: Vec<u32>,
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
            grid: SpatialHash::new(cutoff),
            positions: Vec::new(),
            forces: Vec::new(),
            old_accel: Vec::new(),
            candidates: Vec::new(),
        }
    }
}

/// Resolves an FFI `AtomHandle` to its current array position, verifying
/// it's still alive along the way. `None` for a stale handle (already
/// despawned, or a raw index that was never valid in this context) —
/// never panics on bad input, since this is the first thing every
/// externally-driven call does with caller-supplied data.
fn resolve(ctx: &SimContext, h: AtomHandle) -> Option<usize> {
    let &pos = ctx.slot_of.get(h.index)?;
    let real = ctx.handles[pos as usize];
    if real.generation() == h.generation {
        Some(pos as usize)
    } else {
        None // slot was reused by a different atom since this handle was issued
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

/// Despawn an atom by handle. `false` for a stale handle — same
/// not-panicking-on-bad-external-input discipline as `resolve`.
/// Swap-remove keeps `atoms`/`handles` dense: the last element moves into
/// the vacated slot, and that moved atom's `slot_of` entry is updated to
/// its new position.
pub fn despawn_atom(ctx: &mut SimContext, h: AtomHandle) -> bool {
    let Some(pos) = resolve(ctx, h) else { return false; };
    let real = ctx.handles[pos];
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
/// positions, then a velocity half-step blending old and new acceleration.
///
/// ## Units
/// Position: Angstrom. Time: femtosecond. Mass: amu. Energy: eV.
/// Force: eV/A — see the historical derivation of `AMU_TO_EFF_MASS` in
/// this crate's git history if that constant ever looks suspicious;
/// derived from SI, not recalled from memory, and sanity-checked against
/// expected 300K thermal velocities.
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
/// scalar remainder for whatever doesn't fill a full chunk. See module
/// docs for why this currently benches slower than scalar.
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
    /// directly — validates the exact functions everything else uses,
    /// including the `lj_kernel` bench group.
    #[test]
    fn simd_batched_forces_match_scalar_reference() {
        // Sizes deliberately not multiples of 4, so both the SIMD chunk
        // path and the scalar remainder path get exercised at each scale.
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

        // Despawn the middle one — exercises the swap-remove reslotting.
        assert!(despawn_atom(&mut ctx, b));
        assert_eq!(atom_count(&ctx), 2);

        // Stale handle: same slot, wrong generation now.
        assert!(!despawn_atom(&mut ctx, b));
        assert!(get_atom(&ctx, b).is_none());

        // Both surviving atoms still resolve correctly by handle,
        // regardless of where swap-remove actually moved them.
        let got_a = get_atom(&ctx, a).expect("a should still be alive");
        assert_eq!(got_a.atomic_number, 1);
        let got_c = get_atom(&ctx, c).expect("c should still be alive");
        assert_eq!(got_c.atomic_number, 3);
    }
}
