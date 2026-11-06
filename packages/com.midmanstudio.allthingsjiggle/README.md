# All Things Jiggle - Bone Jiggle Physics

`com.midmanstudio.allthingsjiggle` is jiggle physics for chains of bones: hair,
tails, ears, cloth strips, chests. A rig is a chain of bones hanging from an
anchor that the animation moves. The solver keeps every bone near the pose the
animation gives, lets it lag, swing and bounce, and hands back a position and a
rotation per bone.

What the core does today:

- verlet solving per bone with a pull to the animated shape, a pull to the bone
  length, an optional angle limit, drag, air drag, gravity and a root spring
- colliders: sphere, capsule, plane, box (optionally rounded) and inside sphere,
  with an optional test of the whole bone segment, not only its end
- force fields: directional, radial and vortex, as an acceleration or as a hit
  impulse, with falloff, a radius and gusts
- two way coupling: give bones a mass and a bone pulls its parent back, so a weight on
  a strand drags it down. Bones without a mass stay one way. The number of passes is a
  setting
- sleep and wake, teleport and reset, and a fixed step accumulator
- two backends over the same flat buffers that give the same bits: a managed
  C# kernel and the Rust library `jiggle_core` through a C interface

## State of this release

This is the core drop. It has the solver, the bindings and the proof that the two
backends agree. It does not yet have the Unity side: no component to put on a
bone chain, no Burst jobs, no transform reading and writing, no collider
components. Those come in the next drop. Until then the package is a library you
can drive from your own code with `JiggleWorld`.

A bone is pulled by its parent and, unless both have a mass, never pulls it back. Set
`JiggleParams.Mass` on bones (or `JiggleWorld.SetMass` afterwards, tip included) to
turn the coupling on for the links between them, and `Cfg.CouplingPasses` for how many
passes a step uses. More passes hold bone lengths better under a heavy tip at a higher
cost. The mass ratio between neighbours counts as at most 16.
`docs/com.midmanstudio.allthingsjiggle.md` lists this and the other known limits.

Version 0.2.0 changes the native interface to version 2. A build of the native
libraries older than this package is refused by `JiggleLibrary.Verify`.

The C# layer has no Unity engine references, so the same files compile and run
under plain .NET. That is how CI tests them.

## Install

Add the package from the repository with Package Manager, "Add package from git
URL":

    https://github.com/MidManStudio/unity-chem-sim.git?path=packages/com.midmanstudio.allthingsjiggle

The native libraries under `Runtime/Plugins/Native` are built and committed by
the `build-jiggle-rust-libs` workflow. Until it has run, the package has no
binaries. The managed backend works without them. The native backend throws
`DllNotFoundException` on its first call.

## Use

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

`JiggleWorld` is a model of the call sequence, with plain managed arrays. A host
that keeps its own native buffers calls `JiggleSolver.StepRigs` and
`JiggleSolver.ResolveRigs` (managed) or the matching `jgl_` functions (native)
on them directly. Both take the same arguments and give the same status codes.

## Tests

`Tests/Editor/JiggleParityTests.cs` runs in the Unity Test Runner (Edit Mode).
CI runs the same file under .NET 8 against the freshly built library. It checks
every struct offset field by field, every exported function, random scenes of
every feature (with and without masses) bit for bit between the two backends, the
error codes, threaded batches, and two golden scenes, one way and coupled, whose
hashes the Rust tests assert too.

See `docs/com.midmanstudio.allthingsjiggle.md` for the design and the known problems.
