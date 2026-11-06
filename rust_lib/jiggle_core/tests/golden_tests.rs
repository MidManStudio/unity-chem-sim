// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/jiggle_core.md, section "tests/golden_tests.rs"
// ============================================================================
//! The golden scene: one fixed scenario whose result is folded into a single hash.
//!
//! The C# package runs the same scenario through its own `JiggleWorld` (see `JiggleParityTests.cs`,
//! test `GoldenSceneMatchesTheRustWorld`) and asserts the same literal. Both sides build the scene
//! from integer arithmetic and exact IEEE operations only (add, subtract, multiply, divide, floor, abs,
//! sqrt), never from sin, cos or pow, so the two languages see bit-identical inputs. If either
//! implementation changes behaviour, one of the two tests fails.
//!
//! When a deliberate change of the solver moves the hash, update `GOLDEN` here and `Golden` in the
//! C# test together, after the C# managed kernel, the Rust kernel and the native-vs-managed parity
//! tests all agree on the new behaviour.

use jiggle_core::math::{Q, V3};
use jiggle_core::params::AuthoringParams;
use jiggle_core::types::*;
use jiggle_core::world::World;

/// The hash of the golden scene. Keep in sync with `Golden` in JiggleParityTests.cs.
const GOLDEN: u64 = 0xbd735dd528713541;

struct Lcg(u32);

impl Lcg {
    fn next(&mut self) -> u32 {
        self.0 = self.0.wrapping_mul(1664525).wrapping_add(1013904223);
        self.0
    }
    fn unit(&mut self) -> f32 {
        (self.next() >> 8) as f32 * (1.0 / 16777216.0)
    }
    fn range(&mut self, lo: f32, hi: f32) -> f32 {
        lo + (hi - lo) * self.unit()
    }
}

/// A triangle wave in -1 to 1 from exact operations only.
fn tri(x: f32) -> f32 {
    let w = x - x.floor();
    (w - 0.5).abs() * 4.0 - 1.0
}

struct Fnv(u64);

impl Fnv {
    fn word(&mut self, v: u32) {
        for k in 0..4 {
            self.0 ^= ((v >> (8 * k)) & 0xff) as u64;
            self.0 = self.0.wrapping_mul(0x100000001b3);
        }
    }
    fn float(&mut self, v: f32) {
        self.word(v.to_bits());
    }
}

const RIGS: usize = 6;
const FRAMES: usize = 120;

fn build() -> (World, Vec<Vec<V3>>) {
    let mut rng = Lcg(20261105);
    let mut w = World::new();
    w.cfg.sleep_speed = 0.002;
    w.cfg.sleep_deviation = 0.05;
    w.cfg.sleep_frames = 6;
    w.cfg.wake_motion = 0.01;

    w.colliders.push(Collider { kind: COLLIDER_SPHERE, radius: 0.12, center: [0.4, 0.55, 0.0, 0.0], ..Collider::default() });
    w.colliders.push(Collider { kind: COLLIDER_CAPSULE, radius: 0.06, half_height: 0.25, center: [0.9, 0.5, 0.0, 0.0], axis: [0.0, 1.0, 0.0, 0.0], ..Collider::default() });
    w.colliders.push(Collider { kind: COLLIDER_PLANE, center: [0.0, 0.2, 0.0, 0.0], axis: [0.0, 1.0, 0.0, 0.0], ..Collider::default() });
    w.colliders.push(Collider {
        kind: COLLIDER_BOX,
        radius: 0.02,
        center: [1.6, 0.6, 0.0, 0.0],
        extents: [0.15, 0.1, 0.2, 0.0],
        rot: [0.0, 0.38268343, 0.0, 0.9238795],
        ..Collider::default()
    });
    w.colliders.push(Collider { kind: COLLIDER_INSIDE_SPHERE, radius: 1.0, center: [2.2, 0.8, 0.0, 0.0], ..Collider::default() });

    w.fields.push(Field { kind: FIELD_DIRECTIONAL, mode: FIELD_ACCELERATION, dir: [0.5, 0.0, 1.0, 0.8], strength: 6.0, turbulence: 0.6, frequency: 1.3, ..Field::default() });
    w.fields.push(Field { kind: FIELD_RADIAL, mode: FIELD_IMPULSE, power: 2, center: [1.0, 0.5, 0.0, 0.0], radius: 1.5, strength: 3.0, ..Field::default() });
    w.fields.push(Field { kind: FIELD_VORTEX, mode: FIELD_ACCELERATION, power: 1, center: [2.0, 0.5, 0.0, 0.0], dir: [0.0, 1.0, 0.0, 0.0], radius: 2.0, strength: 4.0, ..Field::default() });

    let mut rests = Vec::new();
    for r in 0..RIGS {
        let bones = 3 + r;
        let len = 0.12 + 0.01 * (r as f32);
        let anchor = V3::new(r as f32 * 0.45, 1.0, 0.0);
        let mut rest = vec![anchor];
        for i in 1..=bones {
            rest.push(V3::new(anchor.x + i as f32 * len, anchor.y - 0.05 * i as f32, anchor.z));
        }
        let mut params = Vec::new();
        for _ in 0..bones {
            params.push(AuthoringParams {
                stiffness: rng.range(0.2, 0.9),
                soften: rng.range(0.0, 0.5),
                root_stretch: rng.range(0.0, 0.3),
                angle_limit: 0.5,
                angle_limit_soften: rng.range(0.0, 0.5),
                stretch: rng.range(0.0, 0.4),
                drag: rng.range(0.0, 0.3),
                air_drag: rng.range(0.0, 0.2),
                gravity: rng.range(0.5, 1.5),
                collision_radius: 0.03,
                blend: rng.range(0.5, 1.0),
                ignore_root_motion: rng.range(0.0, 0.3),
                wind_response: rng.range(0.5, 1.5),
                impulse_response: rng.range(0.5, 1.5),
                angle_limited: r % 2 == 1,
                collide: true,
                segment_collision: r % 3 == 0,
            });
        }
        if r == 4 {
            // Rig 4 is the sleeper: no gravity and no field response, so it settles at once while it is held still.
            for p in &mut params {
                p.gravity = 0.0;
                p.wind_response = 0.0;
                p.impulse_response = 0.0;
            }
        }
        let rig = w.add_chain_with_tip(&rest, &params, 1.0 + (r % 3) as f32);
        // The baked cosine and sine of the angle limit come from the math library of each language and may
        // differ in the last bit, so the scene sets them to fixed literals.
        let off = w.rigs[rig].particle_offset as usize;
        let n = w.rigs[rig].particle_count as usize;
        for d in &mut w.defs[off..off + n] {
            if d.flags & FLAG_ANGLE_LIMIT != 0 {
                d.limit_cos = 0.8;
                d.limit_sin = 0.6;
            }
        }
        let list: Vec<u32> = if r % 2 == 0 { vec![0, 1, 2, 3, 4] } else { vec![0, 2, 4] };
        w.set_collider_list(rig, &list);
        rests.push(rest);
    }
    (w, rests)
}

fn pose(rests: &[Vec<V3>], r: usize, f: usize) -> (Vec<V3>, Vec<Q>) {
    let rest = &rests[r];
    let moving = !(r == 4 && f < 90);
    let jump = if r == 5 && f >= 80 { 9.0 } else { 0.0 };
    let ph = f as f32 * 0.037 + r as f32 * 0.31;
    let offset = if moving {
        V3::new(tri(ph) * 0.3 + jump, tri(ph * 1.7 + 0.2) * 0.1, tri(ph * 0.6 + 0.5) * 0.2)
    } else {
        V3::new(jump, 0.0, 0.0)
    };
    let mut out = Vec::new();
    let mut rots = Vec::new();
    for (i, p) in rest.iter().enumerate() {
        let sway = if moving && i > 0 {
            let q = ph + i as f32 * 0.11;
            V3::new(tri(q) * 0.04, tri(q * 1.3 + 0.1) * 0.04, tri(q * 0.7 + 0.3) * 0.04)
        } else {
            V3::ZERO
        };
        out.push(*p + offset + sway);
        let a = ph + i as f32 * 0.17;
        rots.push(Q { x: tri(a) * 0.1, y: tri(a * 1.9) * 0.2, z: 0.0, w: 1.0 }.normalized());
    }
    (out, rots)
}

fn frame_dt(rng: &mut Lcg, f: usize) -> f32 {
    if f == 70 {
        0.2
    } else if f % 17 == 5 {
        0.0
    } else {
        rng.range(0.004, 0.044)
    }
}

fn fold(h: &mut Fnv, w: &World) {
    for s in &w.state {
        for v in s.pos.iter().chain(s.prev.iter()) {
            h.float(*v);
        }
    }
    for o in &w.out {
        for v in o.pos.iter().chain(o.rot.iter()) {
            h.float(*v);
        }
    }
    for r in &w.rigs {
        h.word(r.flags);
        h.word(r.sleep_counter);
    }
}

/// What a run of the golden scene gives back: the hash and counts that show the scene does its job.
struct Run {
    hash: u64,
    asleep_frames: u32,
    awake_after_sleep: bool,
}

/// Runs the golden scene. `colliders` and `fields` switch those parts off to show they matter.
fn golden_scene(colliders: bool, fields: bool) -> Run {
    let (mut w, rests) = build();
    if !colliders {
        for r in &mut w.rigs {
            r.ref_count = 0;
        }
    }
    if !fields {
        w.fields.clear();
    }
    let mut rng = Lcg(77);
    let mut h = Fnv(0xcbf29ce484222325);
    let mut asleep_frames = 0;
    let mut was_asleep = false;
    let mut awake_after_sleep = false;
    for f in 0..FRAMES {
        for r in 0..RIGS {
            let (p, q) = pose(&rests, r, f);
            w.set_pose(r, &p, Some(&q));
        }
        if f == 30 {
            w.rigs[3].flags |= RIG_DISABLED;
        }
        if f == 40 {
            w.rigs[3].flags &= !RIG_DISABLED;
        }
        if f == 50 {
            w.rigs[2].flags |= RIG_RESET;
        }
        let dt = frame_dt(&mut rng, f);
        w.step(dt).unwrap();
        fold(&mut h, &w);
        let asleep = w.rigs[4].flags & RIG_ASLEEP != 0;
        if asleep {
            asleep_frames += 1;
        }
        if was_asleep && !asleep {
            awake_after_sleep = true;
        }
        was_asleep = asleep;
    }
    Run { hash: h.0, asleep_frames, awake_after_sleep }
}

#[test]
fn the_golden_scene_has_the_recorded_hash() {
    let a = golden_scene(true, true);
    // The scene is a pure function of its code, so a second run gives the same hash.
    assert_eq!(a.hash, golden_scene(true, true).hash);
    assert_eq!(a.hash, GOLDEN, "golden hash is now {:#018x}; see the note at the top of this file", a.hash);
}

#[test]
fn the_golden_scene_exercises_what_it_claims() {
    let full = golden_scene(true, true);
    // Rig 4 holds still until frame 90, so it falls asleep and then wakes when it starts to move.
    assert!(full.asleep_frames > 10, "rig 4 slept for {} frames", full.asleep_frames);
    assert!(full.awake_after_sleep);
    // Taking the colliders or the fields away changes the result, so both do something.
    assert_ne!(golden_scene(false, true).hash, full.hash);
    assert_ne!(golden_scene(true, false).hash, full.hash);
}
