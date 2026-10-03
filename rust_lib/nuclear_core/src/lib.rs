// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "lib.rs"
// ============================================================================
//! nuclear_core: event-level nuclear physics for MidManStudio games.
//!
//! This crate works with whole nuclei, not particles. A [`Nuclide`] is a pair of
//! counts (protons, neutrons). Energies come from the AME2020 atomic mass
//! table, with a liquid drop formula as a fallback for nuclides the table does
//! not list. Every number reports which of the two produced it.
//!
//! Units: energies are in keV unless a name says otherwise.
//!
//! What exists today:
//!
//! * [`Nuclide`] and element symbols.
//! * [`mass_excess`] and [`binding_energy`] for any nuclide.
//! * [`q_value`] for reactions written as lists of nuclides.
//! * [`half_life`](decay::half_life), [`outcomes`] and [`chain_to_stability`] for
//!   decay, from NUBASE2020.
//! * [`Propagator`], [`Sample`] and [`Amounts`] for atoms that decay over time,
//!   with a seeded [`Rng`].
//!
//! ```
//! use nuclear_core::{q_value, Nuclide};
//!
//! // Deuterium + tritium -> helium-4 + neutron.
//! let q = q_value(&[Nuclide::H2, Nuclide::H3], &[Nuclide::HE4, Nuclide::NEUTRON]).unwrap();
//! assert!((q.mev() - 17.589).abs() < 0.01);
//!
//! // Carbon-14 decays to stable nitrogen-14.
//! let chain = nuclear_core::chain_to_stability(Nuclide::new(6, 8));
//! assert_eq!(chain.end, Nuclide::new(7, 7));
//!
//! // A thousand carbon-14 atoms after 5700 years: some are now nitrogen-14.
//! let mut pile = nuclear_core::Sample::new();
//! pile.add(Nuclide::new(6, 8), 1_000).unwrap();
//! let mut rng = nuclear_core::Rng::new(1);
//! let mut propagator = nuclear_core::Propagator::new();
//! pile.advance(5_700.0 * 31_556_926.0, &mut rng, &mut propagator).unwrap();
//! assert_eq!(pile.count(Nuclide::new(6, 8)) + pile.count(Nuclide::new(7, 7)), 1_000);
//! assert!(pile.count(Nuclide::new(7, 7)) > 0);
//! ```

mod ame2020_data;
pub mod binding;
pub mod decay;
pub mod elements;
pub mod lineage;
pub mod liquid_drop;
pub mod mass_table;
pub mod matrix;
mod nubase2020_data;
pub mod nuclide;
pub mod reaction;
pub mod rng;
pub mod sample;

pub use binding::{binding_energy, mass_excess, Binding, MassExcess, Source};
pub use decay::{
    chain_to_stability, decay_q_value, outcomes, Chain, ChainEnd, DecayMode, HalfLife,
    HalfLifeState, Outcome,
};
pub use lineage::{Propagator, Transition};
pub use nuclide::Nuclide;
pub use reaction::{q_value, QValue, ReactionError};
pub use rng::Rng;
pub use sample::{Amounts, Sample, StepReport};
