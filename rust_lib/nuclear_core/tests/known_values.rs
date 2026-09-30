// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "tests/known_values.rs"
// ============================================================================
//! Published-value checks through the public API. Expected values agree with
//! textbook and AME2020 figures and are typed in by hand, so a change in the
//! crate cannot move them.

use nuclear_core::{binding_energy, mass_excess, q_value, Nuclide, ReactionError, Source};

fn assert_near(actual: f64, expected: f64, tolerance: f64, what: &str) {
    assert!(
        (actual - expected).abs() <= tolerance,
        "{what}: got {actual}, expected {expected} +/- {tolerance}"
    );
}

fn nuclide(text: &str) -> Nuclide {
    Nuclide::parse(text).unwrap_or_else(|| panic!("bad nuclide text {text}"))
}

#[test]
fn carbon_12_defines_the_mass_scale() {
    let c12 = nuclide("C-12");
    let excess = mass_excess(c12).unwrap();
    assert_eq!(excess.kev, 0.0, "the atomic mass unit is defined by C-12");
    assert_eq!(excess.source, Source::Measured);
}

#[test]
fn light_nucleus_binding_energies() {
    // (nuclide, total binding energy in keV)
    let cases = [
        ("H-2", 2224.566),
        ("H-3", 8481.797),
        ("He-3", 7718.041),
        ("He-4", 28295.663),
        ("C-12", 92161.736),
        ("O-16", 127619.316),
    ];
    for (text, expected) in cases {
        let binding = binding_energy(nuclide(text)).unwrap();
        assert_near(binding.total_kev, expected, 0.05, text);
        assert_eq!(binding.source, Source::Measured, "{text}");
    }
}

#[test]
fn binding_energy_per_nucleon_anchors() {
    // (nuclide, binding energy per nucleon in keV)
    let cases = [
        ("Fe-56", 8790.356),
        ("Ni-62", 8794.555),
        ("Pb-208", 7867.453),
        ("U-235", 7590.915),
        ("U-238", 7570.126),
    ];
    for (text, expected) in cases {
        let nuc = nuclide(text);
        let per_nucleon = binding_energy(nuc).unwrap().per_nucleon_kev(nuc);
        assert_near(per_nucleon, expected, 0.05, text);
    }
}

#[test]
fn fusion_q_values() {
    // (reactants, products, Q in keV)
    let cases: [(&[&str], &[&str], f64); 5] = [
        (&["H-2", "H-3"], &["He-4", "n"], 17589.30),
        (&["H-2", "H-2"], &["H-3", "H-1"], 4032.66),
        (&["H-2", "H-2"], &["He-3", "n"], 3268.91),
        (&["H-1", "Li-7"], &["He-4", "He-4"], 17346.24),
        (&["C-12", "C-12"], &["Ne-20", "He-4"], 4617.02),
    ];
    for (reactants, products, expected) in cases {
        let reactants: Vec<Nuclide> = reactants.iter().map(|t| nuclide(t)).collect();
        let products: Vec<Nuclide> = products.iter().map(|t| nuclide(t)).collect();
        let q = q_value(&reactants, &products).unwrap();
        assert_near(q.kev, expected, 0.1, "fusion Q");
        assert_eq!(q.source, Source::Measured);
    }
}

#[test]
fn a_textbook_fission_channel() {
    // U-235 + n -> Ba-141 + Kr-92 + 3n, about 173 MeV.
    let q = q_value(
        &[nuclide("U-235"), Nuclide::NEUTRON],
        &[
            nuclide("Ba-141"),
            nuclide("Kr-92"),
            Nuclide::NEUTRON,
            Nuclide::NEUTRON,
            Nuclide::NEUTRON,
        ],
    )
    .unwrap();
    assert_near(q.mev(), 173.28, 0.1, "fission Q");
    assert_eq!(q.source, Source::Measured);
}

#[test]
fn unbalanced_reactions_are_rejected() {
    assert_eq!(
        q_value(&[Nuclide::H2, Nuclide::H3], &[Nuclide::HE4]),
        Err(ReactionError::BaryonNumberMismatch {
            reactants: 5,
            products: 4
        })
    );
    assert_eq!(
        q_value(&[Nuclide::H1, Nuclide::H1], &[Nuclide::H2]),
        Err(ReactionError::ChargeMismatch {
            reactants: 2,
            products: 1
        })
    );
    assert_eq!(q_value(&[Nuclide::HE4], &[]), Err(ReactionError::Empty));
}

#[test]
fn unlisted_participants_downgrade_the_q_value_source() {
    // Sn-200 is far beyond the table. The neutron separation energy still
    // comes out, labelled as a liquid drop number.
    let unlisted = Nuclide::new(50, 150);
    let q = q_value(&[unlisted], &[Nuclide::new(50, 149), Nuclide::NEUTRON]).unwrap();
    assert_eq!(q.source, Source::LiquidDrop);
}
