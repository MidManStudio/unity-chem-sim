// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "binding.rs"
// ============================================================================
//! Mass excess and binding energy for any nuclide, with the data source
//! reported alongside every number.

use crate::liquid_drop;
use crate::mass_table::{self, H1_MASS_EXCESS_KEV, NEUTRON_MASS_EXCESS_KEV};
use crate::nuclide::Nuclide;

/// Where a number came from. Ordered from most to least trustworthy, so the
/// larger of two sources is the weaker one.
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum Source {
    /// AME2020 lists the value as measured.
    Measured,
    /// AME2020 lists the value, marked as taken from systematic trends.
    Estimated,
    /// Not in the table. Computed with the liquid drop fallback.
    LiquidDrop,
}

fn table_source(estimated: bool) -> Source {
    if estimated {
        Source::Estimated
    } else {
        Source::Measured
    }
}

/// Atomic mass excess of a nuclide.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct MassExcess {
    /// Mass excess in keV.
    pub kev: f64,
    /// Where the value came from.
    pub source: Source,
}

/// Total binding energy of a nuclide.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Binding {
    /// Total binding energy in keV. Negative means unbound.
    pub total_kev: f64,
    /// Where the value came from.
    pub source: Source,
}

impl Binding {
    /// Binding energy per nucleon in keV, for the nuclide this was computed for.
    pub fn per_nucleon_kev(self, nuclide: Nuclide) -> f64 {
        self.total_kev / f64::from(nuclide.a())
    }
}

/// Binding energy from the constituent masses: `z * dH + n * dn - d`, all mass
/// excesses in keV. The free constituents are hydrogen-1 atoms and neutrons.
fn binding_from_mass_excess(nuclide: Nuclide, mass_excess_kev: f64) -> f64 {
    f64::from(nuclide.z()) * H1_MASS_EXCESS_KEV + f64::from(nuclide.n()) * NEUTRON_MASS_EXCESS_KEV
        - mass_excess_kev
}

/// Atomic mass excess of `nuclide`.
///
/// Uses the AME2020 table when it lists the nuclide, and the liquid drop
/// fallback otherwise. Returns `None` only where neither applies.
pub fn mass_excess(nuclide: Nuclide) -> Option<MassExcess> {
    if let Some(entry) = mass_table::lookup(nuclide) {
        return Some(MassExcess {
            kev: entry.mass_excess_kev,
            source: table_source(entry.estimated),
        });
    }
    let binding = liquid_drop::binding_energy_kev(nuclide.z(), nuclide.n())?;
    Some(MassExcess {
        kev: f64::from(nuclide.z()) * H1_MASS_EXCESS_KEV
            + f64::from(nuclide.n()) * NEUTRON_MASS_EXCESS_KEV
            - binding,
        source: Source::LiquidDrop,
    })
}

/// Total binding energy of `nuclide`.
///
/// Uses the AME2020 table when it lists the nuclide, and the liquid drop
/// fallback otherwise. Returns `None` only where neither applies.
pub fn binding_energy(nuclide: Nuclide) -> Option<Binding> {
    if let Some(entry) = mass_table::lookup(nuclide) {
        return Some(Binding {
            total_kev: binding_from_mass_excess(nuclide, entry.mass_excess_kev),
            source: table_source(entry.estimated),
        });
    }
    let total_kev = liquid_drop::binding_energy_kev(nuclide.z(), nuclide.n())?;
    Some(Binding {
        total_kev,
        source: Source::LiquidDrop,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn free_nucleons_have_zero_binding() {
        assert_eq!(binding_energy(Nuclide::H1).unwrap().total_kev, 0.0);
        assert_eq!(binding_energy(Nuclide::NEUTRON).unwrap().total_kev, 0.0);
    }

    #[test]
    fn source_ordering_puts_the_fallback_last() {
        assert!(Source::Measured < Source::Estimated);
        assert!(Source::Estimated < Source::LiquidDrop);
        assert_eq!(Source::Measured.max(Source::LiquidDrop), Source::LiquidDrop);
    }

    #[test]
    fn fallback_mass_excess_and_binding_agree() {
        let unlisted = Nuclide::new(50, 150);
        let me = mass_excess(unlisted).unwrap();
        let b = binding_energy(unlisted).unwrap();
        assert_eq!(me.source, Source::LiquidDrop);
        assert_eq!(b.source, Source::LiquidDrop);
        let rebuilt = binding_from_mass_excess(unlisted, me.kev);
        assert!((rebuilt - b.total_kev).abs() < 1e-6);
    }

    #[test]
    fn nothing_for_a_lone_neutron_cluster() {
        assert_eq!(binding_energy(Nuclide::new(0, 2)), None);
        assert_eq!(mass_excess(Nuclide::new(0, 0)), None);
    }
}
