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
//! ```

mod ame2020_data;
pub mod binding;
pub mod decay;
pub mod elements;
pub mod liquid_drop;
pub mod mass_table;
mod nubase2020_data;
pub mod nuclide;
pub mod reaction;

pub use binding::{binding_energy, mass_excess, Binding, MassExcess, Source};
pub use decay::{
    chain_to_stability, decay_q_value, outcomes, Chain, ChainEnd, DecayMode, HalfLife,
    HalfLifeState, Outcome,
};
pub use nuclide::Nuclide;
pub use reaction::{q_value, QValue, ReactionError};
