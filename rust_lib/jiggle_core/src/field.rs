// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "field.rs"
// ============================================================================
//! Wind and force fields.
//!
//! A field gives a push at a point. Mode acceleration fields are summed every
//! step and scaled by the particle's wind response. Mode impulse fields are a
//! velocity change that the kernel applies on the first step of a frame only.
//! Gusts use a hash based value noise, so the same time and place give the same
//! push on every machine and in the C# port.

use crate::math::V3;
use crate::types::*;

#[inline]
fn hash_unit(i: i32, channel: u32) -> f32 {
    let mut h = (i as u32).wrapping_mul(0x9E37_79B1) ^ channel.wrapping_mul(0x85EB_CA6B);
    h ^= h >> 15;
    h = h.wrapping_mul(0x2C1B_3C6D);
    h ^= h >> 12;
    h = h.wrapping_mul(0x297A_2D39);
    h ^= h >> 15;
    ((h >> 8) as f32) * (2.0 / 16_777_216.0) - 1.0
}

/// Smooth value noise in -1 to 1 along one axis.
#[inline]
pub fn noise1(x: f32, channel: u32) -> f32 {
    let f = x.floor();
    let i = f as i32;
    let t = x - f;
    let s = t * t * (3.0 - 2.0 * t);
    let a = hash_unit(i, channel);
    let b = hash_unit(i.wrapping_add(1), channel);
    a + (b - a) * s
}

/// Three noise channels as a vector, each in -1 to 1.
#[inline]
pub fn noise_vec(phase: f32) -> V3 {
    V3::new(noise1(phase, 1), noise1(phase + 17.31, 2), noise1(phase + 41.7, 3))
}

#[inline]
fn falloff(u: f32, power: u32) -> f32 {
    if u >= 1.0 {
        return 0.0;
    }
    let t = if u < 0.0 { 1.0 } else { 1.0 - u };
    let mut r = 1.0;
    let mut k = 0;
    while k < power {
        r *= t;
        k += 1;
    }
    r
}

/// The push of field `f` at a particle.
///
/// `center_rel` is the field center and `p_rel` the particle position, both in
/// the rig frame. `p_world` is the particle in world space and `time` the time
/// in seconds, used only for gusts.
pub fn push(f: &Field, center_rel: V3, p_rel: V3, p_world: V3, time: f32) -> V3 {
    match f.kind {
        FIELD_DIRECTIONAL => {
            let mut w = 1.0;
            if f.radius > 0.0 {
                let d = (p_rel - center_rel).length();
                w = falloff(d / f.radius, f.power);
            }
            if w <= 0.0 {
                return V3::ZERO;
            }
            let mut v = V3::new(f.dir[0], f.dir[1], f.dir[2]);
            if f.turbulence != 0.0 {
                let k = V3::new(0.37, 0.53, 0.29);
                let phase = time * f.frequency + p_world.dot(k) * f.dir[3];
                v = v + noise_vec(phase) * f.turbulence;
            }
            v * (f.strength * w)
        }
        FIELD_RADIAL => {
            if f.radius <= 0.0 {
                return V3::ZERO;
            }
            let d = p_rel - center_rel;
            let dist = d.length();
            let w = falloff(dist / f.radius, f.power);
            if w <= 0.0 {
                return V3::ZERO;
            }
            let dir = d.normalize_safe(V3::UP);
            dir * (f.strength * w)
        }
        FIELD_VORTEX => {
            if f.radius <= 0.0 {
                return V3::ZERO;
            }
            let d = p_rel - center_rel;
            let dist = d.length();
            let w = falloff(dist / f.radius, f.power);
            if w <= 0.0 {
                return V3::ZERO;
            }
            let axis = V3::new(f.dir[0], f.dir[1], f.dir[2]).normalize_safe(V3::UP);
            let radial = d - axis * d.dot(axis);
            let tangent = axis.cross(radial).normalize_safe(V3::ZERO);
            tangent * (f.strength * w)
        }
        _ => V3::ZERO,
    }
}

/// Whether the field can reach a rig whose anchor is at `anchor_world` and whose particles stay within `extent` of it.
pub fn reaches(f: &Field, anchor_world: V3, extent: f32) -> bool {
    if f.strength == 0.0 {
        return false;
    }
    if f.kind == FIELD_DIRECTIONAL && f.radius <= 0.0 {
        return true;
    }
    if f.radius <= 0.0 {
        return false;
    }
    let c = V3::from4(&f.center);
    (anchor_world - c).length() < f.radius + extent
}
