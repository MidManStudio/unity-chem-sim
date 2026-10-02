// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "decay.rs"
// ============================================================================
//! Decay modes, half-lives and decay outcomes from NUBASE2020.
//!
//! Every nuclide in the chart has a [`HalfLife`] and a list of raw [`Branch`]
//! rows. [`outcomes`] turns the raw rows into a distribution that sums to 100
//! percent, and [`chain_to_stability`] follows the most likely outcome until it
//! reaches a stable nuclide.
//!
//! Masses here are atomic masses, as everywhere in this crate. Emitted protons
//! are written as hydrogen-1 atoms, and electrons and neutrinos are not listed.

use core::fmt;
use core::iter;

use crate::binding;
use crate::elements;
use crate::nubase2020_data::{self as data, BranchRow, Row};
use crate::nuclide::Nuclide;
use crate::reaction::QValue;

/// Number of nuclides that have decay data. It is the same set as the mass table.
pub const ROW_COUNT: usize = data::ROW_COUNT;

/// Electron rest energy in keV (CODATA 2018).
pub const ELECTRON_MASS_KEV: f64 = 510.998950;

/// Upper bound on chain length. Real chains are far shorter, so reaching it
/// means something is wrong.
pub const MAX_CHAIN_STEPS: usize = 500;

/// How to read a [`HalfLife`] value.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum HalfLifeState {
    /// NUBASE2020 gives neither a half-life nor a limit.
    Unknown,
    /// Stable, or no finite half-life has been established.
    Stable,
    /// Decays by particle emission and no half-life is given.
    ParticleUnstable,
    /// A value, usually with an uncertainty.
    Exact,
    /// An approximate value (written with `~`).
    Approximate,
    /// The value is a lower limit (written with `>`).
    AtLeast,
    /// The value is an upper limit (written with `<`).
    AtMost,
}

/// A ground-state half-life.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct HalfLife {
    /// How to read `seconds`.
    pub state: HalfLifeState,
    /// Half-life in seconds. Zero when `state` carries no number.
    pub seconds: f64,
    /// True when NUBASE2020 estimates the value from trends in neighboring
    /// nuclei instead of measurement.
    pub estimated: bool,
}

impl HalfLife {
    /// The half-life in seconds when `state` carries a number, else `None`.
    pub fn finite_seconds(self) -> Option<f64> {
        match self.state {
            HalfLifeState::Exact
            | HalfLifeState::Approximate
            | HalfLifeState::AtLeast
            | HalfLifeState::AtMost => Some(self.seconds),
            HalfLifeState::Unknown | HalfLifeState::Stable | HalfLifeState::ParticleUnstable => {
                None
            }
        }
    }

    /// True for nuclides NUBASE2020 lists as stable.
    pub fn is_stable(self) -> bool {
        self.state == HalfLifeState::Stable
    }
}

/// How a decay-mode intensity is written in NUBASE2020.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Qualifier {
    /// `=`, a measured intensity.
    Exact,
    /// `~`, approximately.
    Approximate,
    /// `>`, a lower limit.
    AtLeast,
    /// `<`, an upper limit.
    AtMost,
    /// `=?`, observed but the intensity is not known.
    Unquantified,
    /// `?`, energetically allowed but not observed.
    Possible,
}

/// A decay mode, named as in NUBASE2020.
///
/// `BetaPlus` means beta-plus decay including electron capture, the way
/// NUBASE2020 uses the symbol. `ElectronCapture` and `PositronEmission` appear
/// where only one of the two was measured. The modes from `BetaMinusNeutron`
/// down to `BetaPlusFission` are delayed emissions: a beta decay followed by
/// particle emission from the daughter.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum DecayMode {
    /// Beta-minus decay.
    BetaMinus,
    /// Beta-plus decay, electron capture included.
    BetaPlus,
    /// Electron capture alone.
    ElectronCapture,
    /// Positron emission alone.
    PositronEmission,
    /// Alpha emission.
    Alpha,
    /// Proton emission.
    Proton,
    /// Two-proton emission.
    TwoProtons,
    /// Three-proton emission.
    ThreeProtons,
    /// Neutron emission.
    Neutron,
    /// Two-neutron emission.
    TwoNeutrons,
    /// Three-neutron emission.
    ThreeNeutrons,
    /// Spontaneous fission.
    SpontaneousFission,
    /// Double beta-minus decay.
    DoubleBetaMinus,
    /// Double beta-plus decay.
    DoubleBetaPlus,
    /// Beta-minus decay followed by one neutron.
    BetaMinusNeutron,
    /// Beta-minus decay followed by two neutrons.
    BetaMinus2Neutrons,
    /// Beta-minus decay followed by three neutrons.
    BetaMinus3Neutrons,
    /// Beta-minus decay followed by four neutrons.
    BetaMinus4Neutrons,
    /// Beta-minus decay followed by an alpha particle.
    BetaMinusAlpha,
    /// Beta-minus decay followed by a deuteron.
    BetaMinusDeuteron,
    /// Beta-minus decay followed by a triton.
    BetaMinusTriton,
    /// Beta-minus decay followed by a proton.
    BetaMinusProton,
    /// Beta-minus decay followed by fission.
    BetaMinusFission,
    /// Beta-plus decay followed by one proton.
    BetaPlusProton,
    /// Beta-plus decay followed by two protons.
    BetaPlus2Protons,
    /// Beta-plus decay followed by three protons.
    BetaPlus3Protons,
    /// Beta-plus decay followed by an alpha particle.
    BetaPlusAlpha,
    /// Beta-plus decay followed by a proton and an alpha particle.
    BetaPlusProtonAlpha,
    /// Beta-plus decay followed by fission.
    BetaPlusFission,
    /// Emission of a heavy cluster such as carbon-14 or neon-24.
    Cluster(Nuclide),
    /// NUBASE2020 lists two cluster modes under one intensity, so there is no
    /// single daughter.
    ClusterMixture,
}

/// Which beta process a mode belongs to.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum ModeFamily {
    /// Beta-minus decay and its delayed emissions.
    BetaMinus,
    /// Beta-plus decay, electron capture, and their delayed emissions.
    BetaPlus,
    /// Everything else.
    Other,
}

/// What a decay mode does to the nucleus.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Transformation {
    /// Change in the proton count of the residual nucleus.
    pub delta_z: i32,
    /// Change in the neutron count of the residual nucleus.
    pub delta_n: i32,
    /// Emitted nuclei. A free proton is hydrogen-1.
    pub emitted: Vec<Nuclide>,
    /// By how much beta processes raise the atomic number: +1 per beta-minus,
    /// -1 per beta-plus or electron capture.
    pub beta_charge: i32,
}

impl DecayMode {
    /// True for decays that follow a beta decay with particle emission.
    pub fn is_delayed(self) -> bool {
        use DecayMode::*;
        matches!(
            self,
            BetaMinusNeutron
                | BetaMinus2Neutrons
                | BetaMinus3Neutrons
                | BetaMinus4Neutrons
                | BetaMinusAlpha
                | BetaMinusDeuteron
                | BetaMinusTriton
                | BetaMinusProton
                | BetaMinusFission
                | BetaPlusProton
                | BetaPlus2Protons
                | BetaPlus3Protons
                | BetaPlusAlpha
                | BetaPlusProtonAlpha
                | BetaPlusFission
        )
    }

    /// The beta process this mode belongs to.
    pub fn family(self) -> ModeFamily {
        use DecayMode::*;
        match self {
            BetaMinus | BetaMinusNeutron | BetaMinus2Neutrons | BetaMinus3Neutrons
            | BetaMinus4Neutrons | BetaMinusAlpha | BetaMinusDeuteron | BetaMinusTriton
            | BetaMinusProton | BetaMinusFission => ModeFamily::BetaMinus,
            BetaPlus | ElectronCapture | PositronEmission | BetaPlusProton | BetaPlus2Protons
            | BetaPlus3Protons | BetaPlusAlpha | BetaPlusProtonAlpha | BetaPlusFission => {
                ModeFamily::BetaPlus
            }
            _ => ModeFamily::Other,
        }
    }

    /// What the mode does to the nucleus, or `None` when there is no single
    /// daughter (fission modes and the cluster mixture).
    pub fn transformation(self) -> Option<Transformation> {
        use DecayMode::*;
        let n = Nuclide::NEUTRON;
        let p = Nuclide::H1;
        let make = |delta_z: i32, delta_n: i32, emitted: Vec<Nuclide>, beta_charge: i32| {
            Some(Transformation {
                delta_z,
                delta_n,
                emitted,
                beta_charge,
            })
        };
        match self {
            BetaMinus => make(1, -1, vec![], 1),
            BetaPlus | ElectronCapture | PositronEmission => make(-1, 1, vec![], -1),
            Alpha => make(-2, -2, vec![Nuclide::HE4], 0),
            Proton => make(-1, 0, vec![p], 0),
            TwoProtons => make(-2, 0, vec![p; 2], 0),
            ThreeProtons => make(-3, 0, vec![p; 3], 0),
            Neutron => make(0, -1, vec![n], 0),
            TwoNeutrons => make(0, -2, vec![n; 2], 0),
            ThreeNeutrons => make(0, -3, vec![n; 3], 0),
            DoubleBetaMinus => make(2, -2, vec![], 2),
            DoubleBetaPlus => make(-2, 2, vec![], -2),
            BetaMinusNeutron => make(1, -2, vec![n], 1),
            BetaMinus2Neutrons => make(1, -3, vec![n; 2], 1),
            BetaMinus3Neutrons => make(1, -4, vec![n; 3], 1),
            BetaMinus4Neutrons => make(1, -5, vec![n; 4], 1),
            BetaMinusAlpha => make(-1, -3, vec![Nuclide::HE4], 1),
            BetaMinusDeuteron => make(0, -2, vec![Nuclide::H2], 1),
            BetaMinusTriton => make(0, -3, vec![Nuclide::H3], 1),
            BetaMinusProton => make(0, -1, vec![p], 1),
            BetaPlusProton => make(-2, 1, vec![p], -1),
            BetaPlus2Protons => make(-3, 1, vec![p; 2], -1),
            BetaPlus3Protons => make(-4, 1, vec![p; 3], -1),
            BetaPlusAlpha => make(-3, -1, vec![Nuclide::HE4], -1),
            BetaPlusProtonAlpha => make(-4, -1, vec![p, Nuclide::HE4], -1),
            Cluster(cluster) => make(
                -i32::from(cluster.z()),
                -i32::from(cluster.n()),
                vec![cluster],
                0,
            ),
            SpontaneousFission | BetaMinusFission | BetaPlusFission | ClusterMixture => None,
        }
    }
}

impl fmt::Display for DecayMode {
    /// Writes the NUBASE2020 spelling: `B-`, `B+p`, `EC`, `14C`.
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        use DecayMode::*;
        let text = match self {
            BetaMinus => "B-",
            BetaPlus => "B+",
            ElectronCapture => "EC",
            PositronEmission => "e+",
            Alpha => "A",
            Proton => "p",
            TwoProtons => "2p",
            ThreeProtons => "3p",
            Neutron => "n",
            TwoNeutrons => "2n",
            ThreeNeutrons => "3n",
            SpontaneousFission => "SF",
            DoubleBetaMinus => "2B-",
            DoubleBetaPlus => "2B+",
            BetaMinusNeutron => "B-n",
            BetaMinus2Neutrons => "B-2n",
            BetaMinus3Neutrons => "B-3n",
            BetaMinus4Neutrons => "B-4n",
            BetaMinusAlpha => "B-A",
            BetaMinusDeuteron => "B-d",
            BetaMinusTriton => "B-t",
            BetaMinusProton => "B-p",
            BetaMinusFission => "B-SF",
            BetaPlusProton => "B+p",
            BetaPlus2Protons => "B+2p",
            BetaPlus3Protons => "B+3p",
            BetaPlusAlpha => "B+A",
            BetaPlusProtonAlpha => "B+pA",
            BetaPlusFission => "B+SF",
            ClusterMixture => "cluster mix",
            Cluster(cluster) => {
                let symbol = elements::symbol(cluster.z()).unwrap_or("?");
                return write!(f, "{}{}", cluster.a(), symbol);
            }
        };
        f.write_str(text)
    }
}

/// One raw decay-mode row from NUBASE2020.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Branch {
    /// The decay mode.
    pub mode: DecayMode,
    /// Intensity in percent of 100 decays of the parent. Zero for the
    /// qualifiers that carry no number.
    pub percent: f64,
    /// How the intensity is written.
    pub qualifier: Qualifier,
    /// True when NUBASE2020 marks the intensity as estimated.
    pub estimated: bool,
}

/// One possible result of a decay, with its share of all decays.
#[derive(Clone, Debug, PartialEq)]
pub struct Outcome {
    /// The decay mode.
    pub mode: DecayMode,
    /// Share of decays in percent. The shares of one nuclide sum to 100.
    pub percent: f64,
    /// True when NUBASE2020 gives no intensity and the share was assumed.
    pub assumed: bool,
    /// The residual nucleus. `None` for modes with no single daughter.
    pub daughter: Option<Nuclide>,
    /// Emitted nuclei, not counting electrons and neutrinos.
    pub emitted: Vec<Nuclide>,
}

fn find_row(nuclide: Nuclide) -> Option<&'static Row> {
    let z = u8::try_from(nuclide.z()).ok()?;
    let a = nuclide.a();
    let index = data::ROWS
        .binary_search_by(|row| (row.0, row.1).cmp(&(z, a)))
        .ok()?;
    Some(&data::ROWS[index])
}

fn row_branches(row: &Row) -> Vec<Branch> {
    let start = usize::from(row.6);
    let end = start + usize::from(row.7);
    data::BRANCHES[start..end]
        .iter()
        .map(
            |&(mode_index, percent, qualifier, estimated): &BranchRow| Branch {
                mode: data::MODES[usize::from(mode_index)],
                percent,
                qualifier,
                estimated,
            },
        )
        .collect()
}

/// Ground-state half-life of `nuclide`, or `None` when it is not in the table.
pub fn half_life(nuclide: Nuclide) -> Option<HalfLife> {
    let row = find_row(nuclide)?;
    Some(HalfLife {
        state: row.2,
        seconds: row.3,
        estimated: row.4,
    })
}

/// Natural isotopic abundance in percent, for nuclides that have one.
pub fn isotopic_abundance_percent(nuclide: Nuclide) -> Option<f64> {
    let abundance = find_row(nuclide)?.5;
    if abundance < 0.0 {
        None
    } else {
        Some(abundance)
    }
}

/// The raw decay-mode rows of `nuclide`. Empty when it is not in the table or
/// NUBASE2020 lists no modes.
pub fn branches(nuclide: Nuclide) -> Vec<Branch> {
    find_row(nuclide).map(row_branches).unwrap_or_default()
}

/// Iterates every nuclide that has decay data, in ascending (z, a) order.
pub fn nuclides() -> impl Iterator<Item = Nuclide> {
    data::ROWS
        .iter()
        .map(|row| Nuclide::new(u16::from(row.0), row.1 - u16::from(row.0)))
}

fn apply(parent: Nuclide, delta_z: i32, delta_n: i32) -> Option<Nuclide> {
    let z = i32::from(parent.z()) + delta_z;
    let n = i32::from(parent.n()) + delta_n;
    Some(Nuclide::new(u16::try_from(z).ok()?, u16::try_from(n).ok()?))
}

/// Turns raw rows into (mode, share in percent, assumed), shares summing to 100.
///
/// NUBASE2020 gives each intensity per 100 decays of the parent, and a delayed
/// emission such as `B-n` is a part of the `B-` intensity, not an addition to
/// it. So the plain beta share is the beta intensity minus its delayed
/// emissions. Electron capture and positron emission rows only refine a `B+`
/// row when one exists, so they are skipped then. Limits and unquantified rows
/// carry no usable number. When no row has one, the nuclide falls back to its
/// unquantified rows, then to its first possible primary mode, with an assumed
/// share.
fn resolve_shares(raw: &[Branch]) -> Vec<(DecayMode, f64, bool)> {
    let counted: Vec<&Branch> = raw
        .iter()
        .filter(|b| {
            matches!(
                b.qualifier,
                Qualifier::Exact | Qualifier::Approximate | Qualifier::AtLeast
            ) && b.percent > 0.0
        })
        .collect();
    let has_beta_plus = counted.iter().any(|b| b.mode == DecayMode::BetaPlus);
    let counted: Vec<&Branch> = counted
        .into_iter()
        .filter(|b| {
            !(has_beta_plus
                && matches!(
                    b.mode,
                    DecayMode::ElectronCapture | DecayMode::PositronEmission
                ))
        })
        .collect();

    if counted.is_empty() {
        let unquantified: Vec<DecayMode> = raw
            .iter()
            .filter(|b| b.qualifier == Qualifier::Unquantified && !b.mode.is_delayed())
            .map(|b| b.mode)
            .collect();
        if !unquantified.is_empty() {
            let share = 100.0 / unquantified.len() as f64;
            return unquantified.into_iter().map(|m| (m, share, true)).collect();
        }
        return raw
            .iter()
            .find(|b| b.qualifier == Qualifier::Possible && !b.mode.is_delayed())
            .map(|b| vec![(b.mode, 100.0, true)])
            .unwrap_or_default();
    }

    let delayed_sum = |family: ModeFamily| -> f64 {
        counted
            .iter()
            .filter(|b| b.mode.is_delayed() && b.mode.family() == family)
            .map(|b| b.percent)
            .sum()
    };
    let mut shares: Vec<(DecayMode, f64)> = counted.iter().map(|b| (b.mode, b.percent)).collect();
    for family in [ModeFamily::BetaMinus, ModeFamily::BetaPlus] {
        let delayed = delayed_sum(family);
        if delayed <= 0.0 {
            continue;
        }
        // Take the delayed share out of the largest primary row of the family.
        let target = shares
            .iter()
            .enumerate()
            .filter(|(_, (m, _))| !m.is_delayed() && m.family() == family)
            .max_by(|(_, a), (_, b)| a.1.total_cmp(&b.1))
            .map(|(index, _)| index);
        if let Some(index) = target {
            shares[index].1 = (shares[index].1 - delayed).max(0.0);
        }
    }
    let total: f64 = shares.iter().map(|(_, share)| share).sum();
    if total <= 0.0 {
        return Vec::new();
    }
    let mut resolved: Vec<(DecayMode, f64, bool)> = shares
        .into_iter()
        .filter(|(_, share)| *share > 0.0)
        .map(|(mode, share)| (mode, share * 100.0 / total, false))
        .collect();
    // sort_by is stable, so equal shares keep the order NUBASE2020 lists them in.
    resolved.sort_by(|a, b| b.1.total_cmp(&a.1));
    resolved
}

/// The decay outcomes of `nuclide`, most likely first, with shares summing to
/// 100. Empty for stable nuclides and for nuclides with no usable decay data.
pub fn outcomes(nuclide: Nuclide) -> Vec<Outcome> {
    let Some(row) = find_row(nuclide) else {
        return Vec::new();
    };
    if row.2 == HalfLifeState::Stable {
        return Vec::new();
    }
    resolve_shares(&row_branches(row))
        .into_iter()
        .map(|(mode, percent, assumed)| {
            let (daughter, emitted) = match mode.transformation() {
                Some(t) => (apply(nuclide, t.delta_z, t.delta_n), t.emitted),
                None => (None, Vec::new()),
            };
            Outcome {
                mode,
                percent,
                assumed,
                daughter,
                emitted,
            }
        })
        .collect()
}

/// The most likely decay outcome of `nuclide`, if it has any.
pub fn dominant_outcome(nuclide: Nuclide) -> Option<Outcome> {
    outcomes(nuclide).into_iter().next()
}

/// Energy released by `mode` acting on `parent`, in keV.
///
/// Q is the mass excess of the parent minus those of the daughter and the
/// emitted nuclei. For `BetaPlus`, `ElectronCapture` and the beta-plus delayed
/// modes this is the electron-capture energy, and a positron can only be
/// emitted when it exceeds twice the electron rest energy. `PositronEmission`
/// has that amount taken off. Returns `None` for modes with no single daughter
/// and when a participant has no mass data.
pub fn decay_q_value(parent: Nuclide, mode: DecayMode) -> Option<QValue> {
    let t = mode.transformation()?;
    let daughter = apply(parent, t.delta_z, t.delta_n)?;
    let parent_excess = binding::mass_excess(parent)?;
    let mut kev = parent_excess.kev;
    let mut source = parent_excess.source;
    for nuclide in iter::once(daughter).chain(t.emitted.iter().copied()) {
        let excess = binding::mass_excess(nuclide)?;
        kev -= excess.kev;
        source = source.max(excess.source);
    }
    if mode == DecayMode::PositronEmission {
        kev -= 2.0 * ELECTRON_MASS_KEV;
    }
    Some(QValue { kev, source })
}

/// One decay along a chain.
#[derive(Clone, Debug, PartialEq)]
pub struct ChainStep {
    /// The nuclide that decays.
    pub parent: Nuclide,
    /// The outcome the chain follows.
    pub outcome: Outcome,
}

/// Why a chain stopped.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ChainEnd {
    /// The last nuclide is stable.
    Stable,
    /// The last nuclide decays by a mode with no single daughter.
    NoDaughter(DecayMode),
    /// The last nuclide has no usable decay data.
    NoData,
    /// The last nuclide is not in the table.
    NotInTable,
    /// The last nuclide was already visited.
    Loop,
    /// The chain reached [`MAX_CHAIN_STEPS`].
    TooLong,
}

/// A decay chain, following the most likely outcome at every step.
///
/// Only the residual nucleus is followed. Emitted nuclei are listed in each
/// step and are not decayed further.
#[derive(Clone, Debug, PartialEq)]
pub struct Chain {
    /// Where the chain starts.
    pub start: Nuclide,
    /// The decays taken, in order.
    pub steps: Vec<ChainStep>,
    /// The last nuclide reached. For [`ChainEnd::NotInTable`] that is the
    /// missing daughter, and for [`ChainEnd::Loop`] the repeated nuclide.
    pub end: Nuclide,
    /// Why the chain stopped.
    pub reason: ChainEnd,
}

/// Follows the most likely decay outcome from `start` until a stable nuclide.
pub fn chain_to_stability(start: Nuclide) -> Chain {
    let mut steps: Vec<ChainStep> = Vec::new();
    let mut visited = vec![start];
    let mut current = start;
    loop {
        let stop = |steps: Vec<ChainStep>, end: Nuclide, reason: ChainEnd| Chain {
            start,
            steps,
            end,
            reason,
        };
        let Some(row) = find_row(current) else {
            return stop(steps, current, ChainEnd::NotInTable);
        };
        if row.2 == HalfLifeState::Stable {
            return stop(steps, current, ChainEnd::Stable);
        }
        let Some(outcome) = dominant_outcome(current) else {
            return stop(steps, current, ChainEnd::NoData);
        };
        let Some(daughter) = outcome.daughter else {
            let mode = outcome.mode;
            return stop(steps, current, ChainEnd::NoDaughter(mode));
        };
        steps.push(ChainStep {
            parent: current,
            outcome,
        });
        if visited.contains(&daughter) {
            return stop(steps, daughter, ChainEnd::Loop);
        }
        if steps.len() >= MAX_CHAIN_STEPS {
            return stop(steps, daughter, ChainEnd::TooLong);
        }
        visited.push(daughter);
        current = daughter;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::binding::Source;

    fn nuc(text: &str) -> Nuclide {
        Nuclide::parse(text).unwrap()
    }

    fn mode_percents(nuclide: Nuclide) -> Vec<(DecayMode, f64)> {
        outcomes(nuclide)
            .into_iter()
            .map(|o| (o.mode, o.percent))
            .collect()
    }

    #[test]
    fn every_mode_conserves_baryon_number_and_charge() {
        let parent = Nuclide::new(60, 80);
        let modes = [
            DecayMode::BetaMinus,
            DecayMode::BetaPlus,
            DecayMode::ElectronCapture,
            DecayMode::PositronEmission,
            DecayMode::Alpha,
            DecayMode::Proton,
            DecayMode::TwoProtons,
            DecayMode::ThreeProtons,
            DecayMode::Neutron,
            DecayMode::TwoNeutrons,
            DecayMode::ThreeNeutrons,
            DecayMode::DoubleBetaMinus,
            DecayMode::DoubleBetaPlus,
            DecayMode::BetaMinusNeutron,
            DecayMode::BetaMinus2Neutrons,
            DecayMode::BetaMinus3Neutrons,
            DecayMode::BetaMinus4Neutrons,
            DecayMode::BetaMinusAlpha,
            DecayMode::BetaMinusDeuteron,
            DecayMode::BetaMinusTriton,
            DecayMode::BetaMinusProton,
            DecayMode::BetaPlusProton,
            DecayMode::BetaPlus2Protons,
            DecayMode::BetaPlus3Protons,
            DecayMode::BetaPlusAlpha,
            DecayMode::BetaPlusProtonAlpha,
            DecayMode::Cluster(Nuclide::new(6, 8)),
        ];
        for mode in modes {
            let t = mode.transformation().unwrap();
            let daughter = apply(parent, t.delta_z, t.delta_n).unwrap();
            let emitted_a: u32 = t.emitted.iter().map(|e| u32::from(e.a())).sum();
            let emitted_z: i32 = t.emitted.iter().map(|e| i32::from(e.z())).sum();
            assert_eq!(
                u32::from(daughter.a()) + emitted_a,
                u32::from(parent.a()),
                "{mode}: mass number"
            );
            assert_eq!(
                i32::from(daughter.z()) + emitted_z,
                i32::from(parent.z()) + t.beta_charge,
                "{mode}: charge"
            );
        }
    }

    #[test]
    fn modes_without_a_daughter_say_so() {
        for mode in [
            DecayMode::SpontaneousFission,
            DecayMode::BetaMinusFission,
            DecayMode::BetaPlusFission,
            DecayMode::ClusterMixture,
        ] {
            assert_eq!(mode.transformation(), None, "{mode}");
        }
    }

    #[test]
    fn labels_follow_nubase() {
        assert_eq!(DecayMode::BetaMinus.to_string(), "B-");
        assert_eq!(DecayMode::BetaPlusProtonAlpha.to_string(), "B+pA");
        assert_eq!(DecayMode::Cluster(Nuclide::new(6, 8)).to_string(), "14C");
        assert_eq!(DecayMode::Cluster(Nuclide::new(10, 14)).to_string(), "24Ne");
    }

    #[test]
    fn families_group_delayed_modes_with_their_beta_decay() {
        assert_eq!(DecayMode::BetaMinusNeutron.family(), ModeFamily::BetaMinus);
        assert_eq!(DecayMode::ElectronCapture.family(), ModeFamily::BetaPlus);
        assert_eq!(DecayMode::BetaPlusAlpha.family(), ModeFamily::BetaPlus);
        assert_eq!(DecayMode::Alpha.family(), ModeFamily::Other);
        assert!(DecayMode::BetaMinusNeutron.is_delayed());
        assert!(!DecayMode::BetaMinus.is_delayed());
    }

    #[test]
    fn carbon_14_decays_to_nitrogen_14() {
        let o = outcomes(nuc("C-14"));
        assert_eq!(o.len(), 1);
        assert_eq!(o[0].mode, DecayMode::BetaMinus);
        assert_eq!(o[0].percent, 100.0);
        assert_eq!(o[0].daughter, Some(nuc("N-14")));
        assert!(!o[0].assumed);
    }

    #[test]
    fn delayed_emissions_come_out_of_the_beta_share() {
        // He-8: B-=100, B-n=16, B-t=0.9, so plain beta decay is 83.1 percent.
        let shares = mode_percents(nuc("He-8"));
        let get = |mode| shares.iter().find(|(m, _)| *m == mode).unwrap().1;
        assert!((get(DecayMode::BetaMinus) - 83.1).abs() < 1e-9);
        assert!((get(DecayMode::BetaMinusNeutron) - 16.0).abs() < 1e-9);
        assert!((get(DecayMode::BetaMinusTriton) - 0.9).abs() < 1e-9);
    }

    #[test]
    fn li_8_always_decays_through_the_alpha_channel() {
        // B-=100 and B-A=100: no plain beta share is left.
        assert_eq!(
            mode_percents(nuc("Li-8")),
            vec![(DecayMode::BetaMinusAlpha, 100.0)]
        );
    }

    #[test]
    fn potassium_40_branches_to_calcium_and_argon() {
        let shares = mode_percents(nuc("K-40"));
        assert_eq!(shares.len(), 2);
        assert_eq!(shares[0].0, DecayMode::BetaMinus);
        assert!((shares[0].1 - 89.28).abs() < 1e-9);
        assert_eq!(shares[1].0, DecayMode::BetaPlus);
        assert!((shares[1].1 - 10.72).abs() < 1e-9);
    }

    #[test]
    fn electron_capture_rows_refine_beta_plus_instead_of_adding_to_it() {
        // Na-22 lists B+=100, e+=90.57 and EC=9.43 as parts of the one decay.
        let o = outcomes(nuc("Na-22"));
        assert_eq!(o.len(), 1);
        assert_eq!(o[0].mode, DecayMode::BetaPlus);
        assert_eq!(o[0].daughter, Some(nuc("Ne-22")));
    }

    #[test]
    fn stable_nuclides_have_no_outcomes() {
        assert!(outcomes(nuc("Fe-56")).is_empty());
        assert!(half_life(nuc("Fe-56")).unwrap().is_stable());
        assert!(dominant_outcome(nuc("Fe-56")).is_none());
    }

    #[test]
    fn unobserved_modes_are_assumed_when_nothing_else_is_known() {
        // Li-3 lists only "p ?": proton emission is allowed but not observed.
        let o = outcomes(nuc("Li-3"));
        assert_eq!(o.len(), 1);
        assert_eq!(o[0].mode, DecayMode::Proton);
        assert!(o[0].assumed);
    }

    #[test]
    fn half_life_in_seconds_uses_the_tropical_year() {
        let c14 = half_life(nuc("C-14")).unwrap();
        assert_eq!(c14.state, HalfLifeState::Exact);
        assert_eq!(c14.finite_seconds(), Some(5700.0 * 31_556_926.0));
        assert_eq!(
            half_life(Nuclide::NEUTRON).unwrap().finite_seconds(),
            Some(609.8)
        );
        assert_eq!(half_life(Nuclide::new(50, 150)), None);
    }

    #[test]
    fn isotopic_abundance_is_listed_for_stable_isotopes() {
        assert_eq!(isotopic_abundance_percent(nuc("H-2")), Some(0.0145));
        assert_eq!(isotopic_abundance_percent(nuc("C-14")), None);
        assert_eq!(isotopic_abundance_percent(nuc("K-40")), Some(0.0117));
    }

    #[test]
    fn q_value_for_carbon_14_beta_decay() {
        let q = decay_q_value(nuc("C-14"), DecayMode::BetaMinus).unwrap();
        assert!((q.kev - 156.475).abs() < 0.01, "{}", q.kev);
        assert_eq!(q.source, Source::Measured);
        assert_eq!(
            decay_q_value(nuc("U-238"), DecayMode::SpontaneousFission),
            None
        );
    }

    #[test]
    fn chain_from_a_stable_nuclide_is_empty() {
        let chain = chain_to_stability(nuc("Fe-56"));
        assert!(chain.steps.is_empty());
        assert_eq!((chain.end, chain.reason), (nuc("Fe-56"), ChainEnd::Stable));
    }

    #[test]
    fn chain_from_an_unlisted_nuclide_reports_it() {
        let chain = chain_to_stability(Nuclide::new(50, 150));
        assert_eq!(chain.reason, ChainEnd::NotInTable);
        assert_eq!(chain.end, Nuclide::new(50, 150));
    }
}
