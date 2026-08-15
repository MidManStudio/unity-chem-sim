// crates/chemistry_core/src/element_data.rs
//! Per-element physics parameters, transcribed by hand from
//! `mdix_files/chemistry_db/elements_database.mdix` in DixScript-Rust
//! (the `elements.<name>.interaction.lennard_jones` / `lj_sigma` / `lj_epsilon`
//! fields specifically). Only the fields the simulation actually needs.
//!
//! Source has 5 elements right now (H through B) — this table grows with it.
//! Not generated from the .mdix automatically yet; re-sync by eye against
//! the source when it grows. Unmapped atomic numbers fall back to zero mass
//! and zero LJ params rather than panicking — callers already guard divides
//! with `.max(1e-6)`.

/// One element's simulation-relevant physics.
#[derive(Clone, Copy, Debug, Default)]
pub struct ElementParams {
    pub mass_amu:   f32,
    pub lj_sigma_a: f32, // Angstrom
    pub lj_eps_ev:  f32, // eV (converted from the source's Kelvin convention below)
}

/// Boltzmann constant, eV/K. Used here to convert the source's `lj_epsilon`
/// (stored as epsilon/k_B in Kelvin — standard LJ force-field convention)
/// into eV, which is the energy unit the rest of this crate works in.
const K_B_EV_PER_K: f32 = 8.617_333e-5;

/// (atomic_number, mass_amu, lj_sigma_angstrom, lj_epsilon_kelvin).
/// Kept in Kelvin here (not pre-converted to eV) so this table reads as a
/// direct, eyeball-diffable transcription of the .mdix source's own units.
const TABLE: &[(i32, f32, f32, f32)] = &[
    // Z   mass (amu)  sigma (A)  epsilon (K)     name
    (1,    1.008,      2.928,     37.0),        // Hydrogen
    (2,    4.0026,     2.551,     10.22),        // Helium
    (3,    6.94,       2.451,     183.0),        // Lithium
    (4,    9.0122,     0.0,       0.0),          // Beryllium — unparameterized in source
    (5,    10.81,      0.0,       0.0),          // Boron — unparameterized in source
];

/// Look up an element's simulation parameters by atomic number.
/// Unmapped `z` returns all-zero params (zero mass, zero LJ) — not a panic.
pub fn params(z: i32) -> ElementParams {
    match TABLE.iter().find(|&&(tz, ..)| tz == z) {
        Some(&(_, mass, sigma, eps_k)) => ElementParams {
            mass_amu:   mass,
            lj_sigma_a: sigma,
            lj_eps_ev:  eps_k * K_B_EV_PER_K,
        },
        None => ElementParams::default(),
    }
}

/// Lorentz-Berthelot combining rules for a heteroatomic pair — standard
/// mixing rule for when the source only gives per-element self-interaction
/// LJ terms (it does; there's no unlike-pair table in the .mdix source).
/// Returns (sigma, epsilon) for the pair.
#[inline]
pub fn combine(a: ElementParams, b: ElementParams) -> (f32, f32) {
    let sigma = 0.5 * (a.lj_sigma_a + b.lj_sigma_a);
    let eps   = (a.lj_eps_ev * b.lj_eps_ev).max(0.0).sqrt();
    (sigma, eps)
}
