# All Things Jiggle - Bone Jiggle Physics

`com.midmanstudio.allthingsjiggle` is jiggle physics for chains of bones: hair,
tails, ears, cloth strips, chests. A rig is a chain of bones hanging from an
anchor that the animation moves. The solver keeps every bone near the pose the
animation gives, lets it lag, swing and bounce, and hands back a position and a
rotation per bone.

What it does today:

- **Components.** Add a `JiggleChain` to the first bone of a chain. A `JiggleManager`
  appears by itself and simulates every chain once a frame in `LateUpdate`, after the
  animation, and writes the local rotation of each bone
- **Three backends over the same flat buffers.** Burst jobs (the default), the Rust
  library `jiggle_core` through a C interface, and a managed C# kernel. The managed kernel
  and the Rust library give the same bits. Burst is checked to a tolerance of 1e-4 meters
- **A lab in the editor** (MidManStudio, AllThingsJiggle, Jiggle Lab) that checks Burst and the native
  library, compares the three backends frame by frame and benchmarks them on the same scenes
  as the Rust `scene_bench`
- verlet solving per bone with a pull to the animated shape, a pull to the bone
  length, an optional angle limit, drag, air drag, gravity and a root spring
- two way coupling: give bones a mass and a bone pulls its parent back, so a weight on
  a strand drags it down. Bones without a mass stay one way
- **Colliders from the scene.** A `JiggleColliderShape` (sphere, capsule, box, plane, inside sphere) on any
  object. A chain tests the ones near it when `Collide` is on in its settings. The layer is the object's layer, and
  the chain's `ignoreLayers` is a mask where **a set bit means no collision**
- **Force fields and hits.** A `JiggleForceField` (directional, radial, vortex, with falloff and gusts) pushes all
  the time. `JiggleChain.AddImpulse` and `AddImpulseAt`, and `JiggleManager.AddBurst` and `AddShove`, push once
- **Level of detail.** By distance, and by what the camera sees: chains farther than a limit stop testing
  colliders, farther than another (or out of view) they are not simulated and show their pose. A chain can inherit
  the manager's limits, always simulate, or have its own
- **Sleep controls.** `allowSleep`, `IsAsleep`, `Wake()`. A hit and a collider that moves up to a sleeping chain wake it
- **Presets.** Hair, tail, ear, chest, horn and wing, applied from the chain inspector or with `chain.ApplyPreset`,
  or kept as `JigglePreset` assets. Their frictions are per second, so they mean the same at any step rate
- **Body colliders.** `JiggleBodyColliders` fits spheres and capsules to the joints of a humanoid or hand set rig, so hair
  and tails stay out of the body without placing colliders by hand
- sleep and wake, teleport and reset, and a fixed step accumulator

## State of this release

Version 0.5.0 is drop 3: presets and body colliders from the rig, on top of the colliders, fields, hits, level of detail
and sleep controls of 0.4.0. **Nothing in it, or in the drops before it, has run inside Unity yet.** The C# Core is
tested under .NET 8 against the Rust library. The Unity layer, the editor lab and the Unity
tests are compiled against stubs of the Unity API in CI, which proves they are well formed and
nothing more. Burst compiling the kernel, the transform jobs and the lab are untried. Run the lab
in your project first (it has a tab for colliders, one for fields and hits, one for the level of
detail, one for the presets and one for the body colliders), and read the known limits in
`docs/com.midmanstudio.allthingsjiggle.md`.

The numbers of the presets are starting points that nobody has judged by eye. Tune them in the Presets tab.

Not in this drop: the Grand Theft Grimoire chest adapter, which waits for access to that repository. A lattice
modifier for unskinned meshes (props and enemies) is planned as 0.6.0.

Only local rotations are written, so a bone never stretches on screen. A chain is a path: each
bone is the first child of the one above. A bone can be driven by one chain only.

The Burst package must stay on a 1.8 release up to 1.8.29 on Unity 2022.3, because 1.8.30 and
later need Unity 6. The package asks for 1.8.4 as the lowest.

The native libraries under `Runtime/Plugins/Native` were built for interface version 1. The
current interface is 2, so until the `build-jiggle-rust-libs` workflow has run on this drop the
Native backend is refused and the manager runs Burst instead, with one warning.

A bone is pulled by its parent and, unless both have a mass, never pulls it back. Set `mass` on the
chain (or `JiggleParams.Mass` per bone) to turn the coupling on for the links between them, and
`couplingPasses` on the manager for how many passes a step uses. The mass ratio between neighbours
counts as at most 16.

The Core has no Unity engine references, so the same files compile and run under plain .NET.
That is how CI tests them.

## Install

Add the package from the repository with Package Manager, "Add package from git
URL":

    https://github.com/MidManStudio/unity-chem-sim.git?path=packages/com.midmanstudio.allthingsjiggle

The native libraries under `Runtime/Plugins/Native` are built and committed by
the `build-jiggle-rust-libs` workflow. Until it has run, the package has no
binaries. The managed backend works without them. The native backend throws
`DllNotFoundException` on its first call.

## Use

In the editor:

1. Select the first bone that should move, for example the root of a strand of hair.
2. Add Component, MidManStudio, AllThingsJiggle, Jiggle Chain. The parent of that bone is the anchor.
3. Press play. A Jiggle Manager is created by itself. Add one to the scene yourself to change gravity,
   the step rate or the backend.

To collide, turn on `Collide` in the settings of the chain and add a **Jiggle Collider** (Add Component, MidManStudio,
AllThingsJiggle, Jiggle Collider) to an object. For wind add a **Jiggle Force Field**. For the level of detail set
`lodCollisionDistance` and `lodCullDistance` on the manager (0 is off).

`poseSource` is **Bind** by default: the chain holds the pose its bones had when it was enabled, carried
along by the anchor. Use **Animated** for bones that an Animator rewrites every frame.

From code, `JiggleManager.TryGet()` gives the running manager, `Stats` what the last frame did and
`Simulate(dt)` runs a frame by hand. `chain.AddImpulse(new Vector3(2f, 0f, 0f))` hits a chain once, and
`JiggleManager.TryGet().AddBurst(point, 1f, 4f)` pushes everything near a point away from it. Without Unity components the solver is a library:

```csharp
using MidManStudio.AllThingsJiggle;

JiggleWorld world = new JiggleWorld();

// The anchor first, then the bones from the root down, in world space.
JVec3[] rest =
{
    new JVec3(0f, 1.5f, 0f), new JVec3(0f, 1.4f, 0f),
    new JVec3(0f, 1.3f, 0f), new JVec3(0f, 1.2f, 0f),
};
JiggleParams[] bones = { JiggleParams.Default(), JiggleParams.Default(), JiggleParams.Default() };
int rig = world.AddChain(rest, bones);

// Every frame: give the pose the animation made, step, read the result.
world.SetPose(rig, poseWorldPositions);
world.Step(frameSeconds, JiggleBackend.Managed);   // or JiggleBackend.Native
JVec3 tip = world.WorldPos(rig, 3);
JQuat tipRotation = world.WorldRot(rig, 3);
```

`JiggleWorld` is a model of the call sequence, with plain managed arrays. `JiggleHost` is the same in
unmanaged memory, for jobs and threads. A host that keeps its own native buffers calls
`JiggleSolver.StepRigs` and `JiggleSolver.ResolveRigs` (managed) or the matching `jgl_` functions (native)
on them directly. Both take the same arguments and give the same status codes.

## Tests

`Tests/Editor/JiggleParityTests.cs` (30 tests) and `Tests/Editor/JiggleFeatureTests.cs` (23 tests, for colliders,
fields, hits and the level of detail) run in the Unity Test Runner (Edit Mode). CI runs the same
files under .NET 8 against the freshly built library. The parity file checks every struct offset field by field, every
exported function, random scenes of every feature (with and without masses) bit for bit between the two
backends, the error codes, threaded batches, the host against the world, and two golden scenes, one way and
coupled, whose hashes the Rust tests assert too. The feature file checks the collider lists and layers, the field
queue, hits, the level of detail rules and the sleep rules.

`Tests/Editor/JiggleUnityLayerTests.cs` (10 tests) needs the editor: the settings bake, Burst against the
managed kernel within 1e-4 meters, the step and resolve as two jobs, and the components of this drop. CI compiles it against the Unity
stubs in `Tests~/UnityStubs` but cannot run it.

See `docs/com.midmanstudio.allthingsjiggle.md` for the design and the known problems.
