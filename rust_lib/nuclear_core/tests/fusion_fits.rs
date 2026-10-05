// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "tests/fusion_fits.rs"
// ============================================================================
//! Cross-check of the Bosch and Hale reactivity fits in `fusion` against a
//! numerical Maxwell average of the cross section fit from the same paper.
//!
//! The two fits are separate tables of coefficients (table VII for the
//! reactivity, table IV for the S-factor), so a typing error in either one
//! shows up here.

use nuclear_core::fusion;
use nuclear_core::Nuclide;

const SPEED_OF_LIGHT: f64 = 2.997_924_58e10;

/// One S-factor fit: gamow constant, reduced mass energy, numerator and
/// denominator coefficients. S is in keV millibarns.
struct SFit {
    name: &'static str,
    gamow: f64,
    mrc2: f64,
    a: [f64; 5],
    b: [f64; 4],
}

const FITS: [SFit; 4] = [
    SFit {
        name: "D + T -> He-4 + n",
        gamow: 34.3827,
        mrc2: 1_124_656.0,
        a: [6.927e4, 7.454e8, 2.050e6, 5.2002e4, 0.0],
        b: [6.38e1, -9.95e-1, 6.981e-5, 1.728e-4],
    },
    SFit {
        name: "D + D -> He-3 + n",
        gamow: 31.3970,
        mrc2: 937_814.0,
        a: [5.3701e4, 3.3027e2, -1.2706e-1, 2.9327e-5, -2.5151e-9],
        b: [0.0; 4],
    },
    SFit {
        name: "D + D -> T + p",
        gamow: 31.3970,
        mrc2: 937_814.0,
        a: [5.5576e4, 2.1054e2, -3.2638e-2, 1.4987e-6, 1.8181e-10],
        b: [0.0; 4],
    },
    SFit {
        name: "D + He-3 -> He-4 + p",
        gamow: 68.7508,
        mrc2: 1_124_572.0,
        a: [5.7501e6, 2.5226e3, 4.5566e1, 0.0, 0.0],
        b: [-3.1995e-3, -8.5530e-6, 5.9014e-8, 0.0],
    },
];

/// Cross section in cm^2 at energy `e` keV.
fn sigma(f: &SFit, e: f64) -> f64 {
    let num = f.a[0] + e * (f.a[1] + e * (f.a[2] + e * (f.a[3] + e * f.a[4])));
    let den = 1.0 + e * (f.b[0] + e * (f.b[1] + e * (f.b[2] + e * f.b[3])));
    num / den / e * (-f.gamow / e.sqrt()).exp() * 1.0e-27
}

/// Maxwell average of the cross section, in cm^3/s, by Simpson's rule.
fn maxwell_average(f: &SFit, t: f64) -> f64 {
    let (lo, hi) = (0.5f64, (60.0 * t).max(200.0));
    let steps = 20_000usize;
    let (x0, x1) = (lo.ln(), hi.ln());
    let h = (x1 - x0) / steps as f64;
    let g = |x: f64| {
        let e = x.exp();
        sigma(f, e) * e * (-e / t).exp() * e
    };
    let mut sum = g(x0) + g(x1);
    for i in 1..steps {
        sum += if i % 2 == 1 { 4.0 } else { 2.0 } * g(x0 + h * i as f64);
    }
    let integral = sum * h / 3.0;
    SPEED_OF_LIGHT * (8.0 / (core::f64::consts::PI * f.mrc2)).sqrt() * t.powf(-1.5) * integral
}

#[test]
fn reactivity_fits_agree_with_the_cross_section_fits() {
    let all = fusion::reactions();
    for fit in &FITS {
        let channel = all.iter().find(|c| c.name == fit.name).unwrap();
        for t in [2.0, 5.0, 10.0, 20.0, 50.0, 100.0] {
            let ratio = channel.reactivity(t) / maxwell_average(fit, t);
            assert!(
                (ratio - 1.0).abs() < 0.04,
                "{} at {t} keV: ratio {ratio:.4}",
                fit.name
            );
        }
    }
}

#[test]
fn deuterium_tritium_is_far_faster_than_deuterium_helium_3_when_cool() {
    let dt = fusion::channels_for(Nuclide::H2, Nuclide::H3).remove(0);
    let dhe = fusion::channels_for(Nuclide::H2, Nuclide::HE3).remove(0);
    let at = |t: f64| dt.reactivity(t) / dhe.reactivity(t);
    assert!(at(20.0) > 100.0, "{}", at(20.0));
    assert!(at(100.0) > 3.0 && at(100.0) < 8.0, "{}", at(100.0));
}
