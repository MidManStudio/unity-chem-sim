// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "fusion.rs"
// ============================================================================
//! Fusion of light nuclei: which channels exist, what they release, and how
//! fast they run at a given temperature.
//!
//! Speed is the thermal reactivity, the cross section times the relative
//! speed averaged over a Maxwell distribution of temperature `T`, in cm^3/s.
//! Four channels (D-T, D-D twice, D-He3) use the Bosch and Hale (1992) fits.
//! Three more (p-p, p-D, He3-He3) use a Gamow-factor integral over a
//! polynomial S-factor, with zero-energy S-factors from Solar Fusion II.
//!
//! The reactivity of a pair of identical nuclei counts each pair once, so the
//! reaction rate density is `n^2 * reactivity / 2`.
//!
//! ```
//! use nuclear_core::fusion;
//! use nuclear_core::Nuclide;
//!
//! let dt = fusion::channels_for(Nuclide::H2, Nuclide::H3);
//! assert_eq!(dt.len(), 1);
//! // Deuterium plus tritium at 10 keV: about 1.1e-16 cm^3/s.
//! let r = dt[0].reactivity(10.0);
//! assert!(r > 1.0e-16 && r < 1.3e-16);
//! ```

use crate::binding;
use crate::decay::ELECTRON_MASS_KEV;
use crate::nuclide::Nuclide;
use crate::reaction::q_value;

/// Atomic mass unit in keV.
const ATOMIC_MASS_UNIT_KEV: f64 = 931_494.102_42;

/// Fine structure constant.
const FINE_STRUCTURE: f64 = 7.297_352_569_3e-3;

/// Speed of light in cm/s.
const SPEED_OF_LIGHT: f64 = 2.997_924_58e10;

/// Intervals of the Simpson rule behind the Gamow-factor integral.
const INTEGRATION_STEPS: usize = 600;

/// How the reactivity of a channel is computed.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Fit {
    /// The Bosch and Hale (1992) reactivity fit.
    BoschHale {
        /// Gamow constant in sqrt(keV).
        gamow: f64,
        /// Reduced mass energy in keV, as the fit defines it.
        mrc2_kev: f64,
        /// Coefficients C1 to C7.
        c: [f64; 7],
        /// Lowest temperature the fit was published for, in keV.
        low_kev: f64,
        /// Highest temperature the fit was published for, in keV.
        high_kev: f64,
    },
    /// A Gamow-factor integral over `S(E) = s0 + s1 E + s2 E^2 / 2`, with E
    /// in keV.
    Gamow {
        /// S at zero energy in keV barns.
        s0_kev_b: f64,
        /// Slope of S at zero energy in barns.
        s1_b: f64,
        /// Curvature of S at zero energy in barns per keV.
        s2_b_per_kev: f64,
    },
}

/// One fusion reaction.
#[derive(Clone, Debug, PartialEq)]
pub struct Channel {
    /// A short name, such as `D + T -> He-4 + n`.
    pub name: &'static str,
    /// The two nuclei that fuse, in ascending order.
    pub reactants: [Nuclide; 2],
    /// Nuclei that come out with how many of each. Photons, positrons and
    /// neutrinos are not nuclei and are not listed.
    pub products: Vec<(Nuclide, u8)>,
    /// Energy released in keV, including the energy of photons and neutrinos.
    /// For p + p it is the kinetic energy of the positron and neutrino, not
    /// counting the annihilation of the positron.
    pub q_kev: f64,
    /// How the reactivity is computed.
    pub fit: Fit,
}

impl Channel {
    /// Thermal reactivity in cm^3/s at temperature `temperature_kev`. Zero for
    /// a temperature that is not positive and finite. Outside the published
    /// range of a [`Fit::BoschHale`] fit the value is an extrapolation.
    pub fn reactivity(&self, temperature_kev: f64) -> f64 {
        if !(temperature_kev > 0.0 && temperature_kev.is_finite()) {
            return 0.0;
        }
        match self.fit {
            Fit::BoschHale {
                gamow, mrc2_kev, c, ..
            } => bosch_hale(gamow, mrc2_kev, &c, temperature_kev),
            Fit::Gamow {
                s0_kev_b,
                s1_b,
                s2_b_per_kev,
            } => gamow_reactivity(
                self.reactants,
                (s0_kev_b, s1_b, s2_b_per_kev),
                temperature_kev,
            ),
        }
    }
}

/// Reactivity from the Bosch and Hale fit, in cm^3/s.
fn bosch_hale(gamow: f64, mrc2: f64, c: &[f64; 7], t: f64) -> f64 {
    let theta =
        t / (1.0 - t * (c[1] + t * (c[3] + t * c[5])) / (1.0 + t * (c[2] + t * (c[4] + t * c[6]))));
    let xi = (gamow * gamow / (4.0 * theta)).cbrt();
    c[0] * theta * (xi / (mrc2 * t * t * t)).sqrt() * (-3.0 * xi).exp()
}

/// Mass of the bare nucleus in keV, from the atomic mass table.
fn nuclear_mass_kev(nuclide: Nuclide) -> Option<f64> {
    let excess = binding::mass_excess(nuclide)?.kev;
    Some(
        f64::from(nuclide.a()) * ATOMIC_MASS_UNIT_KEV + excess
            - f64::from(nuclide.z()) * ELECTRON_MASS_KEV,
    )
}

/// Reactivity from the Gamow-factor integral, in cm^3/s.
fn gamow_reactivity(reactants: [Nuclide; 2], s: (f64, f64, f64), t: f64) -> f64 {
    let (m1, m2) = match (
        nuclear_mass_kev(reactants[0]),
        nuclear_mass_kev(reactants[1]),
    ) {
        (Some(a), Some(b)) => (a, b),
        _ => return 0.0,
    };
    let mu = m1 * m2 / (m1 + m2);
    let charges = f64::from(reactants[0].z()) * f64::from(reactants[1].z());
    let b = core::f64::consts::PI * FINE_STRUCTURE * charges * (2.0 * mu).sqrt();
    // The integrand peaks at e0 and is negligible below e0 / 60 and above
    // e0 plus a few widths plus a few thermal energies.
    let e0 = (b * t / 2.0).powf(2.0 / 3.0);
    let width = 4.0 * (e0 * t / 3.0).sqrt();
    let lo = (e0 / 60.0).ln();
    let hi = (e0 + 40.0 * t + 14.0 * width).ln();
    let h = (hi - lo) / INTEGRATION_STEPS as f64;
    let f = |x: f64| -> f64 {
        let e = x.exp();
        let s_factor = (s.0 + s.1 * e + 0.5 * s.2 * e * e).max(0.0);
        s_factor * (-e / t - b / e.sqrt()).exp() * e
    };
    let mut sum = f(lo) + f(hi);
    for i in 1..INTEGRATION_STEPS {
        let weight = if i % 2 == 1 { 4.0 } else { 2.0 };
        sum += weight * f(lo + h * i as f64);
    }
    let integral = sum * h / 3.0;
    1.0e-24 * SPEED_OF_LIGHT * (8.0 / (core::f64::consts::PI * mu)).sqrt() * t.powf(-1.5) * integral
}

fn channel(
    name: &'static str,
    a: Nuclide,
    b: Nuclide,
    products: &[(Nuclide, u8)],
    fit: Fit,
) -> Channel {
    let (first, second) = if a <= b { (a, b) } else { (b, a) };
    let flat: Vec<Nuclide> = products
        .iter()
        .flat_map(|&(n, count)| std::iter::repeat(n).take(usize::from(count)))
        .collect();
    let q_kev = q_value(&[first, second], &flat)
        .map(|q| q.kev)
        .unwrap_or(0.0);
    Channel {
        name,
        reactants: [first, second],
        products: products.to_vec(),
        q_kev,
        fit,
    }
}

/// Every channel the module knows, ordered by reactants and then name.
pub fn reactions() -> Vec<Channel> {
    let n = Nuclide::NEUTRON;
    let (p, d, t, he3, he4) = (
        Nuclide::H1,
        Nuclide::H2,
        Nuclide::H3,
        Nuclide::HE3,
        Nuclide::HE4,
    );
    let bh = |gamow, mrc2_kev, c, low_kev, high_kev| Fit::BoschHale {
        gamow,
        mrc2_kev,
        c,
        low_kev,
        high_kev,
    };
    let mut all = vec![
        // Bosch and Hale (1992), Nucl. Fusion 32, 611, table VII.
        channel(
            "D + T -> He-4 + n",
            d,
            t,
            &[(he4, 1), (n, 1)],
            bh(
                34.3827,
                1_124_656.0,
                [
                    1.17302e-9,
                    1.51361e-2,
                    7.51886e-2,
                    4.60643e-3,
                    1.35000e-2,
                    -1.06750e-4,
                    1.36600e-5,
                ],
                0.2,
                100.0,
            ),
        ),
        channel(
            "D + D -> He-3 + n",
            d,
            d,
            &[(he3, 1), (n, 1)],
            bh(
                31.3970,
                937_814.0,
                [
                    5.43360e-12,
                    5.85778e-3,
                    7.68222e-3,
                    0.0,
                    -2.96400e-6,
                    0.0,
                    0.0,
                ],
                0.2,
                100.0,
            ),
        ),
        channel(
            "D + D -> T + p",
            d,
            d,
            &[(t, 1), (p, 1)],
            bh(
                31.3970,
                937_814.0,
                [
                    5.65718e-12,
                    3.41267e-3,
                    1.99167e-3,
                    0.0,
                    1.05060e-5,
                    0.0,
                    0.0,
                ],
                0.2,
                100.0,
            ),
        ),
        channel(
            "D + He-3 -> He-4 + p",
            d,
            he3,
            &[(he4, 1), (p, 1)],
            bh(
                68.7508,
                1_124_572.0,
                [
                    5.51036e-10,
                    6.41918e-3,
                    -2.02896e-3,
                    -1.91080e-5,
                    1.35776e-4,
                    0.0,
                    0.0,
                ],
                0.5,
                190.0,
            ),
        ),
        // Solar Fusion II, Adelberger et al., Rev. Mod. Phys. 83, 195 (2011).
        channel(
            "p + D -> He-3 + gamma",
            p,
            d,
            &[(he3, 1)],
            Fit::Gamow {
                s0_kev_b: 2.14e-4,
                s1_b: 0.0,
                s2_b_per_kev: 0.0,
            },
        ),
        channel(
            "He-3 + He-3 -> He-4 + 2p",
            he3,
            he3,
            &[(he4, 1), (p, 2)],
            Fit::Gamow {
                s0_kev_b: 5.21e3,
                s1_b: -4.9,
                s2_b_per_kev: 2.2e-2,
            },
        ),
    ];
    // p + p -> D + e+ + nu is weak: the positron carries a unit of charge, so
    // the energy needs the electron mass by hand. Atomic masses of two
    // hydrogens in, one deuterium out, and two electron masses for the
    // positron and the electron the extra proton left behind.
    let q_pp = match (binding::mass_excess(p), binding::mass_excess(d)) {
        (Some(hp), Some(hd)) => 2.0 * hp.kev - hd.kev - 2.0 * ELECTRON_MASS_KEV,
        _ => 0.0,
    };
    all.push(Channel {
        name: "p + p -> D + e+ + nu",
        reactants: [p, p],
        products: vec![(d, 1)],
        q_kev: q_pp,
        fit: Fit::Gamow {
            s0_kev_b: 4.01e-22,
            s1_b: 4.01e-22 * 0.0112,
            s2_b_per_kev: 0.0,
        },
    });
    all.sort_by(|x, y| (x.reactants, x.name).cmp(&(y.reactants, y.name)));
    all
}

/// The channels for a pair of nuclei, in either order. Empty when the pair
/// has none.
pub fn channels_for(a: Nuclide, b: Nuclide) -> Vec<Channel> {
    let pair = if a <= b { [a, b] } else { [b, a] };
    reactions()
        .into_iter()
        .filter(|c| c.reactants == pair)
        .collect()
}

/// The share of events that take each channel at temperature `t_kev`: the
/// reactivities, scaled to sum to 1. All zero when no channel runs.
pub fn branching(channels: &[Channel], t_kev: f64) -> Vec<f64> {
    let weights: Vec<f64> = channels.iter().map(|c| c.reactivity(t_kev)).collect();
    let sum: f64 = weights.iter().sum();
    if sum > 0.0 && sum.is_finite() {
        weights.iter().map(|w| w / sum).collect()
    } else {
        vec![0.0; channels.len()]
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn by_name(name: &str) -> Channel {
        reactions()
            .into_iter()
            .find(|c| c.name == name)
            .unwrap_or_else(|| panic!("no channel {name}"))
    }

    #[test]
    fn energies_match_the_known_values() {
        let mev = |name: &str| by_name(name).q_kev / 1000.0;
        assert!((mev("D + T -> He-4 + n") - 17.589).abs() < 0.005);
        assert!((mev("D + D -> He-3 + n") - 3.269).abs() < 0.005);
        assert!((mev("D + D -> T + p") - 4.033).abs() < 0.005);
        assert!((mev("D + He-3 -> He-4 + p") - 18.354).abs() < 0.005);
        assert!((mev("p + D -> He-3 + gamma") - 5.494).abs() < 0.005);
        assert!((mev("He-3 + He-3 -> He-4 + 2p") - 12.860).abs() < 0.005);
        assert!((mev("p + p -> D + e+ + nu") - 0.420).abs() < 0.005);
    }

    #[test]
    fn nucleons_and_charge_are_conserved() {
        for c in reactions() {
            let a_in: u16 = c.reactants.iter().map(|n| n.a()).sum();
            let a_out: u16 = c.products.iter().map(|&(n, k)| n.a() * u16::from(k)).sum();
            assert_eq!(a_in, a_out, "{}", c.name);
            let z_in: u16 = c.reactants.iter().map(|n| n.z()).sum();
            let z_out: u16 = c.products.iter().map(|&(n, k)| n.z() * u16::from(k)).sum();
            if c.name.starts_with("p + p") {
                assert_eq!(z_in, z_out + 1, "{}: one unit leaves as a positron", c.name);
            } else {
                assert_eq!(z_in, z_out, "{}", c.name);
            }
        }
    }

    #[test]
    fn deuterium_tritium_at_ten_kev() {
        // 1.1e-22 m^3/s, the textbook value, is 1.1e-16 cm^3/s.
        let r = by_name("D + T -> He-4 + n").reactivity(10.0);
        assert!((r - 1.1362e-16).abs() < 1.0e-19, "{r}");
    }

    #[test]
    fn deuterium_tritium_beats_deuterium_deuterium_by_two_orders_at_ten_kev() {
        let dt = by_name("D + T -> He-4 + n").reactivity(10.0);
        let dd = by_name("D + D -> He-3 + n").reactivity(10.0);
        assert!(dt / dd > 100.0 && dt / dd < 300.0, "{}", dt / dd);
    }

    #[test]
    fn reactivity_rises_with_temperature_up_to_the_peak() {
        for c in reactions() {
            let mut last = 0.0;
            for t in [1.0, 2.0, 5.0, 10.0] {
                let r = c.reactivity(t);
                assert!(r > last, "{} at {t} keV", c.name);
                last = r;
            }
        }
    }

    #[test]
    fn bad_temperatures_give_zero() {
        let c = by_name("D + T -> He-4 + n");
        for t in [0.0, -1.0, f64::NAN, f64::INFINITY] {
            assert_eq!(c.reactivity(t), 0.0);
        }
    }

    #[test]
    fn the_gamow_integral_reproduces_the_deuterium_deuterium_fit() {
        // The same machinery as the weak and radiative channels, fed the
        // S-factor of the D(d,n)He-3 cross section fit of Bosch and Hale
        // (table IV: 53.701 keV mb, 330.27 keV mb per keV and so on),
        // against their reactivity fit.
        let bh = by_name("D + D -> He-3 + n");
        let gamow = Channel {
            fit: Fit::Gamow {
                s0_kev_b: 53.701,
                s1_b: 0.330_27,
                s2_b_per_kev: -2.5412e-4,
            },
            ..bh.clone()
        };
        for t in [5.0, 10.0, 20.0, 50.0] {
            let ratio = gamow.reactivity(t) / bh.reactivity(t);
            assert!((ratio - 1.0).abs() < 0.03, "T = {t} keV: ratio {ratio}");
        }
    }

    #[test]
    fn proton_proton_matches_the_solar_core() {
        // At the centre of the Sun, 1.35 keV, a proton waits about 1e10 years
        // to fuse. With n_p near 3e25 per cm^3 that is a reactivity near
        // 1e-43 cm^3/s.
        let r = by_name("p + p -> D + e+ + nu").reactivity(1.35);
        assert!(r > 4.0e-44 && r < 2.5e-43, "{r}");
    }

    #[test]
    fn channels_for_ignores_order_and_finds_both_deuterium_branches() {
        let dd = channels_for(Nuclide::H2, Nuclide::H2);
        assert_eq!(dd.len(), 2);
        assert_eq!(
            channels_for(Nuclide::H3, Nuclide::H2),
            channels_for(Nuclide::H2, Nuclide::H3)
        );
        assert!(channels_for(Nuclide::H3, Nuclide::H3).is_empty());
        let share = branching(&dd, 10.0);
        assert!((share.iter().sum::<f64>() - 1.0).abs() < 1e-12);
        // The two branches run at nearly the same rate.
        assert!((share[0] - 0.5).abs() < 0.05, "{share:?}");
        assert_eq!(branching(&dd, 0.0), vec![0.0, 0.0]);
    }
}
