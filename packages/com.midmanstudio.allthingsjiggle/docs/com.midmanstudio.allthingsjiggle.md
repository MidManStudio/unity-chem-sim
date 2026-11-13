# com.midmanstudio.allthingsjiggle

All Things Jiggle is a Unity package for jiggle physics on chains of bones. It has
the solver (a managed kernel, bindings to the Rust library and the proof that the
two agree, with two way coupling), since drop 2 the runtime (a `JiggleChain`
component for each chain, a manager that simulates all of them once a frame with
Burst jobs, and an editor lab that runs the same scenes on the Burst, native and
managed backends) and since drop 2b the scene side: collider and force field
components, hit impulses, level of detail and sleep controls, and since drop 3 the
authoring side: presets for the usual kinds of chain and body colliders fitted to a rig, and since
drop 4 a lattice modifier that makes a prop or an enemy wobble as one body.
Nothing here has run inside Unity yet (see Known limits). The Rust half is documented in
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
  JiggleLod.cs, JiggleWorld.cs, JiggleDamping.cs, JigglePresets.cs, JiggleBodyFit.cs,
  JiggleTreeBuild.cs, JiggleLatticeGrid.cs, JiggleFfd.cs, AssemblyInfo.cs
- Runtime/Unity: JiggleRuntimeTypes.cs, JiggleChainSettings.cs, JiggleChain.cs,
  JiggleChainLod.cs, JiggleColliderShape.cs, JigglePreset.cs, JiggleBodyColliders.cs,
  JiggleForceField.cs, JiggleLatticeSettings.cs, JiggleLattice.cs, JiggleLatticeMesh.cs,
  JiggleLatticeJobs.cs, JiggleManager.cs, JiggleRuntime.cs, JiggleJobs.cs,
  JiggleBurstSolver.cs, JiggleMem.cs, AssemblyInfo.cs
- Editor: JiggleChainEditor.cs, JiggleShapeEditors.cs, JigglePresetEditors.cs,
  JiggleBodyEditors.cs, JiggleLatticeEditor.cs, Lab/JiggleBenchScenes.cs, Lab/JiggleLabRunner.cs,
  Lab/JiggleLabScene.cs, Lab/JiggleColliderLab.cs, Lab/JiggleFieldLab.cs,
  Lab/JiggleLodLab.cs, Lab/JigglePresetLab.cs, Lab/JiggleBodyLab.cs, Lab/JiggleLatticeLab.cs,
  Lab/JiggleLabWindow.cs
- Assembly definitions
- Tests/Editor/JiggleParityTests.cs
- Tests/Editor/JiggleFeatureTests.cs
- Tests/Editor/JigglePresetTests.cs
- Tests/Editor/JiggleBodyFitTests.cs
- Tests/Editor/JiggleLatticeTests.cs
- Tests/Editor/JiggleUnityLayerTests.cs
- Tests/Editor/JigglePresetUnityTests.cs
- Tests/Editor/JiggleLatticeUnityTests.cs
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
   **2b Colliders, fields and level of detail (0.4.0).** The collider
   broadphase in C# (a list of near colliders for each chain, filtered by layer),
   collider components, force field components and hit impulses, sleeping controls,
   level of detail and culling by distance and by the camera, and one lab tab for each
   feature. The layer rule is the one of the MMD rig study with the sense that Unity
   uses for it: a set bit in a chain's `ignoreLayers` means no collision.
3. **Presets and body colliders (0.5.0).** Presets for hair, tail, ear, chest,
   horn and wing, from the technique level findings of the rig study where it has them and from
   reasoning where it does not, with the frictions given per second so they mean the same at any
   step rate. Body colliders fitted to the joints of a rig, a humanoid one or one assigned by hand.
   A lab tab for each. No Rust and no interface change. The Grand Theft Grimoire chest adapter
   is not in this drop: it waits for access to that repository.
4. **Lattice modifier (this drop, 0.6.0).** A free form deformation of unskinned meshes, for props and
   enemies, whose control points pull their neighbours. The Core has a tree builder for the grid, a tree
   rig in `JiggleWorld` and `JiggleHost`, and the deformation itself on pointers. The Unity layer has the
   `JiggleLattice` component, a Burst control job and a Burst vertex job after the solver, the upload to a
   private copy of the mesh, an inspector with gizmos and a lab tab. The solver already took trees, so there
   is no Rust change and no interface change. It ships in its own archive.

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

**Presets.** In the inspector of a chain, pick a kind (Hair, Tail, Ear, Chest, Horn, Wing) and press Apply, or
call `chain.ApplyPreset(JigglePresetKind.Hair)` from code. A preset replaces the settings and the tip scale of the
chain and leaves the rest: the pose source, the end bone, the layers, sleeping and the level of detail stay. The
frictions of a preset are per second, and Apply turns them into the per step drag of the manager's step rate (60 when
there is no manager yet), so apply a preset again after you change `fixedHz`. To keep your own tuning, make a
**Jiggle Preset** asset (Create, MidManStudio, AllThingsJiggle, Jiggle Preset), start it from a built in kind and edit
it, then apply the asset from the chain inspector or with `chain.ApplyPreset(asset)`. The numbers of the built in
kinds are starting points that nobody has judged by eye yet. The **Presets** tab of the lab shows all six side by side
and has a seventh chain that takes the values you edit.

**Body colliders.** Add a **Jiggle Body Colliders** (Add Component, MidManStudio, AllThingsJiggle, Jiggle Body
Colliders) to the character, press "Fill empty joints from the Animator" for a humanoid rig, or set the joints by hand
for any other, stand the character in its bind pose (a T pose or an A pose) and press Build. It makes a
`JiggleColliderShape` under each joint: a sphere for the head and the hands, capsules between joints for the neck, the
trunk, the arms, the legs and the feet. They follow the animation. Chains collide with them while `Collide` is on,
except for the layers they ignore. `scale` multiplies every radius, `height` overrides the height of the body, and
`parts` leaves parts out. The scene view draws what a build would make before you build it. The **Body** tab of the lab
builds a test figure and sweeps a hair strand across it.

**Lattices.** For a prop or an enemy that is not skinned, add a **Jiggle Lattice** (Add Component, MidManStudio,
AllThingsJiggle, Jiggle Lattice) to the object that has the MeshFilter and the renderer, and press play. The bounds of
the mesh become a box with 1 to 5 control points on each axis (3 by 3 by 3 by default), the control points are simulated
by the same manager as the chains, and the vertices follow them, so a shove travels through the body and it wobbles as one
piece. One control point is held by the object and never moves: the one nearest the middle (`Center`), or the one nearest
the pivot (`Pivot`, for a prop that stands on its pivot). Move or turn the object and the rest lags behind it. The mesh must
be readable (Read/Write in its import settings). The manager swaps the mesh on the filter for a private copy while the
lattice is enabled and gives the original back when it is disabled, so the asset is never changed. Skinned meshes and
meshes under static batching cannot be deformed. Settings are in `JiggleLatticeSettings`: stiffness, soften, stretch, drag,
air drag, gravity, ignore root motion, mass (0 turns the coupling between control points off), and the wind and impulse response. `lod` and `allowSleep` work as they do on a chain. From code: `lattice.AddImpulse(dv)` and `AddImpulseAt(point, dv, radius, falloff)`, `Wake()`,
`ResetSimulation()` and `SettingsChanged()` after a script edits `settings`. The **Lattice** tab of the lab builds a cube and
moves it so the lattice has to wobble.

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
`MidManStudio/AllThingsJiggle/Jiggle Lab`, `.../Jiggle Chain`, `.../Jiggle Manager`, `.../Jiggle Collider`,
`.../Jiggle Force Field`, `.../Jiggle Preset` and `.../Jiggle Body Colliders`.

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

`WriteTree` (0.6.0) is the same for a tree rig: no virtual end, one world position for each particle with the anchor
first, and the rotation of each particle taken from an optional array (the identity when there is none, which is what a
lattice has, because a lattice never turns a bone). It moves the last pose, makes every position relative to the anchor
and sets the anchor move and the world anchor, so the solver cannot tell a tree from a chain.

## Runtime/Core/JiggleHost.cs

The solver arrays in unmanaged memory with room to grow, for a caller that runs the
solver from jobs or threads. `JiggleWorld` keeps its arrays in managed memory that the garbage
collector may move, so jobs cannot hold their addresses. `JiggleHost` can: the addresses stay valid
until the next call that adds something, so nothing may be added while a frame runs.

- `AddChain(rest, params, tipScale)` builds a rig the same way `JiggleWorld.AddChain` does and
  returns its index. `SetMass` sets the mass of one particle.
- `AddTree(rest, parent, params, pinRoots)` (0.6.0) builds a tree rig: the anchor, then one particle for each entry of
  `parent` (entry i is the parent of particle i + 1 and must be an earlier particle), with no virtual end. `IsTree(rig)`
  says which kind a rig is, from its last particle, because a chain always ends in a virtual one. `SetTreeParams(rig,
  params)` bakes new parameters into a tree in place, keeping its parents. The chain calls refuse a tree and the tree calls
  refuse a chain, with an `ArgumentException`, because the two build different pose data.
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
- `AddTree`, `IsTree` and `SetTreePose(rig, positions, rotations)` (0.6.0) are the tree forms, built on
  `JiggleTreeBuild` and `JigglePose.WriteTree`, with the same refusals as the host.
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

## Runtime/Core/JiggleDamping.cs

Converts a damping given per second into the share lost per fixed step, and back. The solver's `Drag` and
`AirDrag` are per step, so the same number looks different at 30 and at 60 steps per second. A preset states its
frictions per second and becomes per step only when it is turned into parameters.

- `PerStep(perSecond, hz)` is `1 - (1 - perSecond) ^ (1 / hz)`, worked in double precision with `Math.Pow` and
  clamped to 0 to 1. Hair at 0.999 a second is 0.1088 per step at 60 Hz and 0.2056 at 30 Hz.
- `PerSecond(perStep, hz)` is the inverse.
- `CleanHz(hz)`: a rate that is not finite or is below 1 counts as `DefaultHz`, 60. Values outside 0 to 1 and values
  that are not numbers are clamped to 0 and 1 first, so no input gives a NaN.

Only the two frictions are converted. Stiffness and coupling stay per step: see Known limits for why, and for what
that means for the coupled presets.

## Runtime/Core/JigglePresets.cs

The six built in presets and what turns them into solver parameters. No Unity references.

- `JigglePresetKind`: `Hair`, `Tail`, `Ear`, `Chest`, `Horn`, `Wing`. `JigglePresets.Count` is 6, `Name(kind)` is the
  name the inspector shows, and a kind that does not exist gives the hair preset from `Get`.
- `JigglePresetValues` (serializable, so Unity can keep it in an asset) has the fields of `JiggleParams` and four more:
  the two frictions per second (`DampingPerSecond`, `AirDampingPerSecond`), `StiffnessAtTip` (a multiplier on the
  stiffness of the last bone, with a straight line from the first bone) and `TipScale`.
- `ToParams(values, bone, bones, fixedHz)` makes the parameters of one bone, and `ToParams(values, bones, fixedHz)` of
  every bone. Every value is clamped into its range and a value that is not a number falls to a safe one, so a preset
  with bad numbers still bakes into something the solver accepts. `CleanTipScale` gives the tip scale a chain takes,
  with a value that is not a positive number counting as 1.

The numbers, at 60 Hz. The swing limit is in the units of `AngleLimit`, where 1 is 90 degrees.

| Kind | Stiffness | At tip | Swing limit | Damping a second | Air damping | Gravity | Mass | Collides |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Hair | 0.30 | 0.7 | 0.22 (20 degrees) | 0.999 | 0.5 | 1.4 | none | yes, 0.02 m |
| Tail | 0.45 | 0.55 | 0.55 | 0.995 | 0.4 | 1.0 | 1 | yes, 0.04 m |
| Ear | 0.70 | 0.8 | 0.35 | 0.99 | 0.3 | 0.6 | none | no |
| Chest | 0.85 | 1.0 | 0.12 | 0.9995 | 0.3 | 0.5 | 1 | no |
| Horn | 0.97 | 1.0 | 0.05 | 0.999 | 0.5 | 0.1 | none | no |
| Wing | 0.40 | 0.6 | 0.60 | 0.995 | 0.6 | 0.8 | none | no |

Where they come from. The hair and chest values follow the technique level findings of the MMD rig study in
`rust_lib/jiggle_core/docs/jiggle_core.md` (section "What the MMD rig study taught the design"): hair swings inside a cone of
about 20 degrees with a damping near 1 a second, and a chest is a short chain with a low swing limit and a strong pull
to the pose. The hair gravity of 1.4 is a choice: in a one way solver the slow swing of a weighted strand is made with
gravity and drag together. The tail, ear, horn and wing are reasoned from what the kind of chain does. The wind
response of the wing (2) and of the horn and chest (0.1 and 0.2) are in the same class of guess. None of the numbers
was judged by eye, and the tests check only that they are stable, finite, equal on both backends and bounded. Tune them
in the lab. The chest and ear presets have `Collide` off because their bones sit inside the body shapes that
`JiggleBodyFit` makes, and a bone inside a collider would only be pushed out.

## Runtime/Core/JiggleBodyFit.cs

Fits body colliders to the joints of a rig. It is the maths only, with no Unity references, so it is tested outside
Unity. The component that applies it is `JiggleBodyColliders`.

- `JiggleBodyPart` (head, neck, torso, upper arm, forearm, hand, thigh, shin, foot), `JiggleBodyShapeKind` (sphere,
  capsule) and `JiggleBodyShape`: a shape in world space with its center, axis, length from end to end (caps
  included) and radius. `ToCollider()` makes the solver's collider. A capsule's solver half height is
  `Length * 0.5 - Radius`, which is the distance between the two joints over two.
- `RadiusFraction(part)`: the radius of each part as a share of the body height. Head 0.05, neck 0.03, torso 0.085,
  upper arm 0.028, forearm 0.022, hand 0.025, thigh 0.047, shin 0.032, foot 0.025. They are the proportions of an
  average adult, so the caller scales them: `Radius(part, height, scale)` is the fraction times the height times the
  scale, and 0 for a height or scale that is not a positive number.
- Height. `Height(head, feet)` takes the distance from the head joint to the middle of the ankles and multiplies it by
  1.2 (`HeightPerAnkleToHead`), because the head joint is at the base of the skull and the ankle is a few centimeters
  above the sole. `HeightFromTorso(hips, head)` multiplies the distance from the hips to the head joint by 2.8, for a
  rig with no feet. For an average adult the two land within a few percent of each other, and a test says so.
- `Capsule(a, b, radius)` runs from joint to joint, so its caps reach one radius past each joint and two capsules that
  meet at a joint overlap there and leave no gap a thin particle could slip through. `Sphere(center, radius)` is a ball.
  Both refuse joints that are not finite or are closer than `MinLength` (0.1 mm), and a radius that is not positive,
  and return false with an empty shape.
- `Head(neck, head, height, scale)`: a sphere whose center is 0.9 of its radius above the head joint along the neck.
- `Torso(hips, neck, height, scale)`: a capsule from the hips up to the neck joint. Its top cap ends at the neck joint
  and does not reach past it, because a cap one radius above the neck would swallow the back of the head. When the hips
  and the neck are closer than a radius the capsule runs joint to joint.
- `Hand(elbow, wrist, height, scale)`: a sphere one radius past the wrist along the forearm.

The head sphere is small on purpose. At 0.05 of the height it is about 8.5 cm in radius on a 1.7 m body, under the real
size of a head, so that a hair strand rooted at the scalp is not inside it. The cost is that hair can pass through the
outer part of a real head. Raise `scale` for the head with the body, or give hair a collision radius to taste.

## Runtime/Core/JiggleTreeBuild.cs

`internal`. What adding a tree rig computes, in one place, so the world and the host cannot drift apart: `Validate`
(an anchor and at least one more particle, at most 128 in all, one parent and one parameter set for each particle after
the anchor, every parent an earlier particle), `BakeDefs` (the anchor, then the baked particles) and `IsTree`. With
`pinRoots` a particle whose parent is the anchor is a root particle, held to its pose by its root stretch the way the first
bone of a chain is. Without it that particle hangs on the anchor like any other on its parent, which is what a lattice
uses (see the section on `JiggleLatticeGrid` for why).

## Runtime/Core/JiggleLatticeGrid.cs

The control grid of a lattice and the tree it is simulated as. A grid has 1 to 5 points on each axis, at most 125, so it
fits the 128 particles of a rig, and a lattice needs at least two points to move. The points sit evenly from one side of
the box to the other (an axis with one point puts it in the middle), which is what makes the deformation reproduce a
straight stretch or turn of the whole box exactly. The grid index is `x + nx * (y + ny * z)`.

`BuildTree(nx, ny, nz, rootPoint)` makes a `JiggleLatticeTree`. The root becomes the anchor of the rig, particle 0, and is
`rootPoint` or, when that is not a point of the grid, the point nearest the middle (the lowest index when several are
equally near). The other points are reached breadth first from the root through their six grid neighbours, in the order
x up, x down, y up, y down, z up, z down. A point gets its particle number when it is reached, so the parent of every
particle has a lower number, as the solver requires, and the result is the same every time. The tree has `PointOfParticle`,
`ParticleOfPoint`, `Parent` and `ParentsOfParticles()` (the Count - 1 entries `AddTree` takes). `ParticleRest`, `PointRest`,
`MiddlePoint` and `NearestPoint` give rest positions and pick roots.

The anchor is a control point itself and not a separate particle sitting on top of it. This matters: a particle at the same
place as its parent has no direction to measure a swing from, the solver turns all its children by a swing that is not
there, and a coupled lattice blew up (see Fixes and Problems).

## Runtime/Core/JiggleFfd.cs

Free form deformation (Sederberg and Parry, 1986) on pointers, with no allocation, so a Burst job runs it. A position
inside the box has coordinates (s, t, u) between 0 and 1 and moves by the sum over the control points of the product of three
Bernstein weights times the displacement of the point. Control points sit evenly in the box, so any displacement that is an
affine function of position is reproduced exactly (a test holds it to 2e-4 on grids up to 5 by 5 by 5), and every displacement is a weighted average of
the control displacements, so it is never larger than the largest of them. That second property is why the bounds of a
deformed mesh are the bounds of the original widened by the largest control displacement. A position outside the box takes
the displacement of the nearest point on its surface. Only the control points at the corners of the box lie on the deformed
surface: the weight of an interior point is small (0.125 for the centre of a 3 by 3 by 3 grid), which is a property of
Bernstein deformation, so the surface follows the interior points loosely and the corners tightly.

- `Weights` and `Derivatives` give the Bernstein weights of an axis and their derivatives by repeated linear steps. `InverseSize` is 0 for a flat
  axis, so a mesh with no thickness has coordinate 0 on that axis and no division by zero. `Coordinate` clamps to 0..1 and maps a value that is
  not a number to 0.
- `FitBox` fits a box to positions with a padding. The component takes its box from the bounds of the mesh and does not use it. It is there for
  scripts that fit a lattice to part of a mesh. `Coordinates` turns positions into coordinates once, at build time.
- `Displace` and `DisplaceWithGradient` give the displacement, and with the gradient its Jacobian by the derivative weights.
- `TransformNormal` moves a normal by the inverse transpose of the Jacobian (the cofactor matrix), flips the sign when the
  determinant is negative, and keeps the input normal as it was when the result has no length. Tangents are not deformed.
- `DeformRange` moves a range of vertices, writing positions and, when the normal pointers are not null, normals. It is what a job index runs.
- `ControlDisplacement` turns the solver output into the displacement of each control point in the space of the mesh: where the solver put the
  particle minus where the pose has it, both from the anchor, turned by the inverse of the linear part of the object's matrix. `MaxLength` is the
  longest of a set of displacements.
- `JiggleMat3` is the small matrix type for that, with `Mul` and `TryInvert` (the identity and false for a flat matrix or a value that is not a number).

## Runtime/Core/AssemblyInfo.cs

Makes the internals visible to the editor test assembly, so the layout test can use the
bindings directly.

## Runtime/Unity/JiggleRuntimeTypes.cs

`JiggleRuntimeBackend` (Burst, Native, Managed), `JigglePoseSource` (Bind, Animated, described in
Using the package) and `JiggleFrameStats`, a plain struct with what the last frame did: rigs,
particles, steps, the frame time in milliseconds, the backend that ran, the solver status and
the number of rebuilds. Since 0.4.0 it also has `Colliders` (shapes written), `ColliderRefs` (collider tests listed
over all rigs), `Fields` (fields written, impulses included), `LodNoCollision`, `LodOff` and `Asleep` (counts of
chains). Since 0.6.0 it also has `Lattices` (how many of the rigs are lattices) and `LatticeUploads` (meshes given new
vertices in the last frame, which is 0 when every lattice is at rest or asleep).

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

Since 0.5.0 it has `ApplyPreset(values, fixedHz)` and `ApplyPreset(kind, fixedHz)`. They set every value to the preset:
the numbers come from `JigglePresets.ToParams` for one bone, so they are clamped the same way, the frictions become per
step for `fixedHz`, and `StiffnessAtTip` becomes a straight line in `stiffnessAlong` (a constant when it is 1). A test
checks that the baked parameters of every bone equal what the Core makes from the same preset, for all six kinds at 30,
60 and 120 Hz and for chains of 1, 2 and 6 bones.

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

Since 0.5.0 it has `ApplyPreset(kind)`, `ApplyPreset(asset)` and `ApplyPreset(values)`. They replace the settings and
the tip scale and keep everything else, convert the frictions for the manager's `fixedHz` (60 when there is no manager),
and call `SettingsChanged`. A null asset does nothing. A tip scale that is not a positive number becomes 1, and the
smallest tip scale a chain takes is 0.01.

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

Lattices (0.6.0) register in `OnEnable` and leave in `OnDisable` like chains, with their own lists for changes and resets.
`LatticeCount` counts them and `GetLattices(list)` copies them. A lattice whose values changed is baked in place with
`SetTreeParams`. One whose mesh, box, grid or anchor changed makes the manager rebuild, because its rig has a different
shape. Its state is carried over a rebuild only while none of those changed. The rigs of the chains come first in a build,
then those of the lattices.

Each frame (`Simulate(dt)`, called from `LateUpdate`) does this:

1. If chains came or went, rebuild: a new `JiggleRuntime` is built from all registered chains and the old
   one is disposed after the state of the chains that stayed was copied over.
2. Bake the settings of the chains that changed, apply the manager settings to the host, and
   apply the reset requests.
3. Prepare the rigs. Read each anchor (the transform of the chain's anchor, or the chain's own if there is none; for a lattice the
   world place of its anchor control point),
   decide the level of detail of each chain from the distance to the camera and whether the camera sees it, set or
   clear the disabled flag (switching a chain on again resets it), wake the chains that may not sleep, and build the
   `ignore` mask of each rig. A chain that is NoCollision or Off gets the mask with every bit set. A lattice tests no colliders,
   so its mask always has every bit set. The level of detail is decided by one helper for both kinds.
4. Write the scene: the collider lists (`JiggleColliderSet.Write`) and the fields (`JiggleFieldQueue.Write`).
5. Run the frame on the backend asked for. Native falls back to Burst when the library does not
   load or does not have this package's interface version, with one warning. This is what happens
   until the build workflow has been run by hand and has committed libraries of this interface version.
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

## Runtime/Unity/JigglePreset.cs

A preset kept as an asset (`MidManStudio/AllThingsJiggle/Jiggle Preset` in the Create menu). It holds one
`JigglePresetValues`, which starts as the hair preset. `JigglePreset.Create(kind)` makes one in memory with the values of
a built in kind, for scripts and tests. The built in kinds need no asset. The values struct has no Range attributes,
because the Core has no Unity references, so the asset inspector draws sliders for every field.

## Runtime/Unity/JiggleBodyColliders.cs

The component that turns `JiggleBodyFit` into scene objects (`MidManStudio/AllThingsJiggle/Jiggle Body Colliders`).

- `joints` (`JiggleBodyJoints`): the hips, the base of the neck, the head joint, and for each side the shoulder, elbow
  and wrist, the hip joint, knee, ankle and toes. Left and right are those of the character, as in the humanoid rig.
  `FillFrom(animator)` fills only the empty ones from a humanoid avatar and returns how many it filled. The neck and the
  toes are optional bones in a humanoid rig and stay empty when the avatar does not map them: without a neck the trunk
  runs 85 percent of the way from the hips to the head, and without toes there is no foot.
- `animator` (empty means the first one on this object or below it), `parts` (a flags mask: head, neck, torso, arms,
  hands, legs, feet), `scale` (a multiplier on every radius), `height` (0 takes it from the joints), `colliderLayer`
  (the layer of the objects it makes, 0 to 31) and `buildOnAwake` (for characters made at run time that never had colliders
  baked in).
- `ResolveHeight()` is the height a build would use: `height` when it is set, else from the head joint and the ankles,
  else from the hips and the head joint, else 0. With only one foot set, that foot stands for both.
- `Plan(list, out problem)` works out what a build would make from the joints as they are now, without making anything or
  changing the component. It is what the scene view draws and what `Build` follows. It returns false with the reason
  when the height is unknown or no part can be made.
- `Build()` removes the colliders it made before, fills the empty joints from the animator, plans, and makes one
  `JiggleColliderShape` object for each planned shape, named `JiggleBody_<Side><Part>`, as a child of the first joint of
  the part so it follows the animation. A capsule object is turned so its local Y axis runs along the shape and its
  center is the middle of the two joints. The sizes are written in the local units of the new object and a uniform scale
  of the joint is taken out of them, because `JiggleColliderShape` multiplies them by the scale again. It returns how many
  it made, and 0 with a console warning when the joints give no body. `Clear()` removes what it made, `BuiltCount` and
  `GetBuilt(list)` say what exists.
- The list of what it made is serialized (hidden), so the editor can build once and the objects stay with the scene or the
  prefab. At run time the objects are removed with `Destroy`, in the editor with `DestroyImmediate`.

The build reads the pose the joints have at that moment. A character that is crouched or reaching builds a smaller or
lopsided body, so build in the bind pose. A uniform scale on the joints is assumed. The generated shapes are not
animated per frame: they are children of the joints, so they follow the bones, and nothing here reads the pose again.

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

Since 0.6.0 a build also holds lattices. Their rigs come after those of the chains, the list `Lattices` is indexed by rig
number minus the number of chains, and a lattice rig has rig mode 2. It has one entry for each control point, all on the one
transform of the object, and the bind position of an entry is the rest place of its control point in the space of the mesh. The
transform is read for its matrix and never written, and it takes no slot in the array of local rotations. After the solver
the lattice batch (`JiggleLatticeBatch`) schedules the control job and the vertex job, side by side with the bone
rotation and write jobs, and when the frame is complete the main thread gives the moved vertices to the meshes.

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
  Since 0.6.0 a rig in mode 2 (a lattice) goes to `PoseLattice`: each control point is the matrix of the object times its
  rest place, and the result goes to `JigglePose.WriteTree`.
- `JiggleLocalJob` calls `JigglePose.LocalRotations` per rig. It skips a lattice, which has no bones and no room in the array
    of local rotations.
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

## Runtime/Unity/JiggleLatticeSettings.cs

The values of a lattice, in the ranges of `JiggleParams`, the same for every control point: stiffness, soften, stretch, drag, air drag,
gravity, ignore root motion, mass, wind response and impulse response. `ToParams()` makes the parameters of one control point and
`ToParams(count)` an array of them for `AddTree`. A lattice never collides and has no root stretch, no angle limit and a blend of 1.
The defaults are the numbers the response of a 3 by 3 by 3 lattice was measured with in the harness: stiffness 0.4, drag 0.2, stretch 0.3,
mass 1, no gravity and no air drag. With them a jump of the object has settled in about 112 frames at 60 Hz. Larger grids settle slower,
and a lattice always lags a little as a whole because the one anchor holds it. These were picked from numbers and not by eye.

## Runtime/Unity/JiggleLattice.cs

The component. It needs a MeshFilter and allows one lattice for each object. Fields: `settings`, `divisionsX`, `divisionsY`, `divisionsZ`
(1 to 5), `padding` (0 to 1, how far the box reaches past the bounds of the mesh as a share of the size on each axis, 0 by default),
`anchorPoint` (`Center` or `Pivot`), `allowSleep` and `lod`. An axis on which the box has no size, or almost none (under a thousandth of
the largest), counts as one control point whatever its divisions say: points that lie on one another are the zero length link of the Fixes
section. `TryGetBox` and `TryGetDivisions` give the box and the grid as they will be used, `TryGetControlPoint(index, out world)` the
simulated place of a point, and the editor draws all of them. A box with fewer than two control points is refused with a warning.

`Prepare` is called by the manager when it builds. It takes a private copy of the mesh when there is none, or when the filter shows
another mesh or the padding changed, builds the tree and the rest places, and bumps `BuildStamp` whenever the mesh, the box, the grid or the
anchor changed. The runtime carries the state of the rig over a rebuild only when `BuildStamp` equals what it was when the rig was attached.
`OnDisable` unregisters, detaches and gives the original mesh back. The box is the bounds of the mesh and not a fit to its vertices, so
what the editor draws is what is simulated and no scan of the vertices is needed in edit mode. A mesh with loose bounds makes a loose box.

## Runtime/Unity/JiggleLatticeMesh.cs

`internal`. What a lattice holds of its mesh, owned by the component and alive while it is enabled, so the arrays of the manager can be
rebuilt without touching it: a private copy of the mesh (`Instantiate`, marked dynamic, which the filter shows), the vertices and normals at
rest, the lattice coordinates of every vertex (computed once), room for the control displacements of this frame and of the frame the mesh shows (125
points each), and two `NativeArray<Vector3>` that the vertex job writes into, which are also what `Mesh.SetVertices` and `SetNormals` read.
The jobs get raw pointers into these buffers. Nothing frees them while a frame runs, because a frame is complete when it returns and
`Dispose` is only called on the main thread outside the frame.

`Create` refuses a filter with no mesh, a mesh with no vertices, a mesh that is not readable (it says to turn on Read/Write) and bounds
that are not finite. `Upload(reach)` sets the vertices and normals with `MeshUpdateFlags.DontRecalculateBounds` and sets the bounds of the
copy to the bounds of the source widened by `reach`, which is the largest control displacement and an upper bound of how far any vertex
moved, so the mesh is culled only when it is really off screen and no pass over the vertices is needed. `Dispose` puts the original mesh back
if the filter still shows the copy and destroys the copy.

## Runtime/Unity/JiggleLatticeJobs.cs

`JiggleLatticeData` holds what the jobs need for one lattice (rig, entry, grid, pointers). `JiggleLatticeControlJob` has one index for each
lattice: it turns the output of the solver into control displacements in the space of the mesh (the inverse of the linear part of the
object's matrix, which is the matrix the pose job used) and compares them with the displacements the mesh shows. When no control point
differs by more than a hundred thousandth of the largest side of the box, or a value is not a number, it marks the lattice to skip, and then
nothing is deformed and nothing is uploaded. That is what makes a lattice at rest, or asleep, cost almost nothing, and it also keeps a lattice
whose displacement decays but never reaches exactly 0 from being uploaded every frame forever. `JiggleLatticeDeformJob` has one index for each
chunk of 256 vertices and calls `JiggleFfd.DeformRange`. Both jobs use strict floating point mode like the others.

`JiggleLatticeBatch` is made by the runtime from the lattices it accepted. It lays out the chunks, `Schedule` queues the control job and then
the vertex job behind the solver, and `Apply` gives the moved vertices to the meshes that were not skipped and returns how many it uploaded (the
`LatticeUploads` of the stats). On the Burst backend the two jobs follow the solver job. On the Native and Managed backends the solver runs on the
main thread, and the same two jobs are scheduled after it and completed at once, so a lattice is always deformed by these jobs.

## Runtime/Unity/AssemblyInfo.cs

Makes the internals of the Unity assembly visible to the editor test assembly, so the lattice tests can run the control job, the vertex job and
the mesh data on arrays they make by hand.

## Runtime/Unity/JiggleMem.cs

`internal`. Zeroed persistent arrays in unmanaged memory through `UnsafeUtility`, used for the job
data.

## Editor/JiggleChainEditor.cs

The inspector of `JiggleChain`: the default fields, a line saying which anchor and which bones the chain
walks, errors for a missing anchor or an end bone that is not on the way down, and in play mode
buttons for `ResetSimulation` and `Rebind`. Since 0.4.0 it also shows the level of detail and the sleep state in
play mode, with Wake and "Hit sideways" buttons. `JiggleChainGizmos` draws the bones of a selected
chain in blue and the simulated particles in orange while the game runs. Since 0.5.0 it has a Presets part above the
walk: a popup of the built in kinds with Apply, a field for a preset asset with "Apply the asset", and a note about what a
preset replaces. Apply records an undo step and marks the chain dirty.

## Editor/JiggleShapeEditors.cs

The inspectors of `JiggleColliderShape` and `JiggleForceField` (the default fields, with help boxes for what is
missing, such as a radial field without a radius, and in play mode a Fire button on the field), and
`JiggleShapeGizmos`, which draws a selected shape or field: the sphere, capsule, box, plane or inside sphere, the
direction and radius of a field. The drawing uses the Core's own vector math on the same records the solver
gets, so what the gizmo shows is the shape the solver uses. Since 0.5.0 the drawing of one solver shape is
`JiggleShapeGizmos.Shape(collider, color)`, which the body colliders use for their preview.

## Editor/JigglePresetEditors.cs

`JigglePresetGui` holds what the asset inspector, the chain inspector and the preset lab share: a popup of the built in
kinds, the step rate to show (the manager's in play mode, else 60) and `Values(ref values)`, which draws every field of a
preset and returns whether one changed. The two frictions are number fields from 0 to 1 and not sliders, because the
interesting values are near 1 (0.999) and a slider cannot be set that finely. Each shows the per step value it comes to
at the current rate. `JigglePresetEditor` is the inspector of a preset asset: a "Start from" popup with Load, and the
values. Every change records an undo step and marks the asset dirty.

## Editor/JiggleBodyEditors.cs

`JiggleBodyCollidersEditor` is the inspector of the body component: the default fields, what a build would make and
how tall a body that is, how many colliders exist, and the buttons "Fill empty joints from the Animator", "Build" (or
"Rebuild") and "Remove". Each button is one undo step: the old objects are destroyed with `Undo.DestroyObjectImmediate`,
the new ones are registered with `Undo.RegisterCreatedObjectUndo`, and the group is collapsed. `JiggleBodyGizmos`
draws the colliders in green when they exist and a preview of the plan in blue when they do not, while the component is
selected.

## Editor/JiggleLatticeEditor.cs

The inspector of `JiggleLattice`: the default fields, then an error when the mesh is missing or not readable, an error when the box has no size on two
axes, a line with the grid, the number of control points, the box and the number of vertices, a note when an axis was reduced to one point, and in
play mode whether the lattice is simulated, its level of detail, whether it sleeps, and buttons to reset it, wake it and hit it. `JiggleLatticeGizmos`
draws, while the object is selected, the box and the rest places of the control points in blue in the space of the mesh, and in play mode the simulated
control points in orange, joined along the grid.

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

Since 0.6.0 it also builds a readable cube with many vertices (`SubdividedCube`) and a prop from it (`CubeProp`) for the lattice lab.

The builders the feature labs share: `Root(name)` (an empty object, selected), `Chain(parent, name, position,
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

## Editor/Lab/JigglePresetLab.cs

One chain for each built in preset, hung side by side and given the same motion (the anchors move from side to side
with a range and a speed you set), plus a seventh chain, "Tuned", that takes the values you edit. The kinds have their
own length: hair 8 bones of 9 cm, tail 10 of 10 cm, ear 3 of 8 cm, chest 3 of 8 cm, horn 2 of 10 cm and wing 6 of 12 cm. It
shows for every chain how far the tip is from where its pose puts it, now and at the peak since the last reset, and has
a button that hits every chain sideways. The tuned chain follows the sliders at once. "Apply the tuned values to the
selected chain" sends them to the chain you have selected, with an undo step, and "Save the tuned values as a preset
asset" writes a `JigglePreset`. The anchors are moved and the peaks are tracked from `EditorApplication.update`, so they
keep going while the window is not drawn. This is where the numbers of the presets get judged by eye.

## Editor/Lab/JiggleBodyLab.cs

Builds a standing figure of empty joints, 1.7 m tall, in a T pose, adds a `JiggleBodyColliders` with the joints set by
hand and builds it, and hangs an eight bone hair strand from the back of the head with the hair preset, 12 cm behind the
head joint, so its pose runs through the body below the shoulders and the colliders have to hold it out. The figure leans
and turns from `EditorApplication.update` to sweep the strand across the back and the shoulders. Controls: the
collider scale with a rebuild button, the motion on and off and its speed, and whether the strand collides. It shows the
height the build used, how many colliders were made, and how close the closest bone of the strand is to the surface of
any body collider, minus the radius of the bone, where below 0 means inside. The figure has no animator, so it does
not try the humanoid path.

## Editor/Lab/JiggleLatticeLab.cs

Builds a cube with 20 by 20 cells on each face (`JiggleLabScene.CubeProp`, which takes a primitive cube for its default material, removes its collider and gives it
the readable mesh from `SubdividedCube`), puts a lattice on it and moves it from `EditorApplication.update`: back and forth with sudden stops (a
sine clipped flat at the ends), round, or up and down, or not at all. Controls: the speed and the distance of the motion, the grid on each axis,
the padding, the anchor, stiffness, drag, stretch, soften, mass, gravity, air drag and ignore root motion (all of them live, through
`SettingsChanged`), a hit, and a reset. It shows whether the lattice is simulated, the number of control points and vertices, how many meshes were
uploaded in the last frame (which drops to 0 when the cube is at rest), whether it sleeps and how far the simulated control point farthest from its
pose is. Play mode only. This is the check of everything the harness cannot reach: the transform jobs, the control and vertex jobs, the mesh swap
and the upload.

## Editor/Lab/JiggleLabWindow.cs

MidManStudio, AllThingsJiggle, Jiggle Lab (menu bar). Nine tabs:

- **Live** checks that Burst compiles the jobs and that the native library loads with this package's
  interface version, and in play mode shows the manager: the backend (editable), the backend that runs, the
  counts (with the collider, field, level of detail and sleep counts since 0.4.0), the steps, the frame time and the
  status.
- **Colliders**, **Fields and hits**, **LOD**, **Presets**, **Body** and **Lattice** draw the six labs above. The window repaints
  Live and these six tabs while the editor runs, subscribes a tick to `EditorApplication.update` in `OnEnable` (so the ball moves
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

## Tests/Editor/JigglePresetTests.cs

Eight tests of the presets, in the shared style: no UnityEngine, so the .NET harness runs them.

- the damping conversion is right and reverses, and its edges are clamped (a damping of 0 or 1, values that are not
  numbers, a step rate that is below 1 or not finite);
- every preset bakes into range for every bone and has a name, a kind that does not exist is the hair preset, and a tip
  scale that is not a positive number counts as 1;
- the stiffness runs in a straight line from the first bone to the tip by the tip factor, and one bone sits at the start;
- the presets keep what this file promises: hair has a limit near 20 degrees and a damping of 0.999 a second, which is
  0.1087 per step at 60 Hz, the chest swings less than hair and is pulled harder to its pose, only hair and tail collide,
  only tail and chest are coupled, the horn swings least and is stiffest, the wing takes more wind than hair and the chest
  little;
- every preset runs 240 frames of a sway on both backends with the same bits, every position finite, and the tip never
  farther from its pose than the length of the chain;
- every chain settles, which is a tip that moved under 2 mm in the last 20 frames of 480, and sags under gravity by
  under 0.2 m, and the horn swings under half as far as every other preset;
- the swing of an uncoupled preset (hair, wing, ear, horn) at 30 Hz against 60 Hz, given the same sway in real time, is
  within 15 percent plus a centimeter. It measured about 6 percent.

## Tests/Editor/JiggleBodyFitTests.cs

Eight tests of the body fit, also in the shared style.

- a capsule between two joints has the right center, axis, length and solver half height, flat and slanted, and a sphere
  the right solver shape;
- what is not a shape is refused: the same joint, joints closer than the minimum, no radius, a negative or NaN radius,
  joints that are not finite, no height and no scale for the head, and a refused shape is empty;
- the height from the head and the ankles has its fifth added, the two ways to find the height agree within 6 percent on
  an average figure, the radii follow the proportions and the scale, and the trunk is the thickest part;
- the head sphere sits above the head joint, leans with the neck, and goes straight up when the neck and the head are at
  one point;
- the torso ends at the neck joint and reaches a radius below the hips, falls back to joint to joint when it is short and
  refuses bad input, and the hand sits one radius past the wrist along the forearm or on the wrist without a forearm;
- capsules that meet at a joint overlap there;
- body shapes made from the joints of a standing figure (a head, a neck and a torso) keep a hair strand out of the body
  on both backends for 300 frames, with the same bits, while the pose of the strand is bent into the torso and back, and
  the strand does come close to the body, so the test is not passing because nothing touched.

## Tests/Editor/JiggleLatticeTests.cs

27 tests of the Core of the lattice, free of UnityEngine so the harness runs them. Grid and tree: the clamps and the point count, the tree of every grid from
1 by 1 by 1 to 5 by 5 by 5 (every particle reached once, every parent earlier, every parent a grid neighbour, the same every time), the root in the middle,
an explicit root and `NearestPoint`. Deformation: the Bernstein weights and their derivatives, any affine field reproduced exactly, the displacement never above the largest control
displacement, positions outside the box clamped, normals under a turn and a stretch, the gradient against a finite difference, zero displacement
changing nothing, a range split in two giving the same result as one call, `FitBox`, the matrix inverse, `ControlDisplacement`, and an end to end
deformed mesh. Rigs: `AddTree` validation, a chain refusing the tree calls and a tree the chain calls, `WriteTree`, rest stability on both backends,
a jump that settles, a turn, coupling that moves the whole body, sleeping, the managed kernel against the native one bit for bit on
branching trees, the host against the world, and `SetTreeParams`. A test that the middle of the surface follows the middle control point
more than the rim was dropped because it is false for Bernstein deformation (see Fixes and Problems): corner interpolation is tested instead.

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

## Tests/Editor/JigglePresetUnityTests.cs

Eight Edit Mode tests of the Unity layer of drop 3. They need Unity, so the harness does not run them and the stub build
only compiles them.

- applying a preset to the settings bakes the same parameters as the Core makes from the same preset, for all six kinds,
  at 30, 60 and 120 Hz, for chains of 1, 2 and 6 bones;
- a chain takes the tip scale of a preset and keeps its bone count, its sleep flag and its layers, takes the values of an
  asset, ignores a null asset, and treats a tip scale that is not a number as 1;
- a preset asset made from a kind has the values and the name of that kind;
- body colliders built from the joints of a standing figure under a root of scale 1, 2 and 0.5 have, for every planned
  shape, the same kind, radius, half height, center and axis (up to its sign) as the fit says in world space, which is
  what proves the scale handling of `Place`;
- the layer is applied to every object, a second build replaces the first, `Clear` removes them, and none is static;
- the part mask limits what is planned, and a mask of none is refused with a reason;
- the height comes from the ankles, then the hips, and the given height wins;
- a component with no joints plans nothing, fills nothing, builds nothing.

## Tests/Editor/JiggleLatticeUnityTests.cs

Six tests of the lattice Unity layer for the Unity Test Runner (Edit Mode), with no scene: the defaults of the settings and what they bake to, one set of
parameters for every particle after the anchor, the box as the bounds widened by the padding (including a padding past 1 and one that is not a number), the
control job on arrays made by hand (a scaled object turns a world displacement into the right one in the space of the mesh, an unchanged frame is skipped, a return to
rest is shown once, and a value that is not a number is never shown), the mesh data deforming a quad by a straight move of all four control points (every vertex ends
at the moved place, the original mesh is untouched, the bounds reach the moved vertices, and disposing puts the original back), and a filter with no mesh being
refused with a reason. They are compiled in CI and have not run: they need the editor.

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

The harness runs `JiggleParityTests.cs` (30 tests), `JiggleFeatureTests.cs` (23), `JigglePresetTests.cs` (8),
`JiggleBodyFitTests.cs` (8) and `JiggleLatticeTests.cs` (27), 96 in all. It does not compile the Unity layer.

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

The stubs grew again with 0.5.0, checked against the scripting reference: `ScriptableObject` with `CreateInstance`
(replacing a placeholder base class that the editor types used to have), `CreateAssetMenuAttribute`, `SerializeField`,
`HideInInspector`, `Animator` (`isHuman` and `GetBoneTransform`) and `HumanBodyBones`, `Component.GetComponent` and
`GetComponentInChildren`, `Vector3.up`, `Quaternion.FromToRotation`, and in the editor `Undo`, `AssetDatabase`,
`EditorUtility.SetDirty` and `SaveFilePanelInProject`, `EditorGUILayout.ObjectField` and `FloatField`.

The stubs grew with 0.6.0, checked against the scripting reference: `Mesh` (vertices, normals, triangles, bounds, `isReadable`, `vertexCount`,
`MarkDynamic`, `RecalculateBounds`, `RecalculateNormals` and the `SetVertices` and `SetNormals` that take a `NativeArray` with a start, a length and
flags), `MeshFilter`, `Bounds`, `MeshUpdateFlags`, `Object.Instantiate`, `RequireComponent`, `GameObject.CreatePrimitive` with `PrimitiveType`,
`Collider`, `Gizmos.matrix` and `DrawWireCube`, `JobHandle.CombineDependencies`, `NativeArray` with `NativeArrayOptions` and `CopyFrom`, and
`NativeArrayUnsafeUtility.GetUnsafePtr`.

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

The build workflow starts only when it is run by hand: Actions, Build Jiggle Core Native Libs, Run workflow. Its
`push` trigger does not fire for a change that arrived through Apply Replacements, because those commits are marked
`[skip ci]`. It builds whatever `jiggle_core` is on the branch, so a run after the lattice drop gives libraries of
interface version 2, which is what `JiggleLibrary.Verify` expects. A run commits six platforms in one commit.

## CI and workflows

| Workflow | What it does |
| --- | --- |
| `.github/workflows/jiggle-core.yml` | Runs on a push that touches the crate, the package or the workflow, by hand, and (0.6.1) after a manual run of Apply Replacements that succeeded. The last is needed because the commits of that action are marked `[skip ci]` and never start a `push` workflow, so before 0.6.1 nothing tested a drop that arrived as an archive unless somebody ran the workflow by hand. The `ffi` job builds the release library and runs `Tests~/DotnetHarness` against it. The `unity-stubs` job compiles Core, Runtime/Unity, Editor and the editor tests against the Unity stubs, fails if `MathF` appears in the Core outside `JiggleParams.cs` (Burst does not support it), and fails if an asset has no `.meta` file. The manual bench job runs both Rust benches. The other jobs are described in the crate documentation |
| `.github/workflows/build-jiggle-rust-libs.yml` | Builds and commits the native libraries. Run it by hand, see Native libraries above |
| `.github/workflows/menu-rule.yml` | Repository wide, not only this package. Reads every `MenuItem`, `AddComponentMenu` and `CreateAssetMenu` of every package and fails when its path is not `MidManStudio/<Package>/<Item>` (a `CreateAssetMenu` with no `menuName` fails too, because it would land under Assets/Create). Runs on a push that touches a `.cs` file under `packages`, by hand, and after a manual run of Apply Replacements. Comments are ignored. `SettingsProvider` and `ContextMenu` are not read, because no package has either |

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
- Nothing about the features of 0.5.0 has run inside Unity either. The presets and the body fit are tested under .NET 8
  (16 tests). The preset and body component, the inspectors, the two labs and the eight Edit Mode tests are compiled
  against stubs only.
- The numbers of the presets have not been judged by eye. What is tested is that they are stable, finite, the same on
  both backends, bounded, that every chain settles, and that the horn moves least. Whether hair "looks like hair" is for
  the lab.
- Stiffness and coupling are per step, and the presets do not convert them for the step rate. The two frictions are
  converted, and with that an uncoupled preset swings about the same at 30 and at 60 Hz (a test holds it to 15 percent
  plus a centimeter). The tail and the chest are coupled, and a coupled chain at 30 Hz sags and swings differently: in
  one try the 30 Hz tail sagged 0.28 m where the 60 Hz tail sagged 0.075 m. Treat those two as tuned for 60 Hz.
- `ApplyPreset` converts the frictions for the step rate of the manager at that moment. Change `fixedHz` afterwards
  and the drag stays what it was: apply the preset again.
- Body colliders use the proportions of an average adult, from the distances between joints in the pose of the build.
  They are not fitted to a mesh, and a stylised character with a big head, a long torso or thin limbs needs `scale` or
  `height`, or the joints placed by hand. A uniform scale on the joints is assumed.
- The humanoid path, `FillFrom` with an `Animator`, has never run against a real avatar: it is a list of
  `GetBoneTransform` calls checked against the scripting reference. The test figure of the lab has no animator.
- The head sphere is small on purpose (see `JiggleBodyFit`), so hair can pass through the outer part of a real head. The
  torso stops at the neck joint, so hair that hangs between the neck and the head is held by the neck capsule only.
- The body colliders are not animated by this package. They are children of the joints and follow them, and a joint that
  is scaled non uniformly by an animation gives a collider that is wrong by the same amount.
- Nothing about the lattice of 0.6.0 has run inside Unity. The Core (grid, tree, deformation, tree rigs) is tested under .NET 8 (27 tests, with the managed kernel
  against the native one bit for bit on branching trees). The component, the mesh swap, the control and vertex jobs, the manager hooks, the inspector,
  the gizmos, the lab tab and the six Edit Mode tests are compiled against stubs only. The calls on `Mesh` (`SetVertices` and `SetNormals` with flags,
  `bounds`, `MarkDynamic`) and on `NativeArray` are from the scripting reference and have not been tried.
- The lattice is for unskinned meshes. A skinned mesh, a mesh under static batching and a mesh that is not readable cannot be deformed. A mesh that is not
  readable is refused with a warning that says to turn on Read/Write. A lattice copies the mesh once while it is enabled, so each lattice costs one mesh of memory.
- The lattice has at most 125 control points, 5 on an axis, and it is simulated as a tree of 124 plus the anchor. Larger grids settle slower. In the harness
  a 3 by 3 by 3 lattice at the defaults settles in about 112 frames at 60 Hz.
- A lattice with one anchor lags as a whole when the object moves, because every control point except the anchor is pulled along by its neighbours. That
  is inherent to deforming around one held point. Set `ignoreRootMotion` above 0 to make it lag less.
- The anchor is a control point and may lie off the pivot. The anchor move the solver works with is a translation only.
- Only the corner control points lie on the deformed surface. The surface follows interior points loosely (a weight of 0.125 for the centre of a 3 by 3 by 3
  grid). Widen the box with `padding` or raise the divisions if a body does not follow a hit the way you want.
- Normals are moved by the inverse transpose of the Jacobian at the clamped coordinates, so a vertex outside the box takes the normal of the
  nearest point on its surface. Tangents are not deformed, so a normal map shows a small error on a strongly bent lattice.
- A lattice tests no colliders. Force fields, hits, sleep and level of detail apply to it. Its numbers are tuned at 60 Hz and the coupling is per step, as for the
  coupled presets.
- Preset-style numbers for lattices do not exist yet: a lattice has the defaults and the lab sliders.
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
- The native plugin binaries under `Runtime/Plugins/Native` are built by the build workflow, which has to be run by hand. Until it
  has, the folders hold only a `NOTE.md` each, the library does not load, `JiggleLibrary.Verify` or the load throws, and the manager
  catches that, logs one warning and runs Burst instead. The managed backend does not need the binaries.
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

### Runtime/Core/JigglePresets.cs, JiggleDamping.cs (0.5.0)

- A rescale of the stiffness for the step rate was tried and thrown away. A relaxation share rescale and then a
  60 over rate spring rescale both made 30 Hz worse or stopped the motion altogether (the ear peaked at 0.004 m against
  0.394 m at 60 Hz). Unscaled stiffness with per second frictions gave 30 Hz peaks within about 6 percent of the 60 Hz
  ones, so the stiffness stays per step and only the frictions are converted. The scaling code and its helper were removed.
- Ordering assertions between presets (which swings farther, ear or hair) were dropped: they depended on numbers that
  nobody has judged and broke whenever one was moved. Only the horn is held to swing under half of the others.

### Runtime/Core/JiggleBodyFit.cs (0.5.0)

- The first draft used the distance from the head joint to the ankles as the height of the body, which makes every body a
  fifth too small, because the head joint is at the base of the skull and the ankle is above the sole. Found when the
  Unity layer was written and the two ways to find the height were compared, before any run. The factor is
  `HeightPerAnkleToHead`, 1.2, with a test that the two ways agree.
- The torso was the plain capsule from the hips to the neck joint, whose top cap reaches a radius above the neck and
  swallows the back of the head. It is now `Torso`, which ends the cap at the neck joint.

### Tests/Editor/JigglePresetTests.cs, JiggleBodyFitTests.cs (0.5.0)

- Four of the 67 first tests failed and were corrected, not loosened without a reason:
  - the tail "settled near its pose" test measured the sag under gravity, which is no ringing. It now measures how far
    the tip moved in the last 20 frames, with a separate cap on the sag, over 480 frames because hair creeps under heavy
    damping;
  - the step rate test saw the coupled tail sag at 30 Hz, which is the limit above, so it now covers the uncoupled
    presets only;
  - the hair against head test had the wrong sign, and its root was pinned inside the head because the whole pose went
    through the body. The scenario is a strand 0.2 m behind the body whose pose bends toward the torso with a ramp;
  - the joint overlap test had its logic inverted: the cap of the near capsule reaches past the joint, so the distance is
    greater, not less.
- A leftover variable and an assertion that could not fail were removed from the hair against body test, and the test
  that holds the horn to swing least was renamed to say that.

### Tests~/UnityStubs (0.5.0)

- The editor types had a placeholder base class, `ScriptableObjectStub`, instead of the real `ScriptableObject`. Drop 3
  needs `ScriptableObject.CreateInstance`, so the placeholder was replaced by the real name.

### Runtime/Core/JiggleLatticeGrid.cs, JiggleTreeBuild.cs (0.6.0)

- The first lattice had a separate root particle standing on the anchor, held by its root stretch, and the grid points hung under it. A jump of the
  object made the lattice blow up. The cause: a root particle at the same place
  as the anchor has a link of zero length, `AimRot` measures the swing of a link as the turn from the pose direction to the simulated direction, a zero
  length link has no direction and gives a garbage turn, and the solver applies that turn to every child of the particle. On top of that the children drag a
  root that is not heavy. The fix is that the middle control point is the anchor itself, there is no separate root and no `FlagRoot` (`pinRoots` is false), and the
  anchor is never coupled because it is kinematic. The same rule is why the component gives a flat axis one control point: points that coincide are the same bug.


### Runtime/Core/JiggleFfd.cs (0.6.0)

- A test said the middle of the surface should follow the middle control point more than the rim does. That is false for Bernstein deformation: only the
  corner control points are interpolated and an interior point has a small weight (0.125 at the centre of 3 by 3 by 3). The test now checks that the corners
  are interpolated, and the limit is in Known limits.

### Tests/Editor/JiggleLatticeTests.cs (0.6.0)

- A loop ran `p <= a.Count` over an array of `Count` entries and was changed to `<`.
- The "one short" cases of the pose tests needed 7 positions, one fewer than the particles of the rig.

- The grid {1, 1, 1} of the parity test became {2, 1, 1}, because a tree needs at least two particles.


### Runtime/Unity/JiggleJobs.cs, JiggleRuntime.cs (0.6.0)

- The local rotation job now gets the rig mode and skips mode 2, because a lattice has no bones and no slots in the array of local rotations.

### Tests~/UnityStubs (0.6.0)

- `Mesh`, `MeshFilter`, `Bounds`, `NativeArray` and the other types listed in its section were added. Nothing was changed in the existing stubs.

### .github/workflows and Runtime/Plugins/Native (0.6.1)

- Apply Replacements commits its result as `chore: sync project structure from template [skip ci]`, so the `push` trigger of
  `jiggle-core.yml` and of `build-jiggle-rust-libs.yml` does not start for a drop that arrived as an archive. Such a drop has a green
  or red result only if somebody ran the workflow by hand. `jiggle-core.yml` now also starts when a manual run of Apply Replacements
  completes with success, and checks out the branch as it stands, which has the commit the apply made. A run that only previews
  (the push of an archive, or `dry_run` true) can start it as well, and then tests the same commit again, which does no harm. The
  build workflow stays manual, because it builds six platforms.
- The README said the libraries under `Runtime/Plugins/Native` were built for interface version 1. No library had ever been committed
  there: the history of the folder, as far as commit 787fb84, holds only the `NOTE.md` files and the `.meta` files. The text now says what is true in either
  state.
- `menu-rule.yml` is new. The rule it checks was only kept by hand before. At the time of writing the only path it rejected was the
  Questly Studio menu, which moved in version 1.0.1 of that package.
