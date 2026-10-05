// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "sample.rs"
// ============================================================================
//! A pile of atoms that decays over time.
//!
//! [`Sample`] holds whole-number atom counts and moves them with exact
//! multinomial draws, so it shows real counting noise. [`Amounts`] holds
//! fractional amounts and moves them with the exact expectation, for piles too
//! large to count. Both use the [`Propagator`], so a step of any length takes
//! one lookup per nuclide, however many generations it spans.
//!
//! Emitted nuclei (alphas, neutrons, protons) join the pile at the end of the
//! step that produced them, so an unstable emitted nucleus starts to decay on
//! the next step. Atoms that end in a mode with no single daughter (fission)
//! move to a pending list until `resolve_fissions` (in [`react`](crate::react))
//! turns them into fission products.

use core::fmt;
use std::collections::BTreeMap;

use crate::fission::FissionError;
use crate::lineage::{LineageError, Propagator};
use crate::nuclide::Nuclide;
use crate::rng::Rng;

/// Why a step failed.
#[derive(Clone, Debug, PartialEq)]
pub enum SampleError {
    /// The propagator could not build a transition.
    Lineage(LineageError),
    /// A count would pass `u64::MAX`.
    Overflow(Nuclide),
    /// Fission outcomes could not be built.
    Fission(FissionError),
    /// The pile holds less of a nuclide than a reaction needs.
    NotEnough {
        /// The nuclide that runs short.
        nuclide: Nuclide,
        /// How much the pile holds.
        have: f64,
        /// How much the reaction needs.
        need: f64,
    },
    /// No fusion channel runs for this pair at this temperature.
    NoReaction(Nuclide, Nuclide),
    /// A probability outside 0 to 1.
    InvalidProbability(f64),
    /// An amount that is negative or not finite.
    InvalidAmount(f64),
}

impl fmt::Display for SampleError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            SampleError::Lineage(e) => write!(f, "{e}"),
            SampleError::Overflow(n) => write!(f, "the count of {n} would overflow"),
            SampleError::Fission(e) => write!(f, "{e}"),
            SampleError::NotEnough {
                nuclide,
                have,
                need,
            } => {
                write!(
                    f,
                    "the pile holds {have} of {nuclide} and the reaction needs {need}"
                )
            }
            SampleError::NoReaction(a, b) => {
                write!(
                    f,
                    "no fusion channel runs for {a} and {b} at this temperature"
                )
            }
            SampleError::InvalidProbability(p) => write!(f, "{p} is not a probability"),
            SampleError::InvalidAmount(x) => write!(f, "{x} is not a usable amount"),
        }
    }
}

impl std::error::Error for SampleError {}

impl From<LineageError> for SampleError {
    fn from(e: LineageError) -> Self {
        SampleError::Lineage(e)
    }
}

impl From<FissionError> for SampleError {
    fn from(e: FissionError) -> Self {
        SampleError::Fission(e)
    }
}

/// What one step of a [`Sample`] did.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct StepReport {
    /// Atoms that are no longer the nuclide they started the step as.
    pub atoms_changed: u64,
    /// Energy released by all atoms in the step, in keV (total Q).
    pub energy_released_kev: f64,
    /// Atoms that moved to the pending fission list in this step.
    pub new_pending_fissions: u64,
    /// False when some mass was missing, so the energy is a lower estimate.
    pub energy_known: bool,
}

/// A pile of whole atoms.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Sample {
    pub(crate) atoms: BTreeMap<Nuclide, u64>,
    pub(crate) pending: BTreeMap<Nuclide, u64>,
    elapsed_seconds: f64,
}

impl Sample {
    /// An empty sample.
    pub fn new() -> Sample {
        Sample::default()
    }

    /// Adds `count` atoms of `nuclide`.
    pub fn add(&mut self, nuclide: Nuclide, count: u64) -> Result<(), SampleError> {
        if count == 0 {
            return Ok(());
        }
        let slot = self.atoms.entry(nuclide).or_insert(0);
        *slot = slot
            .checked_add(count)
            .ok_or(SampleError::Overflow(nuclide))?;
        Ok(())
    }

    /// Atoms of `nuclide` in the pile.
    pub fn count(&self, nuclide: Nuclide) -> u64 {
        self.atoms.get(&nuclide).copied().unwrap_or(0)
    }

    /// Every (nuclide, count) in the pile, in ascending nuclide order.
    pub fn iter(&self) -> impl Iterator<Item = (Nuclide, u64)> + '_ {
        self.atoms.iter().map(|(&n, &c)| (n, c))
    }

    /// Atoms awaiting fission products, as (fissioning nuclide, count).
    pub fn pending_fissions(&self) -> impl Iterator<Item = (Nuclide, u64)> + '_ {
        self.pending.iter().map(|(&n, &c)| (n, c))
    }

    /// Seconds of simulated time since the sample was created.
    pub fn elapsed_seconds(&self) -> f64 {
        self.elapsed_seconds
    }

    /// Total atoms in the pile, not counting pending fissions.
    pub fn total_atoms(&self) -> u128 {
        self.atoms.values().map(|&c| u128::from(c)).sum()
    }

    /// Total nucleons, pending fissions included. A step never changes it.
    pub fn baryon_number(&self) -> u128 {
        let sum = |map: &BTreeMap<Nuclide, u64>| -> u128 {
            map.iter()
                .map(|(n, &c)| u128::from(n.a()) * u128::from(c))
                .sum()
        };
        sum(&self.atoms) + sum(&self.pending)
    }

    /// Advances the sample by `seconds`, drawing from `rng`.
    ///
    /// Nuclides are processed in ascending order, so the same seed always
    /// gives the same result. On error the sample is unchanged.
    pub fn advance(
        &mut self,
        seconds: f64,
        rng: &mut Rng,
        propagator: &mut Propagator,
    ) -> Result<StepReport, SampleError> {
        let mut next: BTreeMap<Nuclide, u64> = BTreeMap::new();
        let mut pending = self.pending.clone();
        let mut report = StepReport {
            atoms_changed: 0,
            energy_released_kev: 0.0,
            new_pending_fissions: 0,
            energy_known: true,
        };
        let add =
            |map: &mut BTreeMap<Nuclide, u64>, n: Nuclide, c: u64| -> Result<(), SampleError> {
                let slot = map.entry(n).or_insert(0);
                *slot = slot.checked_add(c).ok_or(SampleError::Overflow(n))?;
                Ok(())
            };
        let mut counts: Vec<u64> = Vec::new();
        let mut weights: Vec<f64> = Vec::new();
        for (&nuclide, &count) in &self.atoms {
            let transition = propagator.transition(nuclide, seconds)?;
            report.energy_known &= transition.energy_known;
            weights.clear();
            weights.extend(transition.outcomes.iter().map(|o| o.probability));
            counts.clear();
            counts.resize(weights.len(), 0);
            rng.multinomial(count, &weights, &mut counts);
            for (outcome, &k) in transition.outcomes.iter().zip(&counts) {
                if k == 0 {
                    continue;
                }
                let state = &outcome.state;
                let unchanged =
                    state.residual == nuclide && state.emitted.is_empty() && !state.pending_fission;
                if !unchanged {
                    report.atoms_changed += k;
                }
                if state.pending_fission {
                    add(&mut pending, state.residual, k)?;
                    report.new_pending_fissions += k;
                } else {
                    add(&mut next, state.residual, k)?;
                }
                for &(emitted, multiplicity) in &state.emitted {
                    let n = k
                        .checked_mul(u64::from(multiplicity))
                        .ok_or(SampleError::Overflow(emitted))?;
                    add(&mut next, emitted, n)?;
                }
                report.energy_released_kev += k as f64 * outcome.energy_kev;
            }
        }
        self.atoms = next;
        self.pending = pending;
        self.elapsed_seconds += seconds;
        Ok(report)
    }
}

/// What one step of an [`Amounts`] pile did.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct AmountsReport {
    /// Amount that is no longer the nuclide it started the step as.
    pub amount_changed: f64,
    /// Energy released in the step, in keV per unit of amount (total Q).
    pub energy_released_kev: f64,
    /// Amount that moved to the pending fission list in this step.
    pub new_pending: f64,
    /// False when some mass was missing, so the energy is a lower estimate.
    pub energy_known: bool,
}

/// A pile measured in fractional amounts (atoms, moles, or any unit).
///
/// Each step applies the exact expected change, so there is no noise. Use it
/// for piles too big to count one atom at a time. The energy is in keV per
/// unit of amount, so for atoms it is keV and for moles it is keV per mole.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Amounts {
    pub(crate) amounts: BTreeMap<Nuclide, f64>,
    pub(crate) pending: BTreeMap<Nuclide, f64>,
    elapsed_seconds: f64,
}

impl Amounts {
    /// An empty pile.
    pub fn new() -> Amounts {
        Amounts::default()
    }

    /// Adds `amount` of `nuclide`. Negative and non-finite amounts are ignored.
    pub fn add(&mut self, nuclide: Nuclide, amount: f64) {
        if amount.is_finite() && amount > 0.0 {
            *self.amounts.entry(nuclide).or_insert(0.0) += amount;
        }
    }

    /// Amount of `nuclide` in the pile.
    pub fn amount(&self, nuclide: Nuclide) -> f64 {
        self.amounts.get(&nuclide).copied().unwrap_or(0.0)
    }

    /// Every (nuclide, amount), in ascending nuclide order.
    pub fn iter(&self) -> impl Iterator<Item = (Nuclide, f64)> + '_ {
        self.amounts.iter().map(|(&n, &a)| (n, a))
    }

    /// Amounts awaiting fission products, as (fissioning nuclide, amount).
    pub fn pending_fissions(&self) -> impl Iterator<Item = (Nuclide, f64)> + '_ {
        self.pending.iter().map(|(&n, &a)| (n, a))
    }

    /// Seconds of simulated time since the pile was created.
    pub fn elapsed_seconds(&self) -> f64 {
        self.elapsed_seconds
    }

    /// Total nucleons, pending fissions included.
    pub fn baryon_number(&self) -> f64 {
        let sum = |map: &BTreeMap<Nuclide, f64>| -> f64 {
            map.iter().map(|(n, a)| f64::from(n.a()) * a).sum()
        };
        sum(&self.amounts) + sum(&self.pending)
    }

    /// Advances the pile by `seconds` with the exact expected change.
    /// On error the pile is unchanged.
    pub fn advance(
        &mut self,
        seconds: f64,
        propagator: &mut Propagator,
    ) -> Result<AmountsReport, SampleError> {
        let mut next: BTreeMap<Nuclide, f64> = BTreeMap::new();
        let mut pending = self.pending.clone();
        let mut report = AmountsReport {
            amount_changed: 0.0,
            energy_released_kev: 0.0,
            new_pending: 0.0,
            energy_known: true,
        };
        for (&nuclide, &amount) in &self.amounts {
            let transition = propagator.transition(nuclide, seconds)?;
            report.energy_known &= transition.energy_known;
            for outcome in &transition.outcomes {
                let share = amount * outcome.probability;
                let state = &outcome.state;
                let unchanged =
                    state.residual == nuclide && state.emitted.is_empty() && !state.pending_fission;
                if !unchanged {
                    report.amount_changed += share;
                }
                if state.pending_fission {
                    *pending.entry(state.residual).or_insert(0.0) += share;
                    report.new_pending += share;
                } else {
                    *next.entry(state.residual).or_insert(0.0) += share;
                }
                for &(emitted, multiplicity) in &state.emitted {
                    *next.entry(emitted).or_insert(0.0) += share * f64::from(multiplicity);
                }
                report.energy_released_kev += share * outcome.energy_kev;
            }
        }
        self.amounts = next;
        self.pending = pending;
        self.elapsed_seconds += seconds;
        Ok(report)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn nuc(text: &str) -> Nuclide {
        Nuclide::parse(text).unwrap()
    }

    fn mixed_sample() -> Sample {
        let mut s = Sample::new();
        for (name, count) in [
            ("U-238", 20_000u64),
            ("Kr-92", 30_000),
            ("C-14", 50_000),
            ("Na-22", 10_000),
            ("Cf-252", 40_000),
            ("Li-8", 5_000),
            ("Fe-56", 1_000),
        ] {
            s.add(nuc(name), count).unwrap();
        }
        s
    }

    #[test]
    fn nucleon_count_never_changes() {
        let mut s = mixed_sample();
        let start = s.baryon_number();
        let mut rng = Rng::new(1);
        let mut p = Propagator::new();
        for seconds in [1.0, 60.0, 1e4, 1e8, 1e11, 1e30] {
            s.advance(seconds, &mut rng, &mut p).unwrap();
            assert_eq!(s.baryon_number(), start, "after {seconds} s");
        }
        assert!(
            s.pending_fissions().next().is_some(),
            "Cf-252 should have fissioned some atoms"
        );
    }

    #[test]
    fn the_same_seed_gives_the_same_pile() {
        let run = |seed| {
            let mut s = mixed_sample();
            let mut rng = Rng::new(seed);
            let mut p = Propagator::new();
            s.advance(1e7, &mut rng, &mut p).unwrap();
            s.advance(1e9, &mut rng, &mut p).unwrap();
            s
        };
        assert_eq!(run(5), run(5));
        assert_ne!(run(5), run(6));
    }

    #[test]
    fn counting_noise_matches_the_binomial() {
        let c14 = nuc("C-14");
        let half_life = crate::decay::half_life(c14).unwrap().seconds;
        let mut s = Sample::new();
        s.add(c14, 1_000_000).unwrap();
        let mut rng = Rng::new(77);
        let mut p = Propagator::new();
        let report = s.advance(half_life, &mut rng, &mut p).unwrap();
        // 1e6 atoms, p = 1/2: mean 500000, standard deviation 500.
        assert!((s.count(nuc("N-14")) as f64 - 500_000.0).abs() < 5.0 * 500.0);
        assert_eq!(s.count(c14) + s.count(nuc("N-14")), 1_000_000);
        assert_eq!(report.atoms_changed, s.count(nuc("N-14")));
        assert!(report.energy_known);
        // 156.475 keV each
        let expected = s.count(nuc("N-14")) as f64 * 156.475;
        assert!((report.energy_released_kev - expected).abs() / expected < 1e-3);
    }

    #[test]
    fn alphas_join_the_pile() {
        let mut s = Sample::new();
        s.add(nuc("Po-210"), 100_000).unwrap();
        let mut rng = Rng::new(3);
        let mut p = Propagator::new();
        s.advance(1e12, &mut rng, &mut p).unwrap();
        assert_eq!(s.count(nuc("Po-210")), 0);
        assert_eq!(s.count(nuc("Pb-206")), 100_000);
        assert_eq!(s.count(Nuclide::HE4), 100_000);
    }

    #[test]
    fn failed_steps_leave_the_pile_alone() {
        let mut s = Sample::new();
        s.add(nuc("C-14"), 10).unwrap();
        let before = s.clone();
        let mut rng = Rng::new(1);
        let mut p = Propagator::new();
        assert!(s.advance(-5.0, &mut rng, &mut p).is_err());
        assert_eq!(s, before);
        let mut big = Sample::new();
        big.add(Nuclide::HE4, u64::MAX).unwrap();
        assert!(big.add(Nuclide::HE4, 1).is_err());
    }

    #[test]
    fn amounts_follow_the_exact_expectation() {
        let c14 = nuc("C-14");
        let half_life = crate::decay::half_life(c14).unwrap().seconds;
        let mut a = Amounts::new();
        a.add(c14, 1.0);
        let mut p = Propagator::new();
        a.advance(half_life, &mut p).unwrap();
        a.advance(half_life, &mut p).unwrap();
        assert!((a.amount(c14) - 0.25).abs() < 1e-12);
        assert!((a.amount(nuc("N-14")) - 0.75).abs() < 1e-12);
        assert!((a.elapsed_seconds() - 2.0 * half_life).abs() < 1e-3);
    }

    #[test]
    fn amounts_split_californium_252_between_curium_and_fission() {
        let cf = nuc("Cf-252");
        let half_life = crate::decay::half_life(cf).unwrap().seconds;
        let mut a = Amounts::new();
        a.add(cf, 1.0);
        let mut p = Propagator::new();
        let report = a.advance(half_life, &mut p).unwrap();
        // Half decays. NUBASE2020 gives 3.1028 percent of the decays as
        // spontaneous fission (older tables say 3.092) and the rest as alpha.
        let pending: f64 = a.pending_fissions().map(|(_, x)| x).sum();
        assert!((pending - 0.5 * 0.031_028).abs() < 5e-6, "{pending}");
        assert!((report.new_pending - pending).abs() < 1e-15);
        assert!((a.amount(nuc("Cm-248")) - 0.5 * 0.968_972).abs() < 5e-6);
        assert!((a.baryon_number() - 252.0).abs() < 1e-9);
    }

    #[test]
    fn amounts_ignore_nonsense_input() {
        let mut a = Amounts::new();
        a.add(nuc("C-14"), -1.0);
        a.add(nuc("C-14"), f64::NAN);
        a.add(nuc("C-14"), 0.0);
        assert_eq!(a.iter().count(), 0);
    }
}
