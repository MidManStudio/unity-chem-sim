# com.midmanstudio.allthingsjiggle

All Things Jiggle is a Unity package for jiggle physics on chains of bones. It has
the solver (a managed kernel, bindings to the Rust library and the proof that the
two agree, with two way coupling), since drop 2 the runtime (a `JiggleChain`
component for each chain, a manager that simulates all of them once a frame with
Burst jobs, and an editor lab that runs the same scenes on the Burst, native and
managed backends) and since drop 2b the scene side: collider and force field
components, hit impulses, level of detail and sleep controls. Nothing here has run
inside Unity yet (see Known limits). The Rust half is documented in
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
  JiggleHost.cs, JiggleColliderSet.cs, JiggleFieldQueue.cs, JiggleImpulse.cs,
  JiggleLod.cs, JiggleWorld.cs, AssemblyInfo.cs
- Runtime/Unity: JiggleRuntimeTypes.cs, JiggleChainSettings.cs, JiggleChain.cs,
  JiggleChainLod.cs, JiggleColliderShape.cs, JiggleForceField.cs, JiggleManager.cs,
  JiggleRuntime.cs, JiggleJobs.cs, JiggleBurstSolver.cs, JiggleMem.cs
- Editor: JiggleChainEditor.cs, JiggleShapeEditors.cs, Lab/JiggleBenchScenes.cs,
  Lab/JiggleLabRunner.cs, Lab/JiggleLabScene.cs, Lab/JiggleColliderLab.cs,
  Lab/JiggleFieldLab.cs, Lab/JiggleLodLab.cs, Lab/JiggleLabWindow.cs
- Assembly definitions
- Tests/Editor/JiggleParityTests.cs
- Tests/Editor/JiggleFeatureTests.cs
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
2. **Unity runtime.** Burst jobs, reading and writing transforms through
   `TransformAccessArray`, the backend switch (Burst by default, Rust selectable,
   managed as the fallback), `JiggleChain` and `JiggleManager`, the editor lab and the
   benchmark comparison, and the stub compile job in CI.
   **2b Colliders, fields and level of detail (this drop, 0.4.0).** The collider
   broadphase in C# (a list of near colliders for each chain, filtered by layer),
   collider components, force field components and hit impulses, sleeping controls,
   level of detail and culling by distance and by the camera, and one lab tab for each
   feature. The layer rule is the one of the MMD rig study with the sense that Unity
   uses for it: a set bit in a chain's `ignoreLayers` means no collision.
3. **Extras and presets.** Body colliders from the rig, presets (hair strand, tail, ear,
   chest) from the technique level ranges of the study, and the Grand Theft Grimoire chest
   adapter.

Later: a SIMD lane batch kernel that uses the vendored mid-math wide types.

## Using the package

1. Install the package. It needs the Burst package (`com.unity.burst` 1.8.x on Unity
   2022.3), which the package manifest asks for.
2. Pick the first bone that should move, for example the root of a strand of hair, and add
   a **JiggleChain** to it (Add Component, MidManStudio, AllThingsJiggle, Jiggle Chain). The parent of that
   bone is the anchor that holds the chain. The chain runs down through the first child of
   each bone until a bone has no children, the end bone is reached or `maxBones` is reached.
   Chains that start at a scene root have no anchor and are refused with a warning.
3. Press play. A **JiggleManager** appears by itself the first time a chain is enabled. To
   change gravity, the step rate, the backend and the other global settings, add the
   component to a scene object yourself and edit it there.
4. Open **MidManStudio, AllThingsJiggle, Jiggle Lab** to see the live numbers, to check that Burst
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

**Colliders.** Add a **Jiggle Collider** (Add Component, MidManStudio, AllThingsJiggle, Jiggle Collider) to any
object: a sphere, capsule, box, plane or inside sphere that follows the transform. It is not a Unity collider and
costs nothing in the physics engine, so it can stand for a body part that is only a transform. A chain tests the
colliders near it, and only while `Collide` is on in its settings (it is off by default). The layer of a collider is
the layer of its object (0 to 31). A chain has `ignoreLayers`, and **a set bit means no collision with that layer**:
the default of 0 collides with everything. Tick `staticShape` on a shape that never moves, turns or changes.

**Force fields.** Add a **Jiggle Force Field**: directional (along the forward of the transform), radial or vortex
(around its up), pushing as an acceleration while it is enabled, with a radius, a falloff and gusts. `Fire()` gives
every chain in reach one shove shaped like the field. A chain scales the push of fields with `windResponse` in its
settings.

**Hits.** From code: `chain.AddImpulse(dv)` shoves a whole chain, `chain.AddImpulseAt(point, dv, radius, falloff)`
shoves the bones near a point, `JiggleManager.AddBurst(point, radius, dv)` pushes everything near a point away from it
and `AddShove(dv, center, radius)` pushes everything (a radius of 0 reaches everywhere). `dv` is a change of velocity in
meters per second. A chain scales it with `impulseResponse`. A chain that is not simulated (not built yet, switched
off by the level of detail, disabled, or about to snap to its pose) refuses a hit and `AddImpulse` returns false.

**Level of detail.** The manager has `lodCollisionDistance` (chains farther than this stop testing colliders),
`lodCullDistance` (chains farther than this are not simulated and show their pose), `lodHysteresis`,
`lodCullOffscreen` with `lodOffscreenDelay`, and `lodCamera` (empty means the main camera). A distance of 0 turns that
limit off, and with no camera every chain is simulated in full. A chain's `lod` is Inherit (the manager's limits),
AlwaysSimulate or Custom (its own distances). A chain that comes back from Off snaps to its pose.

**Sleep.** `sleepFrames` on the manager above 0 turns sleeping on. A chain with `allowSleep` off never sleeps.
`IsAsleep` and `Wake()` are on the chain. A hit wakes a chain, and so does a collider that moves up to it.

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

Menu rule, for this package and every MidManStudio package: every menu entry lives under the top level
`MidManStudio` menu, as `MidManStudio/<Package>/<Item>`, and never under `Tools` or `Window`. That covers
editor windows (`MenuItem`), `CreateAssetMenu`, `AddComponentMenu` and settings entries. Here that is
`MidManStudio/AllThingsJiggle/Jiggle Lab`, `.../Jiggle Chain`, `.../Jiggle Manager`, `.../Jiggle Collider` and
`.../Jiggle Force Field`.

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

`Weight(distance, radius, power)` is new in 0.4.0 and is not in the Rust crate: it is the falloff factor of a
point at a distance from the center, 1 when the radius is not positive (no edge), else the falloff of
`distance / radius` by the power. `JiggleImpulse.HitAt` uses it, so a hit fades the way a field does. It does not
touch `Push`, so the parity of the solver is not affected.

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
- To rewrite them every frame (0.4.0): `ClearColliders()` drops every collider and every list,
  `ClearColliderLists()` drops only the lists (it zeroes the offset and count of every rig, because a stale range
  past the end of `Refs` is `ErrBadRig`, which a test documents), `ClearFields()` drops the fields,
  `SetCollider(index, collider)` replaces one collider in place and refuses an index that does not exist, and
  `SetColliderList(rig, pointer, count)` is the pointer form of the list call, so a caller that keeps its own
  buffer copies nothing. `SetColliderList(rig, uint[])` calls it.
- `IsAsleep(rig)`, `IsDisabled(rig)`, `SetDisabled(rig, bool)` and `Wake(rig)` read and set the flags. Enabling a
  rig again sets `RigReset`, so it snaps to its pose and never shows the state it had when it was switched off.
  `Wake` clears `RigAsleep` and the sleep counter.
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

## Runtime/Core/JiggleColliderSet.cs

The collider broadphase, in C# and with no Unity references. Two classes.

`JiggleShapes` builds the solver records in world space: `Sphere`, `Capsule(center, axis, halfHeight, radius)`,
`Box(center, halfExtents, rotation, rounding)`, `Plane(point, normal)`, `InsideSphere`, `Field(kind, mode, center,
direction, gustScale, strength, radius, power, turbulence, frequency)` (the direction is normalized and the gust
scale goes in `Dir.W`) and `Burst(point, radius, velocityChange, power)`, a radial impulse field. Input that is not
finite is replaced by something harmless instead of reaching the solver.

`JiggleColliderSet` holds the shapes of a scene for one frame. `Clear()` then `Add(shape, layer, hasMoved)` for each
shape, then `Write(host, anchors, ignore)`:

1. the host's colliders and lists are cleared and the shapes written;
2. each rig gets a list of the shapes that can reach it. A shape is listed when its layer bit is not set in the
   rig's `ignore` mask and `Near` says it is within reach. The reach of a rig is its `Extent` plus the largest
   collision radius among its particles that collide, plus `Margin` (0.25 m by default, which covers bones that
   stretch and the move of the chain since the extent was measured);
3. `Near` uses the edge of the shape (a sphere is its radius, a capsule its half height plus its radius, a box the
   length of its half extents plus its rounding) against the current anchor passed by the manager. A plane and an
   inside sphere have no edge and are always near;
4. a rig with no particle that collides gets no list, and an `ignore` mask of `uint.MaxValue` gets none either;
5. a sleeping rig is woken when a shape on its list has moved. The solver does not wake a sleeper for a moving
   collider, so without this a chain asleep under a moving shape would stay asleep.

It returns the number of indices written. A layer outside 0 to 31 counts as layer 0. Layers are kept managed side
because the native record has no slot for one: no native binary has to change for them, and both backends see the
same lists.

Cost: building the lists is O(rigs x shapes) on the main thread. That is fine for the hundreds of chains and tens of
colliders of a character and is not for thousands of each. See the known limits.

## Runtime/Core/JiggleFieldQueue.cs

The fields of a frame. Standing fields (the force field components, rebuilt every frame) and impulses that wait to be
given once. `Clear()` and `Add(field)` take the standing fields, `Enqueue(field)` queues an impulse (it forces the
mode to `FieldImpulse`), `Write(host)` replaces the host's fields with the standing ones and then the waiting
impulses, and `Commit(steps)` is called after the frame with the number of fixed steps it ran.

An impulse field acts on the first step of a frame only, so a frame with no step would lose it. `Commit` therefore
removes the waiting impulses only when `steps > 0`; with no step they stay for the next frame. If the solver reports an
error the manager commits 0 steps, so nothing is lost on a failed frame. At most `MaxPending` (256) impulses wait, and
the oldest is dropped when there are more. `DropPending()` empties the queue.

## Runtime/Core/JiggleImpulse.cs

Hits on one rig, written straight into the state: `Hit(host, rig, dv)` for the whole chain and `HitAt(host, rig,
point, radius, power, dv)` with the fade of `JiggleFields.Weight`. For each particle that is not kinematic it does
`Prev -= dv * (FixedDt * weight * ImpulseResponse)`, because velocity is `Pos - Prev` in meters per step and the state
is relative to the anchor. A hit then clears `RigAsleep` and the sleep counter.

- With no drag and no air drag a hit equals a directional impulse field that covers the rig (a test checks it to
  2e-5 m). With drag it differs, because a field acts inside the step and a hit is already in the velocity.
- A hit refuses (returns false, changes nothing) for a rig that is disabled or about to reset, because the solver
  would discard it, and for a velocity or a point that is not finite. `HitAt` returns whether any particle was hit.
- A hit survives a frame with no step, which a field impulse does not, because it is written into the state at once
  and not given on a step. A test checks that both backends agree.
- Call it before the frame runs. It does not lock anything. `JiggleChain.AddImpulse` and `AddImpulseAt` call it on the
  host of the running build.

## Runtime/Core/JiggleLod.cs

The level of detail rules as pure functions, so they are tested outside Unity.

- `JiggleLodLevel`: `Full`, `NoCollision` (simulated, but tests no colliders) and `Off` (not simulated, switched off
  with `SetDisabled`, shows its pose).
- `JiggleLodSettings`: `CollisionDistance`, `CullDistance` (0 means that limit is off), `Hysteresis` (meters),
  `CullOffscreen` and `OffscreenDelay` (seconds). `Always()` is no limits.
- `JiggleLodTracker`: what a chain remembers between frames, its level and how long the camera has not seen it.
- `Decide(settings, ref tracker, distance, visible, dt)`: a chain goes to a lower level at the limit and back
  `Hysteresis` meters inside it, so a chain on the limit does not flicker. A distance that is not finite keeps the
  level. A chain the camera has not seen for `OffscreenDelay` seconds is Off. Seeing it again resets the time.
- `SphereInFrustum(planes, count, center, radius)`: Unity's plane convention, `dot(normal, point) + w` with normals
  pointing inward. A bad sphere or no planes count as seen, so a chain is never culled because of bad input.


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
the number of rebuilds. Since 0.4.0 it also has `Colliders` (shapes written), `ColliderRefs` (collider tests listed
over all rigs), `Fields` (fields written, impulses included), `LodNoCollision`, `LodOff` and `Asleep` (counts of
chains).

## Runtime/Unity/JiggleChainSettings.cs

The values an author sets for a whole chain, in the ranges of `JiggleParams`: stiffness,
soften, root stretch, angle limit (on, size, soften), stretch, drag, air drag, a gravity
multiplier, blend, ignore root motion and mass. `stiffnessAlong` is a curve that scales the
stiffness from the first bone (0) to the last (1), which is how a strand gets looser toward its
tip. `ToParams(bone, bones)` and `ToParams(bones)` make the `JiggleParams` for each bone, and a
chain of one bone reads the curve at 0. The defaults are those of `JiggleParams.Default()`, and a
test checks that they bake to the same values. A missing curve means no scaling.

Since 0.4.0 it also has `collide` (off by default, so a chain only tests colliders when asked), `collisionRadius`
(0.1 m), `segmentCollision` (the whole bone, not only its end), `windResponse` and `impulseResponse` (both 1), all
baked by `ToParams`. Their defaults are the Core's, and a test checks it. Before this, a chain could not collide at
all, because the settings never turned collision on.

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

Since 0.4.0 it also has:

- `ignoreLayers` (a `LayerMask`): the layers of colliders the chain does not collide with, a set bit meaning no
  collision. Layers are read when the manager builds the collider lists, so a change is seen the next frame;
- `allowSleep` (default on): off keeps the chain awake. The manager wakes it each frame before the solver runs. With a
  manager `sleepFrames` of 1 the solver can still snap it to its pose at the end of a frame, which looks the same;
- `lod` (`JiggleChainLod`), and `LodLevel`, `IsAsleep` and `Wake()` to read and change what the manager decided;
- `AddImpulse(dv)` and `AddImpulseAt(point, dv, radius, falloff)`, which hit the bones at once (so they work in a frame
  with no step too) and return false when the chain is not simulated. `AddBurst` and `AddShove` of the manager are
  different: they are queued as impulse fields and given on the first step of the next frame that runs one.

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

Since 0.4.0 it also has `colliderMargin` (0 to 2 m, default 0.25), `lodCamera`, `lodCollisionDistance`,
`lodCullDistance` (both 0 by default, which is off), `lodHysteresis` (2 m), `lodCullOffscreen` (off) and
`lodOffscreenDelay` (0.5 s). `ColliderShapeCount`, `ForceFieldCount` and `PendingImpulses` count what it holds,
`GetChains(list)` copies the chains, and `AddBurst` and `AddShove` queue a push of everything in reach. The collider
and field components register themselves in `OnEnable`.

Each frame (`Simulate(dt)`, called from `LateUpdate`) does this:

1. If chains came or went, rebuild: a new `JiggleRuntime` is built from all registered chains and the old
   one is disposed after the state of the chains that stayed was copied over.
2. Bake the settings of the chains that changed, apply the manager settings to the host, and
   apply the reset requests.
3. Prepare the rigs. Read each anchor (the transform of the chain's anchor, or the chain's own if there is none),
   decide the level of detail of each chain from the distance to the camera and whether the camera sees it, set or
   clear the disabled flag (switching a chain on again resets it), wake the chains that may not sleep, and build the
   `ignore` mask of each rig. A chain that is NoCollision or Off gets the mask with every bit set.
4. Write the scene: the collider lists (`JiggleColliderSet.Write`) and the fields (`JiggleFieldQueue.Write`).
5. Run the frame on the backend asked for. Native falls back to Burst when the library does not
   load or does not have this package's interface version, with one warning. This is what happens
   until the build workflow has refreshed the committed binaries.
6. Commit the impulses with the number of steps that ran (0 if the solver reported an error), so impulses wait through
   a frame that had no step.
7. Record `Stats`.

The distance of a chain is measured from the camera to its anchor, and the sphere tested against the view is the
extent of the rig plus 0.5 m. Culled chains still run the read and write jobs, because the jobs are built once for
all chains (see the known limits).

Chains that cannot be simulated are logged once per rebuild and left out. `ActiveBackend` is
the backend that really runs and `NativeError` says why Native was not used.
## Runtime/Unity/JiggleChainLod.cs

`JiggleLodMode` (Inherit, AlwaysSimulate, Custom) and the serializable `JiggleChainLod` of a chain: the mode, and for
Custom its own `collisionDistance`, `cullDistance`, `cullOffscreen` and `offscreenDelay`. `Resolve(managerLimits,
hysteresis)` gives the `JiggleLodSettings` the manager uses for the chain: the manager's own for Inherit, none for
AlwaysSimulate, the chain's own values and the manager's hysteresis for Custom.

## Runtime/Unity/JiggleColliderShape.cs

The collider component (`MidManStudio/AllThingsJiggle/Jiggle Collider`). `shape` (sphere, capsule, box, plane, inside
sphere), `center`, `radius`, `height` and `axis` (a capsule), `size` (a box, edge to edge, with `radius` as its
rounding) and `staticShape`. The shape is built from the transform each frame: center, axes and `lossyScale` of the
object. A capsule's half height is `height * scale along the axis * 0.5 - radius` and its radius takes the larger of
the two scales across the axis, a sphere, an inside sphere and the rounding of a box take the largest scale, and a box
scales each edge by its own axis. A plane is solid on the side its up direction points away from.

`Build(out moved)` is what the manager calls: it says whether the shape moved since the last build, which wakes
sleeping chains near it. A static shape is built once and then reused. `Peek()` builds the shape with no side effect,
for the gizmos and the tests. A shape that changes while `staticShape` is on is not noticed, and a static shape does
not wake sleepers.

Not a Unity collider: no physics, no trigger, no rigidbody, and a chain does not push a shape.

## Runtime/Unity/JiggleForceField.cs

The force field component (`MidManStudio/AllThingsJiggle/Jiggle Force Field`). `shape` (Directional, Radial,
Vortex), `strength` (m/s^2, negative pulls a radial field in), `radius` (0 reaches everywhere for a directional
field, and a radial or vortex field needs one), `falloff` (0 to 4), `turbulence`, `frequency` and `gustScale` (gusts),
and `impulseStrength`. The direction is the forward of the transform, and for a vortex its up.

`Build()` gives the acceleration field the manager writes every frame while the component is enabled. `Fire()` queues
one impulse of `impulseStrength` with the same shape and returns false when there is no manager (outside play mode).
It works on a disabled component. The chain scales the push with `windResponse` for the acceleration and
`impulseResponse` for the impulse.


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
buttons for `ResetSimulation` and `Rebind`. Since 0.4.0 it also shows the level of detail and the sleep state in
play mode, with Wake and "Hit sideways" buttons. `JiggleChainGizmos` draws the bones of a selected
chain in blue and the simulated particles in orange while the game runs.

## Editor/JiggleShapeEditors.cs

The inspectors of `JiggleColliderShape` and `JiggleForceField` (the default fields, with help boxes for what is
missing, such as a radial field without a radius, and in play mode a Fire button on the field), and
`JiggleShapeGizmos`, which draws a selected shape or field: the sphere, capsule, box, plane or inside sphere, the
direction and radius of a field. The drawing uses the Core's own vector math on the same records the solver
gets, so what the gizmo shows is the shape the solver uses.

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

## Editor/Lab/JiggleLabScene.cs

The builders the three feature labs share: `Root(name)` (an empty object, selected), `Chain(parent, name, position,
bones, spacing, settings)` (an anchor, then bones hanging down, with the component on the first bone), `Tip(chain, out
position)` and `Remove(root)`. The labs build in play mode only, so what they make is gone when play mode ends and
the scene of the project is never touched.

## Editor/Lab/JiggleColliderLab.cs

A chain that collides, a ball that sweeps through it and a floor. Controls: sweep on and off, range and speed, whether
the chain ignores the ball's layer (the ball is on layer 2, Ignore Raycast, which every project has, so ignoring it
does not also ignore the floor), segment collision, whether the chain may sleep, the manager's sleep frames and
collider margin. It shows the colliders written, the collider tests listed over all rigs, whether the chain sleeps and
the closest bone to the ball's surface. The ball is moved from `EditorApplication.update`, so it moves while the window
is not drawn.

## Editor/Lab/JiggleFieldLab.cs

A chain and a wind field, and a button for every way to push: a hit on the chain, a hit at the tip, a fired field
impulse, a burst under the tip, and a shove of every chain. It has sliders for the wind (shape, strength, radius,
falloff, gusts), the two responses of the chain and the sleep settings, and shows the fields written, the impulses
waiting and how far the tip travelled since the last push, so the ways to push can be compared.

## Editor/Lab/JiggleLodLab.cs

Edits the manager's level of detail limits and builds a row of twelve chains in front of the main camera, 3 to 47 m
away. It lists each chain with its distance, level and sleep state, and the counts of chains that are NoCollision,
Off and asleep. It needs a camera and says so when there is none.

## Editor/Lab/JiggleLabWindow.cs

MidManStudio, AllThingsJiggle, Jiggle Lab (menu bar). Six tabs:

- **Live** checks that Burst compiles the jobs and that the native library loads with this package's
  interface version, and in play mode shows the manager: the backend (editable), the backend that runs, the
  counts (with the collider, field, level of detail and sleep counts since 0.4.0), the steps, the frame time and the
  status.
- **Colliders**, **Fields and hits** and **LOD** draw the three labs above. The window repaints Live and these
  three tabs while the editor runs, subscribes a tick to `EditorApplication.update` in `OnEnable` (so the ball moves
  and the tip is followed between draws) and unsubscribes it in `OnDisable`.
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

## Tests/Editor/JiggleFeatureTests.cs

23 tests of the 0.4.0 features, in the same shared file style as the parity tests: no UnityEngine, so the .NET
harness runs them on the managed backend and, where it matters, on both.

- the host: clearing and replacing colliders, a stale list range giving `ErrBadRig`, switching a rig off and on again,
  `Wake`;
- hits: a hit against a directional impulse field (2e-5 m), the fade of `HitAt`, `ImpulseResponse`, the refusals, a hit
  across a frame with no step on both backends;
- the field queue: standing fields and impulses, the cap, and an impulse kept through a zero step frame;
- a burst on both backends, and the layout of what `JiggleShapes` builds;
- the collider set: `Near`, the lists by layer, by reach and by anchor, the margin, the wake rules, rewriting without
  growth, and colliders and layers through the solver;
- level of detail: distance, hysteresis, zero and NaN distances, the offscreen delay, both rules together, and the
  frustum convention.

## Tests/Editor/JiggleUnityLayerTests.cs

Ten tests for the Unity layer that need the editor. The first five: the default settings bake to the defaults
of the Core, the stiffness curve scales along the chain, the Burst solver stays within 1e-4 meters of
the managed kernel over 120 frames of 40 rigs (with and without coupling), the step and the resolve
as two jobs give the same bits as one job, and the probe and an empty host do not throw. Since 0.4.0: the new
settings bake and default to the Core's, the three chain level of detail modes resolve to the limits they promise, a
collider component builds the solver shape from its transform, it reports movement only when it moves (and a static
shape is built once), and a chain that is not simulated refuses a hit and answers quietly. They run
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

The harness runs `JiggleParityTests.cs` (30 tests) and `JiggleFeatureTests.cs` (23), 53 in all. It does not compile
the Unity layer.

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

The stubs grew with 0.4.0: `Camera` (and `Camera.main`), `Plane`, `GeometryUtility.CalculateFrustumPlanes`,
`LayerMask`, `GameObject.layer` and `transform`, more of `Transform` and `Vector3`, `Object.DestroyImmediate`,
`Quaternion.Euler`, more of `Mathf`, and in the editor `EditorApplication` and `Selection`.

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
- Collider lists are built O(rigs x colliders) on the main thread each frame. That suits hundreds of chains and tens
  of colliders and not thousands of each. A spatial grid would be the fix, and nothing here needs one yet.
- A chain that is Off (level of detail) is not solved, but the transform read and write jobs still run for it, because
  the jobs cover all chains of a build. The saving is the solver, not the transforms.
- Level of detail has one camera and no lower step rate for chains that are far but not culled.
- A chain with `allowSleep` off, when the manager's `sleepFrames` is 1, can still be snapped to its pose at the end of
  the frame. It is woken every frame, so it looks the same.
- A hit on a chain that is not simulated yet (just enabled, not built) returns false and is lost. Hits wait only
  through frames with no step.
- Static shapes are not watched. Turn `staticShape` off for a shape that may move. Only the shapes that move wake
  sleeping chains.
- Collider layers are the layers of `GameObject.layer` (0 to 31) with a `LayerMask ignoreLayers` on the chain and the
  rule "a set bit means no collision". This is a choice and not a requirement of the solver: the lists are filtered in
  managed code, so another scheme needs no native change.
- Nothing about the features of 0.4.0 has run inside Unity. They are tested under .NET 8 (the Core, 23 tests) and
  compiled against stubs (the Unity layer and the labs).
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

### Editor/Lab/JiggleLabWindow.cs, Runtime/Unity/JiggleChain.cs, JiggleManager.cs

- The lab was under `Tools/MidMan Studio` and the two components under `MidMan Studio` in the Add Component
  menu. The house rule is a top level `MidManStudio` menu with a sub menu per package, so the paths are now
  `MidManStudio/AllThingsJiggle/...`. Found by Su after the first drop, not by a build.

### Runtime/Unity/JiggleChainSettings.cs, JiggleManager.cs (0.4.0)

- Found while writing the collider tests: a chain could not collide at all. `JiggleChainSettings` never set `Collide`
  or a collision radius, so the baked parameters had colliders off whatever the scene held. The settings now have
  `collide` and the values that go with it, with the Core's defaults.
- A stale collider list range gives `ErrBadRig`, so rewriting the colliders each frame with only `ColliderCount = 0`
  would break any rig whose list pointed past the new end. `ClearColliders` zeroes every rig's range, and a test keeps
  that documented.
- The solver does not wake a sleeping chain for a collider that moves up to it, so `JiggleColliderSet.Write` wakes it
  when a shape on its list has moved.
- An impulse field acts on the first step of a frame only, so a frame with no step would have lost it. The queue now
  commits impulses only when `steps > 0`, and a hit (written into the state) is kept by construction.
- A chain that is switched off by the level of detail snaps to its pose when it comes back. Enabling a rig sets
  `RigReset`, so it never shows the state it had when it was switched off.
- The layer system was a choice: the native record has no slot for a layer, so the lists are filtered in managed code
  by `GameObject.layer` and `ignoreLayers`, a set bit meaning no collision. No native binary changes.

### Tests/Editor/JiggleFeatureTests.cs (0.4.0)

- Corrected before the first run: an arithmetic assertion on the number of indices written that was nonsense (the
  right value is 6), a wrong "the first bone takes 0" assertion, a state read after a step through `WorldPos` (which
  reads the output, so a helper reads the state before any step), the rest position in the hit parity test, and the
  frustum "first two planes" assertion.
- The harness stand in lacked `IsFalse(bool, string)`, `IsNotNull(object)` and `IsNotNull(object, string)`. They were
  added with the NUnit signatures.

### Tests~/UnityStubs

- The first draft of the stub for `TransformAccessArray` did not assign its two properties in the
  constructors and stopped the build with CS0843. The stub was fixed.
- The `.gitignore` rule `*.csproj` would have kept the five stub projects out of the commit, the same
  failure the harness had. A negation for this folder was added.
- 0.4.0: the stub of the `LayerMask` conversion left a struct unassigned (CS0165), and the editor and unity builds
  needed `GameObject.transform`, `Vector3 +` and `Vector3 *`. All were added to the stubs.

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
