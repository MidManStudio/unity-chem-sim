// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "tests/decay_known.rs"
// ============================================================================
//! Published-value checks for decay data through the public API. Expected
//! values come from textbook and standard reference figures and are typed in
//! by hand, so a change in the crate cannot move them.

use nuclear_core::decay::{self, DecayMode};
use nuclear_core::{chain_to_stability, decay_q_value, outcomes, ChainEnd, Nuclide, Source};

const YEAR: f64 = 31_556_926.0;
const DAY: f64 = 86_400.0;

fn nuclide(text: &str) -> Nuclide {
    Nuclide::parse(text).unwrap_or_else(|| panic!("bad nuclide text {text}"))
}

/// The start nuclide followed by the residual nucleus after each decay.
fn chain_names(start: &str) -> Vec<String> {
    let chain = chain_to_stability(nuclide(start));
    assert_eq!(
        chain.reason,
        ChainEnd::Stable,
        "{start} should end in a stable nuclide"
    );
    let mut names = vec![chain.start.to_string()];
    names.extend(
        chain
            .steps
            .iter()
            .map(|s| s.outcome.daughter.unwrap().to_string()),
    );
    names
}

#[test]
fn the_four_decay_series() {
    let series: [(&str, &[&str]); 4] = [
        // Uranium series (4n+2), ends in lead-206.
        (
            "U-238",
            &[
                "U-238", "Th-234", "Pa-234", "U-234", "Th-230", "Ra-226", "Rn-222", "Po-218",
                "Pb-214", "Bi-214", "Po-214", "Pb-210", "Bi-210", "Po-210", "Pb-206",
            ],
        ),
        // Thorium series (4n), ends in lead-208.
        (
            "Th-232",
            &[
                "Th-232", "Ra-228", "Ac-228", "Th-228", "Ra-224", "Rn-220", "Po-216", "Pb-212",
                "Bi-212", "Po-212", "Pb-208",
            ],
        ),
        // Actinium series (4n+3), ends in lead-207.
        (
            "U-235",
            &[
                "U-235", "Th-231", "Pa-231", "Ac-227", "Th-227", "Ra-223", "Rn-219", "Po-215",
                "Pb-211", "Bi-211", "Tl-207", "Pb-207",
            ],
        ),
        // Neptunium series (4n+1). Bismuth-209 is radioactive, so it ends in thallium-205.
        (
            "Np-237",
            &[
                "Np-237", "Pa-233", "U-233", "Th-229", "Ra-225", "Ac-225", "Fr-221", "At-217",
                "Bi-213", "Po-213", "Pb-209", "Bi-209", "Tl-205",
            ],
        ),
    ];
    for (start, expected) in series {
        assert_eq!(chain_names(start), expected, "{start}");
    }
}

#[test]
fn fission_product_and_medical_isotope_chains() {
    let chains: [(&str, &[&str]); 6] = [
        ("Kr-92", &["Kr-92", "Rb-92", "Sr-92", "Y-92", "Zr-92"]),
        ("Ba-141", &["Ba-141", "La-141", "Ce-141", "Pr-141"]),
        ("Sr-90", &["Sr-90", "Y-90", "Zr-90"]),
        ("Cs-137", &["Cs-137", "Ba-137"]),
        ("I-131", &["I-131", "Xe-131"]),
        ("Be-7", &["Be-7", "Li-7"]),
    ];
    for (start, expected) in chains {
        assert_eq!(chain_names(start), expected, "{start}");
    }
}

#[test]
fn half_lives_agree_with_published_values() {
    // (nuclide, half-life in seconds, relative tolerance)
    let cases = [
        ("n", 609.8, 0.01),
        ("H-3", 12.32 * YEAR, 0.01),
        ("C-14", 5730.0 * YEAR, 0.01),
        ("Co-60", 5.2713 * YEAR, 0.01),
        ("Sr-90", 28.8 * YEAR, 0.01),
        ("I-131", 8.02 * DAY, 0.01),
        ("Cs-137", 30.1 * YEAR, 0.01),
        ("Po-210", 138.376 * DAY, 0.01),
        ("Ra-226", 1600.0 * YEAR, 0.01),
        ("Th-234", 24.1 * DAY, 0.01),
        ("Pa-234", 6.70 * 3600.0, 0.01),
        ("Pu-239", 24_110.0 * YEAR, 0.01),
        ("U-238", 4.468e9 * YEAR, 0.01),
        ("Bi-209", 2.01e19 * YEAR, 0.01),
        ("Kr-92", 1.84, 0.01),
    ];
    for (text, expected, tolerance) in cases {
        let seconds = decay::half_life(nuclide(text))
            .and_then(|h| h.finite_seconds())
            .unwrap_or_else(|| panic!("{text} has no half-life"));
        assert!(
            ((seconds - expected) / expected).abs() <= tolerance,
            "{text}: {seconds} s, expected about {expected} s"
        );
    }
}

#[test]
fn decay_energies_of_textbook_decays() {
    // (parent, mode, Q in keV, tolerance in keV)
    let cases = [
        ("H-3", DecayMode::BetaMinus, 18.591, 0.05),
        ("C-14", DecayMode::BetaMinus, 156.475, 0.05),
        ("Sr-90", DecayMode::BetaMinus, 546.0, 0.2),
        ("I-131", DecayMode::BetaMinus, 970.8, 0.2),
        ("Cs-137", DecayMode::BetaMinus, 1175.63, 0.2),
        ("K-40", DecayMode::BetaMinus, 1311.0, 0.5),
        ("Co-60", DecayMode::BetaMinus, 2822.8, 0.2),
        ("K-40", DecayMode::BetaPlus, 1504.4, 0.3),
        ("Be-7", DecayMode::ElectronCapture, 861.9, 0.2),
        ("Po-210", DecayMode::Alpha, 5407.5, 0.2),
        ("Ra-226", DecayMode::Alpha, 4870.7, 0.2),
        ("U-235", DecayMode::Alpha, 4678.0, 0.2),
        ("U-238", DecayMode::Alpha, 4269.8, 0.2),
        ("Pu-239", DecayMode::Alpha, 5244.5, 0.2),
    ];
    for (text, mode, expected, tolerance) in cases {
        let q = decay_q_value(nuclide(text), mode).unwrap_or_else(|| panic!("no Q for {text}"));
        assert!(
            (q.kev - expected).abs() <= tolerance,
            "{text} {mode}: got {} keV, expected {expected} +/- {tolerance}",
            q.kev
        );
        assert_eq!(q.source, Source::Measured, "{text} {mode}");
    }
}

#[test]
fn positron_emission_costs_two_electron_masses() {
    let ec = decay_q_value(nuclide("Na-22"), DecayMode::ElectronCapture).unwrap();
    let positron = decay_q_value(nuclide("Na-22"), DecayMode::PositronEmission).unwrap();
    assert!((ec.kev - positron.kev - 2.0 * decay::ELECTRON_MASS_KEV).abs() < 1e-9);
}

#[test]
fn branching_shares_for_nuclides_that_split() {
    // (nuclide, mode, share in percent, tolerance)
    let cases = [
        ("K-40", DecayMode::BetaMinus, 89.28, 0.01),
        ("K-40", DecayMode::BetaPlus, 10.72, 0.01),
        ("Bi-212", DecayMode::BetaMinus, 64.06, 0.1),
        ("Bi-212", DecayMode::Alpha, 35.94, 0.1),
        ("Ac-227", DecayMode::BetaMinus, 98.62, 0.05),
        ("Ac-227", DecayMode::Alpha, 1.38, 0.05),
    ];
    for (text, mode, expected, tolerance) in cases {
        let share = outcomes(nuclide(text))
            .into_iter()
            .find(|o| o.mode == mode)
            .unwrap_or_else(|| panic!("{text} has no {mode} outcome"))
            .percent;
        assert!(
            (share - expected).abs() <= tolerance,
            "{text} {mode}: {share} percent, expected {expected}"
        );
    }
}

/// NUBASE2020 gives its own abundances, which differ from the current IUPAC
/// table in the last digits for some isotopes (carbon-12 is 98.94 here and
/// 98.93 there, deuterium 0.0145 here and 0.0115 there). Only isotopes where
/// the two agree are listed.
#[test]
fn natural_abundances_of_familiar_isotopes() {
    // (nuclide, percent)
    let cases = [
        ("K-40", 0.0117),
        ("U-235", 0.7204),
        ("U-238", 99.2742),
        ("Fe-56", 91.754),
        ("Te-126", 18.84),
    ];
    for (text, expected) in cases {
        let abundance = decay::isotopic_abundance_percent(nuclide(text)).unwrap();
        assert!((abundance - expected).abs() < 1e-9, "{text}: {abundance}");
    }
}
