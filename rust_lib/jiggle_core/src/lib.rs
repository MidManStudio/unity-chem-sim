// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "lib.rs"
// ============================================================================
//! jiggle_core: jiggle physics kernels for bone chains, for MidManStudio games.
//!
//! A rig is a tree of particles, one per bone, hanging from an anchor that the
//! animation moves. The solver keeps the particles near the pose the animation
//! gives, lets them lag, swing and bounce with verlet integration, and gives
//! back a position and a rotation for every bone.
//!
//! The library works on flat arrays that the host owns. [`kernel`] is the
//! scalar reference for one rig, [`solver`] runs it over a range of rigs with
//! every offset checked, [`world`] is a safe owner of the arrays for Rust
//! callers, and [`ffi`] is the C interface Unity calls. A C# port of the kernel
//! on the same memory layout runs under Burst, and the two are checked against
//! each other.
//!
//! Units: meters, seconds, radians only inside baked cosines and sines.
//!
//! ```
//! use jiggle_core::math::V3;
//! use jiggle_core::params::AuthoringParams;
//! use jiggle_core::world::World;
//!
//! // A three bone chain hanging from an anchor at the origin, stretched along +x.
//! let rest = [V3::new(0.0, 0.0, 0.0), V3::new(0.2, 0.0, 0.0), V3::new(0.4, 0.0, 0.0), V3::new(0.6, 0.0, 0.0)];
//! let mut world = World::new();
//! let rig = world.add_chain(&rest, &[AuthoringParams::default(); 3]);
//! for _ in 0..120 {
//!     world.set_pose(rig, &rest, None);
//!     world.step_fixed(1).unwrap();
//! }
//! // Gravity pulls the tip below its pose, and the stiffness keeps it from falling far.
//! let tip = world.world_pos(rig, 3);
//! assert!(tip.y < 0.0 && tip.y > -0.5);
//! ```

// The kernel is written to match the C# port line by line, so it keeps plain index loops over the parallel
// arrays, explicit `a = a + b` and its own clamp and range helpers. Clippy would rewrite those into forms
// that no longer line up with the C# code, so these style lints are off here.
#![allow(
    clippy::needless_range_loop,
    clippy::assign_op_pattern,
    clippy::manual_clamp,
    clippy::manual_range_contains,
    clippy::too_many_arguments,
    clippy::should_implement_trait
)]

pub mod collide;
pub mod ffi;
pub mod field;
pub mod kernel;
pub mod math;
pub mod params;
pub mod solver;
pub mod types;
pub mod world;

pub use math::{Q, V3};
pub use params::AuthoringParams;
pub use types::*;
pub use world::World;
