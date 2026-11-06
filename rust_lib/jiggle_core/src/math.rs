// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "math.rs"
// ============================================================================
//! Small vector and quaternion helpers, only what the solver needs.
//!
//! Every operation is written in a fixed order with plain `f32` arithmetic, no
//! fused multiply-add and no libm calls beyond `sqrt` and `floor`, so the C#
//! port in `JiggleMath.cs` gives the same numbers.

use core::ops::{Add, AddAssign, Mul, Neg, Sub, SubAssign};

/// Three floats.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct V3 {
    /// X component.
    pub x: f32,
    /// Y component.
    pub y: f32,
    /// Z component.
    pub z: f32,
}

impl V3 {
    /// All zeros.
    pub const ZERO: V3 = V3 { x: 0.0, y: 0.0, z: 0.0 };
    /// World up, used as a fallback direction.
    pub const UP: V3 = V3 { x: 0.0, y: 1.0, z: 0.0 };
    /// Forward, used as a fallback direction.
    pub const FORWARD: V3 = V3 { x: 0.0, y: 0.0, z: 1.0 };

    /// Builds a vector.
    #[inline]
    pub const fn new(x: f32, y: f32, z: f32) -> V3 {
        V3 { x, y, z }
    }

    /// Reads the first three floats of a padded four float slot.
    #[inline]
    pub fn from4(a: &[f32; 4]) -> V3 {
        V3 { x: a[0], y: a[1], z: a[2] }
    }

    /// Reads a plain three float array.
    #[inline]
    pub fn from3(a: &[f32; 3]) -> V3 {
        V3 { x: a[0], y: a[1], z: a[2] }
    }

    /// Writes into a padded four float slot, leaving the fourth float alone.
    #[inline]
    pub fn write4(self, a: &mut [f32; 4]) {
        a[0] = self.x;
        a[1] = self.y;
        a[2] = self.z;
    }

    /// Writes into a plain three float array.
    #[inline]
    pub fn write3(self, a: &mut [f32; 3]) {
        a[0] = self.x;
        a[1] = self.y;
        a[2] = self.z;
    }

    /// Dot product.
    #[inline]
    pub fn dot(self, o: V3) -> f32 {
        self.x * o.x + self.y * o.y + self.z * o.z
    }

    /// Cross product.
    #[inline]
    pub fn cross(self, o: V3) -> V3 {
        V3 {
            x: self.y * o.z - self.z * o.y,
            y: self.z * o.x - self.x * o.z,
            z: self.x * o.y - self.y * o.x,
        }
    }

    /// Squared length.
    #[inline]
    pub fn length_sq(self) -> f32 {
        self.x * self.x + self.y * self.y + self.z * self.z
    }

    /// Length.
    #[inline]
    pub fn length(self) -> f32 {
        self.length_sq().sqrt()
    }

    /// The unit vector, or `fallback` when the length is too small to divide by.
    #[inline]
    pub fn normalize_safe(self, fallback: V3) -> V3 {
        let l2 = self.length_sq();
        if l2 > 1e-20 {
            self * (1.0 / l2.sqrt())
        } else {
            fallback
        }
    }

    /// `a + (b - a) * t`.
    #[inline]
    pub fn lerp(a: V3, b: V3, t: f32) -> V3 {
        a + (b - a) * t
    }

    /// Whether all three components are finite.
    #[inline]
    pub fn is_finite(self) -> bool {
        self.x.is_finite() && self.y.is_finite() && self.z.is_finite()
    }
}

impl Add for V3 {
    type Output = V3;
    #[inline]
    fn add(self, o: V3) -> V3 {
        V3 { x: self.x + o.x, y: self.y + o.y, z: self.z + o.z }
    }
}

impl Sub for V3 {
    type Output = V3;
    #[inline]
    fn sub(self, o: V3) -> V3 {
        V3 { x: self.x - o.x, y: self.y - o.y, z: self.z - o.z }
    }
}

impl Mul<f32> for V3 {
    type Output = V3;
    #[inline]
    fn mul(self, s: f32) -> V3 {
        V3 { x: self.x * s, y: self.y * s, z: self.z * s }
    }
}

impl Neg for V3 {
    type Output = V3;
    #[inline]
    fn neg(self) -> V3 {
        V3 { x: -self.x, y: -self.y, z: -self.z }
    }
}

impl AddAssign for V3 {
    #[inline]
    fn add_assign(&mut self, o: V3) {
        *self = *self + o;
    }
}

impl SubAssign for V3 {
    #[inline]
    fn sub_assign(&mut self, o: V3) {
        *self = *self - o;
    }
}

/// `a + (b - a) * t` for scalars.
#[inline]
pub fn lerpf(a: f32, b: f32, t: f32) -> f32 {
    a + (b - a) * t
}

/// A unit vector perpendicular to `a`, chosen from the axis `a` points along least.
#[inline]
pub fn perpendicular(a: V3) -> V3 {
    let ax = a.x.abs();
    let ay = a.y.abs();
    let az = a.z.abs();
    let axis = if ax <= ay && ax <= az {
        V3::new(1.0, 0.0, 0.0)
    } else if ay <= az {
        V3::new(0.0, 1.0, 0.0)
    } else {
        V3::new(0.0, 0.0, 1.0)
    };
    a.cross(axis).normalize_safe(V3::UP)
}

/// A quaternion, `w` last, Hamilton product, the same layout as Unity.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Q {
    /// X component.
    pub x: f32,
    /// Y component.
    pub y: f32,
    /// Z component.
    pub z: f32,
    /// W component.
    pub w: f32,
}

impl Q {
    /// No rotation.
    pub const IDENTITY: Q = Q { x: 0.0, y: 0.0, z: 0.0, w: 1.0 };

    /// Reads four floats as x, y, z, w.
    #[inline]
    pub fn from4(a: &[f32; 4]) -> Q {
        Q { x: a[0], y: a[1], z: a[2], w: a[3] }
    }

    /// Writes x, y, z, w.
    #[inline]
    pub fn to4(self) -> [f32; 4] {
        [self.x, self.y, self.z, self.w]
    }

    /// Scales to unit length. An all zero quaternion becomes the identity.
    #[inline]
    pub fn normalized(self) -> Q {
        let l2 = self.x * self.x + self.y * self.y + self.z * self.z + self.w * self.w;
        if l2 > 1e-20 {
            let inv = 1.0 / l2.sqrt();
            Q { x: self.x * inv, y: self.y * inv, z: self.z * inv, w: self.w * inv }
        } else {
            Q::IDENTITY
        }
    }

    /// `self * o`: `o` is applied first, as in Unity.
    #[inline]
    pub fn mul(self, o: Q) -> Q {
        Q {
            x: self.w * o.x + self.x * o.w + self.y * o.z - self.z * o.y,
            y: self.w * o.y - self.x * o.z + self.y * o.w + self.z * o.x,
            z: self.w * o.z + self.x * o.y - self.y * o.x + self.z * o.w,
            w: self.w * o.w - self.x * o.x - self.y * o.y - self.z * o.z,
        }
    }

    /// Rotates a vector.
    #[inline]
    pub fn rotate(self, v: V3) -> V3 {
        let u = V3::new(self.x, self.y, self.z);
        let t = u.cross(v) * 2.0;
        v + t * self.w + u.cross(t)
    }

    /// The conjugate, which is the inverse for a unit quaternion.
    #[inline]
    pub fn conjugate(self) -> Q {
        Q { x: -self.x, y: -self.y, z: -self.z, w: self.w }
    }

    /// The shortest rotation taking unit vector `a` to unit vector `b`, with no trigonometry.
    #[inline]
    pub fn from_to(a: V3, b: V3) -> Q {
        let d = a.dot(b);
        if d < -0.999_999 {
            let p = perpendicular(a);
            return Q { x: p.x, y: p.y, z: p.z, w: 0.0 };
        }
        let c = a.cross(b);
        Q { x: c.x, y: c.y, z: c.z, w: 1.0 + d }.normalized()
    }

    /// Scales the rotation `self` toward the identity by `t` in 0..1, by normalized lerp.
    /// Meant for rotations with a non negative `w`, which `from_to` always gives.
    #[inline]
    pub fn scaled_toward_identity(self, t: f32) -> Q {
        Q { x: self.x * t, y: self.y * t, z: self.z * t, w: (1.0 - t) + self.w * t }.normalized()
    }
}
