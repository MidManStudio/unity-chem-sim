// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "tests/decay_sweep.rs"
// ============================================================================
//! Whole-chart checks for decay data: agreement with the mass table, outcome
//! distributions, conservation laws, energy sanity, and how chains end.

use std::collections::BTreeMap;

use nuclear_core::decay::{self, ChainEnd, HalfLifeState};
use nuclear_core::{chain_to_stability, decay_q_value, mass_table, Nuclide, Source};

#[test]
fn decay_data_and_mass_table_list_the_same_nuclides() {
    let with_decay_data: Vec<Nuclide> = decay::nuclides().collect();
    let with_mass_data: Vec<Nuclide> = mass_table::entries().map(|e| e.nuclide).collect();
    assert_eq!(with_decay_data, with_mass_data);
    assert_eq!(with_decay_data.len(), decay::ROW_COUNT);
}

/// Golden counts for the checked-in data. Update these, and the doc file, when
/// scripts/gen_nubase2020.py is rerun against a different source file.
#[test]
fn golden_counts_match_the_generated_file() {
    assert_eq!(decay::ROW_COUNT, 3558);
    let mut stable = 0;
    let mut unknown = 0;
    let mut with_assumed_outcome = 0;
    for nuclide in decay::nuclides() {
        match decay::half_life(nuclide).unwrap().state {
            HalfLifeState::Stable => stable += 1,
            HalfLifeState::Unknown => unknown += 1,
            _ => {}
        }
        if decay::outcomes(nuclide).iter().any(|o| o.assumed) {
            with_assumed_outcome += 1;
        }
    }
    assert_eq!((stable, unknown, with_assumed_outcome), (253, 46, 475));
}

#[test]
fn outcomes_form_a_distribution() {
    for nuclide in decay::nuclides() {
        let outcomes = decay::outcomes(nuclide);
        if decay::half_life(nuclide).unwrap().is_stable() {
            assert!(outcomes.is_empty(), "{nuclide} is stable");
            continue;
        }
        assert!(
            !outcomes.is_empty(),
            "{nuclide} is unstable but has no outcome"
        );
        let total: f64 = outcomes.iter().map(|o| o.percent).sum();
        assert!(
            (total - 100.0).abs() < 1e-9,
            "{nuclide}: shares sum to {total}"
        );
        assert!(outcomes.iter().all(|o| o.percent > 0.0), "{nuclide}");
        assert!(
            outcomes.windows(2).all(|w| w[0].percent >= w[1].percent),
            "{nuclide}: not sorted most likely first"
        );
    }
}

#[test]
fn every_outcome_conserves_mass_number_and_charge() {
    for nuclide in decay::nuclides() {
        for outcome in decay::outcomes(nuclide) {
            match (outcome.mode.transformation(), outcome.daughter) {
                (Some(t), Some(daughter)) => {
                    let emitted_a: u32 = outcome.emitted.iter().map(|e| u32::from(e.a())).sum();
                    let emitted_z: i32 = outcome.emitted.iter().map(|e| i32::from(e.z())).sum();
                    assert_eq!(
                        u32::from(daughter.a()) + emitted_a,
                        u32::from(nuclide.a()),
                        "{nuclide} {}: mass number",
                        outcome.mode
                    );
                    assert_eq!(
                        i32::from(daughter.z()) + emitted_z,
                        i32::from(nuclide.z()) + t.beta_charge,
                        "{nuclide} {}: charge",
                        outcome.mode
                    );
                }
                (None, None) => {}
                other => panic!("{nuclide} {}: {other:?}", outcome.mode),
            }
        }
    }
}

/// The main decay of a nuclide is observed, so the mass table must allow it.
/// Two independent evaluations (AME2020 masses, NUBASE2020 decay modes) have to
/// agree here, which also checks every entry of the mode table.
#[test]
fn main_decay_of_every_measured_nuclide_releases_energy() {
    let mut checked = 0;
    for nuclide in decay::nuclides() {
        let Some(outcome) = decay::outcomes(nuclide).into_iter().next() else {
            continue;
        };
        if outcome.assumed || outcome.daughter.is_none() {
            continue;
        }
        let q = decay_q_value(nuclide, outcome.mode)
            .unwrap_or_else(|| panic!("no Q-value for {nuclide} {}", outcome.mode));
        if q.source == Source::Measured {
            assert!(
                q.kev >= 0.0,
                "{nuclide} {}: Q = {} keV",
                outcome.mode,
                q.kev
            );
            checked += 1;
        }
    }
    assert!(checked >= 2000, "only {checked} decays were checked");
}

/// How every chain from every nuclide ends. The golden counts need updating
/// together with the doc file when the data is regenerated.
#[test]
fn every_chain_ends_in_a_known_way() {
    let mut endings: BTreeMap<String, usize> = BTreeMap::new();
    let mut longest = 0;
    for nuclide in decay::nuclides() {
        let chain = chain_to_stability(nuclide);
        if chain.reason == ChainEnd::Stable {
            assert!(
                decay::half_life(chain.end).unwrap().is_stable(),
                "{nuclide}"
            );
        }
        let key = match chain.reason {
            ChainEnd::NoDaughter(mode) => format!("no daughter ({mode})"),
            other => format!("{other:?}"),
        };
        *endings.entry(key).or_default() += 1;
        longest = longest.max(chain.steps.len());
    }
    let expected: BTreeMap<String, usize> = [
        ("Stable", 3365),
        ("NotInTable", 17),
        ("no daughter (SF)", 175),
        ("no daughter (B+SF)", 1),
    ]
    .into_iter()
    .map(|(name, count)| (name.to_string(), count))
    .collect();
    assert_eq!(endings, expected);
    assert_eq!(longest, 25);
}

#[test]
fn isotopic_abundances_sum_to_100_per_element() {
    let mut sums: BTreeMap<u16, f64> = BTreeMap::new();
    for nuclide in decay::nuclides() {
        if let Some(abundance) = decay::isotopic_abundance_percent(nuclide) {
            *sums.entry(nuclide.z()).or_default() += abundance;
        }
    }
    assert!(
        sums.len() >= 80,
        "only {} elements have abundances",
        sums.len()
    );
    for (z, sum) in &sums {
        assert!((sum - 100.0).abs() < 0.2, "Z={z}: abundances sum to {sum}");
    }
}
