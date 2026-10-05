// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "tests/fission_chart.rs"
// ============================================================================
//! Whole-chart checks that tie the fission yields to the decay data.

use std::collections::BTreeMap;

use nuclear_core::fission::{fissioning_nucleus, Fission, Trigger, YieldSource, MIN_FISSIONABLE_Z};
use nuclear_core::mass_table;
use nuclear_core::{outcomes, Nuclide};

/// Every nuclide in the mass table that has a decay mode with no single
/// daughter: these are the nuclei that wait on the pending fission list.
fn fission_decayers() -> Vec<Nuclide> {
    mass_table::entries()
        .map(|e| e.nuclide)
        .filter(|&n| outcomes(n).iter().any(|o| o.daughter.is_none()))
        .collect()
}

#[test]
fn every_nucleus_that_waits_for_fission_has_outcomes() {
    let waiting = fission_decayers();
    assert_eq!(waiting.len(), 141);
    let mut fission = Fission::new();
    let (mut evaluated, mut extended, mut too_light) = (0, 0, 0);
    for n in &waiting {
        let splitting = fissioning_nucleus(*n);
        if splitting.z() < MIN_FISSIONABLE_Z {
            too_light += 1;
            assert!(fission.outcomes(splitting, Trigger::Spontaneous).is_err());
            continue;
        }
        let o = fission
            .outcomes(splitting, Trigger::Spontaneous)
            .unwrap_or_else(|e| panic!("{n}: {e}"));
        match o.source {
            YieldSource::Evaluated => evaluated += 1,
            YieldSource::Extended => extended += 1,
        }
        assert_eq!(o.compound, splitting);
        let sum: f64 = o.channels.iter().map(|c| c.probability).sum();
        assert!((sum - 1.0).abs() < 1e-9, "{n}");
        for c in &o.channels {
            assert_eq!(c.light.z() + c.heavy.z(), splitting.z(), "{n}");
            assert_eq!(
                c.light.a() + c.heavy.a() + u16::from(c.neutrons),
                splitting.a(),
                "{n}"
            );
        }
    }
    // Seven neutron deficient thallium, bismuth and astatine isotopes with a
    // beta delayed branch are too light to split. Nine nuclei have a table of
    // their own, and ten of the waiting ones use one directly: the extra one
    // is Es-256, whose delayed fission splits Fm-256. The rest borrow a table.
    assert_eq!(too_light, 7);
    assert_eq!(evaluated, 10);
    assert_eq!(evaluated + extended + too_light, 141);
}

/// Probability that the decay chain of `start` passes through `target`,
/// following the main branches by their shares.
fn visit_probability(start: Nuclide, target: Nuclide, memo: &mut BTreeMap<Nuclide, f64>) -> f64 {
    if start == target {
        return 1.0;
    }
    if start.a() < target.a() {
        return 0.0;
    }
    if let Some(&p) = memo.get(&start) {
        return p;
    }
    let mut p = 0.0;
    for o in outcomes(start) {
        if let Some(d) = o.daughter {
            p += o.percent / 100.0 * visit_probability(d, target, memo);
        }
    }
    memo.insert(start, p);
    p
}

/// Cumulative yields of U-235 at thermal energy from the ENDF/B-VIII.0 file,
/// in percent: the number of atoms of a nuclide made per 100 fissions,
/// directly and by the decay of its precursors.
const U235_THERMAL_CUMULATIVE: [(&str, f64); 8] = [
    ("Sr-90", 5.78),
    ("Zr-95", 6.50),
    ("Mo-99", 6.11),
    ("I-131", 2.89),
    ("Xe-135", 6.54),
    ("Cs-137", 6.19),
    ("Ba-140", 6.21),
    ("Ce-144", 5.50),
];

#[test]
fn decaying_the_modelled_yields_gives_the_published_cumulative_yields() {
    let mut fission = Fission::new();
    let o = fission
        .outcomes(
            Nuclide::parse("U-235").unwrap(),
            Trigger::Neutron { energy_ev: 0.0253 },
        )
        .unwrap();
    let yields = o.yields();
    for (name, published) in U235_THERMAL_CUMULATIVE {
        let target = Nuclide::parse(name).unwrap();
        let mut memo = BTreeMap::new();
        let cumulative: f64 = yields
            .iter()
            .map(|&(n, y)| y * visit_probability(n, target, &mut memo))
            .sum::<f64>()
            * 100.0;
        let error = (cumulative - published).abs() / published;
        eprintln!(
            "{name}: model {cumulative:.2}, file {published}, error {:.1} percent",
            100.0 * error
        );
        assert!(
            error < 0.05,
            "{name}: model {cumulative:.2} percent, file {published} percent"
        );
    }
}
