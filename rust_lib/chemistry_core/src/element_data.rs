// crates/chemistry_core/src/element_data.rs
//! Per-element physics parameters, transcribed by hand from
//! `mdix_files/chemistry_db/elements_database.mdix` in DixScript-Rust.
//! Source has 20 elements now — the original H, He, Li, Be, B, plus the
//! full gameplay-doc §1.1 alchemical-naming set (C, N, O, P, S, As, Sb,
//! Zn, Cu, Fe, Sn, Pb, Hg, Ag, Au) added alongside the bonding-generalization
//! pass. This table grows with the source; not generated automatically,
//! re-sync by eye when it grows further.
//!
//! ## What's stored vs what's derived
//!
//! Only genuinely irreducible per-element facts live in `TABLE`: mass,
//! vdW radius, LJ sigma/epsilon, electronegativity, first ionization
//! energy, electron affinity. Nothing here can be computed from anything
//! else we store or any physics we implement — they're measured/tabulated
//! quantum-chemistry constants.
//!
//! `reactivity_index()` and `bond_strength()` are deliberately **not**
//! stored anywhere — they're derived on demand from the raw values above,
//! using the exact same formulas already defined in DixScript-Rust's own
//! `mdix_files/chemistry_db/core/physics.mdix`
//! (`calculateReactivityIndex`, `calculateBondStrength`), reproduced here
//! rather than reinvented. The source database also caches a
//! `physics_calculated.reactivity_index` value per element — that's
//! intentionally redundant with computing it here from the raw inputs,
//! left alone in the source as reference, not treated as a second source
//! of truth for the simulation.
//!
//! ## Where the 15 new elements' LJ parameters come from
//!
//! The original 5 elements' LJ sigma/epsilon are real gas-phase values
//! from transport-property/viscosity literature — the kind of thing
//! tabulated for noble gases and simple molecular gases specifically.
//! That kind of source doesn't cover most of the periodic table, and
//! doesn't exist at all for most metals (they don't occur as simple
//! monatomic gases to measure in the first place). Rather than leave
//! every metal at `(0.0, 0.0)` the way Be/B were left "unparameterized in
//! source" for lack of a real value, all 15 new elements' sigma/epsilon
//! here come from one consistent, real, citable, peer-reviewed source
//! instead: Rappé et al.'s Universal Force Field (UFF) — *"UFF, a full
//! periodic table force field for molecular mechanics and molecular
//! dynamics simulations,"* J. Am. Chem. Soc. 114 (1992) 10024–10035 —
//! which explicitly covers the entire periodic table, metals included,
//! using one uniform parameterization method (element, hybridization,
//! connectivity). Values transcribed from `PorousMaterials.jl`'s
//! `UFF.csv` (SimonEnsemble/PorousMaterials.jl,
//! `test/data/forcefields/UFF.csv`), which already carries UFF's own
//! x1/D1 parameters converted into this exact `4ε[(σ/r)¹²−(σ/r)⁶]` form —
//! not re-derived here, just transcribed.
//!
//! Worth being honest about the tradeoff: UFF is a *generic*
//! molecular-mechanics force field tuned to predict reasonable molecular
//! geometries across the whole periodic table, not a high-precision fit
//! to any one element's real transport/spectroscopic data the way the
//! original H/He values are. Spot check: UFF's own He parameters
//! (σ=2.104 Å, ε/k=28.18 K) are noticeably different from the real
//! spectroscopy-grade values already in this table for He (σ=2.551 Å,
//! ε/k=10.22 K) — UFF gets a real, defensible number for elements that
//! would otherwise have none at all (every metal here), not a guarantee
//! of matching the noble-gas-table precision the original two entries
//! have. Treat the 15 new sigma/epsilon pairs as "real published
//! force-field values," not "spectroscopically exact," when tuning game
//! feel against them.

use crate::AtomState;

/// One element's simulation-relevant physics.
#[derive(Clone, Copy, Debug, Default)]
pub struct ElementParams {
    pub mass_amu: f32,
    pub radius_vdw_pm: f32,
    pub lj_sigma_a: f32,
    pub lj_eps_ev: f32,
    /// Pauling scale. 0.0 for noble gases (not conventionally assigned
    /// one) — this isn't a missing-data sentinel, it's the real value,
    /// and it correctly zeroes out `reactivity_index` below without any
    /// special-casing needed.
    pub electronegativity: f32,
    pub ionization_energy_kj_mol: f32,
    pub electron_affinity_kj_mol: f32,
}

/// Boltzmann constant, eV/K. Used to convert the source's `lj_epsilon`
/// (stored as epsilon/k_B in Kelvin — standard LJ force-field convention)
/// into eV, which is the energy unit the rest of this crate works in.
const K_B_EV_PER_K: f32 = 8.617_333e-5;

/// (Z, mass_amu, vdW_radius_pm, lj_sigma_angstrom, lj_epsilon_kelvin,
///  electronegativity_pauling, ionization_energy_kj_per_mol,
///  electron_affinity_kj_per_mol)
/// Kept in the source's own units (Kelvin for epsilon, kJ/mol for
/// ionization/affinity) so this table reads as a direct, eyeball-diffable
/// transcription — conversions happen in code, not in this table.
const TABLE: &[(i32, f32, f32, f32, f32, f32, f32, f32)] = &[
    // Z   mass    rvdw   sigma  eps_K   en    IE1      EA        name
    (1,    1.008,  120.0, 2.928, 37.0,   2.20, 1312.0,  72.8),  // Hydrogen
    (2,    4.0026, 140.0, 2.551, 10.22,  0.0,  2372.3,  0.0),   // Helium
    (3,    6.94,   182.0, 2.451, 183.0,  0.98, 520.2,   59.6),  // Lithium
    (4,    9.0122, 153.0, 0.0,   0.0,    1.57, 899.5,   0.0),   // Beryllium — LJ unparameterized in source
    (5,    10.81,  192.0, 0.0,   0.0,    2.04, 800.6,   26.7),  // Boron — LJ unparameterized in source
    // --- added alongside the bonding generalization (unbounded bonds/atom) ---
    // sigma/eps_K below are UFF (Rappé et al. 1992), not the H/He-style
    // viscosity-derived values — see module docs.
    (6,    12.011, 170.0, 3.4309, 52.838,  2.55, 1086.5, 121.78), // Carbon
    (7,    14.007, 155.0, 3.2607, 34.722,  3.04, 1402.3, 0.0),    // Nitrogen — EA effectively 0/unbound (half-filled 2p3 is extra-stable), same treatment as He
    (8,    15.999, 152.0, 3.1181, 30.193,  3.44, 1313.9, 141.0),  // Oxygen
    (15,   30.974, 180.0, 3.6946, 153.482, 2.19, 1011.8, 72.03),  // Phosphorus
    (16,   32.06,  180.0, 3.5948, 137.882, 2.58, 999.6,  200.41), // Sulfur
    (26,   55.845, 194.0, 2.5943, 6.542,   1.83, 762.5,  15.7),   // Iron
    (29,   63.546, 140.0, 3.1137, 2.516,   1.90, 745.5,  118.4),  // Copper
    (30,   65.38,  139.0, 2.4616, 62.399,  1.65, 906.4,  0.0),    // Zinc — EA effectively 0/unbound (filled 3d10 4s2), same treatment as He/N
    (33,   74.922, 185.0, 3.7685, 155.495, 2.18, 947.0,  78.5),   // Arsenic
    (47,   107.868,172.0, 2.8045, 18.116,  1.93, 731.0,  125.6),  // Silver
    (50,   118.710,217.0, 3.9128, 285.326, 1.96, 708.6,  107.3),  // Tin
    (51,   121.760,206.0, 3.9378, 225.946, 2.05, 834.0,  103.2),  // Antimony
    (79,   196.967,166.0, 2.9337, 19.626,  2.54, 890.1,  222.8),  // Gold — highest EA of any metal (relativistic effect on 6s), not a typo
    (80,   200.592,155.0, 2.4099, 193.740, 2.00, 1007.1, 0.0),    // Mercury — EA effectively 0/unbound (filled 5d10 6s2), same treatment as He/N/Zn
    (82,   207.2,  202.0, 3.8282, 333.635, 2.33, 715.6,  35.1),   // Lead
];

/// Look up an element's simulation parameters by atomic number.
/// Unmapped `z` returns all-zero params — not a panic. All-zero
/// `ElementParams` behaves safely everywhere it's used: zero LJ params
/// mean no LJ force (existing guard in `combine()`), zero electronegativity
/// and zero ionization energy mean `reactivity_index` comes out 0.0 (via
/// the `hardness <= 0.0` guard below), not a divide-by-zero.
pub fn params(z: i32) -> ElementParams {
    match TABLE.iter().find(|&&(tz, ..)| tz == z) {
        Some(&(_, mass, rvdw, sigma, eps_k, en, ie, ea)) => ElementParams {
            mass_amu: mass,
            radius_vdw_pm: rvdw,
            lj_sigma_a: sigma,
            lj_eps_ev: eps_k * K_B_EV_PER_K,
            electronegativity: en,
            ionization_energy_kj_mol: ie,
            electron_affinity_kj_mol: ea,
        },
        None => ElementParams::default(),
    }
}

/// Lorentz-Berthelot combining rules for a heteroatomic LJ pair — standard
/// mixing rule for when the source only gives per-element self-interaction
/// LJ terms (it does; there's no unlike-pair table in the .mdix source).
/// Returns (sigma, epsilon) for the pair.
#[inline]
pub fn combine(a: ElementParams, b: ElementParams) -> (f32, f32) {
    let sigma = 0.5 * (a.lj_sigma_a + b.lj_sigma_a);
    let eps = (a.lj_eps_ev * b.lj_eps_ev).max(0.0).sqrt();
    (sigma, eps)
}

/// Chemical hardness / electronegativity ratio — same formula as
/// DixScript-Rust's `calculateReactivityIndex` in `core/physics.mdix`:
/// `hardness = (IE - EA) / 2`, `reactivity = electronegativity / hardness`.
/// Higher = more reactive by this metric. 0.0 if hardness is non-positive
/// (guards a divide-by-zero/negative that a real element shouldn't hit,
/// but an unmapped or malformed entry could) — this is a proxy from
/// conceptual DFT, not a literal measure of how violently something
/// reacts; don't over-read the ranking between elements from it.
pub fn reactivity_index(p: ElementParams) -> f32 {
    let hardness = (p.ionization_energy_kj_mol - p.electron_affinity_kj_mol) / 2.0;
    if hardness <= 0.0 {
        return 0.0;
    }
    p.electronegativity / hardness
}

/// Bond strength estimate for a pair at `distance_angstrom` — same formula
/// as DixScript-Rust's `calculateBondStrength` in `core/physics.mdix`.
/// The ionic-character term (`1 - exp(-0.25 * Δχ²)`) is Pauling's actual
/// textbook formula for percent ionic character from electronegativity
/// difference; the `1/distance²` term and how the two combine is their
/// own simplified engineering proxy, not a standard formula — treat the
/// output as a relative comparison between pairs, not an absolute energy.
pub fn bond_strength(a: ElementParams, b: ElementParams, distance_angstrom: f32) -> f32 {
    let en_diff = a.electronegativity - b.electronegativity;
    let ionic_char = 1.0 - (-0.25 * en_diff * en_diff).exp();
    (1.0 / (distance_angstrom * distance_angstrom)) * (1.0 + ionic_char)
}

/// Build an `AtomState` at `position` for element `z`, with mass and
/// (rendering) radius sourced from `TABLE` — the one intended way to
/// construct an atom from just an atomic number. Closes the gap that
/// existed before this: `chem_step`'s mass math was always correct, but
/// nothing outside test/bench code had a *correct* way to build an
/// `AtomState` in the first place — callers had been hardcoding mass and
/// radius by hand instead of sourcing them from here.
pub fn make_atom(z: i32, position: [f32; 3]) -> AtomState {
    let p = params(z);
    AtomState {
        position,
        velocity: [0.0; 3],
        force: [0.0; 3],
        mass: p.mass_amu,
        radius: p.radius_vdw_pm,
        atomic_number: z,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reactivity_index_matches_hand_computed_values() {
        // Cross-checked against a standalone Python computation of the
        // same formula before this was written, not just asserted here.
        let cases: &[(i32, f32)] = &[
            (1, 0.003_551), // H
            (2, 0.0),       // He — zero electronegativity -> zero reactivity, correctly "inert"
            (3, 0.004_255), // Li
            (4, 0.003_491), // Be
            (5, 0.005_272), // B
            (6, 0.005_287), // C
            (7, 0.004_336), // N — EA=0 (unbound), same shape as He's zero-EN case but via zero EA instead
            (8, 0.005_866), // O
            (26, 0.004_901), // Fe
            (30, 0.003_641), // Zn — EA=0 (unbound), like N
            (79, 0.007_613), // Au — highest EA of any metal, still a normal (not inert) reactivity value
        ];
        for &(z, expected) in cases {
            let got = reactivity_index(params(z));
            assert!(
                (got - expected).abs() < 1e-5,
                "Z={z}: got {got}, expected {expected}"
            );
        }
    }

    #[test]
    fn unmapped_element_is_safe_not_panicking() {
        let p = params(999);
        assert_eq!(reactivity_index(p), 0.0);
        let (sigma, eps) = combine(p, params(1));
        assert_eq!(eps, 0.0); // one side has zero epsilon -> pair has zero epsilon
        let _ = sigma; // no assertion on sigma itself, just shouldn't panic/NaN
    }

    #[test]
    fn make_atom_sources_real_mass_and_radius() {
        let a = make_atom(1, [0.0, 0.0, 0.0]);
        assert_eq!(a.atomic_number, 1);
        assert!((a.mass - 1.008).abs() < 1e-6);
        assert!((a.radius - 120.0).abs() < 1e-6);
    }
}
