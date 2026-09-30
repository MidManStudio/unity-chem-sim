// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "elements.rs"
// ============================================================================
//! Element symbols for atomic numbers 1 through 118.

/// Highest atomic number in the chart.
pub const MAX_Z: u16 = 118;

const SYMBOLS: [&str; 118] = [
    "H", "He", "Li", "Be", "B", "C", "N", "O", "F", "Ne", //
    "Na", "Mg", "Al", "Si", "P", "S", "Cl", "Ar", "K", "Ca", //
    "Sc", "Ti", "V", "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn", //
    "Ga", "Ge", "As", "Se", "Br", "Kr", "Rb", "Sr", "Y", "Zr", //
    "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd", "In", "Sn", //
    "Sb", "Te", "I", "Xe", "Cs", "Ba", "La", "Ce", "Pr", "Nd", //
    "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb", //
    "Lu", "Hf", "Ta", "W", "Re", "Os", "Ir", "Pt", "Au", "Hg", //
    "Tl", "Pb", "Bi", "Po", "At", "Rn", "Fr", "Ra", "Ac", "Th", //
    "Pa", "U", "Np", "Pu", "Am", "Cm", "Bk", "Cf", "Es", "Fm", //
    "Md", "No", "Lr", "Rf", "Db", "Sg", "Bh", "Hs", "Mt", "Ds", //
    "Rg", "Cn", "Nh", "Fl", "Mc", "Lv", "Ts", "Og",
];

/// Symbol for atomic number `z`, or `None` outside 1..=118.
pub fn symbol(z: u16) -> Option<&'static str> {
    if z == 0 || z > MAX_Z {
        return None;
    }
    Some(SYMBOLS[usize::from(z) - 1])
}

/// Atomic number for an element symbol. Case sensitive: `"Fe"`, not `"FE"`.
pub fn z_from_symbol(symbol: &str) -> Option<u16> {
    SYMBOLS
        .iter()
        .position(|s| *s == symbol)
        .map(|index| index as u16 + 1)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn known_symbols() {
        assert_eq!(symbol(1), Some("H"));
        assert_eq!(symbol(26), Some("Fe"));
        assert_eq!(symbol(92), Some("U"));
        assert_eq!(symbol(118), Some("Og"));
    }

    #[test]
    fn out_of_range_has_no_symbol() {
        assert_eq!(symbol(0), None);
        assert_eq!(symbol(119), None);
    }

    #[test]
    fn every_symbol_round_trips_and_is_unique() {
        for z in 1..=MAX_Z {
            let sym = symbol(z).unwrap();
            assert_eq!(
                z_from_symbol(sym),
                Some(z),
                "symbol {sym} maps back to its Z"
            );
        }
        assert_eq!(z_from_symbol("Xx"), None);
        assert_eq!(z_from_symbol("fe"), None);
    }
}
