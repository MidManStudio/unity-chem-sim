// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "tests/lineage_sweep.rs"
// ============================================================================
//! Whole-chart checks for the lineage propagator: every decay graph is small
//! and finite, and every transition is a proper probability distribution that
//! conserves mass number.

use nuclear_core::decay;
use nuclear_core::lineage::{Propagator, MAX_STATES};
use nuclear_core::Nuclide;

fn every_nth(step: usize) -> Vec<Nuclide> {
    decay::nuclides().step_by(step).collect()
}

/// Golden numbers for the checked-in data. Update them, and the doc file, when
/// scripts/gen_nubase2020.py is rerun against a different source file.
#[test]
fn every_nuclide_has_a_finite_lineage_graph() {
    let mut propagator = Propagator::new();
    let mut largest = (0, Nuclide::NEUTRON);
    let mut single_state = 0;
    for nuclide in decay::nuclides() {
        let states = propagator
            .state_count(nuclide)
            .unwrap_or_else(|e| panic!("{nuclide}: {e}"));
        assert!(states <= MAX_STATES);
        if states == 1 {
            single_state += 1;
        }
        if states > largest.0 {
            largest = (states, nuclide);
        }
    }
    assert_eq!(
        single_state, 253,
        "only the stable nuclides have a one-state graph"
    );
    assert_eq!(largest, (79, Nuclide::parse("Au-170").unwrap()));
}

#[test]
fn transitions_are_probability_distributions_that_conserve_mass_number() {
    let mut propagator = Propagator::new();
    for nuclide in every_nth(11) {
        for seconds in [1.0, 1e6, 1e12] {
            let transition = propagator.transition(nuclide, seconds).unwrap();
            let total: f64 = transition.outcomes.iter().map(|o| o.probability).sum();
            assert!(
                (total - 1.0).abs() < 1e-12,
                "{nuclide} over {seconds} s: sum {total}"
            );
            for outcome in &transition.outcomes {
                assert!(outcome.probability > 0.0 && outcome.probability <= 1.0 + 1e-12);
                assert!(outcome.energy_kev.is_finite(), "{nuclide}");
                let emitted: u32 = outcome
                    .state
                    .emitted
                    .iter()
                    .map(|(n, count)| u32::from(n.a()) * count)
                    .sum();
                assert_eq!(
                    u32::from(outcome.state.residual.a()) + emitted,
                    u32::from(nuclide.a()),
                    "{nuclide}"
                );
            }
        }
    }
}

#[test]
fn after_a_very_long_time_every_atom_has_stopped_decaying() {
    let mut propagator = Propagator::new();
    for nuclide in every_nth(53) {
        let transition = propagator.transition(nuclide, 1e60).unwrap();
        let settled: f64 = transition
            .outcomes
            .iter()
            .filter(|o| {
                o.state.pending_fission
                    || decay::half_life(o.state.residual).map_or(true, |h| h.is_stable())
            })
            .map(|o| o.probability)
            .sum();
        assert!(
            settled > 1.0 - 1e-9,
            "{nuclide}: only {settled} has settled"
        );
    }
}

#[test]
fn a_transition_does_not_depend_on_what_was_cached_before() {
    for nuclide in every_nth(97) {
        let cold = Propagator::new().transition(nuclide, 5e8).unwrap();
        let mut warm = Propagator::new();
        warm.transition(nuclide, 1.0).unwrap();
        warm.transition(nuclide, 1e12).unwrap();
        let after = warm.transition(nuclide, 5e8).unwrap();
        assert_eq!(cold.outcomes, after.outcomes, "{nuclide}");
    }
}
