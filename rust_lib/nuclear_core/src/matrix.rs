// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "matrix.rs"
// ============================================================================
//! Dense matrix exponential for small square matrices.
//!
//! Decay propagation needs `exp(Q * t)` for a generator matrix `Q` whose
//! entries span many orders of magnitude, so the method must be stable on stiff
//! input. This is the scaling and squaring algorithm with a degree-13 Pade
//! approximant (Higham 2005). Matrices are row-major `Vec<f64>` of size `n * n`.

const THETA_13: f64 = 5.371_920_351_148_152;

const B: [f64; 14] = [
    64_764_752_532_480_000.0,
    32_382_376_266_240_000.0,
    7_771_770_303_897_600.0,
    1_187_353_796_428_800.0,
    129_060_195_264_000.0,
    10_559_470_521_600.0,
    670_442_572_800.0,
    33_522_128_640.0,
    1_323_241_920.0,
    40_840_800.0,
    960_960.0,
    16_380.0,
    182.0,
    1.0,
];

fn identity(n: usize) -> Vec<f64> {
    let mut m = vec![0.0; n * n];
    for i in 0..n {
        m[i * n + i] = 1.0;
    }
    m
}

/// Product of two n by n matrices, using the cache-friendly i-k-j order.
fn multiply(n: usize, a: &[f64], b: &[f64]) -> Vec<f64> {
    let mut out = vec![0.0; n * n];
    for i in 0..n {
        let row = &mut out[i * n..(i + 1) * n];
        for k in 0..n {
            let aik = a[i * n + k];
            if aik == 0.0 {
                continue;
            }
            let b_row = &b[k * n..(k + 1) * n];
            for (o, bkj) in row.iter_mut().zip(b_row) {
                *o += aik * bkj;
            }
        }
    }
    out
}

/// Largest absolute column sum.
fn one_norm(n: usize, a: &[f64]) -> f64 {
    (0..n)
        .map(|j| (0..n).map(|i| a[i * n + j].abs()).sum::<f64>())
        .fold(0.0, f64::max)
}

/// Solves `A X = B` for X by LU decomposition with partial pivoting.
/// Returns a matrix of NaN when A is singular.
fn solve(n: usize, mut a: Vec<f64>, mut b: Vec<f64>) -> Vec<f64> {
    for col in 0..n {
        let mut pivot = col;
        for row in col + 1..n {
            if a[row * n + col].abs() > a[pivot * n + col].abs() {
                pivot = row;
            }
        }
        if a[pivot * n + col] == 0.0 {
            return vec![f64::NAN; n * n];
        }
        if pivot != col {
            for j in 0..n {
                a.swap(col * n + j, pivot * n + j);
                b.swap(col * n + j, pivot * n + j);
            }
        }
        let diagonal = a[col * n + col];
        for row in col + 1..n {
            let factor = a[row * n + col] / diagonal;
            if factor == 0.0 {
                continue;
            }
            for j in col..n {
                a[row * n + j] -= factor * a[col * n + j];
            }
            for j in 0..n {
                b[row * n + j] -= factor * b[col * n + j];
            }
        }
    }
    for col in (0..n).rev() {
        for j in 0..n {
            let mut sum = b[col * n + j];
            for k in col + 1..n {
                sum -= a[col * n + k] * b[k * n + j];
            }
            b[col * n + j] = sum / a[col * n + col];
        }
    }
    b
}

fn combine(n: usize, terms: &[(f64, &[f64])]) -> Vec<f64> {
    let mut out = vec![0.0; n * n];
    for (weight, matrix) in terms {
        for (o, m) in out.iter_mut().zip(matrix.iter()) {
            *o += weight * m;
        }
    }
    out
}

/// `exp(a) - I` for the row-major `n` by `n` matrix `a`, computed so that small
/// entries keep their relative accuracy.
///
/// A generator matrix mixes tiny and huge rates. Scaling and squaring on
/// `exp(a)` itself rounds `1 - 1e-24` to 1 and loses a slow decay completely.
/// Working with `E = exp(a) - I` avoids that. The Pade step gives
/// `E = 2 (V - U)^-1 U` with no subtraction, and squaring uses
/// `exp(2a) - I = E^2 + 2E`, which never adds 1 to a small number.
///
/// Returns a matrix of NaN if `a` has a non-finite entry, and an empty vector
/// if `a.len()` is not `n * n`.
pub fn expm_minus_identity(n: usize, a: &[f64]) -> Vec<f64> {
    if a.len() != n * n || n == 0 {
        return Vec::new();
    }
    if a.iter().any(|x| !x.is_finite()) {
        return vec![f64::NAN; n * n];
    }
    let norm = one_norm(n, a);
    let squarings = if norm > THETA_13 {
        (norm / THETA_13).log2().ceil().max(0.0) as i32
    } else {
        0
    };
    let scale = (-f64::from(squarings)).exp2();
    let a1: Vec<f64> = a.iter().map(|x| x * scale).collect();
    let a2 = multiply(n, &a1, &a1);
    let a4 = multiply(n, &a2, &a2);
    let a6 = multiply(n, &a4, &a2);
    let eye = identity(n);

    let inner_u = combine(n, &[(B[13], &a6), (B[11], &a4), (B[9], &a2)]);
    let u_poly = combine(
        n,
        &[
            (1.0, &multiply(n, &a6, &inner_u)),
            (B[7], &a6),
            (B[5], &a4),
            (B[3], &a2),
            (B[1], &eye),
        ],
    );
    let u = multiply(n, &a1, &u_poly);

    let inner_v = combine(n, &[(B[12], &a6), (B[10], &a4), (B[8], &a2)]);
    let v = combine(
        n,
        &[
            (1.0, &multiply(n, &a6, &inner_v)),
            (B[6], &a6),
            (B[4], &a4),
            (B[2], &a2),
            (B[0], &eye),
        ],
    );

    // exp(a1) - I = (V - U)^-1 (V + U) - I = 2 (V - U)^-1 U
    let denominator = combine(n, &[(1.0, &v), (-1.0, &u)]);
    let two_u = combine(n, &[(2.0, &u)]);
    let mut e = solve(n, denominator, two_u);
    for _ in 0..squarings {
        let square = multiply(n, &e, &e);
        e = combine(n, &[(1.0, &square), (2.0, &e)]);
    }
    e
}

/// Matrix exponential of the row-major `n` by `n` matrix `a`.
///
/// Absolute accuracy near machine precision. When tiny entries matter, use
/// [`expm_minus_identity`], which keeps their relative accuracy. Returns a
/// matrix of NaN if `a` has a non-finite entry, and an empty vector if
/// `a.len()` is not `n * n`.
pub fn expm(n: usize, a: &[f64]) -> Vec<f64> {
    let mut e = expm_minus_identity(n, a);
    for i in 0..e.len() / n.max(1) {
        e[i * n + i] += 1.0;
    }
    e
}

#[cfg(test)]
mod tests {
    use super::*;

    fn assert_close(actual: &[f64], expected: &[f64], tolerance: f64) {
        assert_eq!(actual.len(), expected.len());
        for (i, (a, e)) in actual.iter().zip(expected).enumerate() {
            assert!((a - e).abs() <= tolerance, "entry {i}: {a} vs {e}");
        }
    }

    #[test]
    fn exponential_of_zero_is_the_identity() {
        assert_close(&expm(3, &[0.0; 9]), &identity(3), 1e-15);
    }

    #[test]
    fn exponential_of_a_diagonal_matrix() {
        let a = [-1.0, 0.0, 0.0, 0.0, 2.0, 0.0, 0.0, 0.0, -30.0];
        let expected = [
            (-1.0f64).exp(),
            0.0,
            0.0,
            0.0,
            2.0f64.exp(),
            0.0,
            0.0,
            0.0,
            (-30.0f64).exp(),
        ];
        assert_close(&expm(3, &a), &expected, 1e-14);
    }

    #[test]
    fn exponential_of_a_nilpotent_matrix() {
        // exp([[0, 1], [0, 0]]) = [[1, 1], [0, 1]]
        assert_close(
            &expm(2, &[0.0, 1.0, 0.0, 0.0]),
            &[1.0, 1.0, 0.0, 1.0],
            1e-15,
        );
    }

    #[test]
    fn two_member_decay_chain_matches_the_closed_form() {
        let (a, t) = (0.7, 3.0);
        let q = [-a * t, a * t, 0.0, 0.0];
        let p = expm(2, &q);
        let survive = (-a * t).exp();
        assert_close(&p, &[survive, 1.0 - survive, 0.0, 1.0], 1e-15);
    }

    #[test]
    fn three_member_chain_matches_the_bateman_solution() {
        let (a, b, t) = (0.4, 1.3, 2.5);
        // states: 0 -> 1 -> 2 (absorbing)
        let q = [-a * t, a * t, 0.0, 0.0, -b * t, b * t, 0.0, 0.0, 0.0];
        let p = expm(3, &q);
        let p00 = (-a * t).exp();
        let p01 = a / (b - a) * ((-a * t).exp() - (-b * t).exp());
        assert_close(&p[0..3], &[p00, p01, 1.0 - p00 - p01], 1e-14);
    }

    /// Generator with rates from 1e-12 to 1e12 per second over 1e6 seconds.
    /// The slow decay out of state 0 is 1e-6; plain scaling and squaring would
    /// round it to zero.
    #[test]
    fn stiff_generator_keeps_small_entries_accurate() {
        let t = 1e6;
        let (a, b) = (1e-12, 1e12);
        let q = [-a * t, a * t, 0.0, 0.0, -b * t, b * t, 0.0, 0.0, 0.0];
        let e = expm_minus_identity(3, &q);
        let leave = (-a * t).exp_m1(); // exact E[0][0], about -1e-6
        assert!(
            ((e[0] - leave) / leave).abs() < 1e-12,
            "{} vs {leave}",
            e[0]
        );
        // state 1 holds a/(b-a) = 1e-24 of the probability at most
        assert!(e[1] > 0.0 && e[1] < 2e-24, "{}", e[1]);
        // what left state 0 went on to state 2
        assert!(
            ((e[2] + leave) / -leave).abs() < 1e-12,
            "{} vs {}",
            e[2],
            -leave
        );
        let p = expm(3, &q);
        for row in 0..3 {
            let sum: f64 = p[row * 3..row * 3 + 3].iter().sum();
            assert!((sum - 1.0).abs() < 1e-14, "row {row}: {sum}");
        }
    }

    #[test]
    fn bad_input_is_reported_not_panicked() {
        assert!(expm(2, &[0.0; 3]).is_empty());
        assert!(expm(0, &[]).is_empty());
        assert!(expm(2, &[0.0, f64::NAN, 0.0, 0.0])
            .iter()
            .all(|x| x.is_nan()));
    }
}
