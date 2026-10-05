// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "react.rs"
// ============================================================================
//! Reactions applied to a pile of atoms.
//!
//! Three events, each on [`Sample`] (whole atoms, exact draws) and on
//! [`Amounts`] (fractional amounts, exact expectation):
//!
//! * `resolve_fissions` turns the pending fission list into fission products.
//! * `irradiate` sends neutrons at a target nuclide. Each neutron is absorbed
//!   by one target atom and ends in capture or in fission.
//! * `fuse` fuses pairs of light nuclei at a temperature.
//!
//! The caller says how many events happen. Rates, fluxes, densities and
//! geometry are not modelled here. Neutrons released by fission or fusion join
//! the pile as free neutrons; sending them back at a target is up to the
//! caller. Photons, positrons and neutrinos leave without being tracked.
//!
//! A step never changes the nucleon count, except that `irradiate` adds the
//! neutrons it was given. On error the pile is unchanged.
//!
//! ```
//! use nuclear_core::fission::Fission;
//! use nuclear_core::neutron::branching;
//! use nuclear_core::{Nuclide, Rng, Sample};
//!
//! let u235 = Nuclide::new(92, 143);
//! let mut pile = Sample::new();
//! pile.add(u235, 1_000).unwrap();
//! let mut rng = Rng::new(7);
//! let mut fission = Fission::new();
//! // Slow neutrons on 500 atoms: about 85 in 100 absorptions cause fission.
//! let b = branching(u235, 0.0253).unwrap();
//! let report = pile
//!     .irradiate(u235, 500, 0.0253, b, &mut fission, &mut rng)
//!     .unwrap();
//! assert_eq!(report.fissions + report.captures, 500);
//! assert!(report.fissions > 380 && report.fissions < 470);
//! assert!(report.neutrons_released > 900);
//! ```

use std::collections::BTreeMap;

use crate::fission::{
    fissioning_nucleus, Fission, FissionError, Outcomes, Trigger, MIN_FISSIONABLE_Z,
};
use crate::fusion;
use crate::neutron::{self, Branching};
use crate::nuclide::Nuclide;
use crate::rng::Rng;
use crate::sample::{Amounts, Sample, SampleError};

/// What one reaction call did to a [`Sample`].
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ReactionReport {
    /// Fission events.
    pub fissions: u64,
    /// Neutron captures.
    pub captures: u64,
    /// Fusion events.
    pub fusions: u64,
    /// Free neutrons added to the pile.
    pub neutrons_released: u64,
    /// Pending fissions left waiting because the nucleus is too light for the
    /// model to split. Only `resolve_fissions` sets it.
    pub unresolved: u64,
    /// Energy released in keV (total Q, before any later decay).
    pub energy_released_kev: f64,
    /// False when some mass was missing, so the energy is a lower estimate.
    pub energy_known: bool,
}

impl ReactionReport {
    fn new() -> ReactionReport {
        ReactionReport {
            fissions: 0,
            captures: 0,
            fusions: 0,
            neutrons_released: 0,
            unresolved: 0,
            energy_released_kev: 0.0,
            energy_known: true,
        }
    }
}

/// What one reaction call did to an [`Amounts`] pile. Energy is in keV per
/// unit of amount.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct AmountsReactionReport {
    /// Amount that fissioned.
    pub fissions: f64,
    /// Amount of neutron captures.
    pub captures: f64,
    /// Amount of fusion events.
    pub fusions: f64,
    /// Free neutrons added to the pile.
    pub neutrons_released: f64,
    /// Pending fission left waiting because the nucleus is too light for the
    /// model to split. Only `resolve_fissions` sets it.
    pub unresolved: f64,
    /// Energy released in keV (total Q, before any later decay).
    pub energy_released_kev: f64,
    /// False when some mass was missing, so the energy is a lower estimate.
    pub energy_known: bool,
}

impl AmountsReactionReport {
    fn new() -> AmountsReactionReport {
        AmountsReactionReport {
            fissions: 0.0,
            captures: 0.0,
            fusions: 0.0,
            neutrons_released: 0.0,
            unresolved: 0.0,
            energy_released_kev: 0.0,
            energy_known: true,
        }
    }
}

fn bump(map: &mut BTreeMap<Nuclide, u64>, nuclide: Nuclide, count: u64) -> Result<(), SampleError> {
    if count == 0 {
        return Ok(());
    }
    let slot = map.entry(nuclide).or_insert(0);
    *slot = slot
        .checked_add(count)
        .ok_or(SampleError::Overflow(nuclide))?;
    Ok(())
}

fn mul(a: u64, b: u64, nuclide: Nuclide) -> Result<u64, SampleError> {
    a.checked_mul(b).ok_or(SampleError::Overflow(nuclide))
}

/// Takes `remove` out of the pile and puts `add` in, or changes nothing.
fn commit(
    sample: &mut Sample,
    remove: &BTreeMap<Nuclide, u64>,
    add: &BTreeMap<Nuclide, u64>,
) -> Result<(), SampleError> {
    for (&nuclide, &need) in remove {
        let have = sample.count(nuclide);
        if have < need {
            return Err(SampleError::NotEnough {
                nuclide,
                have: have as f64,
                need: need as f64,
            });
        }
    }
    let mut next = sample.atoms.clone();
    for (&nuclide, &need) in remove {
        if let Some(slot) = next.get_mut(&nuclide) {
            *slot -= need;
            if *slot == 0 {
                next.remove(&nuclide);
            }
        }
    }
    for (&nuclide, &count) in add {
        let slot = next.entry(nuclide).or_insert(0);
        *slot = slot
            .checked_add(count)
            .ok_or(SampleError::Overflow(nuclide))?;
    }
    sample.atoms = next;
    Ok(())
}

/// Splits `count` fissions over the channels and adds the products.
fn draw_fissions(
    outcomes: &Outcomes,
    count: u64,
    rng: &mut Rng,
    add: &mut BTreeMap<Nuclide, u64>,
    report: &mut ReactionReport,
) -> Result<(), SampleError> {
    if count == 0 {
        return Ok(());
    }
    let weights: Vec<f64> = outcomes.channels.iter().map(|c| c.probability).collect();
    let mut drawn = vec![0u64; weights.len()];
    rng.multinomial(count, &weights, &mut drawn);
    for (channel, &k) in outcomes.channels.iter().zip(&drawn) {
        if k == 0 {
            continue;
        }
        bump(add, channel.light, k)?;
        bump(add, channel.heavy, k)?;
        let neutrons = mul(k, u64::from(channel.neutrons), Nuclide::NEUTRON)?;
        bump(add, Nuclide::NEUTRON, neutrons)?;
        report.neutrons_released = report
            .neutrons_released
            .checked_add(neutrons)
            .ok_or(SampleError::Overflow(Nuclide::NEUTRON))?;
        if channel.q_known {
            report.energy_released_kev += k as f64 * channel.q_kev;
        } else {
            report.energy_known = false;
        }
    }
    Ok(())
}

impl Sample {
    /// Turns every pending fission into fission products and returns what
    /// happened.
    ///
    /// A nucleus that ended up here by spontaneous fission splits itself. One
    /// that ended up here by a fission after a beta decay splits its daughter
    /// (see [`fissioning_nucleus`]). A nucleus with fewer than
    /// [`MIN_FISSIONABLE_Z`] protons is not split: those atoms stay on the
    /// pending list and are counted in `unresolved`.
    pub fn resolve_fissions(
        &mut self,
        fission: &mut Fission,
        rng: &mut Rng,
    ) -> Result<ReactionReport, SampleError> {
        let pending: Vec<(Nuclide, u64)> = self.pending_fissions().collect();
        let mut add = BTreeMap::new();
        let mut left = BTreeMap::new();
        let mut report = ReactionReport::new();
        for &(waiting, count) in &pending {
            let splitting = fissioning_nucleus(waiting);
            if splitting.z() < MIN_FISSIONABLE_Z {
                bump(&mut left, waiting, count)?;
                report.unresolved = report
                    .unresolved
                    .checked_add(count)
                    .ok_or(SampleError::Overflow(waiting))?;
                continue;
            }
            let outcomes = fission.outcomes(splitting, Trigger::Spontaneous)?;
            draw_fissions(outcomes, count, rng, &mut add, &mut report)?;
            report.fissions = report
                .fissions
                .checked_add(count)
                .ok_or(SampleError::Overflow(waiting))?;
        }
        commit(self, &BTreeMap::new(), &add)?;
        self.pending = left;
        Ok(report)
    }

    /// Sends `neutrons` neutrons of `energy_ev` at atoms of `target`. Each is
    /// absorbed by a different target atom, so the pile must hold at least
    /// that many. Each absorption is fission with probability
    /// `branching.fission` and capture otherwise. Capture makes the next
    /// heavier isotope. Fission makes products from the evaluated yields.
    ///
    /// The neutrons come from outside the pile, so the nucleon count rises by
    /// `neutrons`.
    pub fn irradiate(
        &mut self,
        target: Nuclide,
        neutrons: u64,
        energy_ev: f64,
        branching: Branching,
        fission: &mut Fission,
        rng: &mut Rng,
    ) -> Result<ReactionReport, SampleError> {
        if !(energy_ev.is_finite() && energy_ev > 0.0) {
            return Err(FissionError::InvalidEnergy(energy_ev).into());
        }
        if !(0.0..=1.0).contains(&branching.fission) {
            return Err(SampleError::InvalidProbability(branching.fission));
        }
        let have = self.count(target);
        if neutrons > have {
            return Err(SampleError::NotEnough {
                nuclide: target,
                have: have as f64,
                need: neutrons as f64,
            });
        }
        let fissions = rng.binomial(neutrons, branching.fission);
        let captures = neutrons - fissions;
        let mut report = ReactionReport::new();
        let mut add = BTreeMap::new();
        let mut remove = BTreeMap::new();
        bump(&mut remove, target, neutrons)?;
        if captures > 0 {
            bump(&mut add, Nuclide::new(target.z(), target.n() + 1), captures)?;
            match neutron::capture_q_kev(target) {
                Some(q) => report.energy_released_kev += captures as f64 * q,
                None => report.energy_known = false,
            }
        }
        if fissions > 0 {
            let outcomes = fission.outcomes(target, Trigger::Neutron { energy_ev })?;
            draw_fissions(outcomes, fissions, rng, &mut add, &mut report)?;
        }
        report.fissions = fissions;
        report.captures = captures;
        commit(self, &remove, &add)?;
        Ok(report)
    }

    /// Fuses `events` pairs of `a` and `b` at `temperature_kev`, choosing the
    /// channel of each event with the shares of the channel reactivities.
    /// Each event uses up one atom of each; for two identical nuclei, two
    /// atoms per event.
    pub fn fuse(
        &mut self,
        a: Nuclide,
        b: Nuclide,
        events: u64,
        temperature_kev: f64,
        rng: &mut Rng,
    ) -> Result<ReactionReport, SampleError> {
        let channels = fusion::channels_for(a, b);
        let shares = fusion::branching(&channels, temperature_kev);
        if channels.is_empty() || shares.iter().all(|&s| s == 0.0) {
            return Err(SampleError::NoReaction(a, b));
        }
        let mut remove = BTreeMap::new();
        bump(&mut remove, a, events)?;
        bump(&mut remove, b, events)?;
        let mut drawn = vec![0u64; channels.len()];
        rng.multinomial(events, &shares, &mut drawn);
        let mut add = BTreeMap::new();
        let mut report = ReactionReport::new();
        for (channel, &k) in channels.iter().zip(&drawn) {
            if k == 0 {
                continue;
            }
            for &(product, per_event) in &channel.products {
                let count = mul(k, u64::from(per_event), product)?;
                bump(&mut add, product, count)?;
                if product == Nuclide::NEUTRON {
                    report.neutrons_released = report
                        .neutrons_released
                        .checked_add(count)
                        .ok_or(SampleError::Overflow(product))?;
                }
            }
            report.energy_released_kev += k as f64 * channel.q_kev;
        }
        report.fusions = events;
        commit(self, &remove, &add)?;
        Ok(report)
    }
}

fn credit(map: &mut BTreeMap<Nuclide, f64>, nuclide: Nuclide, amount: f64) {
    if amount > 0.0 {
        *map.entry(nuclide).or_insert(0.0) += amount;
    }
}

/// The expected products of `amount` fissions.
fn expect_fissions(
    outcomes: &Outcomes,
    amount: f64,
    add: &mut BTreeMap<Nuclide, f64>,
    report: &mut AmountsReactionReport,
) {
    for channel in &outcomes.channels {
        let k = amount * channel.probability;
        credit(add, channel.light, k);
        credit(add, channel.heavy, k);
        let neutrons = k * f64::from(channel.neutrons);
        credit(add, Nuclide::NEUTRON, neutrons);
        report.neutrons_released += neutrons;
        if channel.q_known {
            report.energy_released_kev += k * channel.q_kev;
        } else {
            report.energy_known = false;
        }
    }
}

fn check_amount(x: f64) -> Result<(), SampleError> {
    if x.is_finite() && x >= 0.0 {
        Ok(())
    } else {
        Err(SampleError::InvalidAmount(x))
    }
}

/// Takes `remove` out of the pile and puts `add` in, or changes nothing.
fn commit_amounts(
    amounts: &mut Amounts,
    remove: &BTreeMap<Nuclide, f64>,
    add: &BTreeMap<Nuclide, f64>,
) -> Result<(), SampleError> {
    for (&nuclide, &need) in remove {
        let have = amounts.amount(nuclide);
        if have < need {
            return Err(SampleError::NotEnough {
                nuclide,
                have,
                need,
            });
        }
    }
    let mut next = amounts.amounts.clone();
    for (&nuclide, &need) in remove {
        if let Some(slot) = next.get_mut(&nuclide) {
            *slot -= need;
            if *slot <= 0.0 {
                next.remove(&nuclide);
            }
        }
    }
    for (&nuclide, &x) in add {
        *next.entry(nuclide).or_insert(0.0) += x;
    }
    amounts.amounts = next;
    Ok(())
}

impl Amounts {
    /// The expected result of [`Sample::resolve_fissions`].
    pub fn resolve_fissions(
        &mut self,
        fission: &mut Fission,
    ) -> Result<AmountsReactionReport, SampleError> {
        let pending: Vec<(Nuclide, f64)> = self.pending_fissions().collect();
        let mut add = BTreeMap::new();
        let mut left = BTreeMap::new();
        let mut report = AmountsReactionReport::new();
        for &(waiting, amount) in &pending {
            let splitting = fissioning_nucleus(waiting);
            if splitting.z() < MIN_FISSIONABLE_Z {
                credit(&mut left, waiting, amount);
                report.unresolved += amount;
                continue;
            }
            let outcomes = fission.outcomes(splitting, Trigger::Spontaneous)?;
            expect_fissions(outcomes, amount, &mut add, &mut report);
            report.fissions += amount;
        }
        commit_amounts(self, &BTreeMap::new(), &add)?;
        self.pending = left;
        Ok(report)
    }

    /// The expected result of [`Sample::irradiate`], for a fractional number
    /// of neutrons.
    pub fn irradiate(
        &mut self,
        target: Nuclide,
        neutrons: f64,
        energy_ev: f64,
        branching: Branching,
        fission: &mut Fission,
    ) -> Result<AmountsReactionReport, SampleError> {
        check_amount(neutrons)?;
        if !(energy_ev.is_finite() && energy_ev > 0.0) {
            return Err(FissionError::InvalidEnergy(energy_ev).into());
        }
        if !(0.0..=1.0).contains(&branching.fission) {
            return Err(SampleError::InvalidProbability(branching.fission));
        }
        let fissions = neutrons * branching.fission;
        let captures = neutrons - fissions;
        let mut report = AmountsReactionReport::new();
        let mut add = BTreeMap::new();
        let mut remove = BTreeMap::new();
        credit(&mut remove, target, neutrons);
        if captures > 0.0 {
            credit(&mut add, Nuclide::new(target.z(), target.n() + 1), captures);
            match neutron::capture_q_kev(target) {
                Some(q) => report.energy_released_kev += captures * q,
                None => report.energy_known = false,
            }
        }
        if fissions > 0.0 {
            let outcomes = fission.outcomes(target, Trigger::Neutron { energy_ev })?;
            expect_fissions(outcomes, fissions, &mut add, &mut report);
        }
        report.fissions = fissions;
        report.captures = captures;
        commit_amounts(self, &remove, &add)?;
        Ok(report)
    }

    /// The expected result of [`Sample::fuse`], for a fractional number of
    /// events.
    pub fn fuse(
        &mut self,
        a: Nuclide,
        b: Nuclide,
        events: f64,
        temperature_kev: f64,
    ) -> Result<AmountsReactionReport, SampleError> {
        check_amount(events)?;
        let channels = fusion::channels_for(a, b);
        let shares = fusion::branching(&channels, temperature_kev);
        if channels.is_empty() || shares.iter().all(|&s| s == 0.0) {
            return Err(SampleError::NoReaction(a, b));
        }
        let mut remove = BTreeMap::new();
        credit(&mut remove, a, events);
        credit(&mut remove, b, events);
        let mut add = BTreeMap::new();
        let mut report = AmountsReactionReport::new();
        for (channel, &share) in channels.iter().zip(&shares) {
            let k = events * share;
            for &(product, per_event) in &channel.products {
                let x = k * f64::from(per_event);
                credit(&mut add, product, x);
                if product == Nuclide::NEUTRON {
                    report.neutrons_released += x;
                }
            }
            report.energy_released_kev += k * channel.q_kev;
        }
        report.fusions = events;
        commit_amounts(self, &remove, &add)?;
        Ok(report)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::neutron::branching;

    fn nuc(text: &str) -> Nuclide {
        Nuclide::parse(text).unwrap()
    }

    #[test]
    fn spontaneous_fission_of_californium_conserves_nucleons() {
        let cf = nuc("Cf-252");
        let mut pile = Sample::new();
        pile.pending.insert(cf, 1_000_000);
        let before = pile.baryon_number();
        let mut rng = Rng::new(11);
        let mut fission = Fission::new();
        let report = pile.resolve_fissions(&mut fission, &mut rng).unwrap();
        assert_eq!(report.fissions, 1_000_000);
        assert_eq!(pile.pending_fissions().count(), 0);
        assert_eq!(pile.baryon_number(), before);
        // 3.9 neutrons per fission in the model, 3.76 evaluated.
        let per = report.neutrons_released as f64 / 1.0e6;
        assert!(per > 3.7 && per < 4.1, "{per}");
        assert_eq!(pile.count(Nuclide::NEUTRON), report.neutrons_released);
        // About 190 MeV per fission before the fragments decay.
        let mev = report.energy_released_kev / 1.0e6 / 1000.0;
        assert!(mev > 170.0 && mev < 210.0, "{mev}");
        assert!(report.energy_known);
    }

    #[test]
    fn light_nuclei_and_delayed_fission_are_handled() {
        let mut pile = Sample::new();
        pile.pending.insert(nuc("Tl-178"), 40);
        pile.pending.insert(nuc("Bk-240"), 1_000);
        let before = pile.baryon_number();
        let mut rng = Rng::new(6);
        let r = pile
            .resolve_fissions(&mut Fission::new(), &mut rng)
            .unwrap();
        assert_eq!((r.fissions, r.unresolved), (1_000, 40));
        // The light nucleus waits; the berkelium splits its curium daughter.
        let waiting: Vec<_> = pile.pending_fissions().collect();
        assert_eq!(waiting, vec![(nuc("Tl-178"), 40)]);
        assert_eq!(pile.baryon_number(), before);
        let protons: u64 = pile.iter().map(|(n, c)| u64::from(n.z()) * c).sum();
        assert_eq!(protons, 96 * 1_000);
    }

    #[test]
    fn resolving_is_repeatable_for_a_seed_and_a_noop_when_nothing_waits() {
        let run = || {
            let mut pile = Sample::new();
            pile.pending.insert(nuc("Cf-252"), 5_000);
            let mut rng = Rng::new(3);
            let mut fission = Fission::new();
            pile.resolve_fissions(&mut fission, &mut rng).unwrap();
            pile
        };
        assert_eq!(run(), run());
        let mut empty = Sample::new();
        let mut rng = Rng::new(3);
        let report = empty
            .resolve_fissions(&mut Fission::new(), &mut rng)
            .unwrap();
        assert_eq!(report.fissions, 0);
        assert_eq!(empty, Sample::new());
    }

    #[test]
    fn slow_neutrons_on_uranium_235_mostly_fission() {
        let u235 = nuc("U-235");
        let mut pile = Sample::new();
        pile.add(u235, 100_000).unwrap();
        let before = pile.baryon_number();
        let mut rng = Rng::new(5);
        let mut fission = Fission::new();
        let b = branching(u235, 0.0253).unwrap();
        let r = pile
            .irradiate(u235, 60_000, 0.0253, b, &mut fission, &mut rng)
            .unwrap();
        assert_eq!(r.fissions + r.captures, 60_000);
        let share = r.fissions as f64 / 60_000.0;
        assert!((share - 0.855).abs() < 0.01, "{share}");
        assert_eq!(pile.baryon_number(), before + 60_000);
        assert_eq!(pile.count(u235), 40_000);
        assert_eq!(pile.count(nuc("U-236")), r.captures);
        // Capture releases the neutron separation energy, fission about 180 MeV.
        assert!(r.energy_released_kev / r.fissions as f64 > 1.6e5);
    }

    #[test]
    fn uranium_238_captures_slow_neutrons_and_breeds() {
        let u238 = nuc("U-238");
        let mut pile = Sample::new();
        pile.add(u238, 1_000).unwrap();
        let mut rng = Rng::new(9);
        let mut fission = Fission::new();
        let b = branching(u238, 0.0253).unwrap();
        let r = pile
            .irradiate(u238, 1_000, 0.0253, b, &mut fission, &mut rng)
            .unwrap();
        assert_eq!((r.fissions, r.captures), (0, 1_000));
        assert_eq!(pile.count(nuc("U-239")), 1_000);
        assert_eq!(pile.count(u238), 0);
        assert_eq!(fission.cached(), 0);
        // Two beta decays later it is plutonium: the lineage does the rest.
        let mut propagator = crate::Propagator::new();
        pile.advance(30.0 * 86_400.0, &mut rng, &mut propagator)
            .unwrap();
        assert!(pile.count(nuc("Pu-239")) + pile.count(nuc("Np-239")) > 990);
    }

    #[test]
    fn irradiating_more_than_the_pile_holds_changes_nothing() {
        let u235 = nuc("U-235");
        let mut pile = Sample::new();
        pile.add(u235, 10).unwrap();
        let copy = pile.clone();
        let mut rng = Rng::new(1);
        let b = branching(u235, 0.0253).unwrap();
        let err = pile
            .irradiate(u235, 11, 0.0253, b, &mut Fission::new(), &mut rng)
            .unwrap_err();
        assert!(matches!(err, SampleError::NotEnough { .. }));
        assert_eq!(pile, copy);
        let bad = Branching { fission: 1.5 };
        assert_eq!(
            pile.irradiate(u235, 1, 0.0253, bad, &mut Fission::new(), &mut rng),
            Err(SampleError::InvalidProbability(1.5))
        );
        assert!(matches!(
            pile.irradiate(u235, 1, -1.0, b, &mut Fission::new(), &mut rng),
            Err(SampleError::Fission(FissionError::InvalidEnergy(_)))
        ));
        assert_eq!(pile, copy);
    }

    #[test]
    fn deuterium_tritium_fusion_makes_helium_and_neutrons() {
        let mut pile = Sample::new();
        pile.add(Nuclide::H2, 1_000).unwrap();
        pile.add(Nuclide::H3, 800).unwrap();
        let before = pile.baryon_number();
        let mut rng = Rng::new(2);
        let r = pile
            .fuse(Nuclide::H3, Nuclide::H2, 700, 10.0, &mut rng)
            .unwrap();
        assert_eq!(r.fusions, 700);
        assert_eq!(pile.count(Nuclide::HE4), 700);
        assert_eq!(pile.count(Nuclide::NEUTRON), 700);
        assert_eq!(pile.count(Nuclide::H2), 300);
        assert_eq!(pile.count(Nuclide::H3), 100);
        assert_eq!(pile.baryon_number(), before);
        assert!((r.energy_released_kev / 700.0 - 17_589.0).abs() < 5.0);
    }

    #[test]
    fn deuterium_fuses_with_itself_down_both_branches() {
        let mut pile = Sample::new();
        pile.add(Nuclide::H2, 20_000).unwrap();
        let before = pile.baryon_number();
        let mut rng = Rng::new(4);
        let r = pile
            .fuse(Nuclide::H2, Nuclide::H2, 10_000, 10.0, &mut rng)
            .unwrap();
        assert_eq!(pile.count(Nuclide::H2), 0);
        assert_eq!(pile.baryon_number(), before);
        let he3 = pile.count(Nuclide::HE3);
        let triton = pile.count(Nuclide::H3);
        assert_eq!(he3 + triton, 10_000);
        assert!(he3 > 4_700 && he3 < 5_300, "{he3}");
        assert_eq!(r.neutrons_released, he3);
        // Mean of 3.27 and 4.03 MeV.
        let mean = r.energy_released_kev / 10_000.0;
        assert!(mean > 3_500.0 && mean < 3_800.0, "{mean}");
    }

    #[test]
    fn fusion_refuses_pairs_without_channels_and_short_piles() {
        let mut pile = Sample::new();
        pile.add(Nuclide::H3, 10).unwrap();
        let copy = pile.clone();
        let mut rng = Rng::new(1);
        assert_eq!(
            pile.fuse(Nuclide::H3, Nuclide::H3, 1, 10.0, &mut rng),
            Err(SampleError::NoReaction(Nuclide::H3, Nuclide::H3))
        );
        assert!(matches!(
            pile.fuse(Nuclide::H2, Nuclide::H3, 1, 10.0, &mut rng),
            Err(SampleError::NotEnough { .. })
        ));
        pile.add(Nuclide::H2, 5).unwrap();
        assert!(pile
            .fuse(Nuclide::H2, Nuclide::H3, 6, 10.0, &mut rng)
            .is_err());
        assert_eq!(pile.count(Nuclide::H2), 5);
        assert_ne!(pile, copy);
        assert_eq!(
            pile.fuse(Nuclide::H2, Nuclide::H3, 1, 0.0, &mut rng),
            Err(SampleError::NoReaction(Nuclide::H2, Nuclide::H3))
        );
    }

    #[test]
    fn amounts_match_the_expectation_of_the_sample() {
        let mut fission = Fission::new();
        let u235 = nuc("U-235");
        let mut amounts = Amounts::new();
        amounts.add(u235, 100.0);
        let before = amounts.baryon_number();
        let b = branching(u235, 0.0253).unwrap();
        let r = amounts
            .irradiate(u235, 60.0, 0.0253, b, &mut fission)
            .unwrap();
        assert!((r.fissions - 60.0 * 0.855).abs() < 1e-9);
        assert!((r.fissions + r.captures - 60.0).abs() < 1e-12);
        assert!((amounts.baryon_number() - before - 60.0).abs() < 1e-6);
        assert!((amounts.amount(u235) - 40.0).abs() < 1e-12);
        // Two to three neutrons per fission.
        let per = r.neutrons_released / r.fissions;
        assert!(per > 2.3 && per < 2.7, "{per}");
        assert_eq!(amounts.amount(Nuclide::NEUTRON), r.neutrons_released);
    }

    #[test]
    fn amounts_resolve_pending_fissions_and_fuse() {
        let mut amounts = Amounts::new();
        amounts.pending.insert(nuc("Cf-252"), 2.0);
        let before = amounts.baryon_number();
        let r = amounts.resolve_fissions(&mut Fission::new()).unwrap();
        assert_eq!(r.fissions, 2.0);
        assert!((amounts.baryon_number() - before).abs() < 1e-6);
        assert_eq!(amounts.pending_fissions().count(), 0);

        let mut gas = Amounts::new();
        gas.add(Nuclide::H2, 3.0);
        gas.add(Nuclide::H3, 3.0);
        let f = gas.fuse(Nuclide::H2, Nuclide::H3, 1.5, 10.0).unwrap();
        assert!((gas.amount(Nuclide::HE4) - 1.5).abs() < 1e-12);
        assert!((gas.amount(Nuclide::H2) - 1.5).abs() < 1e-12);
        assert!((f.energy_released_kev / 1.5 - 17_589.0).abs() < 5.0);
        assert!(matches!(
            gas.fuse(Nuclide::H2, Nuclide::H3, -1.0, 10.0),
            Err(SampleError::InvalidAmount(_))
        ));
    }
}
