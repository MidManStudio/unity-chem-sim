// crates/chemistry_core/src/simulation.rs
//! Core physics: Lennard-Jones pairwise forces (neighbor-limited via
//! `spatial_hash`) + velocity Verlet integration.
//!
//! Correctness-first, scalar (no mid_math wide types yet — this operates on
//! one atom pair at a time). The natural next optimization pass batches the
//! LJ force kernel across 4 (or 8, via `Vec3x8` under the `avx2` feature)
//! neighbor pairs at once using mid_math's `Vec3x4`/`f32x4`. Land that once
//! this has a benchmark baseline (`.github/workflows/rust-bench.yml`) to
//! measure against — see `Documentation~/index.md` in the Unity package.
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
use mid_math::{Vec3, Xorshift64};

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

/// Initialise velocities from a Maxwell-Boltzmann distribution at
/// `temperature_k`, zero the force accumulator. Call once after allocating
/// the NativeArray, before the first `step`.
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
///
/// NOTE: allocates two `Vec<Vec3>` scratch buffers (`old_accel` here,
/// `forces` inside `compute_forces`) every call, plus a fresh `SpatialHash`
/// every call — `chem_step`'s FFI signature is stateless per-call, so
/// there's nowhere persistent to cache them. A `SimContext` opaque handle
/// (returned by an init call, holding reusable scratch buffers + the grid)
/// would remove all three allocations; that's a real next step, not done
/// here since it changes the FFI surface and wasn't asked for yet.
pub fn step(atoms: &mut [AtomState], dt: f32, cutoff: f32) {
    let n = atoms.len();
    if n == 0 || dt <= 0.0 {
        return;
    }
    let cutoff = if cutoff > 0.0 { cutoff } else { 10.0 };

    let mut old_accel = vec![Vec3::ZERO; n];
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

    compute_forces(atoms, cutoff);

    for (i, a) in atoms.iter_mut().enumerate() {
        let inv_m = 1.0 / eff_mass(a.mass);
        let f = Vec3::new(a.force[0], a.force[1], a.force[2]);
        let new_accel = f * inv_m;
        let v = Vec3::new(a.velocity[0], a.velocity[1], a.velocity[2]);
        let new_v = v + (old_accel[i] + new_accel) * (0.5 * dt);
        a.velocity = [new_v.x, new_v.y, new_v.z];
    }
}

/// Recompute every atom's `.force` from scratch via pairwise Lennard-Jones
/// within `cutoff`, using the spatial hash to skip out-of-range pairs.
fn compute_forces(atoms: &mut [AtomState], cutoff: f32) {
    let n = atoms.len();
    let positions: Vec<Vec3> = atoms
        .iter()
        .map(|a| Vec3::new(a.position[0], a.position[1], a.position[2]))
        .collect();

    let mut grid = SpatialHash::new(cutoff);
    grid.rebuild(&positions);

    let cutoff_sq = cutoff * cutoff;
    let mut forces = vec![Vec3::ZERO; n];

    for i in 0..n {
        let pi = positions[i];
        let pi_params = element_data::params(atoms[i].atomic_number);

        grid.for_each_candidate(pi, |j| {
            let j = j as usize;
            if j <= i {
                return; // each pair once (i < j); also skips self (j == i)
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
                return; // unparameterized element (see element_data table)
            }

            let r = r2.sqrt();
            let sr6 = (sigma / r).powi(6);
            let sr12 = sr6 * sr6;
            // F(r) = 24*eps/r * [2*(sigma/r)^12 - (sigma/r)^6]; positive = repulsive.
            let f_mag = 24.0 * eps / r * (2.0 * sr12 - sr6);
            let dir = d * (1.0 / r); // unit vector i -> j

            forces[i] -= dir * f_mag;
            forces[j] += dir * f_mag; // Newton's third law
        });
    }

    for (i, a) in atoms.iter_mut().enumerate() {
        a.force = [forces[i].x, forces[i].y, forces[i].z];
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
