// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "liquid_drop.rs"
// ============================================================================
//! Liquid drop (semi-empirical) binding energy, used only as a fallback for
//! nuclides the AME2020 table does not list.
//!
//! The coefficients are a common textbook set. Nothing was fitted to AME2020.

/// Volume term coefficient in MeV.
pub const VOLUME_MEV: f64 = 15.75;
/// Surface term coefficient in MeV.
pub const SURFACE_MEV: f64 = 17.8;
/// Coulomb term coefficient in MeV.
pub const COULOMB_MEV: f64 = 0.711;
/// Asymmetry term coefficient in MeV.
pub const ASYMMETRY_MEV: f64 = 23.7;
/// Pairing term coefficient in MeV.
pub const PAIRING_MEV: f64 = 11.18;

/// Total binding energy in keV for `z` protons and `n` neutrons.
///
/// Returns `None` when `z` or `n` is zero, where the formula has no meaning.
/// A negative result means the model considers the nucleus unbound.
pub fn binding_energy_kev(z: u16, n: u16) -> Option<f64> {
    if z == 0 || n == 0 {
        return None;
    }
    let zf = f64::from(z);
    let nf = f64::from(n);
    let a = zf + nf;
    let cbrt_a = a.cbrt();

    let volume = VOLUME_MEV * a;
    let surface = SURFACE_MEV * cbrt_a * cbrt_a;
    let coulomb = COULOMB_MEV * zf * (zf - 1.0) / cbrt_a;
    let asymmetry = ASYMMETRY_MEV * (nf - zf) * (nf - zf) / a;
    let pairing = match (z % 2, n % 2) {
        (0, 0) => PAIRING_MEV / a.sqrt(),
        (1, 1) => -PAIRING_MEV / a.sqrt(),
        _ => 0.0,
    };
    Some((volume - surface - coulomb - asymmetry + pairing) * 1000.0)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn per_nucleon(z: u16, n: u16) -> f64 {
        binding_energy_kev(z, n).unwrap() / f64::from(z + n)
    }

    #[test]
    fn heavy_nuclei_land_near_the_measured_curve() {
        // Measured: Fe-56 8790 keV, U-238 7570 keV per nucleon.
        assert!((per_nucleon(26, 30) - 8790.0).abs() < 100.0);
        assert!((per_nucleon(92, 146) - 7570.0).abs() < 100.0);
    }

    #[test]
    fn pairing_favours_even_even() {
        // Same mass number 56: even-even Fe-56 binds more than odd-odd Mn-56.
        assert!(binding_energy_kev(26, 30).unwrap() > binding_energy_kev(25, 31).unwrap());
    }

    #[test]
    fn undefined_without_both_kinds_of_nucleon() {
        assert_eq!(binding_energy_kev(0, 5), None);
        assert_eq!(binding_energy_kev(5, 0), None);
    }
}
