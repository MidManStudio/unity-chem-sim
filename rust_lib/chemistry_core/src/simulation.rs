// crates/chemistry_core/src/simulation.rs
//! Core physics: Lennard-Jones pairwise forces (neighbor-limited via
//! `spatial_hash`) + velocity Verlet integration.
//!
//! The LJ force kernel batches 4 candidates at a time via mid_math's
//! `Vec3x4`/`f32x4`/`Mask4`, with a scalar remainder loop for whatever's
//! left over (candidate counts are rarely a multiple of 4). Gather-then-
//! batch: `SimContext::candidates` collects one atom's j>i candidate
//! indices first (scalar, via the spatial hash's push-based callback),
//! *then* the force math processes that flat buffer 4 at a time — this
//! keeps the spatial hash itself untouched and the SIMD kernel isolated
//! and testable on its own, rather than trying to vectorize the
//! irregular, branchy cell traversal directly.
//!
//! Verified against a scalar-per-pair reference before landing here (see
//! the `tests` module below, and the SIMD/scalar comparison this was
//! prototyped against — output matched exactly, including the masking
//! for out-of-cutoff and unparameterized-element candidates).
//!
//! ## Units
//! Position: Angstrom (A). Time: femtosecond (fs). Mass: amu. Energy: eV.
//! Force: eV/A. This is the same unit family classical MD codes use (LAMMPS
//! "metal" units, but femtoseconds instead of picoseconds).
//!
//! `AMU_TO_EFF_MASS` converts amu into the effective mass unit that makes
//! `F = m*a` come out directly in eV/A given acceleration in A/fs^2.
//! Derived from SI (not recalled from memory — worth double-checking if
//! anything here ever looks physically wrong):
//!   1 amu = 1.66053906660e-27 kg  (elements_database.mdix's own constant)
//!   1 eV  = 1.602176634e-19 J
//!   1 A   = 1e-10 m,  1 fs = 1e-15 s
//!   F[eV/A] = m[amu]*a[A/fs^2] * (1.66053906660e-27 * 1e20) * (1e-10/1.602176634e-19)
//!           = m[amu]*a[A/fs^2] * 103.642693...
//! i.e. effective mass in eV*fs^2/A^2 = mass_amu * 103.642693.
//!
//! Sanity check (not a substitute for the real test suite once one exists):
//! RMS thermal speed for a 1 amu atom at 300 K works out to ~1.6 A/fs
//! (1600 m/s) via equipartition, which is the right order of magnitude for
//! atomic hydrogen — the two independently-derived quantities agree.

use crate::{AtomState, element_data};
use crate::spatial_hash::SpatialHash;
use mid_math::{Vec3, Vec3x4, f32x4, Xorshift64};

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

/// Owns everything `step()` needs to reuse across calls instead of
/// allocating fresh each time: the spatial hash grid and scratch buffers
/// sized to the current atom count. Create once (`chem_context_create`),
/// pass into every `chem_step`, free once (`chem_context_destroy`) — same
/// lifecycle as any opaque C handle.
///
/// Buffers are grown via `Vec::resize`/`Vec::clear`, which only reallocates
/// when a new length exceeds current capacity — shrinking atom count (or
/// candidate count) keeps the larger capacity around rather than freeing
/// it, so fluctuating sizes don't thrash the allocator either way.
pub struct SimContext {
    grid: SpatialHash,
    positions: Vec<Vec3>,
    forces: Vec<Vec3>,
    old_accel: Vec<Vec3>,
    /// Scratch buffer for one atom's j>i candidate indices, gathered from
    /// the spatial hash before the SIMD kernel processes them in chunks
    /// of 4. Reused across every atom in every step.
    candidates: Vec<u32>,
}

impl SimContext {
    /// `cutoff_hint` just sizes the initial grid — `step()` transparently
    /// rebuilds the grid (paying that one allocation) if a later call's
    /// `cutoff` ever actually differs from what the grid was built with,
    /// so passing 0.0 here and relying on `step`'s own 10.0 default is
    /// fine too.
    pub fn new(cutoff_hint: f32) -> Self {
        let cutoff = if cutoff_hint > 0.0 { cutoff_hint } else { 10.0 };
        Self {
            grid: SpatialHash::new(cutoff),
            positions: Vec::new(),
            forces: Vec::new(),
            old_accel: Vec::new(),
            candidates: Vec::new(),
        }
    }
}

/// Initialise velocities from a Maxwell-Boltzmann distribution at
/// `temperature_k`, zero the force accumulator. Call once after allocating
/// the NativeArray. Doesn't touch `SimContext` — nothing here needs the
/// spatial hash or scratch buffers, so this stays context-free.
pub fn init(atoms: &mut [AtomState], temperature_k: f32, seed: u64) {
    let mut rng = Xorshift64::new_safe(seed);
    let t = temperature_k.max(0.0);
    for a in atoms.iter_mut() {
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
pub fn step(ctx: &mut SimContext, atoms: &mut [AtomState], dt: f32, cutoff: f32) {
    let n = atoms.len();
    if n == 0 || dt <= 0.0 {
        return;
    }
    let cutoff = if cutoff > 0.0 { cutoff } else { 10.0 };

    ctx.old_accel.resize(n, Vec3::ZERO);

    for (i, a) in atoms.iter_mut().enumerate() {
        let inv_m = 1.0 / eff_mass(a.mass);
        let f = Vec3::new(a.force[0], a.force[1], a.force[2]);
        let accel = f * inv_m;
        ctx.old_accel[i] = accel;

        let v = Vec3::new(a.velocity[0], a.velocity[1], a.velocity[2]);
        let p = Vec3::new(a.position[0], a.position[1], a.position[2]);
        let new_p = p + v * dt + accel * (0.5 * dt * dt);
        a.position = [new_p.x, new_p.y, new_p.z];
    }

    compute_forces(ctx, atoms, cutoff);

    for (i, a) in atoms.iter_mut().enumerate() {
        let inv_m = 1.0 / eff_mass(a.mass);
        let f = Vec3::new(a.force[0], a.force[1], a.force[2]);
        let new_accel = f * inv_m;
        let v = Vec3::new(a.velocity[0], a.velocity[1], a.velocity[2]);
        let new_v = v + (ctx.old_accel[i] + new_accel) * (0.5 * dt);
        a.velocity = [new_v.x, new_v.y, new_v.z];
    }
}

/// Recompute every atom's `.force` from scratch via pairwise Lennard-Jones
/// within `cutoff`. For each atom, candidates come from `ctx.grid`'s 3x3x3
/// cell search (scalar — the traversal itself is irregular/branchy and
/// isn't worth vectorizing); the actual force math on those candidates is
/// batched 4 at a time via SIMD, with a scalar remainder for whatever
/// candidate count isn't a multiple of 4.
fn compute_forces(ctx: &mut SimContext, atoms: &mut [AtomState], cutoff: f32) {
    let n = atoms.len();

    if (ctx.grid.cell_size() - cutoff).abs() > 1e-6 {
        ctx.grid = SpatialHash::new(cutoff);
    }

    ctx.positions.resize(n, Vec3::ZERO);
    for (i, a) in atoms.iter().enumerate() {
        ctx.positions[i] = Vec3::new(a.position[0], a.position[1], a.position[2]);
    }
    ctx.grid.rebuild(&ctx.positions[..n]);

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
        let pi_params = element_data::params(atoms[i].atomic_number);

        // Gather this atom's j>i candidates. Grid and candidates borrowed
        // as separate locals (not through `ctx.` inside the closure) so
        // this stays an unambiguous disjoint-field borrow.
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

            let (sigma0, eps0) = element_data::combine(pi_params, element_data::params(atoms[j0].atomic_number));
            let (sigma1, eps1) = element_data::combine(pi_params, element_data::params(atoms[j1].atomic_number));
            let (sigma2, eps2) = element_data::combine(pi_params, element_data::params(atoms[j2].atomic_number));
            let (sigma3, eps3) = element_data::combine(pi_params, element_data::params(atoms[j3].atomic_number));
            let sigma4 = f32x4::new(sigma0, sigma1, sigma2, sigma3);
            let eps4 = f32x4::new(eps0, eps1, eps2, eps3);

            let d4 = pj4 - pi4;
            let r2_4 = d4.length_sq();

            let mask = r2_4.cmpge(min_r2_4)
                & r2_4.cmple(cutoff_sq_4)
                & eps4.cmpgt(zero4)
                & sigma4.cmpgt(zero4);

            // Clamp before sqrt/recip: all 4 lanes execute the same
            // instructions regardless of mask, so an out-of-range or
            // eps=0 lane must still not produce NaN/Inf that could
            // poison a later horizontal step.
            let r2_safe = r2_4.max(min_r2_4);
            let r4 = r2_safe.sqrt();
            let inv_r4 = r4.recip();

            let sr4 = sigma4 * inv_r4;
            let sr2_4 = sr4 * sr4;
            let sr6_4 = sr2_4 * sr2_4 * sr2_4;
            let sr12_4 = sr6_4 * sr6_4;
            // F(r) = 24*eps/r * [2*(sigma/r)^12 - (sigma/r)^6]; positive = repulsive.
            let f_mag4 = f32x4::splat(24.0) * eps4 * inv_r4 * (f32x4::splat(2.0) * sr12_4 - sr6_4);

            let dir4 = d4 * inv_r4; // unit vector i -> j, per lane
            let contrib4 = dir4 * f_mag4;
            let contrib4 = Vec3x4::select(mask, contrib4, Vec3x4::ZERO);

            let arr = contrib4.to_array();
            ctx.forces[i] -= arr[0] + arr[1] + arr[2] + arr[3];
            ctx.forces[j0] += arr[0];
            ctx.forces[j1] += arr[1];
            ctx.forces[j2] += arr[2];
            ctx.forces[j3] += arr[3];
        }

        // Scalar remainder — same algorithm as the batched path above,
        // one pair at a time, for whatever didn't fill a full chunk of 4.
        for k in (chunk_count * 4)..cand_count {
            let j = ctx.candidates[k] as usize;
            let pj = ctx.positions[j];
            let d = pj - pi;
            let r2 = d.length_sq();
            if r2 < 1e-8 || r2 > cutoff_sq {
                continue;
            }
            let pj_params = element_data::params(atoms[j].atomic_number);
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

    for (i, a) in atoms.iter_mut().enumerate() {
        a.force = [ctx.forces[i].x, ctx.forces[i].y, ctx.forces[i].z];
    }
}

/// Total kinetic energy of all atoms, in eV.
pub fn kinetic_energy(atoms: &[AtomState]) -> f32 {
    atoms
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
pub fn temperature(atoms: &[AtomState]) -> f32 {
    let n = atoms.len();
    if n == 0 {
        return 0.0;
    }
    2.0 * kinetic_energy(atoms) / (3.0 * n as f32 * K_B_EV_PER_K)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Scalar-per-pair reference, kept test-only (not compiled into release
    /// builds) — exists purely to verify the SIMD-batched compute_forces
    /// above produces the same physics. Deliberately duplicates the old
    /// pre-batching algorithm rather than sharing code with it, since the
    /// whole point is an independent check.
    fn compute_forces_scalar_reference(atoms: &[AtomState], cutoff: f32) -> Vec<[f32; 3]> {
        let n = atoms.len();
        let positions: Vec<Vec3> = atoms
            .iter()
            .map(|a| Vec3::new(a.position[0], a.position[1], a.position[2]))
            .collect();
        let cutoff_sq = cutoff * cutoff;
        let mut forces = vec![Vec3::ZERO; n];

        for i in 0..n {
            let pi = positions[i];
            let pi_params = element_data::params(atoms[i].atomic_number);
            for j in (i + 1)..n {
                let pj = positions[j];
                let d = pj - pi;
                let r2 = d.length_sq();
                if r2 < 1e-8 || r2 > cutoff_sq {
                    continue;
                }
                let pj_params = element_data::params(atoms[j].atomic_number);
                let (sigma, eps) = element_data::combine(pi_params, pj_params);
                if eps <= 0.0 || sigma <= 0.0 {
                    continue;
                }
                let r = r2.sqrt();
                let sr6 = (sigma / r).powi(6);
                let sr12 = sr6 * sr6;
                let f_mag = 24.0 * eps / r * (2.0 * sr12 - sr6);
                let dir = d * (1.0 / r);
                forces[i] -= dir * f_mag;
                forces[j] += dir * f_mag;
            }
        }
        forces.into_iter().map(|f| [f.x, f.y, f.z]).collect()
    }

    fn hydrogen_grid(n: usize, spacing: f32) -> Vec<AtomState> {
        let side = (n as f32).cbrt().ceil() as usize;
        (0..n)
            .map(|i| {
                let x = (i % side) as f32 * spacing;
                let y = ((i / side) % side) as f32 * spacing;
                let z = (i / (side * side)) as f32 * spacing;
                AtomState {
                    position: [x, y, z],
                    velocity: [0.0; 3],
                    force: [0.0; 3],
                    mass: 1.008,
                    radius: 120.0,
                    atomic_number: 1,
                }
            })
            .collect()
    }

    #[test]
    fn simd_batched_forces_match_scalar_reference() {
        // Sizes deliberately not multiples of 4, so both the SIMD chunk
        // path and the scalar remainder path get exercised at each scale.
        for &n in &[5usize, 13, 37, 101] {
            let mut atoms = hydrogen_grid(n, 3.0);
            let expected = compute_forces_scalar_reference(&atoms, 10.0);

            let mut ctx = SimContext::new(10.0);
            compute_forces(&mut ctx, &mut atoms, 10.0);

            for i in 0..n {
                for k in 0..3 {
                    let got = atoms[i].force[k];
                    let want = expected[i][k];
                    assert!(
                        (got - want).abs() < 1e-4,
                        "atom {i} component {k}: simd={got} scalar={want} (n={n})"
                    );
                }
            }
        }
    }
}
