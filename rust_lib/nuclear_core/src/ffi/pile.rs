// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "ffi/pile.rs"
// ============================================================================
//! Piles of atoms over the C interface: whole-atom [`NucSample`] and
//! fractional [`NucAmounts`], with time steps and the three reactions.
//!
//! A pile does not own a random number generator or any cache. Each call that
//! needs them takes a [`NucContext`], so many piles share one context and its
//! caches, and a game that wants reproducible runs controls one seed.

use super::*;
use crate::neutron::Branching;
use crate::nuclide::Nuclide;
use crate::react::{AmountsReactionReport, ReactionReport};
use crate::sample::{Amounts, AmountsReport, Sample, StepReport};

/// A pile of whole atoms. Opaque to C.
pub struct NucSample {
    inner: Sample,
}

/// A pile of fractional amounts. Opaque to C.
pub struct NucAmounts {
    inner: Amounts,
}

unsafe fn sample_ref<'a>(p: *mut NucSample) -> Result<&'a mut NucSample, i32> {
    match p.as_mut() {
        Some(s) => Ok(s),
        None => Err(fail(NUC_ERR_NULL, "sample handle is null")),
    }
}

unsafe fn amounts_ref<'a>(p: *mut NucAmounts) -> Result<&'a mut NucAmounts, i32> {
    match p.as_mut() {
        Some(s) => Ok(s),
        None => Err(fail(NUC_ERR_NULL, "amounts handle is null")),
    }
}

fn step_report(r: &StepReport) -> NucStepReport {
    NucStepReport {
        energy_released_kev: r.energy_released_kev,
        atoms_changed: r.atoms_changed,
        new_pending_fissions: r.new_pending_fissions,
        energy_known: u8::from(r.energy_known),
        reserved: [0; 7],
    }
}

fn amounts_step_report(r: &AmountsReport) -> NucAmountsStepReport {
    NucAmountsStepReport {
        energy_released_kev: r.energy_released_kev,
        amount_changed: r.amount_changed,
        new_pending: r.new_pending,
        energy_known: u8::from(r.energy_known),
        reserved: [0; 7],
    }
}

fn reaction_report(r: &ReactionReport) -> NucReactionReport {
    NucReactionReport {
        energy_released_kev: r.energy_released_kev,
        fissions: r.fissions,
        captures: r.captures,
        fusions: r.fusions,
        neutrons_released: r.neutrons_released,
        unresolved: r.unresolved,
        energy_known: u8::from(r.energy_known),
        reserved: [0; 7],
    }
}

fn amounts_reaction_report(r: &AmountsReactionReport) -> NucAmountsReactionReport {
    NucAmountsReactionReport {
        energy_released_kev: r.energy_released_kev,
        fissions: r.fissions,
        captures: r.captures,
        fusions: r.fusions,
        neutrons_released: r.neutrons_released,
        unresolved: r.unresolved,
        energy_known: u8::from(r.energy_known),
        reserved: [0; 7],
    }
}

fn counts(items: impl Iterator<Item = (Nuclide, u64)>) -> Vec<NucCount> {
    items
        .map(|(n, c)| NucCount {
            key: n.key(),
            reserved: 0,
            count: c,
        })
        .collect()
}

fn amounts(items: impl Iterator<Item = (Nuclide, f64)>) -> Vec<NucAmount> {
    items
        .map(|(n, a)| NucAmount {
            key: n.key(),
            reserved: 0,
            amount: a,
        })
        .collect()
}

fn branching(fission_probability: f64) -> Result<Branching, i32> {
    Branching::new(fission_probability).ok_or_else(|| {
        fail(
            NUC_ERR_INVALID,
            format!("{fission_probability} is not a probability"),
        )
    })
}

// ----------------------------------------------------------------------------
// Sample
// ----------------------------------------------------------------------------

/// Creates an empty pile of whole atoms. Free it with [`nuc_sample_destroy`].
#[no_mangle]
pub extern "C" fn nuc_sample_create() -> *mut NucSample {
    Box::into_raw(Box::new(NucSample {
        inner: Sample::new(),
    }))
}

/// Frees a pile. Null is a no-op.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_destroy(sample: *mut NucSample) {
    if !sample.is_null() {
        drop(Box::from_raw(sample));
    }
}

/// Copies a pile, pending fissions and elapsed time included. Returns null
/// for a null handle. Free the copy with [`nuc_sample_destroy`].
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_clone(sample: *mut NucSample) -> *mut NucSample {
    match sample.as_ref() {
        Some(s) => Box::into_raw(Box::new(NucSample {
            inner: s.inner.clone(),
        })),
        None => core::ptr::null_mut(),
    }
}

/// Adds `count` atoms of a nuclide.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_add(sample: *mut NucSample, key: u32, count: u64) -> i32 {
    guard(|| match sample_ref(sample) {
        Ok(s) => match s.inner.add(Nuclide::from_key(key), count) {
            Ok(()) => NUC_OK,
            Err(e) => sample_error(&e),
        },
        Err(code) => code,
    })
}

/// Writes the number of atoms of a nuclide.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_count(sample: *mut NucSample, key: u32, out: *mut u64) -> i32 {
    guard(|| match sample_ref(sample) {
        Ok(s) => put(out, s.inner.count(Nuclide::from_key(key))),
        Err(code) => code,
    })
}

/// Lists every nuclide in the pile with its number of atoms, in ascending
/// nuclide order.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_counts(
    sample: *mut NucSample,
    out: *mut NucCount,
    cap: i32,
    count: *mut i32,
) -> i32 {
    guard(|| match sample_ref(sample) {
        Ok(s) => put_list(&counts(s.inner.iter()), out, cap, count),
        Err(code) => code,
    })
}

/// Lists the atoms waiting for fission products, keyed by the nucleus that is
/// waiting.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_pending(
    sample: *mut NucSample,
    out: *mut NucCount,
    cap: i32,
    count: *mut i32,
) -> i32 {
    guard(|| match sample_ref(sample) {
        Ok(s) => put_list(&counts(s.inner.pending_fissions()), out, cap, count),
        Err(code) => code,
    })
}

/// Writes the simulated seconds since the pile was created.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_elapsed_seconds(sample: *mut NucSample, out: *mut f64) -> i32 {
    guard(|| match sample_ref(sample) {
        Ok(s) => put(out, s.inner.elapsed_seconds()),
        Err(code) => code,
    })
}

/// Writes the number of atoms (not counting pending fissions) and the nucleon
/// count (pending fissions included). Fails with `NUC_ERR_OVERFLOW` if either
/// passes `u64::MAX`.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_totals(sample: *mut NucSample, out: *mut NucTotals) -> i32 {
    guard(|| match sample_ref(sample) {
        Ok(s) => {
            match (
                u64::try_from(s.inner.total_atoms()),
                u64::try_from(s.inner.baryon_number()),
            ) {
                (Ok(atoms), Ok(baryons)) => put(out, NucTotals { atoms, baryons }),
                _ => fail(NUC_ERR_OVERFLOW, "a total is larger than a 64-bit count"),
            }
        }
        Err(code) => code,
    })
}

/// Advances the pile by `seconds`, drawing from the context's generator. The
/// same seed and the same calls give the same result. On failure the pile is
/// unchanged.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_advance(
    ctx: *mut NucContext,
    sample: *mut NucSample,
    seconds: f64,
    out: *mut NucStepReport,
) -> i32 {
    guard(|| {
        let (c, s) = match (context(ctx), sample_ref(sample)) {
            (Ok(c), Ok(s)) => (c, s),
            (Err(code), _) | (_, Err(code)) => return code,
        };
        match s.inner.advance(seconds, &mut c.rng, &mut c.propagator) {
            Ok(r) => put(out, step_report(&r)),
            Err(e) => sample_error(&e),
        }
    })
}

/// Turns every pending fission into fission products.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_resolve_fissions(
    ctx: *mut NucContext,
    sample: *mut NucSample,
    out: *mut NucReactionReport,
) -> i32 {
    guard(|| {
        let (c, s) = match (context(ctx), sample_ref(sample)) {
            (Ok(c), Ok(s)) => (c, s),
            (Err(code), _) | (_, Err(code)) => return code,
        };
        match s.inner.resolve_fissions(&mut c.fission, &mut c.rng) {
            Ok(r) => put(out, reaction_report(&r)),
            Err(e) => sample_error(&e),
        }
    })
}

/// Sends `neutrons` neutrons of `energy_ev` at atoms of the target, each into
/// a different atom. `fission_probability` is the chance an absorption ends in
/// fission, from [`query::nuc_neutron_branching`] or the caller's own number.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_irradiate(
    ctx: *mut NucContext,
    sample: *mut NucSample,
    target: u32,
    neutrons: u64,
    energy_ev: f64,
    fission_probability: f64,
    out: *mut NucReactionReport,
) -> i32 {
    guard(|| {
        let (c, s) = match (context(ctx), sample_ref(sample)) {
            (Ok(c), Ok(s)) => (c, s),
            (Err(code), _) | (_, Err(code)) => return code,
        };
        let b = match branching(fission_probability) {
            Ok(b) => b,
            Err(code) => return code,
        };
        match s.inner.irradiate(
            Nuclide::from_key(target),
            neutrons,
            energy_ev,
            b,
            &mut c.fission,
            &mut c.rng,
        ) {
            Ok(r) => put(out, reaction_report(&r)),
            Err(e) => sample_error(&e),
        }
    })
}

/// Fuses `events` pairs of two light nuclei at a temperature in keV.
#[no_mangle]
pub unsafe extern "C" fn nuc_sample_fuse(
    ctx: *mut NucContext,
    sample: *mut NucSample,
    a: u32,
    b: u32,
    events: u64,
    t_kev: f64,
    out: *mut NucReactionReport,
) -> i32 {
    guard(|| {
        let (c, s) = match (context(ctx), sample_ref(sample)) {
            (Ok(c), Ok(s)) => (c, s),
            (Err(code), _) | (_, Err(code)) => return code,
        };
        match s.inner.fuse(
            Nuclide::from_key(a),
            Nuclide::from_key(b),
            events,
            t_kev,
            &mut c.rng,
        ) {
            Ok(r) => put(out, reaction_report(&r)),
            Err(e) => sample_error(&e),
        }
    })
}

// ----------------------------------------------------------------------------
// Amounts
// ----------------------------------------------------------------------------

/// Creates an empty pile of fractional amounts. Free it with
/// [`nuc_amounts_destroy`].
#[no_mangle]
pub extern "C" fn nuc_amounts_create() -> *mut NucAmounts {
    Box::into_raw(Box::new(NucAmounts {
        inner: Amounts::new(),
    }))
}

/// Frees a pile. Null is a no-op.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_destroy(amounts: *mut NucAmounts) {
    if !amounts.is_null() {
        drop(Box::from_raw(amounts));
    }
}

/// Copies a pile. Returns null for a null handle. Free the copy with
/// [`nuc_amounts_destroy`].
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_clone(amounts: *mut NucAmounts) -> *mut NucAmounts {
    match amounts.as_ref() {
        Some(s) => Box::into_raw(Box::new(NucAmounts {
            inner: s.inner.clone(),
        })),
        None => core::ptr::null_mut(),
    }
}

/// Adds an amount of a nuclide. The amount must be finite and not negative.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_add(amounts: *mut NucAmounts, key: u32, amount: f64) -> i32 {
    guard(|| match amounts_ref(amounts) {
        Ok(a) => {
            if !(amount.is_finite() && amount >= 0.0) {
                return fail(NUC_ERR_INVALID, format!("{amount} is not a usable amount"));
            }
            a.inner.add(Nuclide::from_key(key), amount);
            NUC_OK
        }
        Err(code) => code,
    })
}

/// Writes the amount of a nuclide.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_amount(
    amounts: *mut NucAmounts,
    key: u32,
    out: *mut f64,
) -> i32 {
    guard(|| match amounts_ref(amounts) {
        Ok(a) => put(out, a.inner.amount(Nuclide::from_key(key))),
        Err(code) => code,
    })
}

/// Lists every nuclide in the pile with its amount, in ascending nuclide order.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_list(
    amounts: *mut NucAmounts,
    out: *mut NucAmount,
    cap: i32,
    count: *mut i32,
) -> i32 {
    guard(|| match amounts_ref(amounts) {
        Ok(a) => put_list(&self::amounts(a.inner.iter()), out, cap, count),
        Err(code) => code,
    })
}

/// Lists the amounts waiting for fission products, keyed by the nucleus that
/// is waiting.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_pending(
    amounts: *mut NucAmounts,
    out: *mut NucAmount,
    cap: i32,
    count: *mut i32,
) -> i32 {
    guard(|| match amounts_ref(amounts) {
        Ok(a) => put_list(&self::amounts(a.inner.pending_fissions()), out, cap, count),
        Err(code) => code,
    })
}

/// Writes the simulated seconds since the pile was created.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_elapsed_seconds(
    amounts: *mut NucAmounts,
    out: *mut f64,
) -> i32 {
    guard(|| match amounts_ref(amounts) {
        Ok(a) => put(out, a.inner.elapsed_seconds()),
        Err(code) => code,
    })
}

/// Writes the nucleon count, pending fissions included.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_baryon_number(amounts: *mut NucAmounts, out: *mut f64) -> i32 {
    guard(|| match amounts_ref(amounts) {
        Ok(a) => put(out, a.inner.baryon_number()),
        Err(code) => code,
    })
}

/// Advances the pile by `seconds` with the exact expected change. No random
/// numbers are used, but the context's decay cache is.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_advance(
    ctx: *mut NucContext,
    amounts: *mut NucAmounts,
    seconds: f64,
    out: *mut NucAmountsStepReport,
) -> i32 {
    guard(|| {
        let (c, a) = match (context(ctx), amounts_ref(amounts)) {
            (Ok(c), Ok(a)) => (c, a),
            (Err(code), _) | (_, Err(code)) => return code,
        };
        match a.inner.advance(seconds, &mut c.propagator) {
            Ok(r) => put(out, amounts_step_report(&r)),
            Err(e) => sample_error(&e),
        }
    })
}

/// Turns every pending fission into the expected fission products.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_resolve_fissions(
    ctx: *mut NucContext,
    amounts: *mut NucAmounts,
    out: *mut NucAmountsReactionReport,
) -> i32 {
    guard(|| {
        let (c, a) = match (context(ctx), amounts_ref(amounts)) {
            (Ok(c), Ok(a)) => (c, a),
            (Err(code), _) | (_, Err(code)) => return code,
        };
        match a.inner.resolve_fissions(&mut c.fission) {
            Ok(r) => put(out, amounts_reaction_report(&r)),
            Err(e) => sample_error(&e),
        }
    })
}

/// Sends an amount of neutrons of `energy_ev` at the target and applies the
/// expected result.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_irradiate(
    ctx: *mut NucContext,
    amounts: *mut NucAmounts,
    target: u32,
    neutrons: f64,
    energy_ev: f64,
    fission_probability: f64,
    out: *mut NucAmountsReactionReport,
) -> i32 {
    guard(|| {
        let (c, a) = match (context(ctx), amounts_ref(amounts)) {
            (Ok(c), Ok(a)) => (c, a),
            (Err(code), _) | (_, Err(code)) => return code,
        };
        let b = match branching(fission_probability) {
            Ok(b) => b,
            Err(code) => return code,
        };
        match a.inner.irradiate(
            Nuclide::from_key(target),
            neutrons,
            energy_ev,
            b,
            &mut c.fission,
        ) {
            Ok(r) => put(out, amounts_reaction_report(&r)),
            Err(e) => sample_error(&e),
        }
    })
}

/// Fuses an amount of pairs of two light nuclei at a temperature in keV and
/// applies the expected result.
#[no_mangle]
pub unsafe extern "C" fn nuc_amounts_fuse(
    amounts: *mut NucAmounts,
    a: u32,
    b: u32,
    events: f64,
    t_kev: f64,
    out: *mut NucAmountsReactionReport,
) -> i32 {
    guard(|| match amounts_ref(amounts) {
        Ok(p) => match p
            .inner
            .fuse(Nuclide::from_key(a), Nuclide::from_key(b), events, t_kev)
        {
            Ok(r) => put(out, amounts_reaction_report(&r)),
            Err(e) => sample_error(&e),
        },
        Err(code) => code,
    })
}
