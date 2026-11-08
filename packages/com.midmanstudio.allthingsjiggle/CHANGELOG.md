# Changelog

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
- an editor lab, Tools, MidMan Studio, Jiggle Lab: checks Burst and the native library, compares the three
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
