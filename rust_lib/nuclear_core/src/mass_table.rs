// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "mass_table.rs"
// ============================================================================
//! Lookup over the generated AME2020 table.

use crate::ame2020_data;
use crate::nuclide::Nuclide;

/// Number of rows in the table, neutron included.
pub const ENTRY_COUNT: usize = ame2020_data::ENTRY_COUNT;

/// Mass excess of hydrogen-1 in keV. Used to turn mass excess into binding energy.
pub const H1_MASS_EXCESS_KEV: f64 = ame2020_data::H1_MASS_EXCESS_KEV;

/// Mass excess of the free neutron in keV.
pub const NEUTRON_MASS_EXCESS_KEV: f64 = ame2020_data::NEUTRON_MASS_EXCESS_KEV;

/// One row of the AME2020 table.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct MassEntry {
    /// The nuclide this row describes.
    pub nuclide: Nuclide,
    /// Atomic mass excess in keV: atomic mass minus mass number, in energy units.
    pub mass_excess_kev: f64,
    /// Uncertainty of the mass excess in keV.
    pub uncertainty_kev: f64,
    /// True when AME2020 marks the value as taken from systematic trends of the
    /// mass surface instead of measurement.
    pub estimated: bool,
}

type Row = (u8, u16, f64, f64, bool);

fn entry_from_row(row: Row) -> MassEntry {
    let (z, a, mass_excess_kev, uncertainty_kev, estimated) = row;
    MassEntry {
        nuclide: Nuclide::new(u16::from(z), a - u16::from(z)),
        mass_excess_kev,
        uncertainty_kev,
        estimated,
    }
}

/// Finds the table row for `nuclide`, if AME2020 lists it.
pub fn lookup(nuclide: Nuclide) -> Option<MassEntry> {
    let z = u8::try_from(nuclide.z()).ok()?;
    let a = nuclide.a();
    let index = ame2020_data::RAW
        .binary_search_by(|row| (row.0, row.1).cmp(&(z, a)))
        .ok()?;
    Some(entry_from_row(ame2020_data::RAW[index]))
}

/// Iterates every row in ascending (z, a) order.
pub fn entries() -> impl Iterator<Item = MassEntry> {
    ame2020_data::RAW.iter().copied().map(entry_from_row)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lookup_finds_listed_nuclides() {
        let c12 = lookup(Nuclide::new(6, 6)).unwrap();
        assert_eq!(c12.mass_excess_kev, 0.0);
        assert!(!c12.estimated);
        assert!(lookup(Nuclide::NEUTRON).is_some());
        assert!(lookup(Nuclide::H1).is_some());
    }

    #[test]
    fn lookup_misses_unlisted_nuclides() {
        assert_eq!(lookup(Nuclide::new(50, 150)), None);
        assert_eq!(lookup(Nuclide::new(0, 0)), None);
        assert_eq!(lookup(Nuclide::new(300, 1)), None);
    }

    #[test]
    fn reference_constants_match_their_rows() {
        assert_eq!(
            lookup(Nuclide::H1).unwrap().mass_excess_kev,
            H1_MASS_EXCESS_KEV
        );
        assert_eq!(
            lookup(Nuclide::NEUTRON).unwrap().mass_excess_kev,
            NEUTRON_MASS_EXCESS_KEV
        );
    }

    #[test]
    fn entries_covers_the_whole_table() {
        assert_eq!(entries().count(), ENTRY_COUNT);
    }
}
