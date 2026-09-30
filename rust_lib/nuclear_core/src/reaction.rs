// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "reaction.rs"
// ============================================================================
//! Q-values for reactions written as lists of nuclides.
//!
//! Masses here are atomic masses, so a reaction must conserve the proton count:
//! the electrons cancel on both sides. Beta decay and electron capture change
//! the proton count and need lepton bookkeeping, which lives in a later module.

use core::fmt;

use crate::binding::{self, Source};
use crate::nuclide::Nuclide;

/// Energy released by a reaction.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct QValue {
    /// Q in keV. Positive means the reaction releases energy.
    pub kev: f64,
    /// The least trustworthy source among all participants.
    pub source: Source,
}

impl QValue {
    /// Q in MeV.
    pub fn mev(self) -> f64 {
        self.kev / 1000.0
    }
}

/// Why a Q-value could not be computed.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ReactionError {
    /// One side of the reaction has no participants.
    Empty,
    /// Total mass number differs between the two sides.
    BaryonNumberMismatch {
        /// Total mass number of the reactants.
        reactants: u32,
        /// Total mass number of the products.
        products: u32,
    },
    /// Total proton count differs between the two sides.
    ChargeMismatch {
        /// Total proton count of the reactants.
        reactants: u32,
        /// Total proton count of the products.
        products: u32,
    },
    /// A participant has neither a table row nor a fallback value.
    NoMassData(Nuclide),
}

impl fmt::Display for ReactionError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ReactionError::Empty => {
                write!(f, "a reaction needs at least one reactant and one product")
            }
            ReactionError::BaryonNumberMismatch {
                reactants,
                products,
            } => write!(
                f,
                "mass number not conserved: {reactants} in, {products} out"
            ),
            ReactionError::ChargeMismatch {
                reactants,
                products,
            } => write!(
                f,
                "proton count not conserved: {reactants} in, {products} out"
            ),
            ReactionError::NoMassData(nuclide) => write!(f, "no mass data for {nuclide}"),
        }
    }
}

impl std::error::Error for ReactionError {}

fn totals(side: &[Nuclide]) -> (u32, u32) {
    side.iter().fold((0, 0), |(a, z), nuclide| {
        (a + u32::from(nuclide.a()), z + u32::from(nuclide.z()))
    })
}

/// Sums mass excess over one side, tracking the weakest source seen.
fn side_mass_excess(side: &[Nuclide]) -> Result<(f64, Source), ReactionError> {
    let mut sum = 0.0;
    let mut weakest = Source::Measured;
    for &nuclide in side {
        let excess = binding::mass_excess(nuclide).ok_or(ReactionError::NoMassData(nuclide))?;
        sum += excess.kev;
        weakest = weakest.max(excess.source);
    }
    Ok((sum, weakest))
}

/// Q-value of `reactants -> products`: mass excess in minus mass excess out.
///
/// Both sides must conserve mass number and proton count. Free neutrons are
/// [`Nuclide::NEUTRON`]; a free proton is [`Nuclide::H1`].
pub fn q_value(reactants: &[Nuclide], products: &[Nuclide]) -> Result<QValue, ReactionError> {
    if reactants.is_empty() || products.is_empty() {
        return Err(ReactionError::Empty);
    }
    let (a_in, z_in) = totals(reactants);
    let (a_out, z_out) = totals(products);
    if a_in != a_out {
        return Err(ReactionError::BaryonNumberMismatch {
            reactants: a_in,
            products: a_out,
        });
    }
    if z_in != z_out {
        return Err(ReactionError::ChargeMismatch {
            reactants: z_in,
            products: z_out,
        });
    }
    let (excess_in, source_in) = side_mass_excess(reactants)?;
    let (excess_out, source_out) = side_mass_excess(products)?;
    Ok(QValue {
        kev: excess_in - excess_out,
        source: source_in.max(source_out),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn deuterium_tritium_fusion_releases_about_17_6_mev() {
        let q = q_value(
            &[Nuclide::H2, Nuclide::H3],
            &[Nuclide::HE4, Nuclide::NEUTRON],
        )
        .unwrap();
        assert!((q.mev() - 17.589).abs() < 0.01);
        assert_eq!(q.source, Source::Measured);
    }

    #[test]
    fn mismatched_reactions_are_rejected() {
        assert_eq!(
            q_value(&[Nuclide::H2, Nuclide::H3], &[Nuclide::HE4]),
            Err(ReactionError::BaryonNumberMismatch {
                reactants: 5,
                products: 4
            })
        );
        assert_eq!(
            q_value(&[Nuclide::H1, Nuclide::H1], &[Nuclide::H2]),
            Err(ReactionError::ChargeMismatch {
                reactants: 2,
                products: 1
            })
        );
        assert_eq!(q_value(&[], &[Nuclide::HE4]), Err(ReactionError::Empty));
    }

    #[test]
    fn error_text_names_the_problem() {
        let text = ReactionError::NoMassData(Nuclide::new(0, 3)).to_string();
        assert_eq!(text, "no mass data for 3n");
    }
}
