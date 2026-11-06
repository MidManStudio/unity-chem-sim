// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "solver.rs"
// ============================================================================
//! Running the kernel over a range of rigs in shared arrays, with every offset
//! checked first. This is what the C interface and `World` both call.

use crate::kernel;
use crate::types::*;

/// Why a range was refused. Nothing is written when one of these is returned.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SolverError {
    /// The range of rigs is outside the rig array.
    RigRange,
    /// A rig reaches outside the particle arrays or has a bad particle count.
    BadRig(u32),
    /// A rig's collider list reaches outside the reference array.
    BadRefs(u32),
    /// A particle's parent is not an earlier particle of its rig, or particle 0 is not kinematic.
    BadTopology(u32),
}

/// All the shared arrays of one solver call.
///
/// The particle arrays (`defs`, `state`, the three pose arrays and `out`) may
/// be a window into larger host arrays. `particle_base` is the global index of
/// their first entry, and rig headers keep global offsets. Each thread of a
/// parallel host gets a window of its own rigs, so no two threads hold
/// overlapping mutable slices.
pub struct Buffers<'a> {
    /// Global particle index of the first entry of the particle arrays. 0 when they are not windowed.
    pub particle_base: usize,
    /// Rig headers.
    pub rigs: &'a mut [Rig],
    /// Particle definitions of all rigs.
    pub defs: &'a [ParticleDef],
    /// Particle states of all rigs.
    pub state: &'a mut [ParticleState],
    /// Pose positions of the last frame.
    pub pose_prev: &'a [[f32; 4]],
    /// Pose positions of this frame.
    pub pose_curr: &'a [[f32; 4]],
    /// Pose rotations of this frame.
    pub pose_rot: &'a [[f32; 4]],
    /// Output of `resolve_range`.
    pub out: &'a mut [ParticleOut],
    /// All colliders.
    pub colliders: &'a [Collider],
    /// Collider lists of all rigs.
    pub refs: &'a [u32],
    /// All fields.
    pub fields: &'a [Field],
}

fn check_rig(b: &Buffers, index: usize) -> Result<(usize, usize), SolverError> {
    let r = &b.rigs[index];
    let id = index as u32;
    let off = (r.particle_offset as usize)
        .checked_sub(b.particle_base)
        .ok_or(SolverError::BadRig(id))?;
    let n = r.particle_count as usize;
    if n < 2 || n > MAX_PARTICLES {
        return Err(SolverError::BadRig(id));
    }
    let end = off.checked_add(n).ok_or(SolverError::BadRig(id))?;
    if end > b.defs.len()
        || end > b.state.len()
        || end > b.pose_prev.len()
        || end > b.pose_curr.len()
        || end > b.pose_rot.len()
        || end > b.out.len()
    {
        return Err(SolverError::BadRig(id));
    }
    let roff = r.ref_offset as usize;
    let rend = roff.checked_add(r.ref_count as usize).ok_or(SolverError::BadRefs(id))?;
    if rend > b.refs.len() {
        return Err(SolverError::BadRefs(id));
    }
    let defs = &b.defs[off..end];
    if defs[0].flags & FLAG_KINEMATIC == 0 || defs[0].parent != -1 {
        return Err(SolverError::BadTopology(id));
    }
    for (i, d) in defs.iter().enumerate().skip(1) {
        if d.parent < 0 || d.parent as usize >= i {
            return Err(SolverError::BadTopology(id));
        }
    }
    Ok((off, n))
}

/// Steps rigs `first` to `first + count` for `cfg.steps` fixed steps.
pub fn step_range(cfg: &StepConfig, b: &mut Buffers, first: usize, count: usize) -> Result<(), SolverError> {
    let end = first.checked_add(count).ok_or(SolverError::RigRange)?;
    if end > b.rigs.len() {
        return Err(SolverError::RigRange);
    }
    // Every rig is checked before the first one runs, so a refused call has written nothing.
    for index in first..end {
        check_rig(b, index)?;
    }
    for index in first..end {
        let (off, n) = check_rig(b, index)?;
        let roff = b.rigs[index].ref_offset as usize;
        let rcount = b.rigs[index].ref_count as usize;
        let rig = &mut b.rigs[index];
        kernel::step_rig(
            cfg,
            rig,
            &b.defs[off..off + n],
            &mut b.state[off..off + n],
            &b.pose_prev[off..off + n],
            &b.pose_curr[off..off + n],
            b.colliders,
            &b.refs[roff..roff + rcount],
            b.fields,
        );
    }
    Ok(())
}

/// Writes the position and rotation of every particle of rigs `first` to `first + count` into `b.out`.
pub fn resolve_range(cfg: &StepConfig, b: &mut Buffers, first: usize, count: usize) -> Result<(), SolverError> {
    let end = first.checked_add(count).ok_or(SolverError::RigRange)?;
    if end > b.rigs.len() {
        return Err(SolverError::RigRange);
    }
    for index in first..end {
        check_rig(b, index)?;
    }
    for index in first..end {
        let (off, n) = check_rig(b, index)?;
        let rig = b.rigs[index];
        kernel::resolve_rig(
            cfg,
            &rig,
            &b.defs[off..off + n],
            &b.state[off..off + n],
            &b.pose_curr[off..off + n],
            &b.pose_rot[off..off + n],
            &mut b.out[off..off + n],
        );
    }
    Ok(())
}
