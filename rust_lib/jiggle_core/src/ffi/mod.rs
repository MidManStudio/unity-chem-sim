// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "ffi/mod.rs"
// ============================================================================
//! C interface to jiggle_core for Unity and other hosts.
//!
//! Every function is `jgl_` plus a name. The library is `jiggle_core`.
//!
//! # Conventions
//!
//! * **Host owned memory.** The library keeps no state between calls. The host
//!   owns one flat array of each record and passes their addresses in a
//!   [`SolverArgs`]. The addresses are `u64` fields so the layout does not
//!   depend on the pointer size of the target.
//! * **Ranges.** `jgl_step_rigs` and `jgl_resolve_rigs` work on rigs `first` to
//!   `first + count`. Rigs have disjoint particle ranges, so a host can call
//!   them from several threads at once with disjoint rig ranges. Only the
//!   particles of the rigs in the range are touched mutably.
//! * **Status codes.** 0 for success and a negative `JGL_ERR_*` value otherwise.
//!   After a failure [`jgl_last_error`] copies a description of the most recent
//!   failure on the calling thread. A successful call leaves it alone.
//! * **Structs** are `#[repr(C)]`, hold only 4 byte fields and carry explicit
//!   reserved fields. [`jgl_struct_size`] gives the size of each one and
//!   [`jgl_layout_probe`] fills one with known values, so a host can check its
//!   own declaration field by field.
//! * **Booleans** are not used. Flags are `u32` bits.
//! * **Panics** are caught at the boundary and returned as `JGL_ERR_PANIC`. The
//!   release profile aborts on panic instead, so a panic there ends the process.
//! * **Threads.** There is no global state apart from the per thread last error,
//!   a fixed buffer with no destructor.
//!
//! # Safety
//!
//! Every address in a [`SolverArgs`] must be null with a count of 0, or valid
//! for its count of records for the whole call. The library checks counts and
//! offsets and refuses a rig that reaches outside an array, but it cannot check
//! that an address points at live memory. The module opts out of the crate's
//! `unsafe_code` lint, and it is the only module that does.
#![allow(unsafe_code)]
#![allow(clippy::missing_safety_doc)]

use std::cell::Cell;
use std::panic::{catch_unwind, AssertUnwindSafe};

use crate::solver::{self, Buffers, SolverError};
use crate::types::*;

/// The version of this C interface.
pub const ABI_VERSION: i32 = 1;

/// Success.
pub const JGL_OK: i32 = 0;
/// A required address was null.
pub const JGL_ERR_NULL: i32 = -1;
/// A range or an offset is outside its array.
pub const JGL_ERR_RANGE: i32 = -2;
/// A panic was caught.
pub const JGL_ERR_PANIC: i32 = -3;
/// A rig has a bad particle count, a bad collider list or a bad topology.
pub const JGL_ERR_BAD_RIG: i32 = -4;
/// An argument was not valid, for example an unknown struct kind.
pub const JGL_ERR_ARG: i32 = -5;

/// The addresses and counts of one solver call. 112 bytes.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct SolverArgs {
    /// Address of `rig_count` [`Rig`] records.
    pub rigs: u64,
    /// Address of `particle_count` [`ParticleDef`] records.
    pub defs: u64,
    /// Address of `particle_count` [`ParticleState`] records.
    pub state: u64,
    /// Address of `particle_count` pose positions of the last frame, four floats each.
    pub pose_prev: u64,
    /// Address of `particle_count` pose positions of this frame, four floats each.
    pub pose_curr: u64,
    /// Address of `particle_count` pose rotations of this frame, four floats each.
    pub pose_rot: u64,
    /// Address of `particle_count` [`ParticleOut`] records.
    pub out: u64,
    /// Address of `collider_count` [`Collider`] records.
    pub colliders: u64,
    /// Address of `ref_count` collider indices.
    pub refs: u64,
    /// Address of `field_count` [`Field`] records.
    pub fields: u64,
    /// Number of rigs.
    pub rig_count: u32,
    /// Number of particles over all rigs.
    pub particle_count: u32,
    /// Number of colliders.
    pub collider_count: u32,
    /// Number of collider indices over all rigs.
    pub ref_count: u32,
    /// Number of fields.
    pub field_count: u32,
    /// Reserved, keep 0.
    pub reserved: [u32; 3],
}

const ERR_CAP: usize = 256;

thread_local! {
    // A plain array in a Cell has no destructor, which keeps thread exit free of
    // work that could run after the library has been unloaded.
    static LAST_ERROR: Cell<[u8; ERR_CAP]> = const { Cell::new([0u8; ERR_CAP]) };
}

fn set_error(message: &str) {
    let mut buf = [0u8; ERR_CAP];
    let bytes = message.as_bytes();
    let n = bytes.len().min(ERR_CAP - 1);
    buf[..n].copy_from_slice(&bytes[..n]);
    LAST_ERROR.with(|c| c.set(buf));
}

fn code_of(e: SolverError) -> i32 {
    match e {
        SolverError::RigRange => {
            set_error("rig range is outside the rig array");
            JGL_ERR_RANGE
        }
        SolverError::BadRig(i) => {
            set_error(&format!("rig {} has a bad particle count or reaches outside the particle arrays", i));
            JGL_ERR_BAD_RIG
        }
        SolverError::BadRefs(i) => {
            set_error(&format!("rig {} has a collider list outside the reference array", i));
            JGL_ERR_BAD_RIG
        }
        SolverError::BadTopology(i) => {
            set_error(&format!(
                "rig {} has a bad topology: particle 0 must be kinematic with parent -1 and every other parent must come earlier",
                i
            ));
            JGL_ERR_BAD_RIG
        }
    }
}

unsafe fn slice_of<'a, T>(addr: u64, count: usize) -> Result<&'a [T], i32> {
    window_of(addr, 0, count)
}

/// `count` records starting `start` records after `addr`. The null check comes before the offset,
/// so a null address with a nonzero start is refused instead of turning into a wild pointer.
unsafe fn window_of<'a, T>(addr: u64, start: usize, count: usize) -> Result<&'a [T], i32> {
    if count == 0 {
        return Ok(&[]);
    }
    if addr == 0 {
        set_error("a required address is null");
        return Err(JGL_ERR_NULL);
    }
    let p = (addr as usize as *const T).add(start);
    Ok(std::slice::from_raw_parts(p, count))
}

unsafe fn window_of_mut<'a, T>(addr: u64, start: usize, count: usize) -> Result<&'a mut [T], i32> {
    if count == 0 {
        return Ok(&mut []);
    }
    if addr == 0 {
        set_error("a required address is null");
        return Err(JGL_ERR_NULL);
    }
    let p = (addr as usize as *mut T).add(start);
    Ok(std::slice::from_raw_parts_mut(p, count))
}

/// Builds windowed buffers for rigs `first..first + count` and runs `f` on them.
///
/// The particle arrays are cut to the window of particles the range covers, so a thread that
/// steps its own batch of rigs never holds a slice that overlaps another thread's. For that the
/// rigs must be stored with ascending particle offsets and no overlap, which is checked inside the
/// range, and a host that splits into batches must keep it across batches.
unsafe fn with_buffers(
    a: &SolverArgs,
    first: u32,
    count: u32,
    f: impl FnOnce(&mut Buffers) -> Result<(), SolverError>,
) -> i32 {
    let first = first as usize;
    let count = count as usize;
    match first.checked_add(count) {
        Some(e) if e <= a.rig_count as usize => {}
        _ => return code_of(SolverError::RigRange),
    }
    if count == 0 {
        return JGL_OK;
    }
    if a.rigs == 0 {
        set_error("the rig address is null");
        return JGL_ERR_NULL;
    }
    // Read the headers of the range once to find the window of particles it covers.
    let particle_total = a.particle_count as usize;
    let mut pmin = usize::MAX;
    let mut pmax = 0usize;
    for i in 0..count {
        let r = ((a.rigs as usize as *const Rig).add(first + i)).read();
        let off = r.particle_offset as usize;
        let n = r.particle_count as usize;
        let e = match off.checked_add(n) {
            Some(e) if e <= particle_total => e,
            _ => return code_of(SolverError::BadRig((first + i) as u32)),
        };
        if i > 0 && off < pmax {
            return code_of(SolverError::BadRig((first + i) as u32));
        }
        if off < pmin {
            pmin = off;
        }
        if e > pmax {
            pmax = e;
        }
    }
    let window = pmax - pmin;

    let result = (|| -> Result<(), i32> {
        let rigs = window_of_mut::<Rig>(a.rigs, first, count)?;
        let defs = window_of::<ParticleDef>(a.defs, pmin, window)?;
        let state = window_of_mut::<ParticleState>(a.state, pmin, window)?;
        let pose_prev = window_of::<[f32; 4]>(a.pose_prev, pmin, window)?;
        let pose_curr = window_of::<[f32; 4]>(a.pose_curr, pmin, window)?;
        let pose_rot = window_of::<[f32; 4]>(a.pose_rot, pmin, window)?;
        let out = window_of_mut::<ParticleOut>(a.out, pmin, window)?;
        let colliders = slice_of::<Collider>(a.colliders, a.collider_count as usize)?;
        let refs = slice_of::<u32>(a.refs, a.ref_count as usize)?;
        let fields = slice_of::<Field>(a.fields, a.field_count as usize)?;
        let mut b = Buffers {
            particle_base: pmin,
            rigs,
            defs,
            state,
            pose_prev,
            pose_curr,
            pose_rot,
            out,
            colliders,
            refs,
            fields,
        };
        f(&mut b).map_err(code_of)
    })();
    match result {
        Ok(()) => JGL_OK,
        Err(code) => code,
    }
}

fn guarded(body: impl FnOnce() -> i32) -> i32 {
    match catch_unwind(AssertUnwindSafe(body)) {
        Ok(code) => code,
        Err(_) => {
            set_error("a panic was caught at the C boundary");
            JGL_ERR_PANIC
        }
    }
}

/// The version of the C interface. A host compares it with the one it was written against.
#[no_mangle]
pub extern "C" fn jgl_abi_version() -> i32 {
    ABI_VERSION
}

/// Copies the library version as text into `buffer`, at most `capacity - 1` bytes and a terminating zero.
/// Returns the full length of the text.
#[no_mangle]
pub unsafe extern "C" fn jgl_library_version(buffer: *mut u8, capacity: i32) -> i32 {
    copy_text(env!("CARGO_PKG_VERSION"), buffer, capacity)
}

/// Copies the text of the most recent failure on the calling thread, like `snprintf`.
/// Returns the full length of the text.
#[no_mangle]
pub unsafe extern "C" fn jgl_last_error(buffer: *mut u8, capacity: i32) -> i32 {
    let buf = LAST_ERROR.with(|c| c.get());
    let len = buf.iter().position(|&b| b == 0).unwrap_or(ERR_CAP);
    let text = std::str::from_utf8(&buf[..len]).unwrap_or("");
    copy_text(text, buffer, capacity)
}

unsafe fn copy_text(text: &str, buffer: *mut u8, capacity: i32) -> i32 {
    let bytes = text.as_bytes();
    if !buffer.is_null() && capacity > 0 {
        let n = bytes.len().min(capacity as usize - 1);
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), buffer, n);
        *buffer.add(n) = 0;
    }
    bytes.len() as i32
}

/// The size in bytes of the struct of a `KIND_*` value, or 0 for an unknown kind.
#[no_mangle]
pub extern "C" fn jgl_struct_size(kind: i32) -> i32 {
    crate::types::struct_size(kind) as i32
}

unsafe fn write_probe<T>(value: &T, buffer: *mut u8, capacity: i32) -> i32 {
    let size = std::mem::size_of::<T>();
    if buffer.is_null() || (capacity as usize) < size {
        set_error("the probe buffer is null or too small");
        return JGL_ERR_ARG;
    }
    std::ptr::copy_nonoverlapping(value as *const T as *const u8, buffer, size);
    size as i32
}

/// Writes a struct of a `KIND_*` value filled with known values into `buffer`, so a host can check
/// the offsets of its own declaration. Returns the size written, or a negative code.
///
/// The values: integer fields count 1, 2, 3 and so on in field order, and float fields count 1.5,
/// 2.5, 3.5 and so on in field order, each kind counting from the start. Arrays count on through
/// their elements.
#[no_mangle]
pub unsafe extern "C" fn jgl_layout_probe(kind: i32, buffer: *mut u8, capacity: i32) -> i32 {
    guarded(|| match kind {
        KIND_PARTICLE_DEF => {
            let v = ParticleDef {
                parent: 1,
                flags: 2,
                reserved0: 3,
                reserved1: 4,
                angle_elasticity: 1.5,
                length_elasticity: 2.5,
                soften: 3.5,
                root_elasticity: 4.5,
                limit_cos: 5.5,
                limit_sin: 6.5,
                limit_soften: 7.5,
                drag: 8.5,
                air_drag: 9.5,
                gravity: 10.5,
                collision_radius: 11.5,
                blend: 12.5,
                ignore_root_motion: 13.5,
                wind_response: 14.5,
                impulse_response: 15.5,
                reserved2: 16.5,
            };
            write_probe(&v, buffer, capacity)
        }
        KIND_PARTICLE_STATE => {
            let v = ParticleState { pos: [1.5, 2.5, 3.5, 4.5], prev: [5.5, 6.5, 7.5, 8.5] };
            write_probe(&v, buffer, capacity)
        }
        KIND_RIG => {
            let v = Rig {
                particle_offset: 1,
                particle_count: 2,
                ref_offset: 3,
                ref_count: 4,
                flags: 5,
                sleep_counter: 6,
                extent: 1.5,
                reserved0: 7,
                anchor_delta: [2.5, 3.5, 4.5],
                reserved1: 5.5,
                anchor_world: [6.5, 7.5, 8.5],
                reserved2: 9.5,
            };
            write_probe(&v, buffer, capacity)
        }
        KIND_STEP_CONFIG => {
            let v = StepConfig {
                dt: 1.5,
                steps: 1,
                time: 2.5,
                alpha: 3.5,
                gravity: [4.5, 5.5, 6.5],
                teleport_distance: 7.5,
                sleep_speed: 8.5,
                sleep_deviation: 9.5,
                sleep_frames: 2,
                wake_motion: 10.5,
                reserved: [11.5, 12.5, 13.5, 14.5],
            };
            write_probe(&v, buffer, capacity)
        }
        KIND_COLLIDER => {
            let v = Collider {
                kind: 1,
                flags: 2,
                radius: 1.5,
                half_height: 2.5,
                center: [3.5, 4.5, 5.5, 6.5],
                axis: [7.5, 8.5, 9.5, 10.5],
                extents: [11.5, 12.5, 13.5, 14.5],
                rot: [15.5, 16.5, 17.5, 18.5],
            };
            write_probe(&v, buffer, capacity)
        }
        KIND_FIELD => {
            let v = Field {
                kind: 1,
                mode: 2,
                power: 3,
                reserved: 4,
                center: [1.5, 2.5, 3.5, 4.5],
                dir: [5.5, 6.5, 7.5, 8.5],
                radius: 9.5,
                strength: 10.5,
                turbulence: 11.5,
                frequency: 12.5,
            };
            write_probe(&v, buffer, capacity)
        }
        KIND_PARTICLE_OUT => {
            let v = ParticleOut { pos: [1.5, 2.5, 3.5, 4.5], rot: [5.5, 6.5, 7.5, 8.5] };
            write_probe(&v, buffer, capacity)
        }
        KIND_SOLVER_ARGS => {
            let v = SolverArgs {
                rigs: 1,
                defs: 2,
                state: 3,
                pose_prev: 4,
                pose_curr: 5,
                pose_rot: 6,
                out: 7,
                colliders: 8,
                refs: 9,
                fields: 10,
                rig_count: 11,
                particle_count: 12,
                collider_count: 13,
                ref_count: 14,
                field_count: 15,
                reserved: [16, 17, 18],
            };
            write_probe(&v, buffer, capacity)
        }
        _ => {
            set_error("unknown struct kind");
            JGL_ERR_ARG
        }
    })
}

/// Runs `cfg.steps` fixed steps for rigs `first` to `first + count`.
///
/// Returns 0 or a negative `JGL_ERR_*` code. Nothing is written when a rig is refused, but rigs of
/// the range before the refused one have already been stepped.
#[no_mangle]
pub unsafe extern "C" fn jgl_step_rigs(cfg: *const StepConfig, args: *const SolverArgs, first: u32, count: u32) -> i32 {
    guarded(|| {
        if cfg.is_null() || args.is_null() {
            set_error("cfg or args is null");
            return JGL_ERR_NULL;
        }
        let cfg = *cfg;
        let a = *args;
        with_buffers(&a, first, count, |b| solver::step_range(&cfg, b, 0, count as usize))
    })
}

/// Writes the position and rotation of every particle of rigs `first` to `first + count` into the
/// output array, interpolating by `cfg.alpha`.
#[no_mangle]
pub unsafe extern "C" fn jgl_resolve_rigs(cfg: *const StepConfig, args: *const SolverArgs, first: u32, count: u32) -> i32 {
    guarded(|| {
        if cfg.is_null() || args.is_null() {
            set_error("cfg or args is null");
            return JGL_ERR_NULL;
        }
        let cfg = *cfg;
        let a = *args;
        with_buffers(&a, first, count, |b| solver::resolve_range(&cfg, b, 0, count as usize))
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_panic_becomes_an_error_code() {
        let code = guarded(|| panic!("boom"));
        assert_eq!(code, JGL_ERR_PANIC);
        let mut buf = [0u8; 128];
        let len = unsafe { jgl_last_error(buf.as_mut_ptr(), 128) } as usize;
        assert!(std::str::from_utf8(&buf[..len]).unwrap().contains("panic"));
        // A normal body passes its code through.
        assert_eq!(guarded(|| 7), 7);
    }
}
