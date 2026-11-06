# Changelog

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
