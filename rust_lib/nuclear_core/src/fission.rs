// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "fission.rs"
// ============================================================================
//! Fission products: which pairs of nuclei a fission makes, and how likely
//! each pair is.
//!
//! The data is a table of independent fission product yields from ENDF/B-VIII.0.
//! It says how often each product appears per fission, but not which two
//! products come out together. [`Fission`] pairs them: for each fission it
//! picks a light and a heavy fragment, plus the prompt neutrons, so that
//! protons and nucleons are conserved exactly and the product yields stay as
//! close to the table as that allows.
//!
//! A nucleus with no table of its own gets the table of the nearest evaluated
//! nucleus, shifted to fit. Its outcomes are tagged [`YieldSource::Extended`].
//!
//! ```
//! use nuclear_core::fission::{Fission, Trigger};
//! use nuclear_core::Nuclide;
//!
//! let mut fission = Fission::new();
//! let u235 = Nuclide::new(92, 143);
//! let outcomes = fission
//!     .outcomes(u235, Trigger::Neutron { energy_ev: 0.0253 })
//!     .unwrap();
//! // About 2.4 prompt neutrons per thermal fission of uranium-235.
//! assert!(outcomes.mean_neutrons > 2.3 && outcomes.mean_neutrons < 2.7);
//! ```

use core::fmt;
use std::collections::HashMap;

use crate::binding;
use crate::decay::{self, DecayMode};
use crate::fission_yields_data as data;
use crate::nuclide::Nuclide;

/// Lightest nucleus the model will split, by proton count.
pub const MIN_FISSIONABLE_Z: u16 = 88;

/// Most prompt neutrons one fission releases in the model.
pub const MAX_NEUTRONS: u8 = 10;

/// Width of the prior on the neutron count of one fission. Terrell (1957)
/// described the spread of prompt neutron counts by a Gaussian of width 1.08,
/// and Geant4 uses 1.079. Measured widths for the spontaneous fission of
/// plutonium, curium and californium are nearer 1.15 to 1.2, so the prior is a
/// little narrow for those.
const NEUTRON_SPREAD: f64 = 1.08;

/// Rounds of scaling used to fit the pair probabilities to the yields.
const SCALING_ITERATIONS: usize = 200;

/// Rounds of scaling after the centre of the neutron prior has moved.
const REFIT_ITERATIONS: usize = 40;

/// Damping exponent of each scaling round. Below 1 so that fragments with no
/// good partner settle for a lower yield and the rest still converge.
const SCALING_EXPONENT: f64 = 0.99;

/// Pairs less likely than this are dropped and the rest are rescaled.
const MIN_PROBABILITY: f64 = 1e-12;

/// Most extra scaling runs spent matching the mean neutron count.
const FIT_ROUNDS: usize = 6;

/// A mean neutron count this close to the one the yields imply is close enough.
const FIT_TOLERANCE: f64 = 0.005;

/// How far to move the centre of the neutron prior per neutron of error.
const FIT_GAIN: f64 = 2.0;

/// What starts the fission.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Trigger {
    /// The nucleus splits by itself. The nucleus passed to
    /// [`Fission::outcomes`] is the one that splits.
    Spontaneous,
    /// A neutron of this energy in eV is absorbed. The nucleus passed to
    /// [`Fission::outcomes`] is the target, and the compound nucleus is the
    /// target plus one neutron.
    Neutron {
        /// Neutron energy in eV. Must be positive and finite.
        energy_ev: f64,
    },
}

/// Where the yields behind an outcome list came from.
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum YieldSource {
    /// The nucleus has an evaluated table of its own, for this trigger.
    Evaluated,
    /// The table of the nearest evaluated nucleus, with the light fragments
    /// shifted to fit the compound nucleus. A systematic guess, not data.
    Extended,
}

/// One way a fission can end.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Channel {
    /// The lighter fragment.
    pub light: Nuclide,
    /// The heavier fragment.
    pub heavy: Nuclide,
    /// Prompt neutrons released.
    pub neutrons: u8,
    /// Probability of this channel per fission. The probabilities of an
    /// [`Outcomes`] sum to 1.
    pub probability: f64,
    /// Energy released in keV (total Q, before the fragments decay): mass
    /// excess in minus mass excess out. 0 when `q_known` is false.
    pub q_kev: f64,
    /// False when a mass was missing, so `q_kev` is 0.
    pub q_known: bool,
}

/// Every way one kind of fission can end.
#[derive(Clone, Debug, PartialEq)]
pub struct Outcomes {
    /// The nucleus that fissions: the one passed in for a spontaneous
    /// fission, the target plus a neutron for a neutron-induced one.
    pub compound: Nuclide,
    /// The parent of the evaluated table behind these outcomes. For a
    /// neutron-induced table this is the target.
    pub reference: Nuclide,
    /// Neutron energy in eV of that table, or 0 for a spontaneous one.
    pub reference_energy_ev: f64,
    /// Whether the table belongs to the nucleus or was borrowed.
    pub source: YieldSource,
    /// The channels, ordered by light fragment, then heavy fragment.
    pub channels: Vec<Channel>,
    /// Mean prompt neutrons per fission.
    pub mean_neutrons: f64,
    /// Mean energy released per fission in keV, over the channels whose mass
    /// data is complete.
    pub mean_q_kev: f64,
}

impl Outcomes {
    /// Yield of each product per fission, in ascending nuclide order. The
    /// yields sum to 2, one per fragment.
    pub fn yields(&self) -> Vec<(Nuclide, f64)> {
        let mut map: std::collections::BTreeMap<Nuclide, f64> = std::collections::BTreeMap::new();
        for c in &self.channels {
            *map.entry(c.light).or_insert(0.0) += c.probability;
            *map.entry(c.heavy).or_insert(0.0) += c.probability;
        }
        map.into_iter().collect()
    }
}

/// Why fission outcomes could not be built.
#[derive(Clone, Debug, PartialEq)]
pub enum FissionError {
    /// The neutron energy is not positive and finite.
    InvalidEnergy(f64),
    /// The nucleus has fewer than [`MIN_FISSIONABLE_Z`] protons.
    NotFissionable(Nuclide),
}

impl fmt::Display for FissionError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            FissionError::InvalidEnergy(e) => {
                write!(f, "neutron energy {e} eV is not positive and finite")
            }
            FissionError::NotFissionable(n) => write!(
                f,
                "{n} has fewer than {MIN_FISSIONABLE_Z} protons and is not split by this model"
            ),
        }
    }
}

impl std::error::Error for FissionError {}

/// Builds and caches fission outcomes. Building the pairs for one table takes
/// a few milliseconds, so keep one `Fission` and reuse it.
#[derive(Debug, Default)]
pub struct Fission {
    cache: HashMap<(usize, u16, u16), Outcomes>,
}

impl Fission {
    /// An empty cache.
    pub fn new() -> Fission {
        Fission::default()
    }

    /// How many outcome lists are cached.
    pub fn cached(&self) -> usize {
        self.cache.len()
    }

    /// The outcomes of fission of `nuclide` under `trigger`.
    ///
    /// For [`Trigger::Spontaneous`] `nuclide` is the nucleus that splits. For
    /// [`Trigger::Neutron`] it is the target that absorbs the neutron.
    pub fn outcomes(
        &mut self,
        nuclide: Nuclide,
        trigger: Trigger,
    ) -> Result<&Outcomes, FissionError> {
        if let Trigger::Neutron { energy_ev } = trigger {
            if !(energy_ev.is_finite() && energy_ev > 0.0) {
                return Err(FissionError::InvalidEnergy(energy_ev));
            }
        }
        if nuclide.z() < MIN_FISSIONABLE_Z {
            return Err(FissionError::NotFissionable(nuclide));
        }
        let (set, source) = select_set(nuclide, trigger);
        let key = (set, nuclide.z(), nuclide.a());
        Ok(self
            .cache
            .entry(key)
            .or_insert_with(|| build(nuclide, trigger, set, source)))
    }
}

/// The nucleus that splits when `waiting` ends up on the pending fission list.
///
/// For spontaneous fission that is the nucleus itself. For a fission that
/// follows a beta decay it is the daughter of the decay: one proton fewer
/// after beta plus decay or electron capture, one more after beta minus.
pub fn fissioning_nucleus(waiting: Nuclide) -> Nuclide {
    let modes: Vec<DecayMode> = decay::outcomes(waiting)
        .iter()
        .filter(|o| o.daughter.is_none())
        .map(|o| o.mode)
        .collect();
    if !modes.is_empty() && modes.iter().all(|m| *m == DecayMode::BetaPlusFission) {
        Nuclide::new(waiting.z().saturating_sub(1), waiting.n() + 1)
    } else if !modes.is_empty() && modes.iter().all(|m| *m == DecayMode::BetaMinusFission) {
        Nuclide::new(waiting.z() + 1, waiting.n().saturating_sub(1))
    } else {
        waiting
    }
}

/// The evaluated tables, one per entry: the nucleus to pass to
/// [`Fission::outcomes`] and the trigger that selects the table. A neutron
/// entry is the target nucleus at the energy of the table.
pub fn evaluated() -> Vec<(Nuclide, Trigger)> {
    data::SETS
        .iter()
        .map(|&(pz, pa, spontaneous, energy, _, _)| {
            let nuclide = Nuclide::new(u16::from(pz), pa - u16::from(pz));
            let trigger = if spontaneous {
                Trigger::Spontaneous
            } else {
                Trigger::Neutron { energy_ev: energy }
            };
            (nuclide, trigger)
        })
        .collect()
}

/// Picks the evaluated table for `nuclide`: the nearest parent on the chart
/// with the right kind of trigger, then the nearest energy of that parent.
fn select_set(nuclide: Nuclide, trigger: Trigger) -> (usize, YieldSource) {
    let spontaneous = matches!(trigger, Trigger::Spontaneous);
    let mut best: Option<(i64, u8, u16)> = None;
    for &(pz, pa, sf, _, _, _) in data::SETS.iter() {
        if sf != spontaneous {
            continue;
        }
        let dz = i64::from(pz) - i64::from(nuclide.z());
        let dn = i64::from(pa) - i64::from(pz) - i64::from(nuclide.n());
        let distance = dz * dz + dn * dn;
        let better = match best {
            Some((d, _, _)) => distance < d,
            None => true,
        };
        if better {
            best = Some((distance, pz, pa));
        }
    }
    let (distance, best_z, best_a) = best.unwrap_or((0, 0, 0));
    let wanted = match trigger {
        Trigger::Neutron { energy_ev } => energy_ev.ln(),
        Trigger::Spontaneous => 0.0,
    };
    let mut chosen = 0;
    let mut chosen_gap = f64::INFINITY;
    for (i, &(pz, pa, sf, energy, _, _)) in data::SETS.iter().enumerate() {
        if sf != spontaneous || pz != best_z || pa != best_a {
            continue;
        }
        let gap = if spontaneous {
            0.0
        } else {
            (energy.ln() - wanted).abs()
        };
        if gap < chosen_gap {
            chosen = i;
            chosen_gap = gap;
        }
    }
    let source = if distance == 0 {
        YieldSource::Evaluated
    } else {
        YieldSource::Extended
    };
    (chosen, source)
}

/// A fragment with its share of the fragments on its side of the split.
struct Fragment {
    z: i32,
    a: i32,
    p: f64,
}

fn mass_excess_kev(nuclide: Nuclide) -> Option<f64> {
    binding::mass_excess(nuclide).map(|m| m.kev)
}

/// Builds the pair table for one nucleus, trigger and evaluated set.
fn build(nuclide: Nuclide, trigger: Trigger, set: usize, source: YieldSource) -> Outcomes {
    let (pz, pa, spontaneous, set_energy, start, end) = data::SETS[set];
    let neutron_in: i32 = if spontaneous { 0 } else { 1 };
    let ref_z = i32::from(pz);
    let ref_a = i32::from(pa) + neutron_in;
    let comp_z = i32::from(nuclide.z());
    let comp_a = i32::from(nuclide.a()) + neutron_in;
    let (shift_z, shift_a) = (comp_z - ref_z, comp_a - ref_a);

    // Split the evaluated fragments into the light and heavy side of the
    // reference compound. Light fragments move with the compound; the heavy
    // peak stays where the shells put it.
    let rows = &data::ROWS[start as usize..end as usize];
    let total: f64 = rows.iter().map(|r| f64::from(r.2)).sum();
    let mean_a: f64 = rows
        .iter()
        .map(|r| f64::from(r.1) * f64::from(r.2))
        .sum::<f64>()
        / total;
    let mut light: Vec<Fragment> = Vec::new();
    let mut heavy: Vec<Fragment> = Vec::new();
    for &(z, a, y) in rows {
        let (z, a) = (i32::from(z), i32::from(a));
        let p = f64::from(y) / total;
        if 2 * z < ref_z || (2 * z == ref_z && f64::from(a) <= mean_a) {
            let (z, a) = (z + shift_z, a + shift_a);
            if z >= 1 && a >= z {
                light.push(Fragment { z, a, p });
            }
        } else {
            heavy.push(Fragment { z, a, p });
        }
    }

    // Mean neutron count that the fragment masses imply.
    let share: f64 = light.iter().chain(heavy.iter()).map(|f| f.p).sum();
    let mean_fragment: f64 = light
        .iter()
        .chain(heavy.iter())
        .map(|f| f64::from(f.a) * f.p)
        .sum::<f64>()
        / share;
    let centre = (f64::from(comp_a) - 2.0 * mean_fragment).clamp(0.0, f64::from(MAX_NEUTRONS));

    // Edges join a light and a heavy fragment whose charges add up to the
    // compound and whose masses leave room for 0 to MAX_NEUTRONS neutrons.
    let heavy_index: HashMap<(i32, i32), usize> = heavy
        .iter()
        .enumerate()
        .map(|(j, f)| ((f.z, f.a), j))
        .collect();
    let mut forward: Vec<Vec<(usize, u8)>> = vec![Vec::new(); light.len()];
    let mut backward: Vec<Vec<(usize, u8)>> = vec![Vec::new(); heavy.len()];
    for (i, l) in light.iter().enumerate() {
        for nu in 0..=MAX_NEUTRONS {
            if let Some(&j) = heavy_index.get(&(comp_z - l.z, comp_a - l.a - i32::from(nu))) {
                forward[i].push((j, nu));
                backward[j].push((i, nu));
            }
        }
    }

    // Drop fragments that no longer have a partner, until none are left.
    let mut alive_l = vec![true; light.len()];
    let mut alive_h = vec![true; heavy.len()];
    loop {
        let mut changed = false;
        for i in 0..light.len() {
            if alive_l[i] && !forward[i].iter().any(|&(j, _)| alive_h[j]) {
                alive_l[i] = false;
                changed = true;
            }
        }
        for j in 0..heavy.len() {
            if alive_h[j] && !backward[j].iter().any(|&(i, _)| alive_l[i]) {
                alive_h[j] = false;
                changed = true;
            }
        }
        if !changed {
            break;
        }
    }
    let side = |fragments: &[Fragment], alive: &[bool]| -> Vec<f64> {
        let sum: f64 = fragments
            .iter()
            .zip(alive)
            .filter(|(_, &a)| a)
            .map(|(f, _)| f.p)
            .sum();
        fragments
            .iter()
            .zip(alive)
            .map(|(f, &a)| if a && sum > 0.0 { f.p / sum } else { 0.0 })
            .collect()
    };
    let target_l = side(&light, &alive_l);
    let target_h = side(&heavy, &alive_h);

    // Scale rows and columns of the edge weights until the pair probabilities
    // have the fragment shares as their sums. The prior on the neutron count
    // is a Gaussian around `centre`. `u` and `v` carry over between calls, so
    // a repeat call with a nearby centre needs few rounds.
    let mut u = vec![0.0f64; light.len()];
    let mut v: Vec<f64> = alive_h.iter().map(|&a| if a { 1.0 } else { 0.0 }).collect();
    let mut solve = |centre: f64, rounds: usize| -> (Vec<(usize, usize, u8, f64)>, f64) {
        let mut weight = [0.0f64; MAX_NEUTRONS as usize + 1];
        for (nu, w) in weight.iter_mut().enumerate() {
            let d = (nu as f64 - centre) / NEUTRON_SPREAD;
            *w = (-0.5 * d * d).exp();
        }
        for _ in 0..rounds {
            for (i, edges) in forward.iter().enumerate() {
                let sum: f64 = edges
                    .iter()
                    .map(|&(j, nu)| weight[nu as usize] * v[j])
                    .sum();
                u[i] = if alive_l[i] && sum > 0.0 {
                    (target_l[i] / sum).powf(SCALING_EXPONENT)
                } else {
                    0.0
                };
            }
            for (j, edges) in backward.iter().enumerate() {
                let sum: f64 = edges
                    .iter()
                    .map(|&(i, nu)| weight[nu as usize] * u[i])
                    .sum();
                v[j] = if alive_h[j] && sum > 0.0 {
                    (target_h[j] / sum).powf(SCALING_EXPONENT)
                } else {
                    0.0
                };
            }
        }
        let mut pairs: Vec<(usize, usize, u8, f64)> = Vec::new();
        for (i, edges) in forward.iter().enumerate() {
            for &(j, nu) in edges {
                let mass = u[i] * weight[nu as usize] * v[j];
                if mass > 0.0 && mass.is_finite() {
                    pairs.push((i, j, nu, mass));
                }
            }
        }
        let sum: f64 = pairs.iter().map(|p| p.3).sum();
        pairs.retain(|p| p.3 / sum >= MIN_PROBABILITY);
        let sum: f64 = pairs.iter().map(|p| p.3).sum();
        let mean: f64 = pairs.iter().map(|p| f64::from(p.2) * p.3).sum::<f64>() / sum;
        (pairs, mean)
    };

    // Unmatched fragments pull the mean neutron count away from the one the
    // yields imply. Move the centre of the prior until they agree.
    let wanted = f64::from(comp_a) - 2.0 * mean_fragment;
    let mut centre = centre;
    let (mut pairs, mut mean) = solve(centre, SCALING_ITERATIONS);
    for _ in 0..FIT_ROUNDS {
        let gap = wanted - mean;
        if gap.abs() < FIT_TOLERANCE {
            break;
        }
        centre = (centre + FIT_GAIN * gap).clamp(0.0, f64::from(MAX_NEUTRONS));
        (pairs, mean) = solve(centre, REFIT_ITERATIONS);
    }
    let sum: f64 = pairs.iter().map(|p| p.3).sum();

    // Energy released by each pair.
    let neutron_excess = mass_excess_kev(Nuclide::NEUTRON);
    let reactants = match trigger {
        Trigger::Spontaneous => mass_excess_kev(nuclide),
        Trigger::Neutron { .. } => match (mass_excess_kev(nuclide), neutron_excess) {
            (Some(t), Some(n)) => Some(t + n),
            _ => None,
        },
    };
    let to_nuclide = |f: &Fragment| Nuclide::new(f.z as u16, (f.a - f.z) as u16);
    let mut channels = Vec::with_capacity(pairs.len());
    let mut mean_neutrons = 0.0;
    let mut mean_q = 0.0;
    let mut known_share = 0.0;
    for (i, j, nu, mass) in pairs {
        let (l, h) = (to_nuclide(&light[i]), to_nuclide(&heavy[j]));
        let probability = mass / sum;
        let q = match (
            reactants,
            mass_excess_kev(l),
            mass_excess_kev(h),
            neutron_excess,
        ) {
            (Some(r), Some(el), Some(eh), Some(en)) => Some(r - el - eh - f64::from(nu) * en),
            _ => None,
        };
        mean_neutrons += probability * f64::from(nu);
        if let Some(q) = q {
            mean_q += probability * q;
            known_share += probability;
        }
        channels.push(Channel {
            light: l,
            heavy: h,
            neutrons: nu,
            probability,
            q_kev: q.unwrap_or(0.0),
            q_known: q.is_some(),
        });
    }
    Outcomes {
        compound: Nuclide::new(nuclide.z(), (comp_a - i32::from(nuclide.z())) as u16),
        reference: Nuclide::new(u16::from(pz), pa - u16::from(pz)),
        reference_energy_ev: if spontaneous { 0.0 } else { set_energy },
        source,
        channels,
        mean_neutrons,
        mean_q_kev: if known_share > 0.0 {
            mean_q / known_share
        } else {
            0.0
        },
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn nuc(text: &str) -> Nuclide {
        Nuclide::parse(text).unwrap()
    }

    #[test]
    fn uranium_235_thermal_conserves_and_sums_to_one() {
        let mut f = Fission::new();
        let o = f
            .outcomes(nuc("U-235"), Trigger::Neutron { energy_ev: 0.0253 })
            .unwrap();
        assert_eq!(o.compound, nuc("U-236"));
        assert_eq!(o.reference, nuc("U-235"));
        assert_eq!(o.source, YieldSource::Evaluated);
        let sum: f64 = o.channels.iter().map(|c| c.probability).sum();
        assert!((sum - 1.0).abs() < 1e-9, "sum {sum}");
        for c in &o.channels {
            assert_eq!(c.light.z() + c.heavy.z(), 92);
            assert_eq!(c.light.a() + c.heavy.a() + u16::from(c.neutrons), 236);
        }
    }

    #[test]
    fn uranium_235_thermal_matches_the_evaluated_yields() {
        let mut f = Fission::new();
        let o = f
            .outcomes(nuc("U-235"), Trigger::Neutron { energy_ev: 0.0253 })
            .unwrap();
        // The table implies 2.40 neutrons per fission and the evaluated mean
        // is 2.44. The pairing lands a little above both.
        assert!((o.mean_neutrons - 2.44).abs() < 0.1, "{}", o.mean_neutrons);
        // The yields sum to two, one per fragment.
        let total: f64 = o.yields().iter().map(|(_, y)| y).sum();
        assert!((total - 2.0).abs() < 1e-9);
        // The model keeps the double hump: heavy peak near mass 139 to 140,
        // light peak near 95, a valley near 117.
        let by_mass = |lo: u16, hi: u16| -> f64 {
            o.yields()
                .iter()
                .filter(|(n, _)| (lo..=hi).contains(&n.a()))
                .map(|(_, y)| y)
                .sum()
        };
        assert!(by_mass(133, 145) > 0.38 * 2.0);
        assert!(by_mass(90, 100) > 0.30 * 2.0);
        assert!(by_mass(113, 121) < 0.01 * 2.0);
    }

    #[test]
    fn energy_released_is_about_180_mev_for_thermal_uranium_235() {
        let mut f = Fission::new();
        let o = f
            .outcomes(nuc("U-235"), Trigger::Neutron { energy_ev: 0.0253 })
            .unwrap();
        let mev = o.mean_q_kev / 1000.0;
        assert!(mev > 165.0 && mev < 195.0, "mean Q {mev} MeV");
    }

    #[test]
    fn neutron_energy_picks_the_nearest_table() {
        let mut f = Fission::new();
        let pu = nuc("Pu-239");
        let pick = |f: &mut Fission, e: f64| {
            f.outcomes(pu, Trigger::Neutron { energy_ev: e })
                .unwrap()
                .reference_energy_ev
        };
        assert_eq!(pick(&mut f, 1.0), 0.0253);
        assert_eq!(pick(&mut f, 1.0e6), 500_000.0);
        assert_eq!(pick(&mut f, 5.0e6), 2_000_000.0);
        assert_eq!(pick(&mut f, 1.0e7), 14_000_000.0);
        assert_eq!(f.cached(), 4);
    }

    #[test]
    fn a_nucleus_without_a_table_borrows_the_nearest() {
        let mut f = Fission::new();
        let o = f.outcomes(nuc("Cf-254"), Trigger::Spontaneous).unwrap();
        assert_eq!(o.source, YieldSource::Extended);
        assert_eq!(o.reference, nuc("Cf-252"));
        assert_eq!(o.compound, nuc("Cf-254"));
        let sum: f64 = o.channels.iter().map(|c| c.probability).sum();
        assert!((sum - 1.0).abs() < 1e-9);
        for c in &o.channels {
            assert_eq!(c.light.z() + c.heavy.z(), 98);
            assert_eq!(c.light.a() + c.heavy.a() + u16::from(c.neutrons), 254);
        }
        // Two more nucleons than the evaluated nucleus: about two more
        // neutrons come out, to within a neutron.
        let base = f.outcomes(nuc("Cf-252"), Trigger::Spontaneous).unwrap();
        assert_eq!(base.source, YieldSource::Evaluated);
        let evaluated_mean = base.mean_neutrons;
        let extended_mean = f
            .outcomes(nuc("Cf-254"), Trigger::Spontaneous)
            .unwrap()
            .mean_neutrons;
        assert!((extended_mean - evaluated_mean).abs() < 0.2);
    }

    #[test]
    fn light_nuclei_and_bad_energies_are_refused() {
        let mut f = Fission::new();
        assert_eq!(
            f.outcomes(nuc("Fe-56"), Trigger::Spontaneous).unwrap_err(),
            FissionError::NotFissionable(nuc("Fe-56"))
        );
        for bad in [0.0, -1.0, f64::NAN, f64::INFINITY] {
            let err = f
                .outcomes(nuc("U-235"), Trigger::Neutron { energy_ev: bad })
                .unwrap_err();
            assert!(matches!(err, FissionError::InvalidEnergy(_)));
        }
        assert_eq!(f.cached(), 0);
    }

    #[test]
    fn delayed_fission_splits_the_daughter() {
        // Bk-240 ends in fission after electron capture or beta plus decay.
        assert_eq!(fissioning_nucleus(nuc("Bk-240")), nuc("Cm-240"));
        // Fm-256 and Cf-252 split themselves.
        assert_eq!(fissioning_nucleus(nuc("Fm-256")), nuc("Fm-256"));
        assert_eq!(fissioning_nucleus(nuc("Cf-252")), nuc("Cf-252"));
        // Pa-236 has a beta minus delayed branch.
        assert_eq!(fissioning_nucleus(nuc("Pa-236")), nuc("U-236"));
        // A nucleus with no fission mode is returned unchanged.
        assert_eq!(fissioning_nucleus(nuc("Fe-56")), nuc("Fe-56"));
    }

    #[test]
    fn outcomes_are_cached_and_repeatable() {
        let mut f = Fission::new();
        let t = Trigger::Neutron { energy_ev: 0.0253 };
        let first = f.outcomes(nuc("Pu-239"), t).unwrap().clone();
        let second = f.outcomes(nuc("Pu-239"), t).unwrap().clone();
        assert_eq!(first, second);
        assert_eq!(f.cached(), 1);
        let mut other = Fission::new();
        assert_eq!(other.outcomes(nuc("Pu-239"), t).unwrap().clone(), first);
    }
}
