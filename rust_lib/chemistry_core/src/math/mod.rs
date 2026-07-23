// SIMD math layer — ported from projectile_core.
// Copy src/math/f32x4.rs and src/math/vec3x4.rs from the projectile_core crate.
// Remove vec2x4.rs (not needed for 3D-only chemistry sim).
// Remove all NativeProjectile / NativeProjectile3D references from vec3x4.rs:
//   change load_pos/load_vel/store_pos/store_vel to use AtomState.
pub mod f32x4;
pub mod vec3x4;
pub use vec3x4::Vec3x4;
