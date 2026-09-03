// crates/chemistry_core/src/element_data.rs
//! Per-element physics parameters, transcribed by hand from
//! `mdix_files/chemistry_db/elements_database.mdix` in DixScript-Rust.
//! Source has 40 elements now — the original H, He, Li, Be, B, the full
//! gameplay-doc §1.1 alchemical-naming set (C, N, O, P, S, As, Sb, Zn, Cu,
//! Fe, Sn, Pb, Hg, Ag, Au) added alongside the bonding-generalization pass,
//! F/Ne/Na/Mg/Al/Si (batch 1, finishes period 2 + starts period 3), Cl/Ar/
//! K/Ca (batch 2, finishes period 3 entirely and finishes the gameplay-doc
//! §1.2 naming set), and now Sc/Ti/V/Cr/Mn/Co/Ni/Ga/Ge/Se (batch 3, closes
//! out period 4's d-block — skipping Fe/Cu/Zn, already present — and
//! starts period 4's p-block). This table grows with the source; not
//! generated automatically, re-sync by eye when it grows further.
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
//!
//! ## Where the F/Ne/Na/Mg/Al/Si batch's LJ parameters come from
//!
//! This batch mixes both sourcing strategies above, per element, rather
//! than picking one uniformly — chosen per the same "real spectroscopic
//! data first, UFF fallback" standard used for He/H:
//!
//! - **F, Ne** — real gas-phase transport/viscosity-derived values (same
//!   category of source as H/He), specifically Poling, Prausnitz &
//!   O'Connell, *The Properties of Gases and Liquids*, 5th ed. (2001),
//!   Appendix B. Cross-checked via the `chemicals` Python package
//!   (CalebBell/chemicals), which transcribes the same table with its own
//!   citation. This is why F's sigma/epsilon here (3.357 Å / 112.6 K)
//!   don't match UFF's F values (2.997 Å / 25.16 K, visible in the raw
//!   `UFF.csv` if compared directly) — the real value took priority, same
//!   as the existing He-vs-UFF spot check above already demonstrates.
//! - **Na, Mg, Al, Si** — UFF, same as the 15-element batch. None of the
//!   four are simple monatomic/diatomic gases, so no real transport-derived
//!   LJ measurement exists for them in the first place (the same reason
//!   every metal in the 15-element batch fell back to UFF) — this isn't
//!   an inconsistent methodology choice, it's the same rule applied
//!   per-element and landing on a different answer because the physical
//!   situation is different.
//!
//! Also folded into this pass: Be and B's sigma/epsilon, left at
//! `(0.0, 0.0)` since the original 5-element table ("unparameterized in
//! source") predate the UFF pass, now get real UFF values too
//! (σ=2.4455 Å/ε=42.774 K for Be, σ=3.6375 Å/ε=90.580 K for B) — pulled
//! from the exact same `UFF.csv` fetch as this batch's own metals, purely
//! because it was a near-zero-cost consistency fix while already in this
//! file, not a signal anything was wrong with leaving them at zero before.

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
    (4,    9.0122, 153.0, 2.4455, 42.774,  1.57, 899.5,   0.0),   // Beryllium — UFF (was unparameterized; backfilled alongside the F-Ne-Na-Mg-Al-Si batch)
    (5,    10.81,  192.0, 3.6375, 90.580,  2.04, 800.6,   26.7),  // Boron — UFF (was unparameterized; backfilled alongside the F-Ne-Na-Mg-Al-Si batch)
    // --- added alongside the bonding generalization (unbounded bonds/atom) ---
    // sigma/eps_K below are UFF (Rappé et al. 1992), not the H/He-style
    // viscosity-derived values — see module docs.
    (6,    12.011, 170.0, 3.4309, 52.838,  2.55, 1086.5, 121.78), // Carbon
    (7,    14.007, 155.0, 3.2607, 34.722,  3.04, 1402.3, 0.0),    // Nitrogen — EA effectively 0/unbound (half-filled 2p3 is extra-stable), same treatment as He
    (8,    15.999, 152.0, 3.1181, 30.193,  3.44, 1313.9, 141.0),  // Oxygen
    // --- periodic-table fill-in, batch 1 (ascending Z, finishes period 2 + starts period 3) ---
    // F/Ne sigma+eps_K are real gas-phase viscosity-derived values (Poling
    // et al. 2001), same category as H/He, NOT UFF. Na/Mg/Al/Si are UFF,
    // same reasoning as every metal above — see module docs.
    (9,    18.998, 147.0, 3.357,  112.6,   3.98, 1681.0,  328.0),  // Fluorine — real (Poling), not UFF
    (10,   20.180, 154.0, 2.82,   32.8,    0.0,  2080.7,  0.0),    // Neon — real (Poling), not UFF; EN=0 (noble gas convention, matches He)
    (11,   22.990, 227.0, 2.6576, 15.097,  0.93, 495.8,   52.8),   // Sodium — UFF
    (12,   24.305, 173.0, 2.6914, 55.857,  1.31, 737.7,   0.0),    // Magnesium — UFF; EA effectively 0/unbound (filled 3s2), same treatment as Be/Zn/Hg
    (13,   26.982, 184.0, 4.0082, 254.126, 1.61, 577.5,   41.76),  // Aluminium — UFF
    (14,   28.085, 210.0, 3.8264, 202.294, 1.90, 786.5,   134.07), // Silicon — UFF
    (15,   30.974, 180.0, 3.6946, 153.482, 2.19, 1011.8, 72.03),  // Phosphorus
    (16,   32.06,  180.0, 3.5948, 137.882, 2.58, 999.6,  200.41), // Sulfur
    // --- periodic-table fill-in, batch 2 (ascending Z, Cl/K/Ca finish the
    // gameplay doc's §1.2 procedural-root-table naming set entirely) ---
    // Cl/Ar sigma+eps_K are real gas-phase viscosity-derived values
    // (Poling et al. 2001), same as batch 1's F/Ne — NOT UFF, even though
    // UFF.csv does carry both Cl and Ar rows this time (unlike Ne). K/Ca
    // are UFF, same reasoning as every other metal in this table.
    (17,   35.45,  175.0, 4.217,  316.0,   3.16, 1251.2, 348.6),  // Chlorine — real (Poling), not UFF
    (18,   39.95,  188.0, 3.542,  93.3,    0.0,  1520.6, 0.0),    // Argon — real (Poling), not UFF; EN=0 (noble gas convention, matches He/Ne)
    (19,   39.098, 275.0, 3.396,  17.613,  0.82, 418.8,  48.4),   // Potassium — UFF
    (20,   40.078, 231.0, 3.028,  119.766, 1.00, 589.8,  2.37),   // Calcium — UFF; EA is a small measured positive value (not unbound like Be/Mg)
    // --- periodic-table fill-in, batch 3 (ascending Z, closes out period 4's
    // d-block: Sc-Ni, skipping Fe/Cu/Zn already present; starts period 4's
    // p-block: Ga/Ge/Se) — all 10 are solids at STP, so all use UFF, no
    // real-vs-UFF split needed this batch.
    (21,   44.956, 211.0, 2.9355, 9.561,   1.36, 633.1,  18.14),  // Scandium — UFF
    (22,   47.867, 0.0,   2.8286, 8.555,   1.54, 658.8,  8.1),    // Titanium — UFF; vdW radius genuinely not commonly cited, left 0.0 rather than guessed
    (23,   50.9415,205.0, 2.8010, 8.052,   1.63, 650.9,  50.65),  // Vanadium — UFF
    (24,   51.9961,0.0,   2.6932, 7.548,   1.66, 652.9,  65.21),  // Chromium — UFF; first ANTIFERROMAGNETIC element in this table
    (25,   54.938, 205.0, 2.6380, 6.542,   1.55, 717.3,  0.0),    // Manganese — UFF; EA effectively 0/unbound (half-filled 3d5 4s2), same treatment as N
    (27,   58.933, 0.0,   2.5587, 7.045,   1.88, 760.4,  64.0),   // Cobalt — UFF; FERROMAGNETIC (Curie point 1394.15 K, highest of any magnetic element)
    (28,   58.6934,163.0, 2.5248, 7.548,   1.91, 737.1,  111.65), // Nickel — UFF; FERROMAGNETIC (Curie point 631.15 K)
    (31,   69.723, 187.0, 3.9048, 208.836, 1.81, 578.8,  39.56),  // Gallium — UFF; famously melts just above room temperature (302.91 K)
    (32,   72.63,  211.0, 3.8130, 190.720, 2.01, 762.0,  118.94), // Germanium — UFF
    (34,   78.971, 190.0, 3.7462, 146.437, 2.55, 941.0,  194.96), // Selenium — UFF
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
///
/// Checks the real, compile-time `TABLE` first (fast, no lock — the
/// common case, every real spawn hits this), and only falls back to the
/// runtime-registered custom-element table (see `register_element` below)
/// for a `z` that isn't real chemistry. The two can never collide —
/// `register_element` refuses anything inside `TABLE`'s real range — so
/// this ordering is purely a hot-path performance choice, not a
/// precedence rule that changes behavior either way.
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
        None => custom_registry()
            .read()
            .ok()
            .and_then(|map| map.get(&z).copied())
            .unwrap_or_default(),
    }
}

// ── Custom element registration ─────────────────────────────────────────
//
// Runtime registration, not code generation: an end user consuming the
// already-*compiled* package (a game dev with no Rust toolchain, no
// interest in one) needs a way to define a fictional reagent — GTG's
// Void-Carbon, Fae-Radon, Adrenium, or anything like them in any other
// game built on this package — without recompiling chemistry_core.
// `.mdix`-driven codegen stays the right tool for *this crate's own*
// authoring of the real 20-element `TABLE` above; it was never going to
// be something an arbitrary downstream consumer could reach for.

/// Real chemistry's actual current ceiling (Oganesson) — atomic numbers
/// at or below this are permanently protected from registration. Not
/// picked arbitrarily: if the real periodic table ever grows, this is
/// the one constant to widen, and until then this guarantees
/// `register_element` can never silently shadow real chemistry.
const MAX_REAL_ELEMENT_Z: i32 = 118;

/// Floor for custom atomic numbers — deliberately a long way past
/// `MAX_REAL_ELEMENT_Z`, not the very next integer. Anyone reading a bug
/// report full of atomic numbers should be able to tell "custom" from
/// "real" or "off-by-one typo" at a glance, not have to cross-reference a
/// periodic table to be sure.
const MIN_CUSTOM_ELEMENT_Z: i32 = 1000;

// register_element's actual guard only checks MIN_CUSTOM_ELEMENT_Z (it
// alone already rejects everything from i32::MIN through 999, real
// elements and the reserved gap both) — this assertion is what keeps
// MAX_REAL_ELEMENT_Z meaningful rather than purely decorative: if a
// future edit ever widened the real periodic table past what
// MIN_CUSTOM_ELEMENT_Z leaves room for, this fails the build instead of
// silently letting the reserved gap disappear.
const _: () = assert!(MIN_CUSTOM_ELEMENT_Z > MAX_REAL_ELEMENT_Z);

fn custom_registry() -> &'static std::sync::RwLock<std::collections::HashMap<i32, ElementParams>> {
    static REGISTRY: std::sync::OnceLock<std::sync::RwLock<std::collections::HashMap<i32, ElementParams>>> =
        std::sync::OnceLock::new();
    REGISTRY.get_or_init(|| std::sync::RwLock::new(std::collections::HashMap::new()))
}

/// Register (or overwrite) a custom element's simulation parameters at
/// runtime. This is the actual mechanism behind "end users can add
/// elements without recompiling Rust" — see the module-level note above.
///
/// Returns `false`, registers nothing, for:
/// - `atomic_number` inside or below the real periodic table's range
///   (`<= MAX_REAL_ELEMENT_Z`, currently 118) — real chemistry is never
///   overridable, on purpose.
/// - `atomic_number` between that and `MIN_CUSTOM_ELEMENT_Z` (1000) —
///   the reserved gap, not a valid target either.
/// - `mass_amu`, `radius_vdw_pm`, or `lj_sigma_a` negative — physically
///   nonsensical, and would corrupt sqrt/force math elsewhere that
///   assumes these are always `>= 0`.
///
/// `electronegativity`, `ionization_energy_kj_mol`, and
/// `electron_affinity_kj_mol` have no such guard: `0.0` is already the
/// correct, safe "don't care" value for all three (see
/// `reactivity_index`'s own `hardness <= 0.0` guard) — nothing special
/// to opt out of for a caller who just wants a physical LJ presence and
/// doesn't care about reactivity at all.
///
/// Global, not per-`SimContext` — element parameters are physics
/// constants, the same thing `params()` already treats real elements as.
/// A per-context table would mean "Void-Carbon" could mean two different
/// things in two simulations running side by side — not a real use case
/// anyone asked for, and a footgun if it existed by accident.
///
/// Persists for the process's lifetime once registered — worth being
/// explicit that this includes across multiple Unity Editor Play Mode
/// sessions in a row, since a native plugin isn't domain-reloaded the
/// way managed C# state is. Call this from one clear, deliberate
/// initialization point, not scattered across arbitrary code paths; see
/// `clear_custom_elements` for a clean slate between runs (an Editor
/// test suite, for instance).
pub fn register_element(
    atomic_number: i32,
    mass_amu: f32,
    radius_vdw_pm: f32,
    lj_sigma_a: f32,
    lj_eps_ev: f32,
    electronegativity: f32,
    ionization_energy_kj_mol: f32,
    electron_affinity_kj_mol: f32,
) -> bool {
    // Belt-and-suspenders with the const _ assertion above: that one
    // catches a broken invariant at compile time (before this function
    // ever runs), this one is what actually satisfies rustc's dead-code
    // analysis for MAX_REAL_ELEMENT_Z — a const referenced only from
    // inside another const's compile-time evaluation apparently doesn't
    // count as "used" for that lint's purposes. Compiles to nothing in
    // release (debug_assert), so no runtime cost either way.
    debug_assert!(MIN_CUSTOM_ELEMENT_Z > MAX_REAL_ELEMENT_Z);

    if atomic_number < MIN_CUSTOM_ELEMENT_Z {
        return false;
    }
    if mass_amu < 0.0 || radius_vdw_pm < 0.0 || lj_sigma_a < 0.0 {
        return false;
    }
    let params = ElementParams {
        mass_amu,
        radius_vdw_pm,
        lj_sigma_a,
        lj_eps_ev,
        electronegativity,
        ionization_energy_kj_mol,
        electron_affinity_kj_mol,
    };
    // A poisoned lock (a previous holder panicked mid-write) means
    // something already went wrong elsewhere in the same process —
    // surfacing that here as "registration failed" is more honest than
    // silently recovering and pretending it succeeded.
    match custom_registry().write() {
        Ok(mut map) => { map.insert(atomic_number, params); true }
        Err(_) => false,
    }
}

/// True if `atomic_number` resolves to something real — either the
/// static real-element `TABLE` or a previously-registered custom entry.
/// False for anything `params()` would only ever return
/// `ElementParams::default()` (all-zero, meaning "nothing here") for.
pub fn is_element_registered(atomic_number: i32) -> bool {
    if TABLE.iter().any(|&(z, ..)| z == atomic_number) {
        return true;
    }
    custom_registry()
        .read()
        .map(|map| map.contains_key(&atomic_number))
        .unwrap_or(false)
}

/// Remove one previously-registered custom element. A no-op (returns
/// `false`) for a real element's atomic number — those were never in
/// this table to begin with, nothing to remove; `register_element`
/// already guarantees that.
pub fn unregister_element(atomic_number: i32) -> bool {
    if atomic_number < MIN_CUSTOM_ELEMENT_Z {
        return false;
    }
    custom_registry()
        .write()
        .map(|mut map| map.remove(&atomic_number).is_some())
        .unwrap_or(false)
}

/// Clear every custom-registered element at once. Mainly for test/Editor
/// hot-reload hygiene — see `register_element`'s own doc on why this
/// state outlives a single Play Mode session by default.
pub fn clear_custom_elements() {
    if let Ok(mut map) = custom_registry().write() {
        map.clear();
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
            (9,  0.005_883), // F
            (10, 0.0),       // Ne — zero electronegativity -> zero reactivity, same shape as He
            (11, 0.004_199), // Na
            (12, 0.003_552), // Mg — EA=0 (unbound), like N/Zn/Hg
            (13, 0.006_010), // Al
            (14, 0.005_824), // Si
            (17, 0.007_002), // Cl
            (18, 0.0),       // Ar — zero electronegativity -> zero reactivity, same shape as He/Ne
            (19, 0.004_428), // K
            (20, 0.003_405), // Ca — EA is small-but-nonzero (2.37), unlike Be/Mg's clean 0.0
            (21, 0.004_423), // Sc
            (22, 0.004_733), // Ti
            (23, 0.005_431), // V
            (24, 0.005_649), // Cr
            (25, 0.004_322), // Mn — EA=0 (unbound, half-filled 3d5), like N
            (27, 0.005_399), // Co
            (28, 0.006_108), // Ni
            (31, 0.006_713), // Ga
            (32, 0.006_251), // Ge
            (34, 0.006_836), // Se
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

    #[test]
    fn periodic_fill_in_batch_1_landed_with_real_values() {
        // Silicon: UFF-sourced LJ, spot-checked against the same UFF.csv
        // transcription used for every other UFF entry in this table.
        let si = params(14);
        assert!((si.mass_amu - 28.085).abs() < 1e-6);
        assert!((si.lj_sigma_a - 3.8264).abs() < 1e-4);
        assert!((si.lj_eps_ev - 202.294 * K_B_EV_PER_K).abs() < 1e-5);

        // Neon: real (Poling) LJ, not UFF — UFF.csv doesn't even carry a
        // Ne row, so this value could only have come from the real source.
        let ne = params(10);
        assert!((ne.lj_sigma_a - 2.82).abs() < 1e-6);
        assert!((ne.electronegativity - 0.0).abs() < 1e-6, "noble gas convention, matches He");

        // Fluorine's sigma should NOT equal UFF's F value (2.996983...) —
        // confirms the real-spectroscopic-first rule actually took
        // priority over UFF for this element, not just documented as if
        // it did.
        let f = params(9);
        assert!(
            (f.lj_sigma_a - 2.996983).abs() > 0.1,
            "F's sigma should be the real Poling value (3.357), not UFF's (2.997)"
        );
    }

    #[test]
    fn periodic_fill_in_batch_3_landed_with_real_values() {
        // Selenium: UFF-sourced LJ, spot-checked.
        let se = params(34);
        assert!((se.mass_amu - 78.971).abs() < 1e-6);
        assert!((se.lj_sigma_a - 3.7462).abs() < 1e-3);
        assert!((se.lj_eps_ev - 146.437 * K_B_EV_PER_K).abs() < 1e-4);

        // Manganese: EA effectively 0/unbound (half-filled 3d5 4s2), same
        // pattern as N's half-filled 2p3 — confirms this landed as a real
        // physical claim, not an oversight.
        let mn = params(25);
        assert!((mn.electron_affinity_kj_mol - 0.0).abs() < 1e-6);

        // No gases in this batch (all 10 are solids at STP), so every LJ
        // pair should trace back to UFF — spot-check Ga's UFF sigma
        // specifically, since it's the odd one out (a metal that's
        // liquid barely above room temperature, easy to mistakenly treat
        // like a "simple" real-spectroscopic case the way F/Ne/Cl/Ar were).
        let ga = params(31);
        assert!((ga.lj_sigma_a - 3.9048090816091072).abs() < 1e-4);
    }

    #[test]
    fn periodic_fill_in_batch_2_landed_with_real_values() {
        // Calcium: UFF-sourced LJ, spot-checked against the same UFF.csv
        // transcription used everywhere else.
        let ca = params(20);
        assert!((ca.mass_amu - 40.078).abs() < 1e-6);
        assert!((ca.lj_sigma_a - 3.028).abs() < 1e-3);
        assert!((ca.lj_eps_ev - 119.766 * K_B_EV_PER_K).abs() < 1e-4);

        // Argon: real (Poling) LJ, not UFF — this is the more interesting
        // regression guard than batch 1's Ne case, since UFF.csv actually
        // DOES carry an Ar row (3.446 A) this time. Confirms the
        // real-spectroscopic-first rule took priority even when a UFF
        // fallback was available and could have been used unnoticed.
        let ar = params(18);
        assert!(
            (ar.lj_sigma_a - 3.4459962417668324).abs() > 0.05,
            "Ar's sigma should be the real Poling value (3.542), not UFF's (3.446)"
        );
        assert!((ar.electronegativity - 0.0).abs() < 1e-6, "noble gas convention, matches He/Ne");

        // Calcium's EA is a small measured POSITIVE value, unlike Be/Mg's
        // clean 0.0-unbound treatment for the same filled-ns2 pattern —
        // confirms this wasn't accidentally force-fit to match them.
        assert!(ca.electron_affinity_kj_mol > 0.0, "Ca's EA should be small-but-nonzero, not forced to 0 like Be/Mg");
    }

    #[test]
    fn be_and_b_backfilled_with_real_uff_values() {
        // Regression guard: Be/B used to be (0.0, 0.0) ("unparameterized
        // in source"). Confirms the batch-1 backfill actually replaced
        // them, and didn't accidentally leave either at the old zero.
        let be = params(4);
        assert!(be.lj_sigma_a > 0.0 && be.lj_eps_ev > 0.0, "Be must no longer be zero-LJ");
        assert!((be.lj_sigma_a - 2.4455).abs() < 1e-3);

        let b = params(5);
        assert!(b.lj_sigma_a > 0.0 && b.lj_eps_ev > 0.0, "B must no longer be zero-LJ");
        assert!((b.lj_sigma_a - 3.6375).abs() < 1e-3);
    }

    // The custom-element registry is a single process-global table, and
    // Rust runs #[test] functions concurrently on separate threads within
    // the SAME process by default — every test below uses its own
    // disjoint Z (10xx/11xx/12xx/13xx/14xx) and never asserts the
    // registry's global state (e.g. "is empty"), only facts about its own
    // entries, specifically so these can't interfere with each other or
    // with a real caller's registrations if this crate is ever linked
    // into something that also calls register_element concurrently.

    #[test]
    fn register_element_is_visible_through_params_and_make_atom() {
        let z = 1001;
        assert!(register_element(z, 42.0, 150.0, 3.0, 0.05, 1.5, 500.0, 50.0));
        assert!(is_element_registered(z));

        let p = params(z);
        assert!((p.mass_amu - 42.0).abs() < 1e-6);
        assert!((p.radius_vdw_pm - 150.0).abs() < 1e-6);
        assert!((p.lj_sigma_a - 3.0).abs() < 1e-6);
        assert!((p.lj_eps_ev - 0.05).abs() < 1e-6);
        assert!((p.electronegativity - 1.5).abs() < 1e-6);

        let a = make_atom(z, [1.0, 2.0, 3.0]);
        assert_eq!(a.atomic_number, z);
        assert!((a.mass - 42.0).abs() < 1e-6);
        assert!((a.radius - 150.0).abs() < 1e-6);

        assert!(unregister_element(z));
        assert!(!is_element_registered(z));
        // params() falls back to all-zero default once unregistered, same
        // safe behavior as any never-registered z.
        assert_eq!(params(z).mass_amu, 0.0);
    }

    #[test]
    fn register_element_rejects_the_real_element_range() {
        // Every real element already in TABLE, plus the reserved gap up
        // to (but not including) MIN_CUSTOM_ELEMENT_Z, must be refused —
        // real chemistry is never overridable through this path.
        assert!(!register_element(1, 999.0, 999.0, 999.0, 999.0, 999.0, 999.0, 999.0), "z=1 (real H) must be rejected");
        assert!(!register_element(82, 999.0, 999.0, 999.0, 999.0, 999.0, 999.0, 999.0), "z=82 (real Pb) must be rejected");
        assert!(!register_element(118, 1.0, 1.0, 1.0, 1.0, 0.0, 0.0, 0.0), "z=118 (Oganesson, MAX_REAL_ELEMENT_Z) must be rejected");
        assert_eq!(MAX_REAL_ELEMENT_Z, 118, "test above assumes this exact value — update both together if it ever changes");
        assert!(!register_element(500, 1.0, 1.0, 1.0, 1.0, 0.0, 0.0, 0.0), "z=500 (reserved gap, below MIN_CUSTOM_ELEMENT_Z) must be rejected");
        assert!(!register_element(999, 1.0, 1.0, 1.0, 1.0, 0.0, 0.0, 0.0), "z=999 (one below MIN_CUSTOM_ELEMENT_Z) must be rejected");

        // Confirm the real element's actual data is untouched by the
        // rejected attempt on z=1 above.
        assert!((params(1).mass_amu - 1.008).abs() < 1e-6, "real H's data must be unaffected by a rejected registration attempt");
    }

    #[test]
    fn register_element_rejects_negative_physical_values() {
        let z = 1101;
        assert!(!register_element(z, -1.0, 100.0, 1.0, 0.0, 0.0, 0.0, 0.0), "negative mass must be rejected");
        assert!(!register_element(z, 1.0, -1.0, 1.0, 0.0, 0.0, 0.0, 0.0), "negative radius must be rejected");
        assert!(!register_element(z, 1.0, 100.0, -1.0, 0.0, 0.0, 0.0, 0.0), "negative sigma must be rejected");
        assert!(!is_element_registered(z), "none of the rejected attempts should have registered anything");

        // Negative electronegativity/IE/EA are NOT guarded — 0.0 is
        // already the correct safe value for all three, and a caller
        // passing something unusual there (not physically real, but not
        // corrupting either) is allowed to.
        assert!(register_element(z, 1.0, 100.0, 1.0, 0.0, -5.0, -5.0, -5.0));
        assert!(unregister_element(z));
    }

    #[test]
    fn unregister_element_is_a_noop_for_real_elements_and_never_registered_custom_ones() {
        assert!(!unregister_element(1), "real H was never in the custom table, nothing to remove");
        assert!(!unregister_element(1201), "never registered, nothing to remove");
    }

    #[test]
    fn overwriting_an_existing_registration_replaces_it_cleanly() {
        let z = 1301;
        assert!(register_element(z, 1.0, 1.0, 1.0, 1.0, 0.0, 0.0, 0.0));
        assert!(register_element(z, 2.0, 2.0, 2.0, 2.0, 0.0, 0.0, 0.0));
        assert!((params(z).mass_amu - 2.0).abs() < 1e-6, "second registration should fully replace the first, not merge with it");
        assert!(unregister_element(z));
    }
     }
