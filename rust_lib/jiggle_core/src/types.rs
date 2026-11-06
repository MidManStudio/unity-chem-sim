// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "types.rs"
// ============================================================================
//! The `repr(C)` records the solver reads and writes.
//!
//! The host owns one flat array of each. Every struct holds only 4 byte fields,
//! has a size that is a multiple of 16, and carries explicit reserved fields,
//! so the layout is the same on every target and the C# declarations in
//! `JiggleTypes.cs` match field for field. `struct_size` and `layout_probe` let
//! a host check that at run time.

/// Largest particle count of one rig, anchor and virtual end particles included.
pub const MAX_PARTICLES: usize = 128;

/// Most coupling passes one step runs, see `StepConfig::coupling_passes`.
pub const MAX_COUPLING_PASSES: u32 = 16;

/// Largest mass ratio between a particle and its parent that the coupling uses. A link whose masses differ
/// more is split as if they differed by exactly this much. Position based solvers become erratic well beyond
/// it: a pinned strand stays smooth to about 64 to 1, a soft one starts to ring past 32 to 1.
pub const MAX_MASS_RATIO: f32 = 16.0;

/// Particle flag: follows the pose exactly, never simulated. Particle 0 always has it.
pub const FLAG_KINEMATIC: u32 = 1;
/// Particle flag: its parent is the anchor, so it is held to the pose by the root spring
/// instead of the shape, length and angle limit constraints.
pub const FLAG_ROOT: u32 = 2;
/// Particle flag: has no transform of its own, only gives the last bone a direction.
pub const FLAG_VIRTUAL: u32 = 4;
/// Particle flag: the angle limit applies.
pub const FLAG_ANGLE_LIMIT: u32 = 8;
/// Particle flag: collides with the rig's colliders.
pub const FLAG_COLLIDE: u32 = 16;
/// Particle flag: tests the bone segment, not only the particle, against colliders.
pub const FLAG_SEGMENT: u32 = 32;

/// Rig flag: skip this rig completely.
pub const RIG_DISABLED: u32 = 1;
/// Rig flag: asleep, so the output is the pose. The kernel sets and clears it.
pub const RIG_ASLEEP: u32 = 2;
/// Rig flag: snap to the pose on the next step. The kernel clears it.
pub const RIG_RESET: u32 = 4;

/// Collider kind: sphere.
pub const COLLIDER_SPHERE: u32 = 0;
/// Collider kind: capsule along an axis.
pub const COLLIDER_CAPSULE: u32 = 1;
/// Collider kind: one sided plane, the normal side is free.
pub const COLLIDER_PLANE: u32 = 2;
/// Collider kind: oriented box, optionally rounded.
pub const COLLIDER_BOX: u32 = 3;
/// Collider kind: particles are kept inside the sphere.
pub const COLLIDER_INSIDE_SPHERE: u32 = 4;

/// Field kind: a constant push along a direction, optionally limited to a sphere, optionally gusty.
pub const FIELD_DIRECTIONAL: u32 = 0;
/// Field kind: pushes away from a center, or pulls toward it when the strength is negative.
pub const FIELD_RADIAL: u32 = 1;
/// Field kind: pushes around an axis through the center.
pub const FIELD_VORTEX: u32 = 2;

/// Field mode: strength is an acceleration in meters per second squared, applied every step.
pub const FIELD_ACCELERATION: u32 = 0;
/// Field mode: strength is a velocity change in meters per second, applied on the first step of a frame only.
pub const FIELD_IMPULSE: u32 = 1;

/// What one particle is and how it is held to its parent. Built once per rig.
///
/// The parameters are baked: the authoring values in 0 to 1 are already turned
/// into the numbers the solver uses (see `params.rs`). Each particle's numbers
/// govern its own link to its parent.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct ParticleDef {
    /// Index of the parent inside the rig, smaller than this particle's own index, or -1 for particle 0.
    pub parent: i32,
    /// `FLAG_*` bits.
    pub flags: u32,
    /// Reserved, keep 0.
    pub reserved0: u32,
    /// Reserved, keep 0.
    pub reserved1: u32,
    /// Pull toward the shape of the parent's pose each step, 0 to 1. The authoring stiffness squared.
    pub angle_elasticity: f32,
    /// Pull toward the pose length of the bone each step, 0 to 1. One minus the authoring stretch, squared.
    pub length_elasticity: f32,
    /// How much the angle pull fades near the pose, 0 to 1. The authoring soften squared.
    pub soften: f32,
    /// Pull of a root particle toward its pose position, 0 to 1. Authoring `1 - root stretch`.
    pub root_elasticity: f32,
    /// Cosine of the angle limit.
    pub limit_cos: f32,
    /// Sine of the angle limit.
    pub limit_sin: f32,
    /// How softly the angle limit corrects, 0 to 1.
    pub limit_soften: f32,
    /// Share of the motion relative to the parent that is lost each step, 0 to 1.
    pub drag: f32,
    /// Share of the parent's own motion that is not followed each step, 0 to 1.
    pub air_drag: f32,
    /// Multiplier on the gravity of the step.
    pub gravity: f32,
    /// Collision radius in world units.
    pub collision_radius: f32,
    /// How much of the swing is applied to the output rotation, 0 to 1.
    pub blend: f32,
    /// Share of the anchor's motion that moves this particle along with it, 0 to 1.
    pub ignore_root_motion: f32,
    /// Multiplier on acceleration fields.
    pub wind_response: f32,
    /// Multiplier on impulse fields.
    pub impulse_response: f32,
    /// Relative mass for two way coupling. 0 keeps the link to the parent one way. A link is coupled only
    /// when this particle and its parent both have a mass above 0 and neither is kinematic.
    pub mass: f32,
}

/// The simulated state of one particle, relative to the rig anchor of the last step.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct ParticleState {
    /// Position now. The fourth float is padding.
    pub pos: [f32; 4],
    /// Position one step ago. The fourth float is padding.
    pub prev: [f32; 4],
}

/// One rig: the header the host fills before each frame. The kernel only touches `flags` and `sleep_counter`.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Rig {
    /// First particle of this rig in the shared particle arrays.
    pub particle_offset: u32,
    /// Number of particles, 2 to `MAX_PARTICLES`.
    pub particle_count: u32,
    /// First entry of this rig's collider list in the shared reference array.
    pub ref_offset: u32,
    /// Number of colliders in the list.
    pub ref_count: u32,
    /// `RIG_*` bits.
    pub flags: u32,
    /// Frames in a row the rig has been at rest.
    pub sleep_counter: u32,
    /// Farthest distance of any particle from the anchor, for field tests.
    pub extent: f32,
    /// Reserved, keep 0.
    pub reserved0: u32,
    /// How far the anchor moved in the world since the last frame.
    pub anchor_delta: [f32; 3],
    /// Reserved, keep 0.
    pub reserved1: f32,
    /// World position of the anchor now.
    pub anchor_world: [f32; 3],
    /// Reserved, keep 0.
    pub reserved2: f32,
}

/// Settings of one call.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct StepConfig {
    /// Fixed step length in seconds.
    pub dt: f32,
    /// Number of fixed steps to run for each rig in this frame.
    pub steps: u32,
    /// Time at the start of the frame in seconds, for gusts.
    pub time: f32,
    /// How far between the last two steps to show, 0 to 1. Used by resolve.
    pub alpha: f32,
    /// Gravity in world space, meters per second squared.
    pub gravity: [f32; 3],
    /// An anchor move farther than this in one frame snaps the rig to its pose. 0 turns the check off.
    pub teleport_distance: f32,
    /// A rig is at rest only while every particle moves slower than this, in meters per step.
    pub sleep_speed: f32,
    /// A rig is at rest only while every particle is closer to its pose than this, in meters.
    pub sleep_deviation: f32,
    /// Frames at rest before a rig falls asleep. 0 turns sleeping off.
    pub sleep_frames: u32,
    /// A sleeping rig wakes when the anchor or the pose moves farther than this in one frame, in meters.
    pub wake_motion: f32,
    /// Passes of the two way coupling in each step, 1 to `MAX_COUPLING_PASSES`. 0 counts as 1, more than the
    /// maximum counts as the maximum. Rigs without coupled links ignore it.
    pub coupling_passes: u32,
    /// Reserved, keep 0.
    pub reserved: [f32; 3],
}

/// A collider in world space.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Collider {
    /// `COLLIDER_*` kind.
    pub kind: u32,
    /// Reserved, keep 0.
    pub flags: u32,
    /// Sphere, capsule or inside sphere radius, or the rounding radius of a box.
    pub radius: f32,
    /// Half of the length of the capsule's axis between the two end sphere centers.
    pub half_height: f32,
    /// Center in world space.
    pub center: [f32; 4],
    /// Unit axis of a capsule, or the unit normal of a plane.
    pub axis: [f32; 4],
    /// Half sizes of a box.
    pub extents: [f32; 4],
    /// Orientation of a box as x, y, z, w.
    pub rot: [f32; 4],
}

/// A force field in world space.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Field {
    /// `FIELD_*` kind.
    pub kind: u32,
    /// `FIELD_ACCELERATION` or `FIELD_IMPULSE`.
    pub mode: u32,
    /// Exponent of the falloff from the center to the radius, 0 to 4. 0 is constant inside the radius.
    pub power: u32,
    /// Reserved, keep 0.
    pub reserved: u32,
    /// Center in world space.
    pub center: [f32; 4],
    /// Direction, or the axis of a vortex. The fourth float is the spatial frequency of gusts.
    pub dir: [f32; 4],
    /// Reach of the field. A directional field with 0 reaches everywhere.
    pub radius: f32,
    /// Acceleration or velocity change.
    pub strength: f32,
    /// Share of the strength added as random gusts, 0 for none.
    pub turbulence: f32,
    /// Gusts per second.
    pub frequency: f32,
}

/// What `resolve` gives back for one particle.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct ParticleOut {
    /// Position relative to the anchor of this frame. Add the anchor's world position for a world position.
    pub pos: [f32; 4],
    /// World rotation as x, y, z, w.
    pub rot: [f32; 4],
}

/// Struct kinds for `struct_size` and `layout_probe`.
pub const KIND_PARTICLE_DEF: i32 = 1;
/// See `KIND_PARTICLE_DEF`.
pub const KIND_PARTICLE_STATE: i32 = 2;
/// See `KIND_PARTICLE_DEF`.
pub const KIND_RIG: i32 = 3;
/// See `KIND_PARTICLE_DEF`.
pub const KIND_STEP_CONFIG: i32 = 4;
/// See `KIND_PARTICLE_DEF`.
pub const KIND_COLLIDER: i32 = 5;
/// See `KIND_PARTICLE_DEF`.
pub const KIND_FIELD: i32 = 6;
/// See `KIND_PARTICLE_DEF`.
pub const KIND_PARTICLE_OUT: i32 = 7;
/// See `KIND_PARTICLE_DEF`.
pub const KIND_SOLVER_ARGS: i32 = 8;

/// Size in bytes of the struct of a kind, or 0 for an unknown kind.
pub fn struct_size(kind: i32) -> usize {
    use core::mem::size_of;
    match kind {
        KIND_PARTICLE_DEF => size_of::<ParticleDef>(),
        KIND_PARTICLE_STATE => size_of::<ParticleState>(),
        KIND_RIG => size_of::<Rig>(),
        KIND_STEP_CONFIG => size_of::<StepConfig>(),
        KIND_COLLIDER => size_of::<Collider>(),
        KIND_FIELD => size_of::<Field>(),
        KIND_PARTICLE_OUT => size_of::<ParticleOut>(),
        KIND_SOLVER_ARGS => size_of::<crate::ffi::SolverArgs>(),
        _ => 0,
    }
}
