// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "params.rs"
// ============================================================================
//! Authoring values and how they are baked into the numbers the solver uses.
//!
//! Authors set values in 0 to 1 that are easy to reason about. The solver gets
//! squares and cosines of them, computed once per rig by `AuthoringParams::bake`
//! and not every step.

use crate::types::*;

/// Largest mass `AuthoringParams::bake` keeps. Only ratios matter, so this just keeps sums finite.
pub const MAX_MASS: f32 = 1.0e6;

/// The values an author sets for one bone.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct AuthoringParams {
    /// How hard the bone is pulled back to its pose, 0 to 1.
    pub stiffness: f32,
    /// How much the pull fades near the pose, 0 to 1.
    pub soften: f32,
    /// How far a root bone may move away from its pose position, 0 to 1.
    pub root_stretch: f32,
    /// Largest swing from the pose, 0 to 1, where 1 is 90 degrees.
    pub angle_limit: f32,
    /// How softly the angle limit corrects, 0 to 1.
    pub angle_limit_soften: f32,
    /// How much the bone length may change, 0 to 1.
    pub stretch: f32,
    /// Friction against the parent, 0 to 1.
    pub drag: f32,
    /// Friction against moving through the world, 0 to 1.
    pub air_drag: f32,
    /// Multiplier on gravity.
    pub gravity: f32,
    /// Collision radius in world units.
    pub collision_radius: f32,
    /// Share of the swing applied to the rotation, 0 to 1.
    pub blend: f32,
    /// Share of the anchor's motion that moves the bone along with it, 0 to 1.
    pub ignore_root_motion: f32,
    /// Multiplier on acceleration fields.
    pub wind_response: f32,
    /// Multiplier on impulse fields.
    pub impulse_response: f32,
    /// Relative mass for two way coupling, 0 for none. A bone pulls its parent back only when both have a mass
    /// above 0. Only the ratio between neighbours matters. Values above `MAX_MASS` count as `MAX_MASS`.
    pub mass: f32,
    /// Whether the angle limit applies.
    pub angle_limited: bool,
    /// Whether the bone collides.
    pub collide: bool,
    /// Whether the whole bone segment collides, not only its end.
    pub segment_collision: bool,
}

impl Default for AuthoringParams {
    fn default() -> AuthoringParams {
        AuthoringParams {
            stiffness: 0.8,
            soften: 0.0,
            root_stretch: 0.0,
            angle_limit: 0.5,
            angle_limit_soften: 0.0,
            stretch: 0.1,
            drag: 0.1,
            air_drag: 0.0,
            gravity: 1.0,
            collision_radius: 0.1,
            blend: 1.0,
            ignore_root_motion: 0.0,
            wind_response: 1.0,
            impulse_response: 1.0,
            mass: 0.0,
            angle_limited: false,
            collide: false,
            segment_collision: false,
        }
    }
}

#[inline]
fn clamp01(v: f32) -> f32 {
    if v < 0.0 {
        0.0
    } else if v > 1.0 {
        1.0
    } else {
        v
    }
}

impl AuthoringParams {
    /// Turns the authoring values into the baked definition of a particle.
    ///
    /// `parent` is the index of the parent in the rig, -1 for the anchor.
    /// `extra_flags` adds `FLAG_KINEMATIC`, `FLAG_ROOT` or `FLAG_VIRTUAL`.
    pub fn bake(&self, parent: i32, extra_flags: u32) -> ParticleDef {
        let stiffness = clamp01(self.stiffness);
        let stretch = clamp01(self.stretch);
        let soften = clamp01(self.soften);
        let limit = clamp01(self.angle_limit) * core::f32::consts::FRAC_PI_2;
        let mut flags = extra_flags;
        if self.angle_limited {
            flags |= FLAG_ANGLE_LIMIT;
        }
        if self.collide {
            flags |= FLAG_COLLIDE;
        }
        if self.segment_collision {
            flags |= FLAG_SEGMENT;
        }
        ParticleDef {
            parent,
            flags,
            reserved0: 0,
            reserved1: 0,
            angle_elasticity: stiffness * stiffness,
            length_elasticity: (1.0 - stretch) * (1.0 - stretch),
            soften: soften * soften,
            root_elasticity: 1.0 - clamp01(self.root_stretch),
            limit_cos: limit.cos(),
            limit_sin: limit.sin(),
            limit_soften: clamp01(self.angle_limit_soften),
            drag: clamp01(self.drag),
            air_drag: clamp01(self.air_drag),
            gravity: self.gravity,
            collision_radius: if self.collision_radius > 0.0 { self.collision_radius } else { 0.0 },
            blend: clamp01(self.blend),
            ignore_root_motion: clamp01(self.ignore_root_motion),
            wind_response: self.wind_response,
            impulse_response: self.impulse_response,
            mass: if self.mass > MAX_MASS {
                MAX_MASS
            } else if self.mass > 0.0 {
                self.mass
            } else {
                0.0
            },
        }
    }
}

/// The definition of the anchor particle: kinematic, no parent.
pub fn anchor_def() -> ParticleDef {
    ParticleDef {
        parent: -1,
        flags: FLAG_KINEMATIC,
        angle_elasticity: 1.0,
        length_elasticity: 1.0,
        root_elasticity: 1.0,
        limit_cos: 1.0,
        blend: 1.0,
        ..ParticleDef::default()
    }
}
