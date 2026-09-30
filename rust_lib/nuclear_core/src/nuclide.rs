// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "nuclide.rs"
// ============================================================================
//! The [`Nuclide`] type: a nucleus identified by its proton and neutron counts.

use core::fmt;

use crate::elements;

/// A nuclide, identified by its proton count `z` and neutron count `n`.
///
/// A `Nuclide` carries no energy-level information. The AME2020 table lists
/// ground states only, so isomers of the same (z, n) pair are one `Nuclide`.
/// Ordering is by `z`, then `n`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub struct Nuclide {
    z: u16,
    n: u16,
}

impl Nuclide {
    /// The free neutron (z = 0, n = 1).
    pub const NEUTRON: Nuclide = Nuclide::new(0, 1);
    /// Hydrogen-1. Also stands in for the proton, since masses here are atomic.
    pub const H1: Nuclide = Nuclide::new(1, 0);
    /// Hydrogen-2, deuterium.
    pub const H2: Nuclide = Nuclide::new(1, 1);
    /// Hydrogen-3, tritium.
    pub const H3: Nuclide = Nuclide::new(1, 2);
    /// Helium-3.
    pub const HE3: Nuclide = Nuclide::new(2, 1);
    /// Helium-4, the alpha particle.
    pub const HE4: Nuclide = Nuclide::new(2, 2);

    /// Builds a nuclide from proton count `z` and neutron count `n`.
    pub const fn new(z: u16, n: u16) -> Nuclide {
        Nuclide { z, n }
    }

    /// Builds a nuclide from proton count `z` and mass number `a`.
    /// Returns `None` when `a < z`.
    pub const fn from_za(z: u16, a: u16) -> Option<Nuclide> {
        if a < z {
            None
        } else {
            Some(Nuclide { z, n: a - z })
        }
    }

    /// Proton count.
    pub const fn z(self) -> u16 {
        self.z
    }

    /// Neutron count.
    pub const fn n(self) -> u16 {
        self.n
    }

    /// Mass number, protons plus neutrons. Saturates at `u16::MAX`.
    pub const fn a(self) -> u16 {
        self.z.saturating_add(self.n)
    }

    /// Packs the nuclide into one `u32`, `z` in the high half and `n` in the
    /// low half. Meant for FFI and hash keys.
    pub const fn key(self) -> u32 {
        ((self.z as u32) << 16) | self.n as u32
    }

    /// Inverse of [`Nuclide::key`].
    pub const fn from_key(key: u32) -> Nuclide {
        Nuclide {
            z: (key >> 16) as u16,
            n: (key & 0xFFFF) as u16,
        }
    }

    /// Parses the text form written by `Display`: `"Fe-56"`, `"U-235"`, or
    /// `"n"` for the neutron. Returns `None` for anything else.
    pub fn parse(text: &str) -> Option<Nuclide> {
        let text = text.trim();
        if text == "n" {
            return Some(Nuclide::NEUTRON);
        }
        let (symbol, a) = text.split_once('-')?;
        let z = elements::z_from_symbol(symbol)?;
        let a: u16 = a.parse().ok()?;
        Nuclide::from_za(z, a)
    }
}

impl fmt::Display for Nuclide {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        if self.z == 0 {
            return if self.n == 1 {
                f.write_str("n")
            } else {
                write!(f, "{}n", self.n)
            };
        }
        match elements::symbol(self.z) {
            Some(symbol) => write!(f, "{}-{}", symbol, self.a()),
            None => write!(f, "Z{}-{}", self.z, self.a()),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn counts_and_mass_number() {
        let fe56 = Nuclide::new(26, 30);
        assert_eq!((fe56.z(), fe56.n(), fe56.a()), (26, 30, 56));
    }

    #[test]
    fn from_za_rejects_a_below_z() {
        assert_eq!(Nuclide::from_za(3, 2), None);
        assert_eq!(Nuclide::from_za(1, 1), Some(Nuclide::H1));
        assert_eq!(Nuclide::from_za(0, 1), Some(Nuclide::NEUTRON));
    }

    #[test]
    fn named_constants_have_the_right_counts() {
        assert_eq!(Nuclide::H2.a(), 2);
        assert_eq!(Nuclide::H3.a(), 3);
        assert_eq!((Nuclide::HE3.z(), Nuclide::HE3.n()), (2, 1));
        assert_eq!((Nuclide::HE4.z(), Nuclide::HE4.n()), (2, 2));
    }

    #[test]
    fn key_round_trip() {
        for nuclide in [Nuclide::NEUTRON, Nuclide::HE4, Nuclide::new(118, 176)] {
            assert_eq!(Nuclide::from_key(nuclide.key()), nuclide);
        }
        assert_eq!(Nuclide::new(1, 2).key(), 0x0001_0002);
    }

    #[test]
    fn display_and_parse_agree() {
        assert_eq!(Nuclide::new(26, 30).to_string(), "Fe-56");
        assert_eq!(Nuclide::NEUTRON.to_string(), "n");
        assert_eq!(Nuclide::new(0, 4).to_string(), "4n");
        assert_eq!(Nuclide::parse("U-235"), Some(Nuclide::new(92, 143)));
        assert_eq!(Nuclide::parse(" n "), Some(Nuclide::NEUTRON));
        assert_eq!(Nuclide::parse("Xx-1"), None);
        assert_eq!(Nuclide::parse("Fe-x"), None);
        assert_eq!(Nuclide::parse("Fe"), None);
        assert_eq!(Nuclide::parse("H-0"), None);
    }
}
