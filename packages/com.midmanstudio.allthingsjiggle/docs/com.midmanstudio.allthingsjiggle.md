# com.midmanstudio.allthingsjiggle

All Things Jiggle is a Unity package for jiggle physics on chains of bones. It has
the solver (a managed kernel, bindings to the Rust library and the proof that the
two agree, with two way coupling), and since drop 2 the runtime: a `JiggleChain`
component for each chain, a manager that simulates all of them once a frame with
Burst jobs, and an editor lab that runs the same scenes on the Burst, native and
managed backends. Colliders and fields are in the solver but not yet wired to Unity
objects. That is the next drop. The Rust half is documented in
`rust_lib/jiggle_core/docs/jiggle_core.md`, including how a frame runs, the records,
the C interface and the known limits of the solver itself. This file covers the C#
half.

## Contents

- Plan of the drops
- Using the package
- Layout and conventions
- Runtime/Core: JiggleConst.cs, JiggleMath.cs, JiggleStructs.cs, JiggleCollide.cs,
  JiggleFields.cs, JiggleKernel.cs, JiggleSolver.cs, JiggleNative.cs,
  JiggleParams.cs, JiggleChainBuild.cs, JiggleClock.cs, JigglePose.cs,
  JiggleHost.cs, JiggleWorld.cs, AssemblyInfo.cs
- Runtime/Unity: JiggleRuntimeTypes.cs, JiggleChainSettings.cs, JiggleChain.cs,
  JiggleManager.cs, JiggleRuntime.cs, JiggleJobs.cs, JiggleBurstSolver.cs,
  JiggleMem.cs
- Editor: JiggleChainEditor.cs, Lab/JiggleBenchScenes.cs, Lab/JiggleLabRunner.cs,
  Lab/JiggleLabWindow.cs
- Assembly definitions
- Tests/Editor/JiggleParityTests.cs
- Tests/Editor/JiggleUnityLayerTests.cs
- Tests~/DotnetHarness
- Tests~/UnityStubs
- Native libraries
- CI and workflows
- Known limits
- Fixes and Problems

## Plan of the drops

1. **Core.** The `jiggle_core` crate, the managed reference kernel, the native
   bindings, the managed world and the parity tests and CI.
   **1.5 Two way coupling.** Per bone mass, `StepConfig.CouplingPasses`, interface
   version 2, in both kernels, before the Burst port so the Burst kernel is written
   once against the final solver.
2. **Unity runtime (this drop).** Burst jobs, reading and writing transforms through
   `TransformAccessArray`, the backend switch (Burst by default, Rust selectable,
   managed as the fallback), `JiggleChain` and `JiggleManager`, the editor lab and the
   benchmark comparison, and the stub compile job in CI.
   **2b Colliders, fields and level of detail.** The collider broadphase in C# with
   collider layers in the style of the group and mask bits found in the MMD rig study,
   fields and hit impulses from Unity objects, sleeping controls, level of detail and
   culling. The solver and the host already store colliders and fields. What is missing
   is the Unity side that fills them each frame.
3. **Extras and presets.** Body colliders from the rig, presets (hair strand, tail, ear,
   chest) from the technique level ranges of the study, and the Grand Theft Grimoire chest
   adapter.

Later: a SIMD lane batch kernel that uses the vendored mid-math wide types.

## Using the package

1. Install the package. It needs the Burst package (`com.unity.burst` 1.8.x on Unity
   2022.3), which the package manifest asks for.
2. Pick the first bone that should move, for example the root of a strand of hair, and add
   a **JiggleChain** to it (Add Component, MidMan Studio, Jiggle Chain). The parent of that
   bone is the anchor that holds the chain. The chain runs down through the first child of
   each bone until a bone has no children, the end bone is reached or `maxBones` is reached.
   Chains that start at a scene root have no anchor and are refused with a warning.
3. Press play. A **JiggleManager** appears by itself the first time a chain is enabled. To
   change gravity, the step rate, the backend and the other global settings, add the
   component to a scene object yourself and edit it there.
4. Open **Tools, MidMan Studio, Jiggle Lab** to see the live numbers, to check that Burst
   and the native library work, and to run the parity check and the benchmark.

Two pose sources exist. **Bind** (the default) holds each chain to the pose its bones had when
the chain was enabled, carried along by the anchor, so it is right for bones nothing animates
and it can never read its own result back. **Animated** holds the chain to the pose the bones
have each frame before the manager runs, so it is right for bones an Animator drives. With
Animated, something has to rewrite the bone rotations each frame. If nothing does, the chain
reads its own last result as the pose and the motion feeds on itself.

The manager runs in `LateUpdate` with an execution order of 10000, after the animation and
after most scripts. It writes the local rotation of every bone and nothing else. Bone positions
are never written, so a bone does not stretch on screen even when the solver lets the particle
stretch. `JiggleManager.Simulate(dt)` runs one frame by hand.

## Layout and conventions

The package follows the other packages of this repository: Unity assets have
`.meta` files because a package installed from a git URL is immutable, the C#
under `Runtime/Core` has no Unity engine references, and the same files compile
under plain .NET for CI.

```
Runtime/Core/                      the solver, the bindings and the host, no Unity references
Runtime/Unity/                     components, manager and the Burst jobs
Runtime/Plugins/Native/<Platform>/ native libraries, built and committed by CI
Editor/                            inspector, gizmos and the lab (Editor/Lab)
Tests/Editor/                      the Unity Test Runner fixtures
Tests~/DotnetHarness/              the parity fixture under .NET 8, ignored by Unity
Tests~/UnityStubs/                 the Unity layer compiled against Unity API stubs
docs/                              this file
Documentation~/                    a pointer for the Package Manager
```

The namespace of every public type is `MidManStudio.AllThingsJiggle`, in all assemblies. The
editor code is in `MidManStudio.AllThingsJiggle.EditorTools`, because a namespace called
`Editor` would hide `UnityEditor.Editor` inside it.

The records are explicit layout structs that match `types.rs` field for field, with
sizes 80, 32, 64, 64, 80, 64, 32 and 112. Names follow C# conventions: `Stiffness`,
not `stiffness`, and `Kind` constants live in `JiggleConst`.

## Runtime/Core/JiggleConst.cs

Every constant in one place: the status codes (Ok 0, ErrNull -1, ErrRange -2,
ErrPanic -3, ErrBadRig -4, ErrArg -5), flag bits for particles and rigs, collider and
field kinds, the struct kind numbers used by the layout probe, `MaxParticles` 128,
`MaxCouplingPasses` 16, `MaxMassRatio` 16 and `ExpectedAbiVersion` 2. The values are the ones of `types.rs` and `ffi/mod.rs`, and
the layout test reads the numbers from the native side to check the struct kinds.

## Runtime/Core/JiggleMath.cs

`JVec4`, `JVec3` and `JQuat`, the C# twins of the Rust `V3` and `Q`. Each operation
keeps the operation order of the Rust code, so results match bit for bit:

- no `Math.FusedMultiplyAdd` and no expressions the compiler may reassociate;
- the only library calls are `JiggleMath.Sqrt`, `Abs` and `Floor`. Each is `(float)Math.Sqrt((double)v)`
  and so on, because Burst does not support `System.MathF`. A double has more than twice the
  bits of a float plus two, so the rounded result is the one `MathF.Sqrt` gives for every input,
  and the golden hashes did not change (`BurstSafeMathGivesTheBitsOfMathF` checks 40000 random
  bit patterns and the edge cases);
- `JiggleMath.IsNaN` and `IsFinite` replace `float.IsNaN` and `float.IsInfinity`, so no call into
  `System.Single` is needed under Burst. `IsNaN` is written as two comparisons that are both false for NaN,
  because the plain `v != v` is refused by the compiler as a comparison of a variable with itself
  (CS1718, an error with warnings as errors);
- float to int conversion goes through `JiggleMath.FloatToInt`, which saturates like
  Rust's `as` instead of leaving the result undefined.

`JVec4` is a sequential struct of four floats and is the element type of the
position arrays, so the arrays match the Rust `[f32; 4]` records. `Write4` and
`To4` store a vector or quaternion into one.

## Runtime/Core/JiggleStructs.cs

The eight explicit layout records. Each field has a `FieldOffset`, reserved fields are
kept and named, and the file turns off the unused field warnings for them. `JiggleRig`
has `AnchorDelta` and `AnchorWorld` vector properties over the plain floats, and
`JiggleStepConfig` has a `Gravity` property, for readable code. Those properties are
not fields, so they do not take part in the layout.

Two way coupling took two reserved slots, so no size changed: `JiggleParticleDef.Mass`
(offset 76, was `Reserved2`) and `JiggleStepConfig.CouplingPasses` (offset 48, was
`Reserved0`, which leaves `Reserved1` to `Reserved3`). The layout test reads both back
from the native probe.

## Runtime/Core/JiggleCollide.cs

`JiggleCollide.PushOut`, the port of `collide.rs`: sphere, capsule, plane, box and
inside sphere, with the same branches in the same order.

## Runtime/Core/JiggleFields.cs

`JiggleFields.Push` and the hash noise, the port of `field.rs`. The hash uses
`unchecked` unsigned arithmetic so the wrap around matches Rust's `wrapping_mul`.

## Runtime/Core/JiggleKernel.cs

`StepRig` and `ResolveRig`, the line by line port of `kernel.rs`. It is a static
class over pointers into the shared arrays, with plain structs and no managed
allocation in the hot path, so Burst can compile it in the next drop. The comments in
the file say what each block does. How a frame runs is in the crate documentation.

The coupling is written with the same helpers as the Rust file, under the same names in
C# casing: `LinkShares`, `HoldLink`, `CollideParticle` and `AimRot`, and in `StepRig` the
parent first sweep (pass 1, which splits the correction of a coupled link between the
bone and its parent), the extra passes and the last collision sweep. A link is coupled
when both bones have a mass above 0, neither is kinematic and the bone is not a root
particle. The operation order is the one of `kernel.rs`, so the bits agree. Details of
the maths are in the crate documentation, section kernel.rs.

Parity is the point of this file. When the Rust kernel changes, this file changes the
same way, and the two golden tests and the bitwise scene tests say whether it was done
right.

## Runtime/Core/JiggleSolver.cs

`StepRigs` and `ResolveRigs`, the managed twins of `jgl_step_rigs` and
`jgl_resolve_rigs`. They take the same arguments, check the same things in the same
order and return the same status codes, so a host can switch backends by switching
the function it calls. They do not set error text: only the native library has a
last error.

All rigs of a call are checked before the first one runs, so a refused call has
written nothing, as in the Rust solver.

## Runtime/Core/JiggleNative.cs

The `DllImport` declarations of the seven `jgl_` functions, as `internal` members of
`JiggleNative`, and the public `JiggleLibrary` helper:

- `JiggleLibrary.Version` reads the library version text.
- `JiggleLibrary.LastError` reads the last error of the calling thread.
- `JiggleLibrary.Verify()` compares the interface version and the eight struct sizes
  with the library's and throws `InvalidOperationException` with a message when they
  differ. A host calls it once at startup.

The library name is `jiggle_core`. On iOS and WebGL it is `__Internal`, because the
static library is linked into the player.

The Linux plugin has the Editor switched off in its import settings (the build
workflow writes the `.meta`), to avoid a crash at Editor shutdown that comes from the
way Unity unloads native libraries. The managed backend is the Editor's backend on
Linux.

## Runtime/Core/JiggleParams.cs

`JiggleParams`, the authoring values of one bone, with `Default()`, `Bake(parent,
extraFlags)` that makes a `JiggleParticleDef`, and `AnchorDef()`. The baking is the
table in the crate documentation (params.rs). Out of range values are clamped, not
passed on.

`Mass` is the relative mass for two way coupling and defaults to 0, which keeps the
bone one way. A negative or NaN mass bakes to 0 and anything above `MaxMass` (1e6)
bakes to `MaxMass`. Only the ratio between neighbours matters.

## Runtime/Core/JiggleChainBuild.cs

`internal static`. The parts of building a chain that the world and the host share, so they
cannot drift apart: `Validate` (the same checks and messages), `CleanTipScale` (a bad value
counts as 1), `BakeDefs` (one `JiggleParticleDef` per particle: the anchor, each bone with its
parent index and the virtual end, which copies the last bone), `TipPosition`, `PoseWithTip`
and `Extent`.

## Runtime/Core/JiggleClock.cs

`JiggleClock.Advance` is the fixed step accumulator on its own: from the time left over, the
frame time, the step length and the step limit it gives the number of steps and the alpha for
the frame. It is what `JiggleWorld.Step` used to do inline. A frame time that is NaN, negative
or infinite counts as 0 and a huge one is cut by the step limit, as before. `FloatToUint`
saturates like Rust's `as`.

## Runtime/Core/JigglePose.cs

`JigglePose.Write` turns the world positions and rotations of a chain for this frame into
what the solver reads: it moves the last pose, makes every position relative to the anchor,
sets the anchor move and the world anchor of the rig and adds the virtual end. `LocalRotations`
turns the resolved world rotations of a rig into the local rotations of its bones, by
`local = conj(parent world) * world` down the chain, starting from the pose rotation of the
anchor. Both are plain pointer code that Burst compiles in the pose and local rotation jobs.
`JiggleWorld.SetPose` uses `Write` too, so the world and the host cannot disagree about it.

## Runtime/Core/JiggleHost.cs

The solver arrays in unmanaged memory with room to grow, for a caller that runs the
solver from jobs or threads. `JiggleWorld` keeps its arrays in managed memory that the garbage
collector may move, so jobs cannot hold their addresses. `JiggleHost` can: the addresses stay valid
until the next call that adds something, so nothing may be added while a frame runs.

- `AddChain(rest, params, tipScale)` builds a rig the same way `JiggleWorld.AddChain` does and
  returns its index. `SetMass` sets the mass of one particle.
- `SetParams(rig, params)` bakes new parameters into a rig in place and leaves its state alone.
  `SetTipScale` moves the virtual end. `Reset(rig)` makes the rig snap to its pose on the next step.
- `AddCollider`, `AddField` and `SetColliderList` fill the collider, field and index arrays.
  `Colliders`, `Refs` and `Fields` are public so a caller can rewrite them every frame. The lists
  sit one after the other in `Refs`.
- `CopyRigRuntime(source, from, to)` carries the state of one rig over to a rig of a rebuilt
  host, and `CopyClock(source)` carries the clock, so a rebuild loses neither a swing nor a
  partial step.
- `BeginFrame(dt)` runs the accumulator and sets the settings record, `BeginFixed(n)` sets an
  exact number of steps, `EndFrame()` advances the time of the world.
- `RefreshArgs()` and `FillArgs(block)` fill an argument block from the current addresses. A caller
  that runs several ranges at once gives each its own block.
- `Run(backend, first, count, resolve)` makes one call on the managed or native backend over a
  range of rigs, with the host's own settings and block. `Call(...)` is the static form with
  a settings record and a block of the caller's choosing, which several threads may use on
  disjoint rig ranges.
- `Step(dt, backend)` and `StepFixed(n, backend)` run a whole frame.
- `WorldPos` and `WorldRot` read the output.

The host is not thread safe. It has no finalizer-free path: `Dispose` frees everything, and the
finalizer frees it if the owner forgot.

## Runtime/Core/JiggleWorld.cs

`JiggleWorld`, a model of the call sequence, with plain managed arrays that it owns.
It is what tests, tools and early users drive, and it is the C# twin of the Rust
`World`:

- `AddChain` builds a rig from the world positions of a chain, with the anchor first,
  and adds the virtual end particle. It throws `ArgumentException` for fewer than two
  points or a parameter count that is not one per bone.
- `SetMass(rig, particle, mass)` sets the mass of one particle after `AddChain`: 1 is the
  first bone and the last index is the virtual end, which takes the mass of the last bone
  until then. It throws `ArgumentException` for the anchor or an index past the rig. A
  heavy tip is how a weight on a strand is made. `Cfg.CouplingPasses` sets the number of
  passes (clamped to 1 to 16, 0 counts as 1).
- `AddCollider`, `AddField` and `SetColliderList` build the shared lists.
- `SetPose` writes the pose of a rig for this frame and moves the last frame's pose.
- `Step(frameDt, backend)` runs the fixed step accumulator and the two calls.
  `StepFixed(steps, backend)` runs an exact number of steps with alpha 1.
- `WorldPos` and `WorldRot` read the output of a particle.

The defaults are 60 Hz, at most 8 steps per frame, gravity of 9.81 meters per
second squared down, a teleport distance of 5 meters and sleeping off.

`Invoke` is an `internal` raw call over a range of rigs with an optional hook on the
argument block. The tests use it to send corrupted arguments to both backends and to
run batches from several threads.

The world is not thread safe. Calls from several threads are fine only on disjoint
rig ranges through `Invoke`, as in the threaded test.

Since drop 2 the world is built on `JiggleChainBuild`, `JigglePose` and `JiggleClock`. Nothing
about its behaviour changed, and the golden hashes still match.

## Runtime/Core/AssemblyInfo.cs

Makes the internals visible to the editor test assembly, so the layout test can use the
bindings directly.

## Runtime/Unity/JiggleRuntimeTypes.cs

`JiggleRuntimeBackend` (Burst, Native, Managed), `JigglePoseSource` (Bind, Animated, described in
Using the package) and `JiggleFrameStats`, a plain struct with what the last frame did: rigs,
particles, steps, the frame time in milliseconds, the backend that ran, the solver status and
the number of rebuilds.

## Runtime/Unity/JiggleChainSettings.cs

The values an author sets for a whole chain, in the ranges of `JiggleParams`: stiffness,
soften, root stretch, angle limit (on, size, soften), stretch, drag, air drag, a gravity
multiplier, blend, ignore root motion and mass. `stiffnessAlong` is a curve that scales the
stiffness from the first bone (0) to the last (1), which is how a strand gets looser toward its
tip. `ToParams(bone, bones)` and `ToParams(bones)` make the `JiggleParams` for each bone, and a
chain of one bone reads the curve at 0. The defaults are those of `JiggleParams.Default()`, and a
test checks that they bake to the same values. A missing curve means no scaling.

The collision, wind and impulse values are not exposed yet. They come with 2b, when something
feeds them.

## Runtime/Unity/JiggleChain.cs

The component. Add it to the first bone. It keeps its settings, the pose source, the optional
`endBone`, `maxBones` (at most 126) and `tipScale`, and it knows its bones.

- On enable it registers with the manager, and on disable it unregisters and puts the bones back
  to their bind rotations when the pose source is Bind.
- `Prepare` (internal) walks down the first children, checks the chain and takes the bind pose:
  for each bone its position in the space of the anchor and its rotation relative to the anchor, and
  the bone's local rotation for the restore. It does so once. When the manager rebuilds, the walk
  is compared with the old one and the bind pose is kept if the bones are the same, so a chain
  that is mid swing is never bound in its swing.
- `SettingsChanged()` tells the manager that the settings, the tip scale or the pose source
  changed. The inspector does it by itself in play mode. The manager bakes the new values in
  place and rebuilds only if the bones or the pose source changed.
- `ResetSimulation()` snaps the chain to its pose on the next frame. `Rebind()` forgets the bind pose,
  puts the bones back first and takes the pose they have now.
- `TryWalk` shows what the chain would walk without changing anything (the inspector and the gizmos use
  it) and `TryGetParticle(i, out p)` gives the simulated world position of the anchor (0), a bone
  (1 to `BoneCount`) or the virtual end (the one after).

Not supported, and documented as limits: bones that are reparented or a first child that is
added at run time (call `Rebind`), non uniform scale on the chain, a bone that belongs to two
chains (the second is refused with a warning), and a chain whose anchor is the bone of another
chain, which works but reads the pose of the last frame, one frame late.

## Runtime/Unity/JiggleManager.cs

The component that runs everything. There is one: a chain that is enabled finds the one in the scene
or makes one with default settings, and it lives through scene loads. Its settings are public
fields: the backend, gravity, the step rate (`fixedHz`, default 60), `maxSteps`, `couplingPasses`,
`teleportDistance`, the sleep values (`sleepFrames`, `sleepSpeed`, `sleepDeviation`, `wakeMotion`)
and `rigsPerBatch`. Sleeping is off until `sleepFrames` is above 0.

Each frame (`Simulate(dt)`, called from `LateUpdate`) does this:

1. If chains came or went, rebuild: a new `JiggleRuntime` is built from all registered chains and the old
   one is disposed after the state of the chains that stayed was copied over.
2. Bake the settings of the chains that changed, apply the manager settings to the host, and
   apply the reset requests.
3. Run the frame on the backend asked for. Native falls back to Burst when the library does not
   load or does not have this package's interface version, with one warning. This is what happens
   until the build workflow has refreshed the committed binaries.
4. Record `Stats`.

Chains that cannot be simulated are logged once per rebuild and left out. `ActiveBackend` is
the backend that really runs and `NativeError` says why Native was not used.

## Runtime/Unity/JiggleRuntime.cs

`internal`. Everything one build of the manager owns: a `JiggleHost` with the rigs of all chains,
a `TransformAccessArray` with each transform once, and the arrays the jobs read. A rig has an
entry list: the anchor first, then each bone, as indices into the unique transform table. Unique
transforms matter because many chains often share an anchor.

A frame is the read job (position, rotation and, for anchors in bind mode, the matrix of each
transform), the pose job, the solve (three backends), the local rotation job and the write job.
With Burst all five are scheduled with dependencies and completed once at the end. With the other
backends the read and pose jobs complete first, the solver runs on the main thread, and the local
rotation and write jobs complete after.

Nothing in a runtime changes while a frame runs. A build is thrown away and replaced when chains come
or go, which costs one allocation of the arrays and the transform table, and chains keep their state.

## Runtime/Unity/JiggleJobs.cs

The Burst jobs, all with `FloatMode.Strict` and `FloatPrecision.Standard`, so the compiler does not
fuse multiply and add or reorder. They hold raw pointers into the host and mark them
`NativeDisableUnsafePtrRestriction`.

- `JiggleReadJob` (`IJobParallelForTransform`, scheduled read only) reads the world pose of the
  transforms that have a flag in `Flags`.
- `JigglePoseJob` builds the pose of each rig: in bind mode from the anchor matrix and the bind
  data, in animated mode from the samples of the bones, and calls `JigglePose.Write`.
- `JiggleLocalJob` calls `JigglePose.LocalRotations` per rig.
- `JiggleWriteJob` sets `localRotation` of every bone that has a slot. A bone has one writer, so
  no ordering is needed. A non finite rotation is skipped.

All jobs check `TransformAccess.isValid`, so a transform destroyed during the frame is skipped.

## Runtime/Unity/JiggleBurstSolver.cs

`public static`. Runs the managed solver of a `JiggleHost` in Burst jobs, one job index per batch of
rigs. The code that runs is `JiggleSolver.StepRigs` and `ResolveRigs` from the Core, compiled by Burst,
so it is the managed kernel and not a second implementation. `Schedule` makes the job (phase: both,
step or resolve), `Worst` reads the status array, `RunNow` schedules and waits, and `IsBurstCompiled` runs
a probe job to say whether Burst really compiles the jobs now. The solve job is compiled
synchronously, so the first call after a domain reload waits for Burst instead of running slowly as
managed code.

## Runtime/Unity/JiggleMem.cs

`internal`. Zeroed persistent arrays in unmanaged memory through `UnsafeUtility`, used for the job
data.

## Editor/JiggleChainEditor.cs

The inspector of `JiggleChain`: the default fields, a line saying which anchor and which bones the chain
walks, errors for a missing anchor or an end bone that is not on the way down, and in play mode
buttons for `ResetSimulation` and `Rebind`. `JiggleChainGizmos` draws the bones of a selected
chain in blue and the simulated particles in orange while the game runs.

## Editor/Lab/JiggleBenchScenes.cs

The scene table, the geometry and the motion of the comparison, written to equal
`scenes()`, `build_world` and the helpers of `rust_lib/jiggle_core/benches/scene_bench.rs`: twelve
scenes from `tiny` (1 rig) to `hair-10k` (10000 rigs), `long-64`, `max-126`, `collide-1k`, `fields-1k`,
`coupled-1k-p1` and `p4`, `asleep-1k` and `full-1k`. The anchors sit on a grid, each chain hangs
down, and each rig sways by a different amount every frame. The pose of a frame is written by
`JiggleBenchPoseJob`, a Burst job, for every backend, so the pose phase costs the same on all of them.
When the Rust table changes, this file changes the same way. A table that drifts makes the two benches
measure different things.

## Editor/Lab/JiggleLabRunner.cs

Runs a scene on the Burst, native or managed backend and compares them.

- `Run` builds the scene, warms it up and times it frame by frame like the Rust bench: the pose phase,
  the step and the resolve, each as wall clock time, and a checksum of the output. The native and
  managed backends can use several threads (`Parallel.For` over windows of rigs, each window with its own
  argument block through `JiggleHost.Call`). Burst uses the job system and reports its worker count.
- `Parity` runs a scene on all three backends frame for frame with the same poses. Native has to equal
  managed in every bit of every frame. Burst is compared by the largest difference in position and in
  rotation and by whether it is bit identical.
- The CSV line has the columns of the Rust bench (`CSV,v1,backend,scene,...`), with the backends named
  `burst`, `native` and `managed`.

What can be compared and what cannot: the Rust bench gives the cost of the kernel on the CI machine,
with times summed over the worker threads. The lab gives the three backends on one machine, with
wall clock times. Compare `wall_us` (a whole frame) within the lab. Do not compare a Rust run with a
lab run: they are different machines and different clocks. The checksum is equal across backends in the lab
only for native and managed. Burst can differ in the last bits, and the Rust bench computes its
sine and cosine with the Rust libm, so its checksum is not the lab's either.

## Editor/Lab/JiggleLabWindow.cs

Tools, MidMan Studio, Jiggle Lab. Three tabs:

- **Live** checks that Burst compiles the jobs and that the native library loads with this package's
  interface version, and in play mode shows the manager: the backend (editable), the backend that runs, the
  counts, the steps, the frame time and the status.
- **Parity** runs the comparison above on a scene and says what it found.
- **Bench** runs the chosen scenes on the chosen backends and thread counts, shows the table and copies or
  saves the CSV. It blocks the editor while it runs and has a cancel button. Editor numbers include the
  safety checks of the job system, so for numbers to quote, switch the Burst safety checks, the jobs debugger and
  leak detection off, or build a player.

## Assembly definitions

- `Runtime/MidManStudio.AllThingsJiggle.asmdef`: `noEngineReferences` true, so the
  runtime assembly cannot take a UnityEngine reference by accident, and
  `allowUnsafeCode` true for the pointer code.
- `Runtime/Unity/MidManStudio.AllThingsJiggle.Unity.asmdef`: references the Core and `Unity.Burst`,
  `allowUnsafeCode` true. The Burst jobs live here because the Core has no engine references.
- `Editor/MidManStudio.AllThingsJiggle.Editor.asmdef`: Editor only, references the Core, the Unity
  assembly and `Unity.Burst`, `allowUnsafeCode` true.
- `Tests/Editor/MidManStudio.AllThingsJiggle.Tests.Editor.asmdef`: Editor only, with the
  NUnit reference, the Unity assembly, `allowUnsafeCode` true, and the `UNITY_INCLUDE_TESTS` constraint.

## Tests/Editor/JiggleParityTests.cs

One NUnit fixture of 30 tests that checks the managed kernel against the real
library on the same memory:

- interface version and `Verify`;
- every struct, field by field, by reading back the probe words;
- random scenes in four mixes (everything, colliders and segments, fields, none),
  eight seeds each, with random parameters, rigs of one to ten bones, jumps,
  resets, disabled rigs, sleeping and waking, frame times that include zero, huge,
  NaN, negative and infinite, compared bit for bit after every frame;
- the largest rig of 126 bones, and that 127 is refused by both backends;
- every collider kind, with a check that each one moves the chain;
- the segment test, with a check that it sees a sphere that only the segment touches;
- every field kind and mode, with a check that each one moves the chain;
- teleport, reset, disable and zero step frames;
- NaN in the state and in the anchor move;
- 24 corrupted argument cases on both backends and both calls, with the expected
  status code, no write on an error and error text on the native side;
- threaded batches of four threads against one call, for both backends;
- a chain that sags and stays near its pose, bone lengths kept, and the formulas of
  the baking;
- the golden scene, whose hash is also asserted by `tests/golden_tests.rs`, and a
  second test that the scene exercises what it claims;
- random scenes with masses (the same four mixes plus coupling, eight seeds each, with
  some bones one way, some far heavier than their neighbours, weights on the tip and
  pass counts from 0 to 40), compared bit for bit after every frame;
- the coupled golden scene, whose hash is `GoldenCoupled`, asserted by `tests/golden_tests.rs`
  too, and a test that the masses, the colliders, the fields and the pass count each change it;
- that the pass count changes nothing in a scene without masses;
- that `Mass` bakes and clamps as documented and `SetMass` refuses the anchor and an index
  past the rig;
- `BurstSafeMathGivesTheBitsOfMathF`;
- the host against the world, bit for bit, on both backends, and a host rebuilt with the state
  carried over, which has to keep simulating the same;
- the clock against the world, and the local rotations composing back to the resolved world rotations;
- the host growing without losing its layout, and refusing use after `Dispose`;
- the host with colliders, lists and fields against the world on both backends;
- `SetParams` baking in place without touching the state and refusing the wrong count, the reset
  flag being cleared by the solver on both backends, and `CopyClock`.

The file has no UnityEngine references and uses `NUnit.Framework` only through
`TestFixture`, `Test` and the `Assert` calls the stubs provide, so the harness can
run it under .NET.

The random scenes use `Math.Sin`, which is fine: both backends read the same
generated pose. The golden scene does not, because there the C# and the Rust sides
build the inputs separately and must agree.

## Tests/Editor/JiggleUnityLayerTests.cs

Five tests for the Unity layer that need the editor: the default settings bake to the defaults
of the Core, the stiffness curve scales along the chain, the Burst solver stays within 1e-4 meters of
the managed kernel over 120 frames of 40 rigs (with and without coupling), the step and the resolve
as two jobs give the same bits as one job, and the probe and an empty host do not throw. They run
in the Unity Test Runner. They need Burst, so the .NET harness does not run them. They are
compiled in CI.

The transform jobs need a scene and a running player loop. They are tried in the lab and by playing
a scene, not by a test.

## Tests~/DotnetHarness

`JiggleHarness.csproj` compiles `Runtime/Core`, the editor tests, a small `NUnitStubs.cs`
and `Program.cs` into a .NET 8 program with language version 9.0, the one Unity 2022.3
compiles, and `TreatWarningsAsErrors`. `Program.cs` is the runner: it loads the native
library path given on the command line, resolves `jiggle_core` to it, checks that the
number of exports found by `nm` equals the number of `DllImport` functions (`--expect-exports`),
and runs every test method of the fixture, printing PASS or FAIL per test.

`NUnitStubs.cs` mirrors the NUnit signatures the fixture uses, including the overloads
with a message, so the same file compiles under real NUnit in Unity.

The folder name ends in a tilde, so Unity ignores it.

The harness runs `JiggleParityTests.cs` only. It does not compile the Unity layer.

Running it by hand needs the release library:

    cargo build --release -p jiggle_core
    dotnet run -c Release --project packages/com.midmanstudio.allthingsjiggle/Tests~/DotnetHarness \
        -- target/release/libjiggle_core.so --expect-exports 7

## Tests~/UnityStubs

The only check of the Unity layer that can run without Unity. `UnityApiStubs.cs` is a hand written subset
of the Unity 2022.3 API (the types the Unity layer, the editor code and the tests use, with
the same names, namespaces and signatures and empty bodies, checked against the scripting reference),
and five projects compile the package against it, one for each assembly:

- `Core.csproj` compiles `Runtime/Core` with no references at all, the same as `noEngineReferences`, so a
  Unity type in the Core cannot compile.
- `Unity.csproj` compiles `Runtime/Unity` with the Core and the stubs.
- `Editor.csproj` compiles `Editor` with the Core, the Unity assembly and the stubs.
- `Tests.csproj` compiles the editor tests with the NUnit stand in of the harness.
- `UnityApiStubs.csproj` is the stubs.

Because each assembly is its own project, using an `internal` of the Core from the Unity layer fails
with CS0122, as it would in Unity. This was tried on purpose when the projects were written.

What it proves is that the code is well formed against those signatures. It does not prove that Burst
accepts the Core code, that the transform jobs behave as the code expects, or that a member of the stubs
really exists in Unity. Each stub was written from the scripting reference. If a stub has a member that
Unity does not, an error can hide behind it. Run the lab in Unity before trusting the layer.

Build by hand:

    cd packages/com.midmanstudio.allthingsjiggle/Tests~/UnityStubs
    dotnet build Tests.csproj -c Release
    dotnet build Editor.csproj -c Release

The `.gitignore` of the repository ignores `*.csproj` and has a negation for this folder, as for the harness.

## Native libraries

`Runtime/Plugins/Native/<Platform>` holds one `NOTE.md` per platform until the build
workflow has run, and the libraries after. The workflow writes the `.meta` files with
the right import settings. Do not add files there by hand.

## CI and workflows

| Workflow | What it does |
| --- | --- |
| `.github/workflows/jiggle-core.yml` | The `ffi` job builds the release library and runs `Tests~/DotnetHarness` against it. The `unity-stubs` job compiles Core, Runtime/Unity, Editor and the editor tests against the Unity stubs, fails if `MathF` appears in the Core outside `JiggleParams.cs` (Burst does not support it), and fails if an asset has no `.meta` file. The manual bench job runs both Rust benches. The other jobs are described in the crate documentation |
| `.github/workflows/build-jiggle-rust-libs.yml` | Builds and commits the native libraries |

No root level scripts are used.

## Known limits

- Nothing here has run inside Unity. The C# Core is compiled and tested under .NET 8 at language
  version 9. The Unity layer, the editor code and the Unity tests are compiled against hand written
  stubs, which proves they are well formed and nothing more. Burst compiling the Core kernel, the
  transform jobs and the lab are untried. The asmdefs, the `.meta` files and the Linux plugin settings
  are copied from the nuclear package, which works, but they have not been tried here.
- Burst parity is tolerance based. The jobs use strict floating point mode, so the results may well be bit
  identical to the managed kernel, but the package only promises 1e-4 meters, which the Unity
  test checks. The managed kernel against Rust is bitwise.
- Only local rotations are written. Bone positions stay where the animation put them, so a bone
  never stretches on screen. The chain does not follow a non uniform scale exactly.
- A chain is a path: each bone is the first child of the one above. The solver supports trees, but the
  component does not build them yet.
- Colliders, fields, hit impulses, level of detail and culling are not wired to Unity objects. The solver
  and the host support them, and the next drop adds the Unity side.
- The transform jobs complete in the same `LateUpdate` that schedules them, so the main thread waits for
  them. There is no frame of delay to hide the cost behind.
- Changing the bones of a running chain (reparenting, adding a first child) is not noticed. Call
  `Rebind` on it.
- `JiggleWorld` is not thread safe and keeps its arrays in managed memory, which the
  garbage collector may move. The `fixed` blocks pin them for the length of one call. Use `JiggleHost` for
  jobs and threads.
- Coupling is opt in per bone and capped at a mass ratio of 16 between neighbours, segment
  collision is two samples, and rigs hold at most 126 bones. The crate documentation lists
  these and the other solver limits.
- Burst 1.8.30 and later need Unity 6. On Unity 2022.3 stay on a 1.8 release up to 1.8.29. The package
  manifest asks for 1.8.4 as the lowest.
- The native plugin binaries committed under `Runtime/Plugins/Native` are rebuilt by the
  build workflow when `rust_lib/jiggle_core` changes. Until it has run after this drop, the
  package has the new C# and the old binaries (interface version 1), and
  `JiggleLibrary.Verify` throws. The manager catches that and runs Burst instead. The managed
  backend does not need the binaries.
- The Rust 1.75 floor is checked only by the `floor` job in CI, because the sandbox compiles with a
  newer toolchain.

## Fixes and Problems

### Runtime/Core/JiggleMath.cs, JiggleCollide.cs, JiggleFields.cs

- The kernel called `MathF.Sqrt`, `Abs` and `Floor`, and Burst does not support `System.MathF`, so the
  jobs would have failed to compile. This was found by reading the Burst documentation before the
  jobs were written, not by a build. The calls now go through `JiggleMath.Sqrt`, `Abs` and `Floor`
  over `Math`, which gives the same bits. A CI step now fails if `MathF` comes back.
- The NaN and finite tests called `float.IsNaN` and `float.IsInfinity`. They now go through
  `JiggleMath.IsNaN` and `IsFinite`, written as plain comparisons, so Burst needs no `System.Single`
  call. The obvious `v != v` is not usable, because CS1718 is an error under warnings as errors.

### Runtime/Core/JiggleHost.cs

- The host first zeroed the colliders, lists and fields in its argument block, so the benchmark scenes
  that have them could not run on it. It now stores them, and a test checks it against the world.

### Runtime/Core/JiggleKernel.cs

- The coupling port matched the Rust golden hash on its first run, with no change to
  the port. Three deliberate breakages were then run against it to prove the new tests
  see it: the sign of the parent share in the extra passes, the last collision sweep
  removed, and the ratio cap removed. The first two were caught by the coupled golden and
  the coupled random scenes, the third by the coupled random scenes only, since the golden
  scene's masses stay inside the cap. Each change was reverted.

### Runtime/Core/JiggleSolver.cs

- The managed solver checked and ran each rig in one loop, so a bad rig late in a call
  left the earlier rigs already stepped. The error code test found it. It now checks all
  rigs first, like the Rust solver.

### Runtime/Core/JiggleWorld.cs

- The raw call lived inline in `Run`, so tests could not send corrupted arguments to
  both backends. It is now `Invoke`, with an argument hook, and `Run` calls it.

### Tests/Editor/JiggleParityTests.cs

- The first collider cases used a stiffness of 0.6, at which a chain sags under 3
  centimeters, so a plane under it never touched anything. The cases now use 0.3 and
  the test asserts that every collider moves the chain.
- The vortex impulse case had a radius of 0, and a vortex with no radius reaches
  nothing. The case now has a radius.
- Both tests were found by the assertion that a collider or field must move the
  chain, which is why that assertion is there.
- Two deliberate one bit changes to the managed field and collider code made the
  tests fail, and the change was reverted. The tests do see single bit differences.

### Tests~/UnityStubs

- The first draft of the stub for `TransformAccessArray` did not assign its two properties in the
  constructors and stopped the build with CS0843. The stub was fixed.
- The `.gitignore` rule `*.csproj` would have kept the five stub projects out of the commit, the same
  failure the harness had. A negation for this folder was added.

### Tests~/DotnetHarness

- CI failed with "Couldn't find a project to run": `JiggleHarness.csproj` was never
  committed, because the repository `.gitignore` has `*.csproj` (for the files Unity
  generates). The project file is now committed and `.gitignore` has a negation for
  `packages/**/Tests~/DotnetHarness/*.csproj`.
- `NUnitStubs.cs` lacked `Assert.AreNotEqual` and `Assert.Throws` with a message, which the
  new tests use and real NUnit has. Both overloads were added.

- `dotnet build` could not reach the package feed from the sandbox, so the harness
  was built with an empty local feed there. CI restores from the normal feed, and the
  project has no package references.
