// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "neutron.rs"
// ============================================================================
//! Neutron absorption: how often it ends in fission instead of capture.
//!
//! This is a small authored table for uranium and plutonium, not an
//! evaluation. It gives one number per nucleus and energy class: the share of
//! absorbed neutrons that cause fission. Everything else absorbed is capture,
//! which turns the target into the next heavier isotope. Elastic and inelastic
//! scattering, (n,2n) and the rest are not modelled.
//!
//! For a nucleus that is not in the table, build a [`Branching`] yourself and
//! hand it to the sample methods in [`react`](crate::react).

use crate::binding;
use crate::nuclide::Nuclide;

/// Neutron energy where the fast class starts, in eV.
pub const FAST_FROM_EV: f64 = 1.0e5;

/// Neutron energy where the high class starts, in eV.
pub const HIGH_FROM_EV: f64 = 1.0e7;

/// Share of absorptions that cause fission at 10 MeV and above. Capture
/// cross sections fall to millibarns there while fission stays near a barn.
const HIGH_FISSION: f64 = 0.99;

/// A neutron energy range with one set of branching numbers.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum EnergyClass {
    /// Below 100 keV: thermal and resonance energies.
    Thermal,
    /// 100 keV up to 10 MeV: fast reactor energies.
    Fast,
    /// 10 MeV and above.
    High,
}

/// The class of a neutron energy given in eV.
pub fn energy_class(energy_ev: f64) -> EnergyClass {
    if energy_ev >= HIGH_FROM_EV {
        EnergyClass::High
    } else if energy_ev >= FAST_FROM_EV {
        EnergyClass::Fast
    } else {
        EnergyClass::Thermal
    }
}

/// How an absorbed neutron ends: fission or capture.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Branching {
    /// Probability that an absorbed neutron causes fission. The rest are
    /// captured.
    pub fission: f64,
}

impl Branching {
    /// A branching with the given fission probability, or `None` if it is not
    /// a number from 0 to 1.
    pub fn new(fission: f64) -> Option<Branching> {
        if (0.0..=1.0).contains(&fission) {
            Some(Branching { fission })
        } else {
            None
        }
    }

    /// Probability that an absorbed neutron is captured.
    pub fn capture(self) -> f64 {
        1.0 - self.fission
    }
}

/// (z, a, thermal, fast): fission probability per absorbed neutron.
///
/// Thermal, 0.0253 eV, for the four nuclei that fission there: fission over
/// fission plus capture, from the IAEA neutron data standards thermal
/// constants of 2017. U-233: 533.0 and 44.9 barns. U-235: 587.3 and 99.5.
/// Pu-239: 752.4 and 269.8. Pu-241: 1023.6 and 362.3.
///
/// Thermal for the other four is authored. U-238, Pu-240 and Pu-242 fission
/// by thermal neutrons far less than once in a hundred absorptions, so 0.
/// Pu-238 has a thermal fission cross section near 18 barns against a capture
/// cross section of several hundred, so about 0.03.
///
/// Fast: fission per absorption in a metal-fuelled sodium fast reactor
/// spectrum, from the table of R. N. Hill (Argonne), "Fast Reactor Physics 1:
/// Spectrum and Cross Sections", NRC Fast Reactor Technology Training
/// Curriculum, 26 March 2019. U-233 is not in that table. Its 0.91 is authored
/// from a measured capture to fission ratio of about 0.10 in a soft spectrum
/// fast assembly.
const TABLE: [(u16, u16, f64, f64); 8] = [
    (92, 233, 0.922, 0.91),
    (92, 235, 0.855, 0.80),
    (92, 238, 0.0, 0.17),
    (94, 238, 0.03, 0.70),
    (94, 239, 0.736, 0.86),
    (94, 240, 0.0, 0.55),
    (94, 241, 0.739, 0.87),
    (94, 242, 0.0, 0.52),
];

/// The authored branching of `target` for a neutron of `energy_ev`, or `None`
/// if the target is not in the table.
pub fn branching(target: Nuclide, energy_ev: f64) -> Option<Branching> {
    let &(_, _, thermal, fast) = TABLE
        .iter()
        .find(|&&(z, a, _, _)| z == target.z() && a == target.a())?;
    let fission = match energy_class(energy_ev) {
        EnergyClass::Thermal => thermal,
        EnergyClass::Fast => fast,
        EnergyClass::High => HIGH_FISSION,
    };
    Some(Branching { fission })
}

/// The nuclei in the branching table, in ascending order.
pub fn tabulated() -> Vec<Nuclide> {
    TABLE
        .iter()
        .map(|&(z, a, _, _)| Nuclide::new(z, a - z))
        .collect()
}

/// Energy released in keV when `target` captures a neutron and becomes the
/// next isotope: the neutron separation energy of the product. `None` when a
/// mass is missing.
pub fn capture_q_kev(target: Nuclide) -> Option<f64> {
    let product = Nuclide::new(target.z(), target.n() + 1);
    let t = binding::mass_excess(target)?.kev;
    let n = binding::mass_excess(Nuclide::NEUTRON)?.kev;
    let p = binding::mass_excess(product)?.kev;
    Some(t + n - p)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn nuc(text: &str) -> Nuclide {
        Nuclide::parse(text).unwrap()
    }

    #[test]
    fn classes_split_at_the_documented_energies() {
        assert_eq!(energy_class(0.0253), EnergyClass::Thermal);
        assert_eq!(energy_class(99_999.0), EnergyClass::Thermal);
        assert_eq!(energy_class(100_000.0), EnergyClass::Fast);
        assert_eq!(energy_class(9.99e6), EnergyClass::Fast);
        assert_eq!(energy_class(1.0e7), EnergyClass::High);
    }

    #[test]
    fn thermal_values_follow_the_standards_cross_sections() {
        let p = |name: &str| branching(nuc(name), 0.0253).unwrap().fission;
        let share = |f: f64, c: f64| f / (f + c);
        assert!((p("U-233") - share(533.0, 44.9)).abs() < 0.001);
        assert!((p("U-235") - share(587.3, 99.5)).abs() < 0.001);
        assert!((p("Pu-239") - share(752.4, 269.8)).abs() < 0.001);
        assert!((p("Pu-241") - share(1023.6, 362.3)).abs() < 0.001);
        assert_eq!(p("U-238"), 0.0);
    }

    #[test]
    fn fertile_nuclei_fission_only_when_fast() {
        for name in ["U-238", "Pu-240", "Pu-242"] {
            let n = nuc(name);
            assert_eq!(branching(n, 0.0253).unwrap().fission, 0.0, "{name}");
            let fast = branching(n, 1.0e6).unwrap().fission;
            assert!(fast > 0.1 && fast < 0.6, "{name}: {fast}");
            assert!(branching(n, 1.4e7).unwrap().fission > 0.9, "{name}");
        }
    }

    #[test]
    fn every_value_is_a_probability_and_the_table_is_sorted() {
        let all = tabulated();
        assert_eq!(all.len(), 8);
        for pair in all.windows(2) {
            assert!(pair[0] < pair[1]);
        }
        for n in all {
            for e in [0.0253, 1.0e6, 1.4e7] {
                let b = branching(n, e).unwrap();
                assert!((0.0..=1.0).contains(&b.fission));
                assert!((b.fission + b.capture() - 1.0).abs() < 1e-15);
            }
        }
    }

    #[test]
    fn nuclei_outside_the_table_have_no_branching() {
        assert!(branching(nuc("Fe-56"), 0.0253).is_none());
        assert!(branching(nuc("Am-241"), 0.0253).is_none());
        assert!(Branching::new(1.5).is_none());
        assert!(Branching::new(f64::NAN).is_none());
        assert_eq!(Branching::new(0.25).unwrap().capture(), 0.75);
    }

    #[test]
    fn capture_energy_is_the_neutron_separation_energy() {
        // Neutron separation energies: U-236 6.545 MeV, U-239 4.806 MeV.
        let q235 = capture_q_kev(nuc("U-235")).unwrap() / 1000.0;
        let q238 = capture_q_kev(nuc("U-238")).unwrap() / 1000.0;
        assert!((q235 - 6.545).abs() < 0.01, "{q235}");
        assert!((q238 - 4.806).abs() < 0.01, "{q238}");
    }
}
