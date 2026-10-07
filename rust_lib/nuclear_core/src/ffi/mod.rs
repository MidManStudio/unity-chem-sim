// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "ffi/mod.rs"
// ============================================================================
//! C interface to nuclear_core for Unity and other hosts.
//!
//! Every function is `nuc_` plus a name. The library is `nuclear_core`.
//!
//! # Conventions
//!
//! * **Handles.** [`NucContext`], [`pile::NucSample`] and [`pile::NucAmounts`]
//!   are opaque. A `*_create` function returns one, the matching `*_destroy`
//!   frees it, and destroying null does nothing. Destroy each handle exactly
//!   once and never use it afterward.
//! * **Status codes.** A function that can fail returns an `i32`: 0 for success
//!   and a negative `NUC_ERR_*` value otherwise. After a failure
//!   [`nuc_last_error`] copies a text description of the most recent failure on
//!   the calling thread. A successful call leaves it alone.
//! * **Nuclides** cross the boundary as one `u32` key, protons in the high 16
//!   bits and neutrons in the low 16 (`Nuclide::key`).
//! * **Lists.** A function that returns a list takes an output buffer, its
//!   capacity in elements, and a pointer for the total count. It always writes
//!   the total count and copies at most `capacity` elements, so call it with
//!   capacity 0 to size the buffer, then again to fill it. A capacity of 0
//!   accepts a null buffer.
//! * **Strings** cross as UTF-8 bytes with an explicit length and no
//!   terminator. A function that returns text writes at most `capacity - 1`
//!   bytes and a terminating zero, and returns the full length, as `snprintf`
//!   does.
//! * **Booleans** are `u8`, 0 or 1.
//! * **Structs** are `#[repr(C)]`, hold only fixed-width fields, and carry
//!   explicit reserved fields where C would add padding, so the layout does not
//!   depend on the target. [`nuc_struct_size`] gives the size of each one and
//!   [`nuc_layout_probe`] fills one with known values, so a host can check its
//!   own declaration field by field.
//! * **Panics** are caught at the boundary and returned as `NUC_ERR_PANIC`.
//!   The release profile aborts on panic instead, so a panic there ends the
//!   process.
//! * **Threads.** There is no global state apart from the per-thread last
//!   error, a fixed 255-byte buffer with no destructor. A handle must not be used from two threads at once. Handles that do
//!   not share a context can be used from different threads.
//!
//! # Safety
//!
//! Every pointer argument must be null or valid for the size the function
//! documents, and a handle must come from the matching `*_create` and not have
//! been destroyed. Null pointers are reported as `NUC_ERR_NULL` and never
//! dereferenced. The module opts out of the crate's `unsafe_code` lint, and it
//! is the only module that does.
#![allow(unsafe_code)]
#![allow(clippy::missing_safety_doc)]

use std::cell::RefCell;
use std::panic::{catch_unwind, AssertUnwindSafe};

use crate::fission::{Fission, FissionError};
use crate::lineage::{LineageError, Propagator};
use crate::rng::Rng;
use crate::sample::SampleError;

pub mod pile;
pub mod query;

/// Version of this C interface. It changes whenever a function or struct
/// changes in a way an older host would misread. A host should refuse to run
/// against a library with a different value.
pub const NUC_ABI_VERSION: i32 = 1;

/// Success.
pub const NUC_OK: i32 = 0;
/// A required pointer argument was null.
pub const NUC_ERR_NULL: i32 = -1;
/// An argument was out of range or not usable: a negative time, a bad energy,
/// an unparsable name, an unknown code.
pub const NUC_ERR_INVALID: i32 = -2;
/// The nuclide, or the data asked for it, is not in the tables.
pub const NUC_ERR_NOT_IN_TABLE: i32 = -3;
/// A count would pass `u64::MAX`.
pub const NUC_ERR_OVERFLOW: i32 = -4;
/// The pile holds less than the operation needs.
pub const NUC_ERR_NOT_ENOUGH: i32 = -5;
/// No fusion channel runs for this pair at this temperature.
pub const NUC_ERR_NO_REACTION: i32 = -6;
/// The nucleus has too few protons for the fission model.
pub const NUC_ERR_NOT_FISSIONABLE: i32 = -7;
/// A decay lineage is too large to solve.
pub const NUC_ERR_TOO_MANY_STATES: i32 = -8;
/// The library panicked. The handle involved may be in an unusable state.
pub const NUC_ERR_PANIC: i32 = -9;

/// Key value that means "no nuclide" in a field that can be empty.
pub const NUC_NO_NUCLIDE: u32 = u32::MAX;

/// Most emitted nuclei one decay outcome lists. A test over the whole chart
/// checks that none lists more.
pub const NUC_MAX_EMITTED: usize = 4;

/// Struct kind for [`nuc_struct_size`] and [`nuc_layout_probe`]: [`NucCount`].
pub const NUC_KIND_COUNT: i32 = 1;
/// Struct kind: [`NucAmount`].
pub const NUC_KIND_AMOUNT: i32 = 2;
/// Struct kind: [`NucEnergy`].
pub const NUC_KIND_ENERGY: i32 = 3;
/// Struct kind: [`NucBinding`].
pub const NUC_KIND_BINDING: i32 = 4;
/// Struct kind: [`NucHalfLife`].
pub const NUC_KIND_HALF_LIFE: i32 = 5;
/// Struct kind: [`NucTotals`].
pub const NUC_KIND_TOTALS: i32 = 6;
/// Struct kind: [`NucStepReport`].
pub const NUC_KIND_STEP_REPORT: i32 = 7;
/// Struct kind: [`NucAmountsStepReport`].
pub const NUC_KIND_AMOUNTS_STEP_REPORT: i32 = 8;
/// Struct kind: [`NucReactionReport`].
pub const NUC_KIND_REACTION_REPORT: i32 = 9;
/// Struct kind: [`NucAmountsReactionReport`].
pub const NUC_KIND_AMOUNTS_REACTION_REPORT: i32 = 10;
/// Struct kind: [`NucDecayOutcome`].
pub const NUC_KIND_DECAY_OUTCOME: i32 = 11;
/// Struct kind: [`NucChainStep`].
pub const NUC_KIND_CHAIN_STEP: i32 = 12;
/// Struct kind: [`NucChainEnd`].
pub const NUC_KIND_CHAIN_END: i32 = 13;
/// Struct kind: [`NucFissionSummary`].
pub const NUC_KIND_FISSION_SUMMARY: i32 = 14;
/// Struct kind: [`NucFissionChannel`].
pub const NUC_KIND_FISSION_CHANNEL: i32 = 15;
/// Struct kind: [`NucFusionChannel`].
pub const NUC_KIND_FUSION_CHANNEL: i32 = 16;

// ----------------------------------------------------------------------------
// Structs. Fields that C would pad are written out as `reserved`, so every
// offset is the same on every target. Sizes are fixed by the assertions below.
// ----------------------------------------------------------------------------

/// A nuclide and a whole number of atoms. 16 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct NucCount {
    /// Nuclide key. Offset 0.
    pub key: u32,
    /// Always 0. Offset 4.
    pub reserved: u32,
    /// Number of atoms. Offset 8.
    pub count: u64,
}

/// A nuclide and a fractional amount. 16 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucAmount {
    /// Nuclide key. Offset 0.
    pub key: u32,
    /// Always 0. Offset 4.
    pub reserved: u32,
    /// The amount. Offset 8.
    pub amount: f64,
}

/// An energy in keV and where it came from. 16 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucEnergy {
    /// Energy in keV. Offset 0.
    pub kev: f64,
    /// 0 measured, 1 estimated, 2 liquid drop fallback. Offset 8.
    pub source: i32,
    /// Always 0. Offset 12.
    pub reserved: i32,
}

/// Binding energy of a nuclide. 24 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucBinding {
    /// Total binding energy in keV. Offset 0.
    pub total_kev: f64,
    /// Binding energy per nucleon in keV. Offset 8.
    pub per_nucleon_kev: f64,
    /// 0 measured, 1 estimated, 2 liquid drop fallback. Offset 16.
    pub source: i32,
    /// Always 0. Offset 20.
    pub reserved: i32,
}

/// A ground-state half-life. 16 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucHalfLife {
    /// Half-life in seconds. 0 when the state carries no number. Offset 0.
    pub seconds: f64,
    /// 0 unknown, 1 stable, 2 particle unstable, 3 exact, 4 approximate,
    /// 5 at least, 6 at most. Offset 8.
    pub state: i32,
    /// 1 when NUBASE2020 estimates the value from trends. Offset 12.
    pub estimated: u8,
    /// Always 0. Offset 13.
    pub reserved: [u8; 3],
}

/// Totals of a pile of atoms. 16 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct NucTotals {
    /// Number of atoms, not counting pending fissions. Offset 0.
    pub atoms: u64,
    /// Nucleon count, pending fissions included. Offset 8.
    pub baryons: u64,
}

/// What one `advance` did to a [`pile::NucSample`]. 32 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucStepReport {
    /// Energy released in keV (total Q). Offset 0.
    pub energy_released_kev: f64,
    /// Atoms that are no longer the nuclide they started as. Offset 8.
    pub atoms_changed: u64,
    /// Atoms that moved to the pending fission list. Offset 16.
    pub new_pending_fissions: u64,
    /// 0 when a mass was missing, so the energy is a lower estimate. Offset 24.
    pub energy_known: u8,
    /// Always 0. Offset 25.
    pub reserved: [u8; 7],
}

/// What one `advance` did to a [`pile::NucAmounts`]. 32 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucAmountsStepReport {
    /// Energy released in keV per unit of amount. Offset 0.
    pub energy_released_kev: f64,
    /// Amount that is no longer the nuclide it started as. Offset 8.
    pub amount_changed: f64,
    /// Amount that moved to the pending fission list. Offset 16.
    pub new_pending: f64,
    /// 0 when a mass was missing. Offset 24.
    pub energy_known: u8,
    /// Always 0. Offset 25.
    pub reserved: [u8; 7],
}

/// What one reaction call did to a [`pile::NucSample`]. 56 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucReactionReport {
    /// Energy released in keV (total Q, before later decay). Offset 0.
    pub energy_released_kev: f64,
    /// Fission events. Offset 8.
    pub fissions: u64,
    /// Neutron captures. Offset 16.
    pub captures: u64,
    /// Fusion events. Offset 24.
    pub fusions: u64,
    /// Free neutrons added to the pile. Offset 32.
    pub neutrons_released: u64,
    /// Pending fissions left waiting because the nucleus is too light. Offset 40.
    pub unresolved: u64,
    /// 0 when a mass was missing. Offset 48.
    pub energy_known: u8,
    /// Always 0. Offset 49.
    pub reserved: [u8; 7],
}

/// What one reaction call did to a [`pile::NucAmounts`]. 56 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucAmountsReactionReport {
    /// Energy released in keV per unit of amount. Offset 0.
    pub energy_released_kev: f64,
    /// Amount that fissioned. Offset 8.
    pub fissions: f64,
    /// Amount of neutron captures. Offset 16.
    pub captures: f64,
    /// Amount of fusion events. Offset 24.
    pub fusions: f64,
    /// Free neutrons added to the pile. Offset 32.
    pub neutrons_released: f64,
    /// Pending fission left waiting because the nucleus is too light. Offset 40.
    pub unresolved: f64,
    /// 0 when a mass was missing. Offset 48.
    pub energy_known: u8,
    /// Always 0. Offset 49.
    pub reserved: [u8; 7],
}

/// One way a nuclide decays. 40 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucDecayOutcome {
    /// Share of decays in percent. Offset 0.
    pub percent: f64,
    /// Decay mode code, see `mode_code`. Offset 8.
    pub mode: i32,
    /// Key of the daughter, or [`NUC_NO_NUCLIDE`] for a mode with none. Offset 12.
    pub daughter: u32,
    /// Key of the emitted cluster for a cluster mode, else [`NUC_NO_NUCLIDE`]. Offset 16.
    pub cluster: u32,
    /// How many of `emitted` are used. Offset 20.
    pub emitted_count: u8,
    /// 1 when NUBASE2020 gives no measured intensity. Offset 21.
    pub assumed: u8,
    /// Always 0. Offset 22.
    pub reserved: [u8; 2],
    /// Keys of the emitted nuclei. Offset 24.
    pub emitted: [u32; NUC_MAX_EMITTED],
}

/// One step of a decay chain. 48 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucChainStep {
    /// Key of the nuclide that decays. Offset 0.
    pub parent: u32,
    /// Always 0. Offset 4.
    pub reserved: u32,
    /// The outcome followed. Offset 8.
    pub outcome: NucDecayOutcome,
}

/// Where a decay chain stopped. 16 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct NucChainEnd {
    /// Key of the last nuclide. Offset 0.
    pub end: u32,
    /// 0 stable, 1 no daughter, 2 no data, 3 not in table, 4 loop,
    /// 5 too long. Offset 4.
    pub reason: i32,
    /// Decay mode code when `reason` is 1, else 0. Offset 8.
    pub mode: i32,
    /// Cluster key when that mode is a cluster mode, else [`NUC_NO_NUCLIDE`]. Offset 12.
    pub cluster: u32,
}

/// Summary of one set of fission outcomes. 40 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucFissionSummary {
    /// Neutron energy in eV of the table used, or 0 for a spontaneous one. Offset 0.
    pub reference_energy_ev: f64,
    /// Mean prompt neutrons per fission. Offset 8.
    pub mean_neutrons: f64,
    /// Mean energy released per fission in keV. Offset 16.
    pub mean_q_kev: f64,
    /// Key of the nucleus that fissions. Offset 24.
    pub compound: u32,
    /// Key of the parent of the table used. Offset 28.
    pub reference: u32,
    /// Number of channels. Offset 32.
    pub channel_count: i32,
    /// 0 evaluated, 1 extended. Offset 36.
    pub source: i32,
}

/// One fission channel. 32 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucFissionChannel {
    /// Probability per fission. Offset 0.
    pub probability: f64,
    /// Energy released in keV. Offset 8.
    pub q_kev: f64,
    /// Key of the light fragment. Offset 16.
    pub light: u32,
    /// Key of the heavy fragment. Offset 20.
    pub heavy: u32,
    /// Prompt neutrons released. Offset 24.
    pub neutrons: u8,
    /// 0 when a mass was missing, so `q_kev` is 0. Offset 25.
    pub q_known: u8,
    /// Always 0. Offset 26.
    pub reserved: [u8; 6],
}

/// One fusion channel. 56 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct NucFusionChannel {
    /// Energy released in keV. Offset 0.
    pub q_kev: f64,
    /// Lowest temperature of the published fit in keV, 0 for a Gamow channel. Offset 8.
    pub fit_low_kev: f64,
    /// Highest temperature of the published fit in keV, 0 for a Gamow channel. Offset 16.
    pub fit_high_kev: f64,
    /// Key of the first reactant. Offset 24.
    pub reactant_a: u32,
    /// Key of the second reactant. Offset 28.
    pub reactant_b: u32,
    /// Keys of the products. Offset 32.
    pub product_keys: [u32; 3],
    /// How many of each product. Offset 44.
    pub product_counts: [u8; 3],
    /// How many of the three product slots are used. Offset 47.
    pub product_len: u8,
    /// 0 Bosch and Hale fit, 1 Gamow integral. Offset 48.
    pub fit_kind: u8,
    /// Always 0. Offset 49.
    pub reserved: [u8; 7],
}

const _: () = assert!(core::mem::size_of::<NucCount>() == 16);
const _: () = assert!(core::mem::size_of::<NucAmount>() == 16);
const _: () = assert!(core::mem::size_of::<NucEnergy>() == 16);
const _: () = assert!(core::mem::size_of::<NucBinding>() == 24);
const _: () = assert!(core::mem::size_of::<NucHalfLife>() == 16);
const _: () = assert!(core::mem::size_of::<NucTotals>() == 16);
const _: () = assert!(core::mem::size_of::<NucStepReport>() == 32);
const _: () = assert!(core::mem::size_of::<NucAmountsStepReport>() == 32);
const _: () = assert!(core::mem::size_of::<NucReactionReport>() == 56);
const _: () = assert!(core::mem::size_of::<NucAmountsReactionReport>() == 56);
const _: () = assert!(core::mem::size_of::<NucDecayOutcome>() == 40);
const _: () = assert!(core::mem::size_of::<NucChainStep>() == 48);
const _: () = assert!(core::mem::size_of::<NucChainEnd>() == 16);
const _: () = assert!(core::mem::size_of::<NucFissionSummary>() == 40);
const _: () = assert!(core::mem::size_of::<NucFissionChannel>() == 32);
const _: () = assert!(core::mem::size_of::<NucFusionChannel>() == 56);

// ----------------------------------------------------------------------------
// Context
// ----------------------------------------------------------------------------

/// The state a host shares between piles: the random number generator, the
/// decay propagation cache and the fission outcome cache. Opaque to C.
pub struct NucContext {
    pub(crate) rng: Rng,
    pub(crate) propagator: Propagator,
    pub(crate) fission: Fission,
}

/// Longest last-error message in bytes. Longer text is cut on a character
/// boundary.
const ERROR_CAPACITY: usize = 255;

/// The last error of a thread: a fixed buffer, so the thread local has no
/// destructor to run. A Rust library unloaded while one of its thread locals
/// still has a destructor registered can crash the host at shutdown.
#[derive(Clone, Copy)]
struct ErrorText {
    len: usize,
    bytes: [u8; ERROR_CAPACITY],
}

thread_local! {
    static LAST_ERROR: RefCell<ErrorText> = const {
        RefCell::new(ErrorText { len: 0, bytes: [0; ERROR_CAPACITY] })
    };
}

/// Records `message` as the last error of this thread and returns `code`.
pub(crate) fn fail(code: i32, message: impl Into<String>) -> i32 {
    let text = message.into();
    let mut end = text.len().min(ERROR_CAPACITY);
    while !text.is_char_boundary(end) {
        end -= 1;
    }
    LAST_ERROR.with(|e| {
        let mut slot = e.borrow_mut();
        slot.bytes[..end].copy_from_slice(&text.as_bytes()[..end]);
        slot.len = end;
    });
    code
}

/// Runs `f`, turning a panic into [`NUC_ERR_PANIC`].
pub(crate) fn guard(f: impl FnOnce() -> i32) -> i32 {
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(code) => code,
        Err(_) => fail(NUC_ERR_PANIC, "the library panicked"),
    }
}

/// Maps a [`SampleError`] to a status code and records its text.
pub(crate) fn sample_error(e: &SampleError) -> i32 {
    let code = match e {
        SampleError::Lineage(LineageError::NotInTable(_)) => NUC_ERR_NOT_IN_TABLE,
        SampleError::Lineage(LineageError::InvalidTime(_)) => NUC_ERR_INVALID,
        SampleError::Lineage(LineageError::TooManyStates(_)) => NUC_ERR_TOO_MANY_STATES,
        SampleError::Overflow(_) => NUC_ERR_OVERFLOW,
        SampleError::Fission(FissionError::InvalidEnergy(_)) => NUC_ERR_INVALID,
        SampleError::Fission(FissionError::NotFissionable(_)) => NUC_ERR_NOT_FISSIONABLE,
        SampleError::NotEnough { .. } => NUC_ERR_NOT_ENOUGH,
        SampleError::NoReaction(..) => NUC_ERR_NO_REACTION,
        SampleError::InvalidProbability(_) | SampleError::InvalidAmount(_) => NUC_ERR_INVALID,
    };
    fail(code, e.to_string())
}

/// Maps a [`FissionError`] to a status code and records its text.
pub(crate) fn fission_error(e: &FissionError) -> i32 {
    let code = match e {
        FissionError::InvalidEnergy(_) => NUC_ERR_INVALID,
        FissionError::NotFissionable(_) => NUC_ERR_NOT_FISSIONABLE,
    };
    fail(code, e.to_string())
}

/// Borrows a context handle, or reports a null pointer.
pub(crate) unsafe fn context<'a>(ctx: *mut NucContext) -> Result<&'a mut NucContext, i32> {
    match ctx.as_mut() {
        Some(c) => Ok(c),
        None => Err(fail(NUC_ERR_NULL, "context handle is null")),
    }
}

/// Writes one value to an output pointer.
pub(crate) unsafe fn put<T>(out: *mut T, value: T) -> i32 {
    if out.is_null() {
        return fail(NUC_ERR_NULL, "output pointer is null");
    }
    out.write(value);
    NUC_OK
}

/// Writes a list through the buffer protocol: the total count always, and at
/// most `cap` elements.
pub(crate) unsafe fn put_list<T: Copy>(items: &[T], out: *mut T, cap: i32, count: *mut i32) -> i32 {
    if count.is_null() {
        return fail(NUC_ERR_NULL, "count pointer is null");
    }
    if cap < 0 {
        return fail(NUC_ERR_INVALID, "capacity is negative");
    }
    if cap > 0 && out.is_null() {
        return fail(NUC_ERR_NULL, "output buffer is null");
    }
    *count = i32::try_from(items.len()).unwrap_or(i32::MAX);
    let n = items.len().min(cap as usize);
    if n > 0 {
        core::ptr::copy_nonoverlapping(items.as_ptr(), out, n);
    }
    NUC_OK
}

/// Writes text through the string protocol and returns its full length.
pub(crate) unsafe fn put_text(text: &str, buf: *mut u8, cap: i32) -> i32 {
    if cap < 0 {
        return fail(NUC_ERR_INVALID, "capacity is negative");
    }
    if cap > 0 && buf.is_null() {
        return fail(NUC_ERR_NULL, "output buffer is null");
    }
    let bytes = text.as_bytes();
    if cap > 0 {
        let n = bytes.len().min(cap as usize - 1);
        core::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, n);
        *buf.add(n) = 0;
    }
    i32::try_from(bytes.len()).unwrap_or(i32::MAX)
}

/// Version of the C interface, [`NUC_ABI_VERSION`].
#[no_mangle]
pub extern "C" fn nuc_abi_version() -> i32 {
    NUC_ABI_VERSION
}

/// Copies the crate version (such as `0.0.1`) as text and returns its length.
#[no_mangle]
pub unsafe extern "C" fn nuc_library_version(buf: *mut u8, cap: i32) -> i32 {
    put_text(env!("CARGO_PKG_VERSION"), buf, cap)
}

/// Copies the description of the last failure on this thread as text and
/// returns its length. Empty when nothing has failed.
#[no_mangle]
pub unsafe extern "C" fn nuc_last_error(buf: *mut u8, cap: i32) -> i32 {
    let slot = LAST_ERROR.with(|e| *e.borrow());
    put_text(
        std::str::from_utf8(&slot.bytes[..slot.len]).unwrap_or(""),
        buf,
        cap,
    )
}

/// Size in bytes of the struct of the given kind (`NUC_KIND_*`), or -1 for an
/// unknown kind. A host compares this with the size of its own declaration.
#[no_mangle]
pub extern "C" fn nuc_struct_size(kind: i32) -> i32 {
    use core::mem::size_of;
    let size = match kind {
        NUC_KIND_COUNT => size_of::<NucCount>(),
        NUC_KIND_AMOUNT => size_of::<NucAmount>(),
        NUC_KIND_ENERGY => size_of::<NucEnergy>(),
        NUC_KIND_BINDING => size_of::<NucBinding>(),
        NUC_KIND_HALF_LIFE => size_of::<NucHalfLife>(),
        NUC_KIND_TOTALS => size_of::<NucTotals>(),
        NUC_KIND_STEP_REPORT => size_of::<NucStepReport>(),
        NUC_KIND_AMOUNTS_STEP_REPORT => size_of::<NucAmountsStepReport>(),
        NUC_KIND_REACTION_REPORT => size_of::<NucReactionReport>(),
        NUC_KIND_AMOUNTS_REACTION_REPORT => size_of::<NucAmountsReactionReport>(),
        NUC_KIND_DECAY_OUTCOME => size_of::<NucDecayOutcome>(),
        NUC_KIND_CHAIN_STEP => size_of::<NucChainStep>(),
        NUC_KIND_CHAIN_END => size_of::<NucChainEnd>(),
        NUC_KIND_FISSION_SUMMARY => size_of::<NucFissionSummary>(),
        NUC_KIND_FISSION_CHANNEL => size_of::<NucFissionChannel>(),
        NUC_KIND_FUSION_CHANNEL => size_of::<NucFusionChannel>(),
        _ => return -1,
    };
    size as i32
}

/// Copies the bytes of an instance of the struct of the given kind into `out`
/// and returns their number. Each named field holds a known value: counting
/// the fields in declaration order from 1 (array elements and nested fields
/// each count, `reserved` fields do not), an integer field of position `k`
/// holds `k` and a floating point field holds `k + 0.25`. A host reads the
/// bytes through its own declaration and compares, which proves every offset
/// and not only the total size.
#[no_mangle]
pub unsafe extern "C" fn nuc_layout_probe(kind: i32, out: *mut u8, cap: i32) -> i32 {
    guard(|| {
        let outcome = |first: u32| NucDecayOutcome {
            percent: f64::from(first) + 0.25,
            mode: first as i32 + 1,
            daughter: first + 2,
            cluster: first + 3,
            emitted_count: (first + 4) as u8,
            assumed: (first + 5) as u8,
            reserved: [0; 2],
            emitted: [first + 6, first + 7, first + 8, first + 9],
        };
        match kind {
            NUC_KIND_COUNT => probe(
                NucCount {
                    key: 1,
                    reserved: 0,
                    count: 2,
                },
                out,
                cap,
            ),
            NUC_KIND_AMOUNT => probe(
                NucAmount {
                    key: 1,
                    reserved: 0,
                    amount: 2.25,
                },
                out,
                cap,
            ),
            NUC_KIND_ENERGY => probe(
                NucEnergy {
                    kev: 1.25,
                    source: 2,
                    reserved: 0,
                },
                out,
                cap,
            ),
            NUC_KIND_BINDING => probe(
                NucBinding {
                    total_kev: 1.25,
                    per_nucleon_kev: 2.25,
                    source: 3,
                    reserved: 0,
                },
                out,
                cap,
            ),
            NUC_KIND_HALF_LIFE => probe(
                NucHalfLife {
                    seconds: 1.25,
                    state: 2,
                    estimated: 3,
                    reserved: [0; 3],
                },
                out,
                cap,
            ),
            NUC_KIND_TOTALS => probe(
                NucTotals {
                    atoms: 1,
                    baryons: 2,
                },
                out,
                cap,
            ),
            NUC_KIND_STEP_REPORT => probe(
                NucStepReport {
                    energy_released_kev: 1.25,
                    atoms_changed: 2,
                    new_pending_fissions: 3,
                    energy_known: 4,
                    reserved: [0; 7],
                },
                out,
                cap,
            ),
            NUC_KIND_AMOUNTS_STEP_REPORT => probe(
                NucAmountsStepReport {
                    energy_released_kev: 1.25,
                    amount_changed: 2.25,
                    new_pending: 3.25,
                    energy_known: 4,
                    reserved: [0; 7],
                },
                out,
                cap,
            ),
            NUC_KIND_REACTION_REPORT => probe(
                NucReactionReport {
                    energy_released_kev: 1.25,
                    fissions: 2,
                    captures: 3,
                    fusions: 4,
                    neutrons_released: 5,
                    unresolved: 6,
                    energy_known: 7,
                    reserved: [0; 7],
                },
                out,
                cap,
            ),
            NUC_KIND_AMOUNTS_REACTION_REPORT => probe(
                NucAmountsReactionReport {
                    energy_released_kev: 1.25,
                    fissions: 2.25,
                    captures: 3.25,
                    fusions: 4.25,
                    neutrons_released: 5.25,
                    unresolved: 6.25,
                    energy_known: 7,
                    reserved: [0; 7],
                },
                out,
                cap,
            ),
            NUC_KIND_DECAY_OUTCOME => probe(outcome(1), out, cap),
            NUC_KIND_CHAIN_STEP => probe(
                NucChainStep {
                    parent: 1,
                    reserved: 0,
                    outcome: outcome(2),
                },
                out,
                cap,
            ),
            NUC_KIND_CHAIN_END => probe(
                NucChainEnd {
                    end: 1,
                    reason: 2,
                    mode: 3,
                    cluster: 4,
                },
                out,
                cap,
            ),
            NUC_KIND_FISSION_SUMMARY => probe(
                NucFissionSummary {
                    reference_energy_ev: 1.25,
                    mean_neutrons: 2.25,
                    mean_q_kev: 3.25,
                    compound: 4,
                    reference: 5,
                    channel_count: 6,
                    source: 7,
                },
                out,
                cap,
            ),
            NUC_KIND_FISSION_CHANNEL => probe(
                NucFissionChannel {
                    probability: 1.25,
                    q_kev: 2.25,
                    light: 3,
                    heavy: 4,
                    neutrons: 5,
                    q_known: 6,
                    reserved: [0; 6],
                },
                out,
                cap,
            ),
            NUC_KIND_FUSION_CHANNEL => probe(
                NucFusionChannel {
                    q_kev: 1.25,
                    fit_low_kev: 2.25,
                    fit_high_kev: 3.25,
                    reactant_a: 4,
                    reactant_b: 5,
                    product_keys: [6, 7, 8],
                    product_counts: [9, 10, 11],
                    product_len: 12,
                    fit_kind: 13,
                    reserved: [0; 7],
                },
                out,
                cap,
            ),
            _ => fail(NUC_ERR_INVALID, format!("unknown struct kind {kind}")),
        }
    })
}

unsafe fn probe<T: Copy>(value: T, out: *mut u8, cap: i32) -> i32 {
    let size = core::mem::size_of::<T>();
    if cap < 0 || (cap as usize) < size {
        return fail(NUC_ERR_INVALID, "probe buffer is too small");
    }
    if out.is_null() {
        return fail(NUC_ERR_NULL, "probe buffer is null");
    }
    core::ptr::copy_nonoverlapping(&value as *const T as *const u8, out, size);
    size as i32
}

/// Creates a context seeded with `seed`. Free it with
/// [`nuc_context_destroy`].
#[no_mangle]
pub extern "C" fn nuc_context_create(seed: u64) -> *mut NucContext {
    Box::into_raw(Box::new(NucContext {
        rng: Rng::new(seed),
        propagator: Propagator::new(),
        fission: Fission::new(),
    }))
}

/// Frees a context. Null is a no-op.
#[no_mangle]
pub unsafe extern "C" fn nuc_context_destroy(ctx: *mut NucContext) {
    if !ctx.is_null() {
        drop(Box::from_raw(ctx));
    }
}

/// Restarts the random number generator from `seed`.
#[no_mangle]
pub unsafe extern "C" fn nuc_context_reseed(ctx: *mut NucContext, seed: u64) -> i32 {
    guard(|| match context(ctx) {
        Ok(c) => {
            c.rng = Rng::new(seed);
            NUC_OK
        }
        Err(code) => code,
    })
}

/// Copies the four 64-bit words of the generator state to `out`, so a saved
/// game can continue the same random sequence.
#[no_mangle]
pub unsafe extern "C" fn nuc_context_rng_state(ctx: *mut NucContext, out: *mut u64) -> i32 {
    guard(|| match context(ctx) {
        Ok(c) => {
            if out.is_null() {
                return fail(NUC_ERR_NULL, "output pointer is null");
            }
            core::ptr::copy_nonoverlapping(c.rng.state().as_ptr(), out, 4);
            NUC_OK
        }
        Err(code) => code,
    })
}

/// Restores a generator state saved by [`nuc_context_rng_state`], read from
/// four 64-bit words. An all-zero state is refused.
#[no_mangle]
pub unsafe extern "C" fn nuc_context_set_rng_state(ctx: *mut NucContext, words: *const u64) -> i32 {
    guard(|| match context(ctx) {
        Ok(c) => {
            if words.is_null() {
                return fail(NUC_ERR_NULL, "state pointer is null");
            }
            let mut state = [0u64; 4];
            core::ptr::copy_nonoverlapping(words, state.as_mut_ptr(), 4);
            match Rng::from_state(state) {
                Some(rng) => {
                    c.rng = rng;
                    NUC_OK
                }
                None => fail(NUC_ERR_INVALID, "an all-zero generator state is not valid"),
            }
        }
        Err(code) => code,
    })
}

/// Empties the decay propagation cache and the fission outcome cache. Results
/// do not change, the next call of each kind just takes longer.
#[no_mangle]
pub unsafe extern "C" fn nuc_context_clear_caches(ctx: *mut NucContext) -> i32 {
    guard(|| match context(ctx) {
        Ok(c) => {
            c.propagator.clear();
            c.fission = Fission::new();
            NUC_OK
        }
        Err(code) => code,
    })
}

/// Writes the number of cached decay transitions and cached fission outcome
/// lists to `out[0]` and `out[1]`.
#[no_mangle]
pub unsafe extern "C" fn nuc_context_cache_sizes(ctx: *mut NucContext, out: *mut i32) -> i32 {
    guard(|| match context(ctx) {
        Ok(c) => {
            if out.is_null() {
                return fail(NUC_ERR_NULL, "output pointer is null");
            }
            *out = i32::try_from(c.propagator.cached_transitions()).unwrap_or(i32::MAX);
            *out.add(1) = i32::try_from(c.fission.cached()).unwrap_or(i32::MAX);
            NUC_OK
        }
        Err(code) => code,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Offset of a field inside an instance, found by address arithmetic.
    macro_rules! offset {
        ($value:expr, $($field:tt)+) => {{
            let base = &$value as *const _ as usize;
            let field = core::ptr::addr_of!($value.$($field)+) as usize;
            field - base
        }};
    }

    #[test]
    fn offsets_match_the_documented_layout() {
        let v = NucCount::default();
        assert_eq!((offset!(v, key), offset!(v, count)), (0, 8));
        let v = NucAmount::default();
        assert_eq!((offset!(v, key), offset!(v, amount)), (0, 8));
        let v = NucEnergy::default();
        assert_eq!((offset!(v, kev), offset!(v, source)), (0, 8));
        let v = NucBinding::default();
        assert_eq!(
            (
                offset!(v, total_kev),
                offset!(v, per_nucleon_kev),
                offset!(v, source)
            ),
            (0, 8, 16)
        );
        let v = NucHalfLife::default();
        assert_eq!(
            (
                offset!(v, seconds),
                offset!(v, state),
                offset!(v, estimated)
            ),
            (0, 8, 12)
        );
        let v = NucStepReport::default();
        assert_eq!(
            (
                offset!(v, energy_released_kev),
                offset!(v, atoms_changed),
                offset!(v, new_pending_fissions),
                offset!(v, energy_known)
            ),
            (0, 8, 16, 24)
        );
        let v = NucReactionReport::default();
        assert_eq!(
            (
                offset!(v, fissions),
                offset!(v, captures),
                offset!(v, fusions),
                offset!(v, neutrons_released),
                offset!(v, unresolved),
                offset!(v, energy_known)
            ),
            (8, 16, 24, 32, 40, 48)
        );
        let v = NucDecayOutcome::default();
        assert_eq!(
            (
                offset!(v, mode),
                offset!(v, daughter),
                offset!(v, cluster),
                offset!(v, emitted_count),
                offset!(v, assumed),
                offset!(v, emitted)
            ),
            (8, 12, 16, 20, 21, 24)
        );
        let v = NucChainStep::default();
        assert_eq!((offset!(v, parent), offset!(v, outcome)), (0, 8));
        let v = NucFissionSummary::default();
        assert_eq!(
            (
                offset!(v, compound),
                offset!(v, reference),
                offset!(v, channel_count),
                offset!(v, source)
            ),
            (24, 28, 32, 36)
        );
        let v = NucFissionChannel::default();
        assert_eq!(
            (
                offset!(v, light),
                offset!(v, heavy),
                offset!(v, neutrons),
                offset!(v, q_known)
            ),
            (16, 20, 24, 25)
        );
        let v = NucFusionChannel::default();
        assert_eq!(
            (
                offset!(v, reactant_a),
                offset!(v, product_keys),
                offset!(v, product_counts),
                offset!(v, product_len),
                offset!(v, fit_kind)
            ),
            (24, 32, 44, 47, 48)
        );
    }

    #[test]
    fn every_kind_has_a_size_and_a_probe_of_that_size() {
        for kind in 1..=16 {
            let size = nuc_struct_size(kind);
            assert!(size > 0, "kind {kind}");
            let mut bytes = vec![0xAAu8; size as usize];
            let written = unsafe { nuc_layout_probe(kind, bytes.as_mut_ptr(), size) };
            assert_eq!(written, size, "kind {kind}");
        }
        assert_eq!(nuc_struct_size(0), -1);
        assert_eq!(nuc_struct_size(17), -1);
        let mut one = [0u8; 1];
        assert_eq!(
            unsafe { nuc_layout_probe(1, one.as_mut_ptr(), 1) },
            NUC_ERR_INVALID
        );
        assert_eq!(
            unsafe { nuc_layout_probe(1, core::ptr::null_mut(), 16) },
            NUC_ERR_NULL
        );
    }

    #[test]
    fn a_probe_reads_back_through_the_same_struct() {
        let mut bytes = [0u8; 56];
        let n = unsafe { nuc_layout_probe(NUC_KIND_FUSION_CHANNEL, bytes.as_mut_ptr(), 56) };
        assert_eq!(n, 56);
        let channel: NucFusionChannel = unsafe { core::ptr::read(bytes.as_ptr() as *const _) };
        assert_eq!(channel.q_kev, 1.25);
        assert_eq!(channel.product_keys, [6, 7, 8]);
        assert_eq!(channel.fit_kind, 13);
    }

    #[test]
    fn guard_turns_a_panic_into_a_status() {
        let code = guard(|| panic!("boom"));
        assert_eq!(code, NUC_ERR_PANIC);
        assert_eq!(guard(|| 5), 5);
    }

    #[test]
    fn text_follows_the_snprintf_protocol() {
        let mut buf = [0xFFu8; 8];
        let n = unsafe { put_text("abcdefghij", buf.as_mut_ptr(), 8) };
        assert_eq!(n, 10);
        assert_eq!(&buf, b"abcdefg\0");
        assert_eq!(unsafe { put_text("abc", core::ptr::null_mut(), 0) }, 3);
        assert_eq!(
            unsafe { put_text("abc", core::ptr::null_mut(), 4) },
            NUC_ERR_NULL
        );
        assert_eq!(
            unsafe { put_text("abc", buf.as_mut_ptr(), -1) },
            NUC_ERR_INVALID
        );
    }

    #[test]
    fn lists_report_the_total_and_copy_what_fits() {
        let items = [1u32, 2, 3, 4, 5];
        let mut out = [0u32; 3];
        let mut count = 0i32;
        let code = unsafe { put_list(&items, out.as_mut_ptr(), 3, &mut count) };
        assert_eq!((code, count, out), (NUC_OK, 5, [1, 2, 3]));
        let code = unsafe { put_list(&items, core::ptr::null_mut(), 0, &mut count) };
        assert_eq!((code, count), (NUC_OK, 5));
        assert_eq!(
            unsafe { put_list(&items, out.as_mut_ptr(), 3, core::ptr::null_mut()) },
            NUC_ERR_NULL
        );
    }

    #[test]
    fn the_last_error_names_the_failure_on_this_thread() {
        let code = unsafe { nuc_context_reseed(core::ptr::null_mut(), 1) };
        assert_eq!(code, NUC_ERR_NULL);
        let mut buf = [0u8; 64];
        let n = unsafe { nuc_last_error(buf.as_mut_ptr(), 64) } as usize;
        assert_eq!(&buf[..n], b"context handle is null");
        // Another thread starts with an empty message.
        let other = std::thread::spawn(|| unsafe { nuc_last_error(core::ptr::null_mut(), 0) });
        assert_eq!(other.join().unwrap(), 0);
    }

    #[test]
    fn rng_state_round_trips_and_reseeding_restarts_the_stream() {
        unsafe {
            let ctx = nuc_context_create(42);
            let mut a = [0u64; 4];
            assert_eq!(nuc_context_rng_state(ctx, a.as_mut_ptr()), NUC_OK);
            assert_eq!(a, Rng::new(42).state());
            assert_eq!(nuc_context_reseed(ctx, 7), NUC_OK);
            let mut b = [0u64; 4];
            nuc_context_rng_state(ctx, b.as_mut_ptr());
            assert_eq!(b, Rng::new(7).state());
            assert_eq!(nuc_context_set_rng_state(ctx, a.as_ptr()), NUC_OK);
            let mut c = [0u64; 4];
            nuc_context_rng_state(ctx, c.as_mut_ptr());
            assert_eq!(c, a);
            let zero = [0u64; 4];
            assert_eq!(
                nuc_context_set_rng_state(ctx, zero.as_ptr()),
                NUC_ERR_INVALID
            );
            nuc_context_destroy(ctx);
            nuc_context_destroy(core::ptr::null_mut());
        }
    }
}
