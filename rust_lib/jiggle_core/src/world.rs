// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "world.rs"
// ============================================================================
//! A safe, self contained world for Rust callers, tests and benches: it owns
//! the shared arrays, builds rigs from chains of world positions, and runs the
//! fixed step accumulator. Unity does not use it. Unity owns the arrays itself
//! and calls the C interface.

use crate::math::{Q, V3};
use crate::params::{anchor_def, AuthoringParams};
use crate::solver::{self, Buffers, SolverError};
use crate::types::*;

/// Shared arrays plus the settings of the fixed step loop.
pub struct World {
    /// Rig headers.
    pub rigs: Vec<Rig>,
    /// Particle definitions.
    pub defs: Vec<ParticleDef>,
    /// Particle states.
    pub state: Vec<ParticleState>,
    /// Pose positions of the last frame, relative to the anchor of the last frame.
    pub pose_prev: Vec<[f32; 4]>,
    /// Pose positions of this frame, relative to the anchor of this frame.
    pub pose_curr: Vec<[f32; 4]>,
    /// Pose rotations of this frame.
    pub pose_rot: Vec<[f32; 4]>,
    /// Output of the last `step`.
    pub out: Vec<ParticleOut>,
    /// Colliders.
    pub colliders: Vec<Collider>,
    /// Collider lists of all rigs.
    pub refs: Vec<u32>,
    /// Fields.
    pub fields: Vec<Field>,
    /// Per rig distance of the virtual end particle from the last bone, as a multiple of the last bone's length.
    pub tip_scale: Vec<f32>,
    /// Settings of the next step. `steps` and `alpha` are set by `step`.
    pub cfg: StepConfig,
    /// Fixed step length in seconds.
    pub fixed_dt: f32,
    /// Most fixed steps one `step` call runs. Leftover time beyond it is dropped.
    pub max_steps: u32,
    accumulator: f32,
}

impl World {
    /// An empty world with a 60 Hz step and gravity of 9.81 meters per second squared downward.
    pub fn new() -> World {
        let fixed_dt = 1.0 / 60.0;
        World {
            rigs: Vec::new(),
            defs: Vec::new(),
            state: Vec::new(),
            pose_prev: Vec::new(),
            pose_curr: Vec::new(),
            pose_rot: Vec::new(),
            out: Vec::new(),
            colliders: Vec::new(),
            refs: Vec::new(),
            fields: Vec::new(),
            tip_scale: Vec::new(),
            cfg: StepConfig {
                dt: fixed_dt,
                steps: 1,
                gravity: [0.0, -9.81, 0.0],
                teleport_distance: 5.0,
                sleep_speed: 0.0005,
                sleep_deviation: 0.002,
                sleep_frames: 0,
                wake_motion: 0.002,
                ..StepConfig::default()
            },
            fixed_dt,
            max_steps: 8,
            accumulator: 0.0,
        }
    }

    /// Adds a rig from the rest pose of a chain in world space. The virtual end particle continues the
    /// last bone by one bone length. See `add_chain_with_tip` to change that.
    pub fn add_chain(&mut self, rest_world: &[V3], params: &[AuthoringParams]) -> usize {
        self.add_chain_with_tip(rest_world, params, 1.0)
    }

    /// Adds a rig from the rest pose of a chain in world space.
    ///
    /// `rest_world[0]` is the anchor, the transform above the first bone. The
    /// others are the bones from the root down, each the child of the one
    /// before. `params` holds one entry per bone. A virtual end particle that
    /// continues the last bone is added, `tip_scale` times the length of the
    /// last bone away from it. The first bone is a root particle. Returns the rig index.
    ///
    /// A link is one way unless both of its particles have a `mass` (see `AuthoringParams::mass`): then the
    /// child also pulls its parent back. With no masses the virtual end particle cannot make the real bones
    /// swing slower or heavier. It only sets how far the last bone turns for a given sideways motion of its
    /// end, a shorter tip turning more and a longer one less. The virtual end particle takes the mass of the
    /// last bone, and `set_mass` gives it a weight of its own, which is how a weight hung on a hair strand
    /// is made.
    pub fn add_chain_with_tip(&mut self, rest_world: &[V3], params: &[AuthoringParams], tip_scale: f32) -> usize {
        assert!(rest_world.len() >= 2 && params.len() == rest_world.len() - 1);
        let bones = rest_world.len() - 1;
        let offset = self.defs.len();
        let tip_scale = if tip_scale.is_finite() && tip_scale > 0.0 { tip_scale } else { 1.0 };

        self.defs.push(anchor_def());
        for b in 0..bones {
            let flags = if b == 0 { FLAG_ROOT } else { 0 };
            self.defs.push(params[b].bake(b as i32, flags));
        }
        let mut tip_params = params[bones - 1];
        tip_params.collide = false;
        self.defs.push(tip_params.bake(bones as i32, FLAG_VIRTUAL));

        let n = bones + 2;
        let anchor = rest_world[0];
        let mut world_pose = rest_world.to_vec();
        world_pose.push(tip_position(rest_world, tip_scale));

        let mut extent = 0.0f32;
        for p in &world_pose {
            let d = (*p - anchor).length();
            if d > extent {
                extent = d;
            }
        }
        for p in &world_pose {
            let rel = *p - anchor;
            let mut a = [0.0f32; 4];
            rel.write4(&mut a);
            self.state.push(ParticleState { pos: a, prev: a });
            self.pose_prev.push(a);
            self.pose_curr.push(a);
            self.pose_rot.push(Q::IDENTITY.to4());
            self.out.push(ParticleOut::default());
        }
        let ref_offset = self.refs.len() as u32;
        let mut rig = Rig {
            particle_offset: offset as u32,
            particle_count: n as u32,
            ref_offset,
            ref_count: 0,
            extent,
            ..Rig::default()
        };
        anchor.write3(&mut rig.anchor_world);
        self.rigs.push(rig);
        self.tip_scale.push(tip_scale);
        self.rigs.len() - 1
    }

    /// Sets the mass of particle `particle` of a rig, counted in the rig: 1 is the first bone and the last
    /// index is the virtual end particle. 0 makes its links one way again. Panics on an index outside the rig.
    pub fn set_mass(&mut self, rig: usize, particle: usize, mass: f32) {
        let r = self.rigs[rig];
        assert!(particle >= 1 && particle < r.particle_count as usize);
        self.defs[r.particle_offset as usize + particle].mass = if mass > crate::params::MAX_MASS {
            crate::params::MAX_MASS
        } else if mass > 0.0 {
            mass
        } else {
            0.0
        };
    }

    /// Gives a rig the colliders it should test, by index into `colliders`.
    /// The list must be set before another rig is added or the arrays are
    /// rebuilt, because lists sit one after the other in `refs`.
    pub fn set_collider_list(&mut self, rig: usize, list: &[u32]) {
        let off = self.refs.len() as u32;
        self.refs.extend_from_slice(list);
        self.rigs[rig].ref_offset = off;
        self.rigs[rig].ref_count = list.len() as u32;
    }

    /// Sets the pose of a rig for the coming frame from world positions, anchor first, bones after.
    /// The virtual end particle continues the last bone. Rotations default to the identity.
    pub fn set_pose(&mut self, rig: usize, pose_world: &[V3], rots: Option<&[Q]>) {
        let r = self.rigs[rig];
        let off = r.particle_offset as usize;
        let n = r.particle_count as usize;
        assert_eq!(pose_world.len(), n - 1);
        let anchor = pose_world[0];
        let old_anchor = V3::from3(&r.anchor_world);
        let mut world = pose_world.to_vec();
        world.push(tip_position(pose_world, self.tip_scale[rig]));

        for i in 0..n {
            self.pose_prev[off + i] = self.pose_curr[off + i];
            let rel = world[i] - anchor;
            rel.write4(&mut self.pose_curr[off + i]);
            self.pose_curr[off + i][3] = 0.0;
            let q = match rots {
                Some(rs) if i < rs.len() => rs[i],
                _ => Q::IDENTITY,
            };
            self.pose_rot[off + i] = q.to4();
        }
        let delta = anchor - old_anchor;
        delta.write3(&mut self.rigs[rig].anchor_delta);
        anchor.write3(&mut self.rigs[rig].anchor_world);
    }

    /// Runs the fixed step accumulator for `frame_dt` seconds and then resolves every rig.
    ///
    /// A frame time that is not finite or is negative counts as 0. A small tolerance keeps a frame time
    /// that is an exact multiple of the fixed step from losing a step to rounding in the accumulator.
    pub fn step(&mut self, frame_dt: f32) -> Result<(), SolverError> {
        if frame_dt.is_finite() && frame_dt > 0.0 {
            self.accumulator += frame_dt;
        }
        let mut n = (self.accumulator / self.fixed_dt + 1e-4) as u32;
        if n > self.max_steps {
            n = self.max_steps;
            self.accumulator = 0.0;
        } else {
            let rest = self.accumulator - n as f32 * self.fixed_dt;
            self.accumulator = if rest > 0.0 { rest } else { 0.0 };
        }
        self.cfg.steps = n;
        let alpha = self.accumulator / self.fixed_dt;
        self.cfg.alpha = if alpha > 1.0 { 1.0 } else { alpha };
        self.run()
    }

    /// Runs exactly `steps` fixed steps and resolves, ignoring the accumulator. `alpha` is 1.
    pub fn step_fixed(&mut self, steps: u32) -> Result<(), SolverError> {
        self.cfg.steps = steps;
        self.cfg.alpha = 1.0;
        self.run()
    }

    fn run(&mut self) -> Result<(), SolverError> {
        self.cfg.dt = self.fixed_dt;
        let cfg = self.cfg;
        let count = self.rigs.len();
        let mut b = Buffers {
            particle_base: 0,
            rigs: &mut self.rigs,
            defs: &self.defs,
            state: &mut self.state,
            pose_prev: &self.pose_prev,
            pose_curr: &self.pose_curr,
            pose_rot: &self.pose_rot,
            out: &mut self.out,
            colliders: &self.colliders,
            refs: &self.refs,
            fields: &self.fields,
        };
        solver::step_range(&cfg, &mut b, 0, count)?;
        solver::resolve_range(&cfg, &mut b, 0, count)?;
        self.cfg.time += cfg.steps as f32 * self.fixed_dt;
        Ok(())
    }

    /// World position of particle `i` of a rig after the last step.
    pub fn world_pos(&self, rig: usize, i: usize) -> V3 {
        let r = &self.rigs[rig];
        V3::from4(&self.out[r.particle_offset as usize + i].pos) + V3::from3(&r.anchor_world)
    }

    /// World rotation of particle `i` of a rig after the last step.
    pub fn world_rot(&self, rig: usize, i: usize) -> Q {
        let r = &self.rigs[rig];
        Q::from4(&self.out[r.particle_offset as usize + i].rot)
    }
}

/// Where the virtual end particle goes: past the last bone, along its direction, `scale` bone lengths away.
fn tip_position(world: &[V3], scale: f32) -> V3 {
    let n = world.len();
    let last = world[n - 1];
    let before = world[n - 2];
    last + (last - before) * scale
}

impl Default for World {
    fn default() -> World {
        World::new()
    }
}
