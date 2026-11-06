# com.midmanstudio.allthingsjiggle

All Things Jiggle is a Unity package for jiggle physics on chains of bones. It is
still the core: the solver, the bindings to the Rust library and the proof that the
two backends agree, now with two way coupling (a bone with a mass pulls its parent
back). It has no Unity components yet. The Rust half is
documented in `rust_lib/jiggle_core/docs/jiggle_core.md`, including how a frame
runs, the records, the C interface and the known limits of the solver itself. This
file covers the C# half.

## Contents

- Plan of the drops
- Layout and conventions
- Runtime/Core: JiggleConst.cs, JiggleMath.cs, JiggleStructs.cs, JiggleCollide.cs,
  JiggleFields.cs, JiggleKernel.cs, JiggleSolver.cs, JiggleNative.cs,
  JiggleParams.cs, JiggleWorld.cs, AssemblyInfo.cs
- Assembly definitions
- Tests/Editor/JiggleParityTests.cs
- Tests~/DotnetHarness
- Native libraries
- CI and workflows
- Known limits
- Fixes and Problems

## Plan of the drops

1. **Core.** The `jiggle_core` crate, the managed reference kernel, the native
   bindings, the managed world and the parity tests and CI.
   **1.5 Two way coupling (this drop).** Per bone mass, `StepConfig.CouplingPasses`,
   interface version 2, in both kernels, before any Burst port so the Burst kernel is
   written once against the final solver.
2. **Unity runtime.** Burst jobs, reading and writing transforms through
   `TransformAccessArray`, the backend switch (Burst by default, Rust selectable),
   editor labs, and the collider broadphase in C#. The broadphase will use collider
   layers in the style of the group and mask bits found in the MMD rig study.
3. **Extras and presets.** Body colliders from the rig, level of detail and culling,
   presets (hair strand, tail, ear, chest) from the technique level ranges of the
   study, and the Grand Theft Grimoire chest adapter.

Later: a SIMD lane batch kernel that uses the vendored mid-math wide types.

## Layout and conventions

The package follows the other packages of this repository: Unity assets have
`.meta` files because a package installed from a git URL is immutable, the C#
under `Runtime/Core` has no Unity engine references, and the same files compile
under plain .NET for CI.

```
Runtime/Core/                      the solver and the bindings
Runtime/Plugins/Native/<Platform>/ native libraries, built and committed by CI
Tests/Editor/                      the Unity Test Runner fixture
Tests~/DotnetHarness/              the same fixture under .NET 8, ignored by Unity
docs/                              this file
Documentation~/                    a pointer for the Package Manager
```

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
- the only library calls are `MathF.Sqrt` and `MathF.Floor`, both exact;
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

## Runtime/Core/AssemblyInfo.cs

Makes the internals visible to the editor test assembly, so the layout test can use the
bindings directly.

## Assembly definitions

- `Runtime/MidManStudio.AllThingsJiggle.asmdef`: `noEngineReferences` true, so the
  runtime assembly cannot take a UnityEngine reference by accident, and
  `allowUnsafeCode` true for the pointer code.
- `Tests/Editor/MidManStudio.AllThingsJiggle.Tests.Editor.asmdef`: Editor only, with the
  NUnit reference, `allowUnsafeCode` true, and the `UNITY_INCLUDE_TESTS` constraint.

## Tests/Editor/JiggleParityTests.cs

One NUnit fixture of 21 tests that checks the managed kernel against the real
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
  past the rig.

The file has no UnityEngine references and uses `NUnit.Framework` only through
`TestFixture`, `Test` and the `Assert` calls the stubs provide, so the harness can
run it under .NET.

The random scenes use `Math.Sin`, which is fine: both backends read the same
generated pose. The golden scene does not, because there the C# and the Rust sides
build the inputs separately and must agree.

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

Running it by hand needs the release library:

    cargo build --release -p jiggle_core
    dotnet run -c Release --project packages/com.midmanstudio.allthingsjiggle/Tests~/DotnetHarness \
        -- target/release/libjiggle_core.so --expect-exports 7

## Native libraries

`Runtime/Plugins/Native/<Platform>` holds one `NOTE.md` per platform until the build
workflow has run, and the libraries after. The workflow writes the `.meta` files with
the right import settings. Do not add files there by hand.

## CI and workflows

| Workflow | What it does |
| --- | --- |
| `.github/workflows/jiggle-core.yml` | The `ffi` job builds the release library and runs `Tests~/DotnetHarness` against it. The other jobs are described in the crate documentation |
| `.github/workflows/build-jiggle-rust-libs.yml` | Builds and commits the native libraries |

No root level scripts are used.

## Known limits

- Drop 1 has no Unity components: nothing reads or writes transforms, and there are no
  Burst jobs yet.
- The managed backend uses plain loops, not Burst. It is the reference and the fallback,
  not the fast path.
- Burst parity will be tolerance based, because Burst may fuse or reorder floating
  point operations unless it is told not to. The managed kernel against Rust is bitwise.
- `JiggleWorld` is not thread safe and keeps its arrays in managed memory, which the
  garbage collector may move. The `fixed` blocks pin them for the length of one call.
- Nothing here has run inside Unity. The C# is compiled and tested under .NET 8 at
  language version 9. The asmdefs, the `.meta` files and the Linux plugin settings are
  copied from the nuclear package, which works, but they have not been tried here.
- Coupling is opt in per bone and capped at a mass ratio of 16 between neighbours, segment
  collision is two samples, and rigs hold at most 126 bones. The crate documentation lists
  these and the other solver limits.
- The native plugin binaries committed under `Runtime/Plugins/Native` are rebuilt by the
  build workflow when `rust_lib/jiggle_core` changes. Until it has run after this drop, the
  package has the new C# and the old binaries (interface version 1), and
  `JiggleLibrary.Verify` throws. The managed backend does not need them.

## Fixes and Problems

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
