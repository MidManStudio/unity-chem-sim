# Changelog

## 0.6.0

Drop 4: the lattice modifier. No Rust and no native change: the interface version stays 2.
Nothing in this drop has run inside Unity: the Core part is tested under .NET 8 (27 new tests, 96 in the harness), the
Unity layer, the inspector, the lab tab and the six new Edit Mode tests are only compiled against stubs of the Unity API.
The Grand Theft Grimoire chest adapter is still not in.

- **Lattice modifier.** `JiggleLattice` (menu `MidManStudio/AllThingsJiggle/Jiggle Lattice`) on an object with a MeshFilter. The
  bounds of the mesh become a box with 1 to 5 control points on each axis (3 by 3 by 3 by default), the control points are
  simulated as a tree by the same manager as the chains, and the vertices follow them by free form deformation. A shove
  travels through the body and the whole prop wobbles. It is for props and enemies, meshes that are not skinned
- **The anchor is a control point.** The middle control point (`Center`), or the one nearest the pivot (`Pivot`), is held by the
  object. The other control points pull their neighbours, so the lattice moves as one body and lags behind the object
- **Free form deformation.** `JiggleFfd` in the Core: Bernstein weights (Sederberg and Parry), displacement with and without
  its gradient, normals by the inverse transpose, a range of vertices at a time on pointers so a Burst job runs it. A
  straight stretch or turn of the whole box is reproduced exactly. Tangents are not deformed
- **Trees in the Core.** `JiggleLatticeGrid` builds the control grid and the tree of its six neighbour links. `JiggleWorld` and
  `JiggleHost` gain `AddTree` and `IsTree` for tree rigs, `SetTreePose` (world) and `SetTreeParams` (host), and `JigglePose.WriteTree` writes their pose. The solver
  already took trees, so nothing in the Rust crate or the interface changes. Chain calls refuse a tree and tree calls refuse a chain
- **Unity side.** Rig mode 2 in the transform jobs, a control job and a vertex job after the solver, and an upload to a private
  copy of the mesh on the main thread. The component swaps the mesh on its filter for the copy while it is enabled and gives
  the original back when it is disabled. A lattice at rest, or asleep, skips the vertex job and the upload
- `JiggleLatticeSettings`: stiffness, soften, stretch, drag, air drag, gravity, ignore root motion, mass, wind response and
  impulse response. The defaults are the numbers the harness measured, not numbers judged by eye. A lattice never collides
- `JiggleLattice.AddImpulse`, `AddImpulseAt`, `Wake`, `ResetSimulation`, `SettingsChanged`, and the box and control points through
  `TryGetBox`, `TryGetDivisions` and `TryGetControlPoint`. `JiggleFrameStats` gains `Lattices` and `LatticeUploads`
- **Editor.** An inspector that says what the grid would be, shows problems (no mesh, a mesh that is not readable, a flat box)
  and has the play mode buttons. Gizmos draw the box and the rest control points in blue and the simulated ones in orange. The
  lab has a ninth tab, Lattice: a cube with a lattice on it that is shuttled with sudden stops, spun or hopped, with live
  sliders, a hit, a reset and the uploads of the last frame
- an axis of the box that has no size, or almost none, gets one control point, because control points on top of each other
  have no direction between them (see the fix below)
- `Tests/Editor/JiggleLatticeTests.cs` (27) runs in the shared harness. `Tests/Editor/JiggleLatticeUnityTests.cs` has 6 Edit Mode
  tests
- the Unity stubs gain `Mesh`, `MeshFilter`, `Bounds`, `NativeArray` and a few more types

Fixed on the way: a first lattice with a separate root particle blew up, because a particle at the same place as its parent has no
direction to measure a swing from. The middle control point is now the anchor itself. See the docs.

Limits: the mesh must be readable, skinned and statically batched meshes cannot be deformed, tangents are not deformed, a lattice
tests no colliders, a lattice lags as a whole behind its object, and a larger grid settles slower. Coupled values are tuned at
60 Hz. Nothing here has been tuned by eye.

## 0.5.0

Drop 3: presets, and body colliders from the rig. No Rust and no native change: the interface version stays 2.
Nothing in this drop has run inside Unity: the Core part is tested under .NET 8 (16 new tests, 69 in the harness), the
Unity layer, the inspectors, the labs and the eight new Edit Mode tests are only compiled against stubs of the Unity API.
The Grand Theft Grimoire chest adapter is not in this drop, and the lattice modifier is planned as 0.6.0.

- **Presets.** `JigglePresetKind` (Hair, Tail, Ear, Chest, Horn, Wing), `JigglePresetValues` and `JigglePresets` in the
  Core. Hair and chest follow the technique level findings of the rig study (a swing limit near 20 degrees and a damping
  of 0.999 a second for hair, a low limit and a strong pull to the pose for the chest). The other four are reasoned.
  None of the numbers has been judged by eye, so tune them in the lab
- **Frictions per second.** A preset gives its drag and air drag as the share lost in a second, and
  `JiggleDamping.PerStep` turns that into the solver's per step value for the step rate. Hair at 0.999 is 0.1088 a
  step at 60 Hz and 0.2056 at 30 Hz. Stiffness and coupling stay per step, so the tail and the chest are tuned for 60 Hz
- `JiggleChain.ApplyPreset` (a kind, a `JigglePreset` asset or values) and `JiggleChainSettings.ApplyPreset`. A preset
  replaces the settings and the tip scale and keeps the rest of the chain. The chain inspector has a Presets part
  with Apply and an asset field
- `JigglePreset` (menu `MidManStudio/AllThingsJiggle/Jiggle Preset` in the Create menu): a preset kept as an asset, with
  an inspector that starts from a built in kind
- **Body colliders.** `JiggleBodyColliders` (menu `MidManStudio/AllThingsJiggle/Jiggle Body Colliders`) fits
  `JiggleColliderShape` objects to the joints of a rig: a sphere for the head and the hands, capsules for the neck, the
  trunk, the arms, the legs and the feet, as children of the joints so they follow the animation. Joints come from a
  humanoid `Animator` or are set by hand. `scale`, `height`, `parts` and `colliderLayer` control it. The inspector
  previews the plan in the scene view, and builds, rebuilds and removes with undo
- Core: `JiggleBodyFit` (the proportions of an average adult as radius fractions of the height, a height from the
  head joint and the ankles or from the hips, capsules whose caps overlap at joints, a head sphere, a trunk that ends at
  the neck, a hand past the wrist) and `JiggleDamping`
- the lab has two new tabs, Presets (one chain per built in preset moved the same way, plus a tuned chain, with peak
  sway, a way to send the values to the selected chain and to save them as an asset) and Body (a test figure with the
  body colliders and a hair strand swept across it)
- `JiggleShapeGizmos.Shape` draws one solver shape in a given color, shared by the body preview
- `Tests/Editor/JigglePresetTests.cs` (8) and `JiggleBodyFitTests.cs` (8) run in the shared harness.
  `Tests/Editor/JigglePresetUnityTests.cs` has 8 Edit Mode tests
- the Unity stubs gain `ScriptableObject`, `Animator`, `HumanBodyBones`, `Undo`, `AssetDatabase` and a few more members.
  The placeholder base class of the editor types is replaced by the real `ScriptableObject`

## 0.4.0

Drop 2b: colliders, force fields, hit impulses, level of detail and sleep, from scene objects.
Nothing in this drop has run inside Unity either: the Core is tested under .NET 8, the Unity layer and
the editor labs are only compiled against stubs of the Unity API. Body colliders from the rig, presets and the
chest adapter (drop 3) are still to come.

- **Colliders.** `JiggleColliderShape` (menu `MidManStudio/AllThingsJiggle/Jiggle Collider`): a sphere, capsule,
  box, plane or inside sphere that registers with the manager and follows its transform. A chain tests the
  colliders that are near it, and only while `Collide` is on in its settings. Static shapes are built once
- **Layers.** A collider's layer is `GameObject.layer`. A chain has `ignoreLayers`, and a set bit means NO
  collision with that layer. There is no layer slot in the native struct, so the lists are filtered in managed
  code when they are built, and no native binary has to change
- **Force fields.** `JiggleForceField` (menu `MidManStudio/AllThingsJiggle/Jiggle Force Field`): a directional,
  radial or vortex field in the scene, with a radius, a falloff, gusts and a `Fire()` that sends one impulse
- **Hits.** `JiggleChain.AddImpulse` and `AddImpulseAt`, and `JiggleManager.AddBurst` and `AddShove`. A hit changes
  the velocity of the bones once. With no drag it gives the same motion as a directional impulse field. A hit
  wakes a sleeping chain. A chain that is not simulated refuses the hit and returns false
- **Level of detail.** Each chain is Full, NoCollision (too far to test colliders) or Off (too far or out of the
  camera's view, so it shows its pose and costs no solver time). The manager has the limits and one camera
  (`lodCamera`, else the main camera). A chain can inherit them, always simulate, or have its own (`lod`).
  Hysteresis stops chains flickering at a limit. A chain that comes back from Off snaps to its pose
- **Sleep.** `JiggleChain.allowSleep`, `IsAsleep` and `Wake()`. A moving collider that a sleeping chain is near wakes it
- chain settings gain `collide`, `collisionRadius`, `segmentCollision`, `windResponse` and `impulseResponse`. Before
  this, a chain could not collide at all because the settings never turned it on. The defaults are the Core's
- Core: `JiggleColliderSet`, `JiggleShapes`, `JiggleFieldQueue`, `JiggleImpulse`, `JiggleLod`. New in `JiggleHost`:
  `SetColliderList` with a pointer, `SetCollider`, `ClearColliders`, `ClearColliderLists`, `ClearFields`, `IsAsleep`,
  `IsDisabled`, `SetDisabled`, `Wake`. New in `JiggleFields`: `Weight`. The solver and the native interface did not
  change: the interface version stays 2
- `JiggleFrameStats` gains `Colliders`, `ColliderRefs`, `Fields`, `LodNoCollision`, `LodOff` and `Asleep`
- the manager's frame is now: rebuild, apply changes and settings, apply resets, prepare the rigs (anchors, level of
  detail, flags, layer masks), write the scene (collider lists, fields), step, then commit the impulses
- impulses that the solver did not step on (a frame with no steps) stay queued for the next frame. Impulse fields act
  on the first step of a frame only
- the editor lab has three new tabs, Colliders, Fields and hits, and LOD, each building its own test setup in play
  mode. Inspectors and gizmos for the two new components. The chain inspector shows the level and the sleep state in
  play mode and has Wake and hit buttons
- `Tests/Editor/JiggleFeatureTests.cs`: 23 tests, in the shared harness. `Tests/Editor/JiggleUnityLayerTests.cs` has 10
  tests (5 new). The harness runs 53
- 13 new `.meta` files
- **Known limits, new:** building the collider lists is O(chains x colliders) on the main thread. The transform jobs still
  run for chains that are Off. There is one LOD camera and no lower step rate for far chains. A chain with
  `allowSleep` off, when the manager's `sleepFrames` is 1, can still be snapped to its pose at the end of the frame, which
  looks the same. Static shapes are not watched, so move one by turning `staticShape` off. See the docs
- the Rust crate version stays 0.0.1 and the interface version stays 2. The native binaries under
  `Runtime/Plugins/Native` are still interface version 1 until the build workflow has run

## 0.3.1

- menu paths follow the house rule (a top level `MidManStudio` menu, never `Tools` or `Window`): the lab is
  `MidManStudio/AllThingsJiggle/Jiggle Lab` and the components are `MidManStudio/AllThingsJiggle/Jiggle Chain` and
  `Jiggle Manager`. Nothing else changed

## 0.3.0

The Unity runtime. Colliders, fields and level of detail from scene objects are the next drop.
Nothing in this drop has run inside Unity: the Core is tested under .NET 8, the Unity layer is only
compiled against stubs of the Unity API.

- `JiggleChain` (add it to the first bone) and `JiggleManager` (made by itself, or added to set the
  global values): one manager simulates all chains once a frame in `LateUpdate`, reads and writes the
  transforms through `TransformAccessArray` and writes the local rotation of every bone
- backends: Burst jobs by default, the Rust library selectable, the managed kernel as a fallback. A Native
  backend that cannot load, or that has another interface version, falls back to Burst with one warning
- the Burst jobs run the managed kernel (`JiggleSolver.StepRigs` and `ResolveRigs`), not a second copy.
  They use strict floating point mode and are checked against the managed kernel to 1e-4 meters
- pose sources: Bind (the pose the bones had when the chain was enabled, carried by the anchor) and Animated
- `JiggleHost`: the solver arrays in unmanaged memory with room to grow, with colliders, fields, `SetParams`,
  `Reset`, `CopyRigRuntime` and `CopyClock`, so jobs and threads can use the solver and a rebuild keeps the state
- `JiggleChainBuild`, `JiggleClock` and `JigglePose` split out of `JiggleWorld`, which now uses them, so the
  world and the host cannot disagree. No behaviour changed: both golden hashes are the same
- the kernel no longer calls `System.MathF`, which Burst does not support. `JiggleMath.Sqrt`, `Abs` and `Floor`
  give the same bits (a test checks 40000 random inputs). `JiggleMath.IsNaN` and `IsFinite` replace the
  `System.Single` calls
- an editor lab, MidManStudio, AllThingsJiggle, Jiggle Lab (called Tools, MidMan Studio, Jiggle Lab until 0.3.1): checks Burst and the native library, compares the three
  backends frame by frame and benchmarks them on the scenes of the Rust `scene_bench`
- inspector and gizmos for `JiggleChain`
- Rust: `benches/scene_bench.rs`, a table of twelve scenes at several thread counts with a CSV line per row,
  and a measurement of the call overhead of the C interface. `step_bench` has the two coupled rows
- `Tests/Editor/JiggleParityTests.cs` has 30 tests (9 new) and `Tests/Editor/JiggleUnityLayerTests.cs` has 5
- `Tests~/UnityStubs`: the Unity layer, the editor code and the tests compiled against a hand written subset of
  the Unity API, one project for each assembly. The Core is compiled with no references at all
- CI: the `unity-stubs` job (with a check that the Core has no `MathF` outside `JiggleParams.cs`, and that every
  asset has a `.meta` file), and the manual bench job runs both benches
- the package depends on `com.unity.burst` 1.8.4. Stay on a 1.8 release up to 1.8.29 with Unity 2022.3
- `.gitignore` has a negation for `Tests~/UnityStubs/*.csproj`, which the `*.csproj` rule would have ignored
- the Rust crate version stays 0.0.1 and the interface version stays 2. The native binaries under
  `Runtime/Plugins/Native` are still interface version 1 until the build workflow has run

## 0.2.0

Two way coupling, in the managed kernel and in jiggle_core, with C interface version 2.
Breaking: the interface version is 2, so a native library older than this package is
refused by `JiggleLibrary.Verify`. The native libraries are rebuilt by the build workflow.

- `JiggleParams.Mass` (default 0) and `JiggleWorld.SetMass`: a bone with a mass pulls its
  parent back when the parent has a mass too. 0 keeps the link one way, as before
- `JiggleStepConfig.CouplingPasses` (0 counts as 1, at most 16): how many passes of length
  and angle limit the coupled links get per step
- the mass ratio between neighbours counts as at most 16, because larger ratios made the
  solver erratic
- a last collision sweep on rigs with coupled links, so a parent moved by its child cannot
  end up inside a collider
- the two records took reserved slots (`JiggleParticleDef.Mass`, `JiggleStepConfig.CouplingPasses`),
  so no size changed
- a scene without masses gives the same bits as 0.1.0 (the first golden hash did not move)
- a second golden scene with masses, random scenes with masses, and bit for bit parity
  between the managed kernel and the native library on both
- `Tests~/DotnetHarness/JiggleHarness.csproj` is now committed. It was missing because
  `.gitignore` ignored every `.csproj`, which made the CI job fail

## 0.1.0

First drop: the core, with no Unity components yet. C# bindings and a managed
reference kernel for jiggle_core 0.0.1 (C interface version 1):

- verlet bone chain solver with shape, length and angle limit constraints, drag,
  air drag, gravity, a root spring, sleep and wake, teleport and reset
- sphere, capsule, plane, box and inside sphere colliders, with an optional
  bone segment test
- directional, radial and vortex fields, as acceleration or hit impulse, with
  falloff and gusts
- a managed kernel and the native library that give identical bits on the same
  buffers, checked field by field and scene by scene
- `JiggleWorld`, a managed model of the call sequence with a fixed step accumulator
- layout and interface version checks through `JiggleLibrary.Verify`
