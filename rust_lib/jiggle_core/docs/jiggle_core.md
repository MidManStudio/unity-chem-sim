# jiggle_core

`jiggle_core` is the native half of the All Things Jiggle package. It solves
jiggle physics for chains of bones: hair, tails, ears, strips of cloth, chests.
A rig is a chain of particles, one per bone, hanging from an anchor that the
animation moves. The solver keeps every particle near the pose the animation
gives, lets it lag, swing and bounce, and gives back a position and a rotation
for every bone.

The crate has no dependencies. It works on flat arrays that the host owns, so
the same memory can go to the scalar Rust kernel, to the managed C# kernel in
`packages/com.midmanstudio.allthingsjiggle`, and later to Burst jobs. The Rust
kernel is the reference. The C# kernel is a line by line port, and CI checks that
both give the same bits on the same memory.

This file is the documentation of the crate. The C# side is documented in
`packages/com.midmanstudio.allthingsjiggle/docs/com.midmanstudio.allthingsjiggle.md`.

## Contents

- How a frame runs
- Files: lib.rs, math.rs, types.rs, collide.rs, field.rs, params.rs, kernel.rs,
  solver.rs, world.rs, ffi/mod.rs
- Tests and benchmark: tests/kernel_tests.rs, tests/ffi_tests.rs,
  tests/golden_tests.rs, benches/step_bench.rs
- Cargo.toml and the workspace
- What the MMD rig study taught the design
- CI and workflows
- Known limits
- Fixes and Problems

## How a frame runs

The host fills the record arrays once when it builds the rigs. Every frame it
writes the pose the animation made, then makes two calls:

1. `jgl_step_rigs` runs `cfg.steps` fixed steps for a range of rigs.
2. `jgl_resolve_rigs` turns the state into an output position and rotation for
   every particle, interpolated between the last two steps by `cfg.alpha`.

The fixed step count comes from an accumulator. `World::step` in Rust and
`JiggleWorld.Step` in C# implement it, and a host with its own buffers does the
same sum itself. Each fixed step does this for one rig:

1. Move the frame origin to the anchor of this step. Every particle is shifted by
   the anchor motion of the step, except for the share its `ignore_root_motion`
   keeps in the world. Kinematic particles are set to the pose.
2. Integrate with verlet. A particle keeps the velocity of its parent without
   the air drag share, and its own velocity relative to the parent without the
   drag share. Gravity and the acceleration fields are added, and impulse
   fields on the first step of a frame only.
3. Constrain the particles parent first. A root particle is pulled toward its
   pose position by the root spring. Any other particle is pulled toward the
   pose of its bone as seen from the parent's current orientation, restored
   to its pose length, kept inside the angle limit cone if it has one, and
   pushed out of colliders last, so nothing can push it back in.
4. Store the new state. A non finite value falls back to the pose.

After the steps, a rig that stayed within `sleep_deviation` of its pose and under
`sleep_speed` for `sleep_frames` frames falls asleep and outputs the pose until
something wakes it.

A rig is a tree stored as arrays: particle 0 is the anchor, kinematic with no
parent, and every other particle has a parent with a smaller index. The first
bone is a root particle. The last entry is a virtual particle that continues the
last bone, so the last bone has a direction to rotate toward.

## lib.rs

The crate root. It holds the crate documentation with a doctest (a three bone
chain sags under gravity but stays near its pose), declares the modules and
re-exports the main types.

A crate level `allow` switches off five clippy style lints: `needless_range_loop`,
`assign_op_pattern`, `manual_clamp`, `manual_range_contains`, `too_many_arguments`
and `should_implement_trait`. The kernel is written to match the C# port line by
line, with plain index loops over parallel arrays, explicit `a = a + b` and its
own clamp helpers, and the lint suggestions would produce code that no longer
lines up with the C# file.

## math.rs

Small vector and quaternion types, `V3` and `Q`, and only the operations the
solver needs. Every operation is written in a fixed order with plain `f32`
arithmetic. There is no fused multiply add and the only library calls are `sqrt`
and `floor`, both exact under IEEE 754. That is what lets the C# port, which
lives in `JiggleMath.cs`, give the same bits.

`V3::normalize_safe` takes a fallback for vectors too short to normalize, and
`perpendicular` picks a unit vector perpendicular to its argument from the axis
the argument points along least. `Q::from_to` builds the shortest rotation between
two unit vectors and `scaled_toward_identity` blends a rotation toward the
identity, which is how the `blend` parameter works.

## types.rs

The `repr(C)` records the solver reads and writes, and the flag and kind
constants. Every record holds only 4 byte fields, has a size that is a multiple of
16, and carries explicit reserved fields, so the layout is the same on every
target.

| Record | Size | What it is |
| --- | --- | --- |
| `ParticleDef` | 80 | Baked parameters of one particle, parent index, flags |
| `ParticleState` | 32 | Position now and one step ago, relative to the rig anchor |
| `Rig` | 64 | Particle range, collider list range, flags, sleep counter, anchor motion |
| `StepConfig` | 64 | Step length, step count, time, alpha, gravity, sleep and teleport settings |
| `Collider` | 80 | Kind, radius, half height, center, axis, extents, rotation |
| `Field` | 64 | Kind, mode, power, center, direction, radius, strength, gusts |
| `ParticleOut` | 32 | Output position and rotation |
| `SolverArgs` | 112 | Ten 64 bit addresses and five counts |

Particle flags are kinematic, root, virtual, angle limit, collide and segment.
Rig flags are disabled, asleep and reset. Collider kinds are sphere, capsule,
plane, box and inside sphere. Field kinds are directional, radial and vortex, and
field modes are acceleration and impulse. `struct_size` gives the size for a kind
number and is what `jgl_struct_size` returns. The largest rig is `MAX_PARTICLES`,
128, anchor and virtual end included, so 126 bones.

## collide.rs

`push_out` moves a particle, a sphere of radius `r`, out of a collider along the
shortest way and returns it unchanged when nothing touches. The collider center
is already in the rig frame.

- Sphere and capsule push from the closest point of the core. A capsule clamps the
  position along its axis to the half height first.
- Plane is one sided: the side the normal points to is free. A particle closer
  than its radius to the plane, or behind it, is pushed to the radius.
- Box is an oriented box, optionally rounded by the collider radius. Outside the
  core box it acts like a sphere around the closest point. Inside it leaves
  through the nearest face.
- Inside sphere keeps the particle inside the sphere, at most radius minus the
  particle radius from the center.

## field.rs

Wind and force fields. A field gives a push at a point.

- Directional pushes along a direction. With a radius of 0 it reaches everywhere,
  and with a radius it fades out toward the edge. Gusts are added when the
  turbulence is not 0.
- Radial pushes away from the center, or toward it with a negative strength. It
  needs a radius.
- Vortex pushes around an axis through the center. It needs a radius.

The falloff is `(1 - distance / radius)` raised to `power`. Power 0 is constant
inside the radius and 0 outside. A power above 4 works but is not meant: it is
a loop, and values that high make the field almost invisible.

Acceleration fields are summed every step and scaled by the particle's wind
response. Impulse fields are a velocity change that the kernel applies on the first
step of a frame only, scaled by the particle's impulse response.

Gusts use hash based value noise. The hash is integer arithmetic and the blend is
a smoothstep, so the same time and place give the same push on every machine
and in the C# port. `reaches` tells the sleep code whether a field can touch a rig,
so a sleeping rig wakes when a field comes near.

## params.rs

Authoring values and how they are baked. An author sets values in 0 to 1 that are
easy to reason about. `AuthoringParams::bake` turns them into what the solver uses,
once per rig:

| Authoring value | Baked to |
| --- | --- |
| `stiffness` | `angle_elasticity` = stiffness squared |
| `stretch` | `length_elasticity` = (1 - stretch) squared |
| `soften` | `soften` = soften squared |
| `root_stretch` | `root_elasticity` = 1 - root_stretch (the kernel squares it again for the root spring) |
| `angle_limit` | `limit_cos` and `limit_sin` of angle_limit times 90 degrees |
| `drag`, `air_drag`, `blend`, `ignore_root_motion` | clamped to 0 to 1 |
| `collision_radius` | clamped to 0 or more |
| `gravity`, `wind_response`, `impulse_response` | passed through |

`anchor_def` is the definition of particle 0: kinematic, no parent. The squares
make the middle of the slider feel like the middle: a stiffness of 0.5 pulls a
quarter of the way back each step, not half.

## kernel.rs

The scalar solver for one rig and the reference every other backend is checked
against. It favors being plain and exact over being fast. `step_rig` runs the
steps of a frame and `resolve_rig` turns the state into output.

Special paths of `step_rig`, in the order the kernel checks them:

- A rig with a bad particle count or the disabled flag is skipped.
- A reset flag, a teleport (an anchor move farther than `teleport_distance`), or a
  non finite anchor move snaps every particle to the pose and clears the flags.
- A sleeping rig snaps to the pose each frame until the anchor, a pose point or a
  field that reaches it moves more than `wake_motion`.
- Zero steps in a frame only move the frame origin to the new anchor and hold the
  kinematic particles at the pose, so the anchor motion is not lost.

Collision tests the particle at its radius against each collider of the rig's
list. With the segment flag it also tests the midpoint between the particle and
its parent and moves the particle by twice the midpoint's correction. That is
two samples of the bone, the end and the middle, not a full segment test.

`resolve_rig` interpolates positions between the last two states by `alpha`. The
rotation of a particle is its pose rotation turned by the swing from the average
direction of its children in the pose to their direction in the simulation, scaled
toward the identity by `blend`. Kinematic particles, particles without children,
and sleeping or disabled rigs output the pose.

The solver is one way. A particle is pulled by its parent and never pulls it back,
and there are no masses. That is why the virtual end particle cannot make the
real bones swing slower. See Known limits.

## solver.rs

Runs the kernel over a range of rigs in shared arrays with every offset checked
first. The C interface and `World` both call it. `Buffers` holds the arrays. The
particle arrays can be a window into larger host arrays, with `particle_base` as
the global index of their first entry, so each thread of a parallel host gets a
window of its own rigs.

Before the first rig of a call runs, every rig of the call is checked: particle
count, particle range, collider list range, and topology (particle 0 kinematic
with parent -1, every other parent earlier). A refused call has written nothing.
The error says which rig and what is wrong.

## world.rs

A safe owner of all the arrays for Rust callers, tests and benches. It builds rigs
from chains of world positions, runs the fixed step accumulator and calls the
solver.

`add_chain_with_tip` takes the rest pose of a chain with the anchor first. It adds
the virtual end particle `tip_scale` bone lengths past the last bone. A bad scale
falls back to 1. The scale changes only how far the last bone turns for a sideways
motion of its end: shorter turns it more, longer turns it less. It does not change
where any real bone goes.

`step` is the accumulator. A frame time that is not finite or is negative counts as
0. The step count is `(accumulator / fixed_dt + 1e-4)` truncated, so a frame time that
is an exact multiple of the fixed step cannot lose a step to rounding. More than
`max_steps` steps drop the rest of the accumulated time. The remainder is clamped to
0 or more and `alpha` is clamped to 1. `step_fixed` runs an exact number of steps with
`alpha` 1.

## ffi/mod.rs

The C interface Unity calls. Seven functions, all prefixed `jgl_`:

| Function | What it does |
| --- | --- |
| `jgl_abi_version` | Interface version, 1 now. The host compares it with the one it was written for |
| `jgl_library_version` | Crate version as text, like `snprintf` |
| `jgl_last_error` | Text of the last failure on the calling thread |
| `jgl_struct_size` | Size of a record by kind number, 0 for an unknown kind |
| `jgl_layout_probe` | Fills a record with known values so the host checks its offsets |
| `jgl_step_rigs` | Runs the fixed steps for rigs `first` to `first + count` |
| `jgl_resolve_rigs` | Writes the output for rigs `first` to `first + count` |

Status codes are 0 for success and negative otherwise: null address (-1), range
or offset outside an array (-2), panic caught (-3), bad rig (-4) and bad
argument (-5). The last error text is a fixed 256 byte thread local array with no
destructor, so thread exit runs no code that could be unloaded already. A success
leaves the text alone.

The layout probe fills integer fields with 1, 2, 3 and so on and float fields with
1.5, 2.5 and so on, each counting in the order of the fields in memory. A host reads
its own struct back from the bytes and compares, which proves every offset.

Rigs of a threaded call must be stored with ascending particle offsets and no
overlap inside the range. The call cuts the particle arrays to the window the range
covers, so two threads on disjoint ranges never hold overlapping slices. The
library keeps no state between calls.

The panic guard turns a caught panic into -3. The release profile of the
workspace sets `panic = "abort"`, so in the libraries CI builds a panic ends the
process and the guard never answers. See Known limits.

The module is the only one that opts out of the `unsafe_code` lint. A null address
with a count of 0 is fine. A null address with a nonzero count is refused before the
offset is added.

## tests/kernel_tests.rs

47 behavior tests of the kernel through `World`: stability over many steps,
gravity direction, stiffness ordering, bone lengths with and without stretch, jerk
decay, drag and air drag, the angle limit bound, determinism, every collider kind
and the segment test, fields and gusts, impulses once per frame, sleep and wake,
teleport, reset and disabled rigs, the zero step path, `ignore_root_motion`, the
resolve rotation with blend and alpha, NaN sanitising, the largest rig, many steps
per frame, the accumulator, the solver errors, rig isolation and the `tip_scale`
effect.

## tests/ffi_tests.rs

14 tests of the C interface: version and interface number, the eight record
sizes, the probe words of every record, probe errors, the call through the C
interface against `World` bit for bit over 60 frames with 23 rigs, four threads on
batches against one call, and the error paths (null pointers, empty calls, a range
past the rig count, a null array with a nonzero offset, bad rigs refused before
anything is written, a bad rig late in a range, per thread last error text, and the
rig array updated in place).

## tests/golden_tests.rs

The golden scene: six rigs, five colliders, three fields, a sleeper, a teleport, a
reset flag, a disabled rig and variable frame times, built from integer arithmetic
and exact IEEE operations only. The whole run is folded into one 64 bit FNV hash.
The C# test `GoldenSceneMatchesTheRustWorld` builds the same scene with its own
`JiggleWorld` and asserts the same literal, so a change in either implementation
fails one of the two tests.

A second test checks that the scene does what it claims: the sleeper sleeps and
wakes, and turning the colliders or the fields off changes the hash.

The scene sets the angle limit cosine and sine to fixed literals after baking. The
bake uses `cos` and `sin`, which the Rust standard library and the .NET runtime may
round differently in the last bit.

When a deliberate change to the solver moves the hash, update `GOLDEN` here and
`Golden` in `JiggleParityTests.cs` together, after the other parity tests agree.

## benches/step_bench.rs

A plain timing harness with no dependencies. Run it with
`cargo bench -p jiggle_core --bench step_bench`. It builds rigs, steps them for 200
frames of one fixed step each and prints microseconds per rig step, pose upload
included. Numbers from one local run, on shared hardware, release profile:

| Rigs | Bones | Per rig step |
| --- | --- | --- |
| 100 | 6 | 1.5 us |
| 600 | 6 | 1.7 us |
| 600 | 12 | 2.7 us |

Read them as a size of the cost, not as a promise. The CI bench job writes the same
table into the job summary.

## Cargo.toml and the workspace

The crate is `cdylib` for Unity on desktop and Android, `staticlib` for iOS and
WebGL, and `rlib` for Rust callers, tests and benches. It declares
`rust-version = "1.75"`. It has no dependencies on purpose: the scalar kernel is the
reference. A SIMD kernel that uses the vendored mid-math wide types comes later
behind a feature.

The root manifest has no `[workspace.lints]` table, so the lint posture is declared in
the crate: `unsafe_code = "deny"` (the ffi module opts out) and `missing_docs = "warn"`.
The crate is a member of the workspace in the root `Cargo.toml`. Release profiles
come from the root: `release` has `panic = "abort"`, `lto`, `codegen-units = 1` and
`strip`, and `release-wasm` is size optimized for WebGL.

## What the MMD rig study taught the design

We read one converted MMD model of a Zenless Zone Zero character (a PMX file) to see
how a bone and rigid body rig is set up for hair and tail motion. The point was to
choose sensible defaults and to see what jiggle rigs in this style need. The model is
a fan conversion and its licence allows non commercial use only, with no
redistribution and no extracting of parts, so nothing of it is copied: no names,
numbers per bone, or shapes. What follows is technique level only.

What the game's own runtime solution does is unknown. We only saw a conversion.

- Hair strands are chains. Each strand is pinned to a bone and the links between
  rigid bodies allow about a 20 degree cone with no spring force at all. The motion
  comes from damping and from the pose pulling the strand back, not from joint
  springs. This fits an angle limit around 20 degrees on hair presets.
- Each strand ends in a weighted capsule that acts as a pendulum. That is the part a
  one way solver cannot copy: the weight at the tip slows the whole strand. In
  jiggle_core the same slow swing is made with low stiffness, higher gravity and
  drag. The mass taper along the strand in such rigs is a stability trick for Bullet
  and has no use here.
- Damping is near 1.0 and applies per second. The loss per step is
  `1 - (1 - d)^dt`, which is 0.1088 at 60 Hz and 0.2056 at 30 Hz for the value we
  saw. This is why our `drag` is a per step share and why a drag preset has to know
  the fixed step. A drag of about 0.11 at 60 Hz is the starting point for hair.
- Collision uses groups and masks, with a set bit meaning no collision between
  those groups. Body colliders sit in one group and strands in another that tests
  against them. This is the model for collider layers in the Unity side, where the
  broadphase lives in C#.
- Chest motion in rigs of this kind is a short chain of springs, not free
  pendulums, which matches a low angle limit with a strong pull to the pose.

## CI and workflows

| Workflow | What it does |
| --- | --- |
| `.github/workflows/jiggle-core.yml` | `test` builds all targets, runs the tests and doc tests, shows clippy lints and builds the docs with broken links as errors. `ffi` builds the release library, counts the `jgl_` exports with `nm` and runs the C# package against it as a plain .NET 8 program. `floor` copies the crate into a workspace of its own and runs the tests on Rust 1.75.0. `bench` is manual |
| `.github/workflows/build-jiggle-rust-libs.yml` | Builds the library for macOS (universal), Windows, Linux, Android (three ABIs), iOS (device and simulator) and WebGL, and commits them with generated `.meta` files into `packages/com.midmanstudio.allthingsjiggle/Runtime/Plugins/Native` |

No root level scripts are used by these workflows. The build workflow is a port of
`build-nuclear-rust-libs.yml`, and like it can only be verified by its first run on
GitHub. The `ffi` job is the proof that the C# bindings and the library agree.

## Known limits

- The solver is one way. A child never pulls its parent, and there are no mass
  ratios. Heavy swing is made with parameters, not with a weight at the tip.
  Two way coupling is a possible later extension.
- Segment collision is two samples, the end and the midpoint. A collider thinner
  than half a bone can slip between them.
- There is no collider broadphase in the core. The host gives each rig a list of
  collider indices.
- The release profile aborts on panic, so the panic guard does not answer in
  shipped libraries. The solver checks every offset before use and the tests found
  no input that panics. A `release-unwind` profile for jiggle would restore the
  guard at the cost of size, and WebGL and iOS builds of it are untested.
- Rust 1.75 is enforced by the CI floor job only. The sandbox this was written in
  could not install that toolchain, so the crate was checked by reading it for newer
  standard library calls and was built on a newer toolchain.
- A rig holds at most 128 particles, 126 bones.
- The kernel sanitizes particle state, the pose and the anchor motion. It does not
  sanitize `StepConfig`, so a NaN gravity or time reaches the particles. `World::step`
  sanitizes the frame time.
- The baked cosine and sine of the angle limit may differ in the last bit between
  languages. Both backends read the same baked records, so their parity does not
  depend on it.
- Burst parity is planned as tolerance based. The managed kernel against this crate
  is bitwise.
- Nothing here has run inside Unity itself. The C# is compiled and tested with .NET 8
  at language version 9, the version Unity 2022.3 compiles.

## Fixes and Problems

### ffi/mod.rs

- A null address plus a nonzero particle offset turned into a non null wild pointer.
  The window helpers now check the null address before adding the offset, and the
  test `a_null_array_is_refused_even_with_a_nonzero_rig_offset` covers it.
- The doc comment says a caught panic returns -3. In the release profile it cannot,
  because panics abort. The comment now says so, and so does Known limits.

### solver.rs

- A call over several rigs checked each rig just before running it, so a bad rig
  late in the range left the earlier rigs already stepped, against the comment that a
  refused call writes nothing. The C# parity test of error codes found this. All rigs
  of a call are now checked first, in both the Rust and the C# solver, and
  `bad_rigs_are_refused_before_anything_is_written` covers a bad third rig.
- Threaded calls need disjoint particle windows, so rigs are required to have
  ascending, non overlapping offsets inside one range. A test copies the header of
  rig 0 into rig 1 to prove the overlap check fires alone. An earlier version of that
  test was refused by the topology check instead and let a broken overlap check
  survive a mutation run.

### world.rs

- The accumulator lost steps when a frame time was an exact multiple of the fixed
  step, because the sum rounded just under. It now adds a tolerance of `1e-4` before
  truncating, clamps the remainder to 0 or more and `alpha` to 1, and counts a
  negative or non finite frame time as 0.
- We first claimed that the length of the virtual tip works like a weight on a
  strand. A measurement showed it does not: the solver is one way, so the tip only
  changes how far the last bone turns. The docs and the test now say that.

### tests/kernel_tests.rs

- Three tests failed early (stretch, impulse, accumulator). Two had wrong
  expectations and were fixed. The third found the accumulator rounding weakness
  above.

### tests/golden_tests.rs

- The first design of the golden scene never put rig 4 to sleep, because gravity
  and the fields kept it moving. Rig 4 now has no gravity and no field response, and
  a second test asserts that it sleeps and wakes.

### lib.rs

- Fourteen clippy style warnings asked for forms that no longer match the C# port.
  They are allowed at the crate level, with the reason in the file.

### Toolchain

- The Rust 1.75 toolchain could not be installed in the sandbox because its download
  host is not on the allowlist. The floor job in CI is the check.
