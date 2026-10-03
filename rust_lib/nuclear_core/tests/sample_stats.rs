// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "tests/sample_stats.rs"
// ============================================================================
//! Counting statistics and closed-form checks for samples and amounts.

use nuclear_core::decay;
use nuclear_core::{Amounts, Nuclide, Propagator, Rng, Sample};

const YEAR: f64 = 31_556_926.0;
const DAY: f64 = 86_400.0;

fn nuclide(text: &str) -> Nuclide {
    Nuclide::parse(text).unwrap()
}

fn lambda(n: Nuclide) -> f64 {
    core::f64::consts::LN_2 / decay::half_life(n).unwrap().seconds
}

/// Strontium-90 -> yttrium-90 -> zirconium-90: a pure two-step beta chain, so
/// the Bateman solution gives the exact probability of each state.
#[test]
fn strontium_chain_counts_stay_within_five_standard_deviations() {
    let (sr, y, zr) = (nuclide("Sr-90"), nuclide("Y-90"), nuclide("Zr-90"));
    let (l1, l2) = (lambda(sr), lambda(y));
    let n = 2_000_000u64;
    for (seed, years) in [(11u64, 5.0f64), (12, 20.0), (13, 80.0)] {
        let t = years * YEAR;
        let p_sr = (-l1 * t).exp();
        let p_y = l1 / (l2 - l1) * ((-l1 * t).exp() - (-l2 * t).exp());
        let p_zr = 1.0 - p_sr - p_y;
        let mut sample = Sample::new();
        sample.add(sr, n).unwrap();
        sample
            .advance(t, &mut Rng::new(seed), &mut Propagator::new())
            .unwrap();
        for (nuc, p) in [(sr, p_sr), (y, p_y), (zr, p_zr)] {
            let mean = n as f64 * p;
            let sd = (n as f64 * p * (1.0 - p)).sqrt();
            let got = sample.count(nuc) as f64;
            assert!(
                (got - mean).abs() <= 5.0 * sd + 1.0,
                "{nuc} after {years} y: {got} vs {mean} +/- {sd}"
            );
        }
        assert_eq!(sample.total_atoms(), u128::from(n));
    }
}

/// Radium-226 -> radon-222 with the exact two-member Bateman amount, in
/// transient and in secular equilibrium.
#[test]
fn radon_growth_from_radium_matches_the_closed_form() {
    let (ra, rn) = (nuclide("Ra-226"), nuclide("Rn-222"));
    let (l1, l2) = (lambda(ra), lambda(rn));
    let mut propagator = Propagator::new();
    for days in [0.5, 3.8, 30.0, 400.0] {
        let t = days * DAY;
        let mut pile = Amounts::new();
        pile.add(ra, 1.0);
        pile.advance(t, &mut propagator).unwrap();
        let expected = l1 / (l2 - l1) * ((-l1 * t).exp() - (-l2 * t).exp());
        let got = pile.amount(rn);
        assert!(
            ((got - expected) / expected).abs() < 1e-10,
            "{days} d: {got} vs {expected}"
        );
    }
    // after many radon half-lives the activities match: N_rn * l2 = N_ra * l1
    let mut pile = Amounts::new();
    pile.add(ra, 1.0);
    pile.advance(120.0 * DAY, &mut propagator).unwrap();
    let ratio = pile.amount(rn) * l2 / (pile.amount(ra) * l1);
    assert!((ratio - 1.0).abs() < 1e-4, "activity ratio {ratio}");
}

#[test]
fn a_uranium_sample_releases_the_series_energy() {
    let mut sample = Sample::new();
    sample.add(nuclide("U-238"), 10_000).unwrap();
    let report = sample
        .advance(1e30, &mut Rng::new(4), &mut Propagator::new())
        .unwrap();
    // 51.695 MeV per atom for the uranium series
    let expected = 10_000.0 * 51_695.0;
    assert!(((report.energy_released_kev - expected) / expected).abs() < 1e-4);
    assert_eq!(sample.count(nuclide("Pb-206")), 10_000);
    assert_eq!(sample.count(Nuclide::HE4), 80_000);
    assert!(report.energy_known);
}

#[test]
fn many_small_steps_agree_with_one_big_step() {
    let c14 = nuclide("C-14");
    let t = decay::half_life(c14).unwrap().seconds;
    let mut propagator = Propagator::new();
    let mut stepped = Amounts::new();
    let mut whole = Amounts::new();
    stepped.add(c14, 1.0);
    whole.add(c14, 1.0);
    for _ in 0..1000 {
        stepped.advance(t / 1000.0, &mut propagator).unwrap();
    }
    whole.advance(t, &mut propagator).unwrap();
    assert!((stepped.amount(c14) - whole.amount(c14)).abs() < 1e-12);
    assert!((whole.amount(c14) - 0.5).abs() < 1e-12);
}

#[test]
fn a_saved_generator_state_continues_the_same_run() {
    let run = |split: bool| {
        let mut sample = Sample::new();
        sample.add(nuclide("Kr-92"), 500_000).unwrap();
        sample.add(nuclide("Na-22"), 500_000).unwrap();
        let mut propagator = Propagator::new();
        let mut rng = Rng::new(2026);
        sample.advance(5.0, &mut rng, &mut propagator).unwrap();
        if split {
            rng = Rng::from_state(rng.state()).unwrap();
        }
        sample.advance(50.0, &mut rng, &mut propagator).unwrap();
        sample
    };
    assert_eq!(run(false), run(true));
}
