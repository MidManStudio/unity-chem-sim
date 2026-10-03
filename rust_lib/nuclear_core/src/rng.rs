// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "rng.rs"
// ============================================================================
//! Deterministic random numbers for the simulation.
//!
//! [`Rng`] is xoshiro256++ seeded through SplitMix64. It needs no dependencies
//! and keeps no global state. Every simulation step takes the generator it
//! should draw from, so a run repeats exactly from its seed, and the four state
//! words can be stored in a save file and restored later.

use core::f64::consts::PI;

/// Largest sample size for which [`Rng::binomial`] is exact. Above it the
/// result is a normal approximation unless the mean is small.
pub const EXACT_BINOMIAL_LIMIT: u64 = 1 << 40;

/// 0.5 * ln(2 * pi), used by the Stirling series.
const HALF_LN_TAU: f64 = 0.918_938_533_204_672_7;

/// xoshiro256++ generator.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Rng {
    state: [u64; 4],
}

fn splitmix64(state: &mut u64) -> u64 {
    *state = state.wrapping_add(0x9E37_79B9_7F4A_7C15);
    let mut z = *state;
    z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
    z ^ (z >> 31)
}

/// Natural log of k! for a whole number k held in an f64.
fn ln_factorial(k: f64) -> f64 {
    if k < 2.0 {
        return 0.0;
    }
    if k < 30.0 {
        let mut product = 1.0;
        let mut i = 2.0;
        while i <= k {
            product *= i;
            i += 1.0;
        }
        return product.ln();
    }
    // Stirling series for ln Gamma(x) with x = k + 1.
    let x = k + 1.0;
    let inv = 1.0 / x;
    let inv2 = inv * inv;
    (x - 0.5) * x.ln() - x
        + HALF_LN_TAU
        + inv * (1.0 / 12.0 - inv2 * (1.0 / 360.0 - inv2 * (1.0 / 1260.0 - inv2 / 1680.0)))
}

impl Rng {
    /// Builds a generator from a seed. The same seed always gives the same stream.
    pub fn new(seed: u64) -> Rng {
        let mut sm = seed;
        Rng {
            state: [
                splitmix64(&mut sm),
                splitmix64(&mut sm),
                splitmix64(&mut sm),
                splitmix64(&mut sm),
            ],
        }
    }

    /// Restores a generator from the words returned by [`Rng::state`].
    /// Returns `None` for the all-zero state, which xoshiro cannot leave.
    pub fn from_state(state: [u64; 4]) -> Option<Rng> {
        if state == [0; 4] {
            None
        } else {
            Some(Rng { state })
        }
    }

    /// The four state words, for saving.
    pub fn state(&self) -> [u64; 4] {
        self.state
    }

    /// Next 64 random bits.
    pub fn next_u64(&mut self) -> u64 {
        let s = &mut self.state;
        let result = s[0].wrapping_add(s[3]).rotate_left(23).wrapping_add(s[0]);
        let t = s[1] << 17;
        s[2] ^= s[0];
        s[3] ^= s[1];
        s[1] ^= s[2];
        s[0] ^= s[3];
        s[2] ^= t;
        s[3] = s[3].rotate_left(45);
        result
    }

    /// Uniform in [0, 1), with 53 random bits.
    pub fn next_f64(&mut self) -> f64 {
        (self.next_u64() >> 11) as f64 * (1.0 / 9_007_199_254_740_992.0)
    }

    /// Uniform in (0, 1], safe to pass to `ln`.
    pub fn next_f64_open(&mut self) -> f64 {
        1.0 - self.next_f64()
    }

    /// Standard normal variate (Box-Muller; the second variate is discarded).
    pub fn next_normal(&mut self) -> f64 {
        let u1 = self.next_f64_open();
        let u2 = self.next_f64();
        (-2.0 * u1.ln()).sqrt() * (2.0 * PI * u2).cos()
    }

    /// Uniform integer in [0, bound) with no modulo bias. Returns 0 when
    /// `bound` is 0.
    pub fn below(&mut self, bound: u64) -> u64 {
        if bound == 0 {
            return 0;
        }
        let mut product = u128::from(self.next_u64()) * u128::from(bound);
        let mut low = product as u64;
        if low < bound {
            let threshold = bound.wrapping_neg() % bound;
            while low < threshold {
                product = u128::from(self.next_u64()) * u128::from(bound);
                low = product as u64;
            }
        }
        (product >> 64) as u64
    }

    /// Number of successes in `n` trials with success probability `p`.
    ///
    /// Exact for `n` up to [`EXACT_BINOMIAL_LIMIT`], and for any `n` when the
    /// mean `n * min(p, 1 - p)` is below 10. Above the limit with a larger mean
    /// it is a normal approximation, accurate to a small fraction of the
    /// standard deviation. A `p` outside [0, 1] is clamped; NaN counts as 0.
    pub fn binomial(&mut self, n: u64, p: f64) -> u64 {
        if n == 0 || p.is_nan() || p <= 0.0 {
            return 0;
        }
        if p >= 1.0 {
            return n;
        }
        let (q, flipped) = if p > 0.5 { (1.0 - p, true) } else { (p, false) };
        let mean = n as f64 * q;
        let k = if mean < 10.0 {
            self.binomial_inversion(n, q)
        } else if n <= EXACT_BINOMIAL_LIMIT {
            self.binomial_btrs(n, q)
        } else {
            let sd = (mean * (1.0 - q)).sqrt();
            let draw = (mean + sd * self.next_normal()).round();
            draw.clamp(0.0, n as f64) as u64
        };
        if flipped {
            n - k
        } else {
            k
        }
    }

    /// Sequential search along the pmf. For p <= 0.5 and n * p < 10.
    fn binomial_inversion(&mut self, n: u64, p: f64) -> u64 {
        let q = 1.0 - p;
        let s = p / q;
        let a = (n as f64 + 1.0) * s;
        let mut r = (n as f64 * (-p).ln_1p()).exp();
        let mut u = self.next_f64();
        let mut x: u64 = 0;
        while u > r {
            u -= r;
            x += 1;
            if x >= n || x > 1000 {
                return x.min(n);
            }
            r *= a / x as f64 - s;
        }
        x
    }

    /// Transformed rejection with squeeze (Hormann 1993). For p <= 0.5,
    /// n * p >= 10 and n <= EXACT_BINOMIAL_LIMIT.
    fn binomial_btrs(&mut self, n: u64, p: f64) -> u64 {
        let nf = n as f64;
        let q = 1.0 - p;
        let spq = (nf * p * q).sqrt();
        let b = 1.15 + 2.53 * spq;
        let a = -0.0873 + 0.0248 * b + 0.01 * p;
        let c = nf * p + 0.5;
        let v_r = 0.92 - 4.2 / b;
        let alpha = (2.83 + 5.1 / b) * spq;
        let lpq = (p / q).ln();
        let m = ((nf + 1.0) * p).floor();
        let h = ln_factorial(m) + ln_factorial(nf - m);
        loop {
            let u = self.next_f64() - 0.5;
            let v = self.next_f64_open();
            let us = 0.5 - u.abs();
            let k = ((2.0 * a / us + b) * u + c).floor();
            if k < 0.0 || k > nf {
                continue;
            }
            if us >= 0.07 && v <= v_r {
                return k as u64;
            }
            let v = (v * alpha / (a / (us * us) + b)).ln();
            if v <= h - ln_factorial(k) - ln_factorial(nf - k) + (k - m) * lpq {
                return k as u64;
            }
        }
    }

    /// Splits `n` items over categories with the given probabilities, writing
    /// the counts into `counts`. The probabilities need not sum to exactly 1;
    /// they are used as relative weights. If the slices differ in length, only
    /// the shorter length is filled. The last category receives the remainder.
    pub fn multinomial(&mut self, n: u64, probabilities: &[f64], counts: &mut [u64]) {
        let len = probabilities.len().min(counts.len());
        if len == 0 {
            return;
        }
        // tail[i] is the total weight of categories i.. , summed from the end.
        let mut tail = vec![0.0; len + 1];
        for i in (0..len).rev() {
            tail[i] = tail[i + 1] + probabilities[i].max(0.0);
        }
        let mut remaining = n;
        for i in 0..len {
            if i + 1 == len {
                counts[i] = remaining;
                break;
            }
            let conditional = if tail[i] > 0.0 {
                (probabilities[i].max(0.0) / tail[i]).min(1.0)
            } else {
                0.0
            };
            let k = self.binomial(remaining, conditional);
            counts[i] = k;
            remaining -= k;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Reference outputs from the rand_xoshiro crate (SplitMix64 seeding).
    const REFERENCE: [(u64, [u64; 5]); 5] = [
        (
            0,
            [
                0x53175D61490B23DF,
                0x61DA6F3DC380D507,
                0x5C0FDF91EC9A7BFC,
                0x02EEBF8C3BBE5E1A,
                0x7ECA04EBAF4A5EEA,
            ],
        ),
        (
            1,
            [
                0xCFC5D07F6F03C29B,
                0xBF424132963FE08D,
                0x19A37D5757AAF520,
                0xBF08119F05CD56D6,
                0x2F47184B86186FA4,
            ],
        ),
        (
            42,
            [
                0xD0764D4F4476689F,
                0x519E4174576F3791,
                0xFBE07CFB0C24ED8C,
                0xB37D9F600CD835B8,
                0xCB231C3874846A73,
            ],
        ),
        (
            0xDEAD_BEEF_CAFE_F00D,
            [
                0x25945A605E7055A9,
                0x3948323EF9775D55,
                0xCB4E90AD7CF1678A,
                0xEC5C7DAEF7B039EB,
                0xA70941145C995825,
            ],
        ),
        (
            u64::MAX,
            [
                0x56CCF8CE948E27B2,
                0xE68588432E5A5B90,
                0xE3E9B5A48119CA8B,
                0x460F19495532AE73,
                0xA7D62040EA9263E1,
            ],
        ),
    ];

    #[test]
    fn splitmix64_matches_the_reference() {
        let mut state = 0;
        assert_eq!(splitmix64(&mut state), 0xE220A8397B1DCDAF);
        assert_eq!(splitmix64(&mut state), 0x6E789E6AA1B965F4);
        assert_eq!(splitmix64(&mut state), 0x06C45D188009454F);
    }

    #[test]
    fn stream_matches_the_reference_for_five_seeds() {
        for (seed, expected) in REFERENCE {
            let mut rng = Rng::new(seed);
            for (i, want) in expected.iter().enumerate() {
                assert_eq!(rng.next_u64(), *want, "seed {seed:#x}, output {i}");
            }
        }
    }

    #[test]
    fn state_round_trip_continues_the_stream() {
        let mut a = Rng::new(7);
        a.next_u64();
        let mut b = Rng::from_state(a.state()).unwrap();
        for _ in 0..10 {
            assert_eq!(a.next_u64(), b.next_u64());
        }
        assert_eq!(Rng::from_state([0; 4]), None);
    }

    #[test]
    fn unit_floats_stay_in_range() {
        let mut rng = Rng::new(3);
        for _ in 0..10_000 {
            let x = rng.next_f64();
            assert!((0.0..1.0).contains(&x));
            let y = rng.next_f64_open();
            assert!(y > 0.0 && y <= 1.0);
        }
    }

    #[test]
    fn below_covers_the_range_evenly() {
        let mut rng = Rng::new(11);
        let mut seen = [0u32; 6];
        for _ in 0..60_000 {
            seen[rng.below(6) as usize] += 1;
        }
        for count in seen {
            assert!((9_000..11_000).contains(&count), "{seen:?}");
        }
        assert_eq!(rng.below(0), 0);
        assert_eq!(rng.below(1), 0);
    }

    #[test]
    fn ln_factorial_matches_known_values() {
        assert!((ln_factorial(10.0) - 15.104_412_573_075_516).abs() < 1e-12);
        assert!((ln_factorial(29.0) - 71.257_038_967_168_01).abs() < 1e-10);
        assert!((ln_factorial(30.0) - 74.658_236_348_830_16).abs() < 1e-10);
        assert!((ln_factorial(100.0) - 363.739_375_555_563_5).abs() < 1e-9);
        // 1e6! from Stirling to full f64 precision
        assert!((ln_factorial(1_000_000.0) - 12_815_518.384_658_71).abs() < 1e-5);
    }

    #[test]
    fn binomial_edge_cases() {
        let mut rng = Rng::new(1);
        assert_eq!(rng.binomial(0, 0.5), 0);
        assert_eq!(rng.binomial(100, 0.0), 0);
        assert_eq!(rng.binomial(100, -1.0), 0);
        assert_eq!(rng.binomial(100, f64::NAN), 0);
        assert_eq!(rng.binomial(100, 1.0), 100);
        assert_eq!(rng.binomial(100, 7.0), 100);
    }

    /// Mean and variance of many draws against the exact values, within 5
    /// standard errors. The seed is fixed, so the outcome never changes.
    #[test]
    fn binomial_mean_and_variance_match_theory() {
        // (n, p): inversion, BTRS, flipped p, large n, tiny p with huge n
        let cases = [
            (50u64, 0.1f64),
            (1_000, 0.3),
            (1_000, 0.97),
            (1_000_000, 0.001),
            (1_000_000_000, 0.5),
            (1_000_000_000_000, 3e-12),
        ];
        let draws = 40_000usize;
        let mut rng = Rng::new(2026);
        for (n, p) in cases {
            let mean = n as f64 * p;
            let var = mean * (1.0 - p);
            let xs: Vec<f64> = (0..draws).map(|_| rng.binomial(n, p) as f64).collect();
            let m = xs.iter().sum::<f64>() / draws as f64;
            let v = xs.iter().map(|x| (x - m) * (x - m)).sum::<f64>() / (draws as f64 - 1.0);
            let se_mean = (var / draws as f64).sqrt();
            assert!(
                (m - mean).abs() < 5.0 * se_mean,
                "n={n} p={p}: mean {m} vs {mean}"
            );
            // relative standard error of a variance estimate is about sqrt(2/(N-1))
            // for near-normal data; allow 5 of them (kurtosis adds a little)
            let tol = 5.0 * (2.0 / (draws as f64 - 1.0)).sqrt() * var + 0.05 * var.min(1.0);
            assert!(
                (v - var).abs() < tol.max(0.05 * var),
                "n={n} p={p}: variance {v} vs {var}"
            );
        }
    }

    #[test]
    fn multinomial_conserves_the_total_and_follows_the_weights() {
        let mut rng = Rng::new(5);
        let probabilities = [0.5, 0.0, 0.3, 0.2];
        let mut counts = [0u64; 4];
        let mut totals = [0u64; 4];
        for _ in 0..2_000 {
            rng.multinomial(1_000, &probabilities, &mut counts);
            assert_eq!(counts.iter().sum::<u64>(), 1_000);
            assert_eq!(counts[1], 0);
            for (t, c) in totals.iter_mut().zip(counts) {
                *t += c;
            }
        }
        let n = 2_000.0 * 1_000.0;
        for (i, p) in probabilities.iter().enumerate() {
            let expected = n * p;
            let sd = (n * p * (1.0 - p)).sqrt();
            assert!(
                (totals[i] as f64 - expected).abs() <= 5.0 * sd,
                "category {i}"
            );
        }
    }

    #[test]
    fn multinomial_handles_degenerate_inputs() {
        let mut rng = Rng::new(9);
        let mut counts = [7u64; 3];
        rng.multinomial(10, &[1.0], &mut counts);
        assert_eq!(counts[0], 10);
        let mut counts = [0u64; 2];
        rng.multinomial(0, &[0.5, 0.5], &mut counts);
        assert_eq!(counts, [0, 0]);
        rng.multinomial(5, &[], &mut []);
    }
}
