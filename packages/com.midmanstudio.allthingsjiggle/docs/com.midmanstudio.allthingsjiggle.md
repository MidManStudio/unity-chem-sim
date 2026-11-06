# com.midmanstudio.allthingsjiggle

All Things Jiggle is a Unity package for jiggle physics on chains of bones. This
first drop is the core: the solver, the bindings to the Rust library and the proof
that the two backends agree. It has no Unity components yet. The Rust half is
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

1. **Core (this drop).** The `jiggle_core` crate, the managed reference kernel, the
   native bindings, the managed world and the parity tests and CI.
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
field kinds, the struct kind numbers used by the layout probe, `MaxParticles` 128 and
`ExpectedAbiVersion` 1. The values are the ones of `types.rs` and `ffi/mod.rs`, and
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

Parity is the point of this file. When the Rust kernel changes, this file changes the
same way, and `GoldenSceneMatchesTheRustWorld` and the bitwise scene tests say whether
it was done right.

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

## Runtime/Core/JiggleWorld.cs

`JiggleWorld`, a model of the call sequence, with plain managed arrays that it owns.
It is what tests, tools and early users drive, and it is the C# twin of the Rust
`World`:

- `AddChain` builds a rig from the world positions of a chain, with the anchor first,
  and adds the virtual end particle. It throws `ArgumentException` for fewer than two
  points or a parameter count that is not one per bone.
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

One NUnit fixture of 16 tests that checks the managed kernel against the real
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
  second test that the scene exercises what it claims.

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
- The solver is one way, segment collision is two samples, and rigs hold at most 126
  bones. The crate documentation lists these and the other solver limits.

## Fixes and Problems

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

- `dotnet build` could not reach the package feed from the sandbox, so the harness
  was built with an empty local feed there. CI restores from the normal feed, and the
  project has no package references.
