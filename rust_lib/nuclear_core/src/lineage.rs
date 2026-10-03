// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "lineage.rs"
// ============================================================================
//! Exact decay propagation for one atom over any time interval.
//!
//! An atom of nuclide X decays along a branching tree. Starting from X, this
//! module builds the graph of everything the atom can turn into, with the decay
//! rate of each branch, and solves the resulting linear system with a matrix
//! exponential. The answer is the exact probability that the atom is in each
//! possible state after `seconds`, however many generations that takes and
//! however different the half-lives are.
//!
//! A state is the residual nucleus together with the nuclei emitted so far, so
//! two different routes to the same residual stay distinct when they threw off
//! different particles. Atoms are independent, so `n` atoms spread over the
//! states as a multinomial draw (see [`crate::sample`]).

use core::f64::consts::LN_2;
use core::fmt;
use std::collections::HashMap;
use std::sync::Arc;

use crate::binding;
use crate::decay::{self, HalfLifeState};
use crate::matrix;
use crate::nuclide::Nuclide;

/// Most states one lineage graph may have. Real graphs are far smaller.
pub const MAX_STATES: usize = 4096;

/// Half-life assumed for nuclides that decay by particle emission and have no
/// half-life listed, and for nuclides whose half-life is unknown. It stands for
/// a nuclear timescale: they are gone before anything else happens.
pub const PROMPT_HALF_LIFE_SECONDS: f64 = 1e-21;

/// Outcomes with a smaller probability than this are dropped and the rest are
/// rescaled to sum to 1.
const MIN_PROBABILITY: f64 = 1e-16;

/// A state an atom can be in: what is left of it, and what it has shed.
#[derive(Clone, Debug, PartialEq, Eq, Hash)]
pub struct LineageState {
    /// The residual nucleus. When `pending_fission` is set this is the nucleus
    /// that fissioned.
    pub residual: Nuclide,
    /// Nuclei emitted on the way, as (nuclide, how many), sorted by nuclide.
    pub emitted: Vec<(Nuclide, u32)>,
    /// True when the atom ended in a mode with no single daughter (fission) and
    /// awaits fission products.
    pub pending_fission: bool,
}

/// One possible state after the interval, with its probability.
#[derive(Clone, Debug, PartialEq)]
pub struct TransitionOutcome {
    /// The state.
    pub state: LineageState,
    /// Probability of ending in this state. The probabilities of a transition
    /// sum to 1.
    pub probability: f64,
    /// Energy released since the start, in keV: the mass excess of the starting
    /// nuclide minus those of the residual and the emitted nuclei. Neutrinos
    /// and the fission energy itself are included or excluded as the mass
    /// difference dictates: this is the total Q, not the heat.
    pub energy_kev: f64,
}

/// Where one atom of `start` can be after `seconds`.
#[derive(Clone, Debug, PartialEq)]
pub struct Transition {
    /// The starting nuclide.
    pub start: Nuclide,
    /// The interval length in seconds.
    pub seconds: f64,
    /// Every state with a probability of at least 1e-16.
    pub outcomes: Vec<TransitionOutcome>,
    /// False when a mass was missing for some state, so its `energy_kev` is 0.
    pub energy_known: bool,
}

/// Why a transition could not be computed.
#[derive(Clone, Debug, PartialEq)]
pub enum LineageError {
    /// The starting nuclide is not in the table.
    NotInTable(Nuclide),
    /// The interval is negative or not finite.
    InvalidTime(f64),
    /// The lineage graph would exceed [`MAX_STATES`].
    TooManyStates(Nuclide),
}

impl fmt::Display for LineageError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            LineageError::NotInTable(n) => write!(f, "{n} is not in the nuclide table"),
            LineageError::InvalidTime(t) => {
                write!(
                    f,
                    "interval {t} is not a finite, non-negative number of seconds"
                )
            }
            LineageError::TooManyStates(n) => {
                write!(
                    f,
                    "the decay graph of {n} has more than {MAX_STATES} states"
                )
            }
        }
    }
}

impl std::error::Error for LineageError {}

struct Graph {
    states: Vec<LineageState>,
    /// For each state, the (target state, rate per second) of every branch.
    edges: Vec<Vec<(usize, f64)>>,
}

/// Decay constant in 1/s, or `None` for stable nuclides and nuclides with no
/// data.
fn decay_rate(nuclide: Nuclide) -> Option<f64> {
    let half_life = decay::half_life(nuclide)?;
    let seconds = match half_life.state {
        HalfLifeState::Stable => return None,
        HalfLifeState::Exact
        | HalfLifeState::Approximate
        | HalfLifeState::AtLeast
        | HalfLifeState::AtMost => {
            if half_life.seconds > 0.0 {
                half_life.seconds
            } else {
                PROMPT_HALF_LIFE_SECONDS
            }
        }
        HalfLifeState::ParticleUnstable | HalfLifeState::Unknown => PROMPT_HALF_LIFE_SECONDS,
    };
    Some(LN_2 / seconds)
}

fn merged(emitted: &[(Nuclide, u32)], more: &[Nuclide]) -> Vec<(Nuclide, u32)> {
    let mut out = emitted.to_vec();
    for &nuclide in more {
        match out.iter_mut().find(|(n, _)| *n == nuclide) {
            Some((_, count)) => *count += 1,
            None => out.push((nuclide, 1)),
        }
    }
    out.sort();
    out
}

fn build_graph(start: Nuclide) -> Result<Graph, LineageError> {
    if decay::half_life(start).is_none() {
        return Err(LineageError::NotInTable(start));
    }
    let first = LineageState {
        residual: start,
        emitted: Vec::new(),
        pending_fission: false,
    };
    let mut index: HashMap<LineageState, usize> = HashMap::new();
    index.insert(first.clone(), 0);
    let mut states = vec![first];
    let mut edges: Vec<Vec<(usize, f64)>> = vec![Vec::new()];
    let mut next = 0;
    while next < states.len() {
        let state = states[next].clone();
        next += 1;
        if state.pending_fission {
            continue;
        }
        let Some(rate) = decay_rate(state.residual) else {
            continue;
        };
        for outcome in decay::outcomes(state.residual) {
            let target = match outcome.daughter {
                Some(daughter) => LineageState {
                    residual: daughter,
                    emitted: merged(&state.emitted, &outcome.emitted),
                    pending_fission: false,
                },
                None => LineageState {
                    residual: state.residual,
                    emitted: state.emitted.clone(),
                    pending_fission: true,
                },
            };
            let id = match index.get(&target) {
                Some(&id) => id,
                None => {
                    if states.len() >= MAX_STATES {
                        return Err(LineageError::TooManyStates(start));
                    }
                    let id = states.len();
                    index.insert(target.clone(), id);
                    states.push(target);
                    edges.push(Vec::new());
                    id
                }
            };
            edges[next - 1].push((id, rate * outcome.percent / 100.0));
        }
    }
    Ok(Graph { states, edges })
}

fn released_kev(start: Nuclide, state: &LineageState) -> Option<f64> {
    let mut kev = binding::mass_excess(start)?.kev - binding::mass_excess(state.residual)?.kev;
    for &(nuclide, count) in &state.emitted {
        kev -= f64::from(count) * binding::mass_excess(nuclide)?.kev;
    }
    Some(kev)
}

fn compute(graph: &Graph, start: Nuclide, seconds: f64) -> Transition {
    let m = graph.states.len();
    let weights: Vec<f64> = if m == 1 || seconds == 0.0 {
        let mut identity_row = vec![0.0; m];
        identity_row[0] = 1.0;
        identity_row
    } else {
        let mut q = vec![0.0; m * m];
        for (i, edges) in graph.edges.iter().enumerate() {
            for &(j, rate) in edges {
                q[i * m + j] += rate * seconds;
                q[i * m + i] -= rate * seconds;
            }
        }
        // Row 0 of exp(Q) = row 0 of (exp(Q) - I) plus the unit vector.
        let e = matrix::expm_minus_identity(m, &q);
        (0..m)
            .map(|j| {
                let x = e[j] + if j == 0 { 1.0 } else { 0.0 };
                if x.is_finite() {
                    x.max(0.0)
                } else {
                    0.0
                }
            })
            .collect()
    };
    let kept: Vec<(usize, f64)> = weights
        .iter()
        .copied()
        .enumerate()
        .filter(|&(_, p)| p >= MIN_PROBABILITY)
        .collect();
    let total: f64 = kept.iter().map(|&(_, p)| p).sum();
    let mut energy_known = true;
    let outcomes = kept
        .into_iter()
        .map(|(j, p)| {
            let state = graph.states[j].clone();
            let energy_kev = released_kev(start, &state).unwrap_or_else(|| {
                energy_known = false;
                0.0
            });
            TransitionOutcome {
                state,
                probability: p / total,
                energy_kev,
            }
        })
        .collect();
    Transition {
        start,
        seconds,
        outcomes,
        energy_known,
    }
}

/// Computes and caches [`Transition`]s. There is no global cache: create one
/// and pass it to every simulation step.
pub struct Propagator {
    graphs: HashMap<u32, Arc<Graph>>,
    transitions: HashMap<(u32, u64), Arc<Transition>>,
    cache_limit: usize,
}

impl Default for Propagator {
    fn default() -> Self {
        Propagator::new()
    }
}

impl Propagator {
    /// A propagator that keeps up to 4096 transitions.
    pub fn new() -> Propagator {
        Propagator::with_cache_limit(4096)
    }

    /// A propagator that keeps up to `limit` transitions. When the limit is
    /// reached the whole cache is dropped before the next insert.
    pub fn with_cache_limit(limit: usize) -> Propagator {
        Propagator {
            graphs: HashMap::new(),
            transitions: HashMap::new(),
            cache_limit: limit.max(1),
        }
    }

    /// Number of transitions currently cached.
    pub fn cached_transitions(&self) -> usize {
        self.transitions.len()
    }

    /// Drops every cached graph and transition.
    pub fn clear(&mut self) {
        self.graphs.clear();
        self.transitions.clear();
    }

    fn graph(&mut self, start: Nuclide) -> Result<Arc<Graph>, LineageError> {
        if let Some(graph) = self.graphs.get(&start.key()) {
            return Ok(Arc::clone(graph));
        }
        let graph = Arc::new(build_graph(start)?);
        self.graphs.insert(start.key(), Arc::clone(&graph));
        Ok(graph)
    }

    /// Number of states in the lineage graph of `start`.
    pub fn state_count(&mut self, start: Nuclide) -> Result<usize, LineageError> {
        Ok(self.graph(start)?.states.len())
    }

    /// Where one atom of `start` can be after `seconds`.
    ///
    /// The result is cached, so asking again for the same nuclide and interval
    /// is a lookup. `seconds` must be finite and not negative.
    pub fn transition(
        &mut self,
        start: Nuclide,
        seconds: f64,
    ) -> Result<Arc<Transition>, LineageError> {
        if !seconds.is_finite() || seconds < 0.0 {
            return Err(LineageError::InvalidTime(seconds));
        }
        let seconds = if seconds == 0.0 { 0.0 } else { seconds };
        let key = (start.key(), seconds.to_bits());
        if let Some(found) = self.transitions.get(&key) {
            return Ok(Arc::clone(found));
        }
        let graph = self.graph(start)?;
        let transition = Arc::new(compute(&graph, start, seconds));
        if self.transitions.len() >= self.cache_limit {
            self.transitions.clear();
        }
        self.transitions.insert(key, Arc::clone(&transition));
        Ok(transition)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn nuc(text: &str) -> Nuclide {
        Nuclide::parse(text).unwrap()
    }

    fn probability_of(t: &Transition, residual: Nuclide) -> f64 {
        t.outcomes
            .iter()
            .filter(|o| o.state.residual == residual && !o.state.pending_fission)
            .map(|o| o.probability)
            .sum()
    }

    fn lambda(nuclide: Nuclide) -> f64 {
        LN_2 / decay::half_life(nuclide).unwrap().seconds
    }

    #[test]
    fn a_stable_nuclide_stays_put() {
        let mut p = Propagator::new();
        let t = p.transition(nuc("Fe-56"), 1e18).unwrap();
        assert_eq!(t.outcomes.len(), 1);
        assert_eq!(t.outcomes[0].probability, 1.0);
        assert_eq!(t.outcomes[0].energy_kev, 0.0);
    }

    #[test]
    fn zero_time_changes_nothing() {
        let mut p = Propagator::new();
        let t = p.transition(nuc("U-238"), 0.0).unwrap();
        assert_eq!(t.outcomes.len(), 1);
        assert_eq!(t.outcomes[0].state.residual, nuc("U-238"));
    }

    #[test]
    fn one_half_life_leaves_half_of_carbon_14() {
        let mut p = Propagator::new();
        let half_life = decay::half_life(nuc("C-14")).unwrap().seconds;
        let t = p.transition(nuc("C-14"), half_life).unwrap();
        assert!((probability_of(&t, nuc("C-14")) - 0.5).abs() < 1e-12);
        assert!((probability_of(&t, nuc("N-14")) - 0.5).abs() < 1e-12);
    }

    #[test]
    fn strontium_90_chain_matches_the_bateman_solution() {
        // Sr-90 -> Y-90 -> Zr-90, each a pure beta-minus decay.
        let (sr, y, zr) = (nuc("Sr-90"), nuc("Y-90"), nuc("Zr-90"));
        let (l1, l2) = (lambda(sr), lambda(y));
        let mut p = Propagator::new();
        for years in [0.01, 1.0, 10.0, 100.0] {
            let t = years * 31_556_926.0;
            let sr_left = (-l1 * t).exp();
            let y_left = l1 / (l2 - l1) * ((-l1 * t).exp() - (-l2 * t).exp());
            let tr = p.transition(sr, t).unwrap();
            assert!(
                (probability_of(&tr, sr) - sr_left).abs() < 1e-12,
                "Sr at {years} y"
            );
            assert!(
                (probability_of(&tr, y) - y_left).abs() < 1e-12,
                "Y at {years} y"
            );
            assert!(
                (probability_of(&tr, zr) - (1.0 - sr_left - y_left)).abs() < 1e-12,
                "Zr at {years} y"
            );
        }
    }

    #[test]
    fn uranium_238_ends_as_lead_206_with_eight_alphas() {
        let mut p = Propagator::new();
        let t = p.transition(nuc("U-238"), 1e30).unwrap();
        // Rare cluster-emission branches also end in Pb-206 with a different
        // set of emitted nuclei, so take the most probable state.
        let lead = t
            .outcomes
            .iter()
            .filter(|o| o.state.residual == nuc("Pb-206") && !o.state.pending_fission)
            .max_by(|a, b| a.probability.total_cmp(&b.probability))
            .unwrap();
        assert_eq!(lead.state.emitted, vec![(Nuclide::HE4, 8)]);
        assert!(lead.probability > 0.999_99);
        // 51.7 MeV is the textbook total for the uranium series.
        assert!(
            (lead.energy_kev - 51_695.0).abs() < 5.0,
            "{}",
            lead.energy_kev
        );
        assert!(t.energy_known);
    }

    #[test]
    fn every_outcome_conserves_mass_number() {
        let mut p = Propagator::new();
        for name in ["U-238", "Kr-92", "Li-8", "Cf-252", "Na-22", "K-40"] {
            let start = nuc(name);
            let t = p.transition(start, 1e9).unwrap();
            let total: f64 = t.outcomes.iter().map(|o| o.probability).sum();
            assert!((total - 1.0).abs() < 1e-12, "{name}: {total}");
            for o in &t.outcomes {
                let emitted: u32 = o
                    .state
                    .emitted
                    .iter()
                    .map(|(n, c)| u32::from(n.a()) * c)
                    .sum();
                assert_eq!(
                    u32::from(o.state.residual.a()) + emitted,
                    u32::from(start.a()),
                    "{name}"
                );
            }
        }
    }

    #[test]
    fn fission_branches_become_pending_states() {
        let mut p = Propagator::new();
        let t = p.transition(nuc("Cf-252"), 1e30).unwrap();
        assert!(t.outcomes.iter().any(|o| o.state.pending_fission));
    }

    #[test]
    fn transitions_are_cached_and_inputs_validated() {
        let mut p = Propagator::new();
        let a = p.transition(nuc("C-14"), 1e10).unwrap();
        let b = p.transition(nuc("C-14"), 1e10).unwrap();
        assert!(Arc::ptr_eq(&a, &b));
        assert_eq!(p.cached_transitions(), 1);
        assert_eq!(
            p.transition(nuc("C-14"), -1.0),
            Err(LineageError::InvalidTime(-1.0))
        );
        assert!(p.transition(nuc("C-14"), f64::NAN).is_err());
        assert!(p.transition(nuc("C-14"), f64::INFINITY).is_err());
        assert_eq!(
            p.transition(Nuclide::new(50, 150), 1.0),
            Err(LineageError::NotInTable(Nuclide::new(50, 150)))
        );
        p.clear();
        assert_eq!(p.cached_transitions(), 0);
    }

    #[test]
    fn the_cache_drops_everything_at_its_limit() {
        let mut p = Propagator::with_cache_limit(2);
        p.transition(nuc("C-14"), 1.0).unwrap();
        p.transition(nuc("C-14"), 2.0).unwrap();
        assert_eq!(p.cached_transitions(), 2);
        p.transition(nuc("C-14"), 3.0).unwrap();
        assert_eq!(p.cached_transitions(), 1);
    }
}
