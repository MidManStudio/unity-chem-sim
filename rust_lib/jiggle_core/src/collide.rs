// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "collide.rs"
// ============================================================================
//! Pushing a particle out of a collider.
//!
//! A particle is a sphere of radius `r`. Each function takes the collider's
//! center already moved into the rig frame and returns the particle position
//! after it has been pushed out along the shortest way, or the same position
//! when nothing touches.

use crate::math::{Q, V3};
use crate::types::*;

#[inline]
fn clampf(v: f32, lo: f32, hi: f32) -> f32 {
    if v < lo {
        lo
    } else if v > hi {
        hi
    } else {
        v
    }
}

#[inline]
fn push_from_core(core: V3, min_dist: f32, x: V3) -> V3 {
    let d = x - core;
    let l2 = d.length_sq();
    if l2 >= min_dist * min_dist {
        return x;
    }
    let l = l2.sqrt();
    let n = if l > 1e-6 { d * (1.0 / l) } else { V3::UP };
    core + n * min_dist
}

/// Pushes the particle at `x` with radius `r` out of `c`, whose center in the rig frame is `center`.
pub fn push_out(c: &Collider, center: V3, x: V3, r: f32) -> V3 {
    match c.kind {
        COLLIDER_SPHERE => push_from_core(center, c.radius + r, x),
        COLLIDER_CAPSULE => {
            let axis = V3::from4(&c.axis);
            let t = clampf((x - center).dot(axis), -c.half_height, c.half_height);
            let core = center + axis * t;
            push_from_core(core, c.radius + r, x)
        }
        COLLIDER_PLANE => {
            let n = V3::from4(&c.axis);
            let s = (x - center).dot(n);
            if s < r {
                x + n * (r - s)
            } else {
                x
            }
        }
        COLLIDER_BOX => push_box(c, center, x, r),
        COLLIDER_INSIDE_SPHERE => {
            let d = x - center;
            let l = d.length();
            let mut max_dist = c.radius - r;
            if max_dist < 0.0 {
                max_dist = 0.0;
            }
            if l > max_dist && l > 1e-6 {
                center + d * (max_dist / l)
            } else {
                x
            }
        }
        _ => x,
    }
}

fn push_box(c: &Collider, center: V3, x: V3, r: f32) -> V3 {
    let q = Q::from4(&c.rot);
    let inv = q.conjugate();
    let local = inv.rotate(x - center);
    let half = V3::from4(&c.extents);
    let rr = c.radius + r;
    let clamped = V3::new(
        clampf(local.x, -half.x, half.x),
        clampf(local.y, -half.y, half.y),
        clampf(local.z, -half.z, half.z),
    );
    let d = local - clamped;
    let l2 = d.length_sq();
    if l2 > 1e-12 {
        // Outside the core box: the same as a sphere around the closest point.
        if l2 >= rr * rr {
            return x;
        }
        let l = l2.sqrt();
        let n = d * (1.0 / l);
        let pushed = clamped + n * rr;
        return center + q.rotate(pushed);
    }
    // Inside the core box: leave through the nearest face.
    let px = half.x - local.x.abs();
    let py = half.y - local.y.abs();
    let pz = half.z - local.z.abs();
    let mut out = local;
    if px <= py && px <= pz {
        let s = if local.x >= 0.0 { 1.0 } else { -1.0 };
        out.x = s * (half.x + rr);
    } else if py <= pz {
        let s = if local.y >= 0.0 { 1.0 } else { -1.0 };
        out.y = s * (half.y + rr);
    } else {
        let s = if local.z >= 0.0 { 1.0 } else { -1.0 };
        out.z = s * (half.z + rr);
    }
    center + q.rotate(out)
}
