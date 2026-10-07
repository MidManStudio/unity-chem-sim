# com.midmanstudio.nuclear

Unity bindings for the `nuclear_core` Rust library. The design of the library is in `rust_lib/nuclear_core/docs/nuclear_core.md`. This file covers the package: the C# files, the tests, the native plugins and the known problems. Each source file points at the section here that carries the name of its path.

## Overview

The package is a thin, typed layer over the C interface of `nuclear_core` (interface version 1, 64 functions). It has no dependencies and no Unity engine references (`noEngineReferences` in the asmdef), so the files compile under plain .NET, which is how CI runs them. Reactions stay at event level: the caller says how many events happen, and rates, fluxes and chain reactions are the game's to build.

Types fall in three groups. Value structs mirror the native structs byte for byte with explicit offsets. Static classes (`NuclearData`, `NuclearFusion`) answer questions that need no pile. Handle classes (`NuclearContext`, `NuclearSample`, `NuclearAmounts`) own native objects and are disposable, with a finalizer as a safety net.

## Runtime/Core/NuclearNative.cs
**What it does:** Every `DllImport`, the native buffer protocols as `ReadList` and `ReadText`, `Check` that turns a negative status into a `NuclearException`, and `NuclearLibrary.Initialize`.

**Decisions:**
- All P/Invoke lives here, as in the Alembic package. iOS and WebGL use `__Internal` because the static library is linked into the player. Everything else uses the name `nuclear_core`.
- `NuclearLibrary.Initialize` runs before any other call and checks the interface version and the size of all 16 structs against the native `nuc_struct_size`. A stale binary committed by an old CI run therefore fails with a message that names the cause, instead of corrupting memory.
- Lists and text follow the native protocols: call with capacity 0 to get the count, then again to fill, and retry once if the count grew in between.

## Runtime/Core/NuclearStructs.cs
**What it does:** The enums and the explicit-layout value structs that cross the boundary.

**Decisions:**
- `LayoutKind.Explicit` with `FieldOffset` and `Size`, as in the Alembic package, so no padding rule differs between Mono, IL2CPP and CoreCLR. Native structs carry explicit reserved fields where C would pad, and the C# side leaves those gaps empty.
- Native arrays become numbered fields (`emitted0` to `emitted3`, three product slots), because C# has no inline arrays that work across the Unity versions in use.
- A `bool` is a native `u8` and a property here. Nuclides are `uint` keys with a `Nuclide` property.
- Warnings 0649 and 0169 are switched off for the file, because native code fills the fields.

## Runtime/Core/Nuclide.cs
**What it does:** `Nuclide`, a readonly struct of protons and neutrons with the 32-bit key, parsing and printing through the native library, ordering that matches the native tables, and constants for the neutron, H-1, H-2, H-3, He-3 and He-4.

## Runtime/Core/NuclearException.cs
**What it does:** `NuclearStatus`, the status codes, and `NuclearException`, thrown for every negative status with the native last-error text as its message.

## Runtime/Core/NuclearData.cs
**What it does:** Static queries that need no pile: element symbols, the list of 3558 nuclides, mass excess, binding energy, reaction Q-values, half-lives, abundance, decay outcomes, decay energies, chains to stability, neutron branching, capture energy and the nucleus that splits after a delayed fission.

**Decisions:** Queries that can reasonably find nothing have a `Try` form (`TryGetHalfLife`, `TryGetNeutronBranching`). The rest throw.

## Runtime/Core/NuclearFusion.cs
**What it does:** The seven fusion channels by index: description, name, thermal reactivity at a temperature, the channels of a pair, and the share of events per channel.

## Runtime/Core/NuclearContext.cs
**What it does:** The shared state of a simulation: the random stream, the decay cache and the fission cache. It also answers the fission queries, because those use its cache.

**Decisions:**
- The generator state can be read and set as four 64-bit words, so a saved game continues the same random stream.
- One context serves many piles. A context is not thread safe. Separate contexts can run on separate threads.

## Runtime/Core/NuclearSample.cs
**What it does:** A pile of whole atoms: add, count, list, totals, `Advance`, `ResolveFissions`, `Irradiate` and `Fuse`. Outcomes are exact draws.

**Decisions:** `Irradiate` has an overload that takes the authored fission probability from the neutron table and one that takes the caller's number, for nuclei the table does not cover. A failed call leaves the pile unchanged, as in the Rust API.

## Runtime/Core/NuclearAmounts.cs
**What it does:** The same operations on fractional amounts, giving the exact expected result instead of a draw.

## Runtime/Core/AssemblyInfo.cs
**What it does:** Lets the editor test assembly see the internal bindings, which the field-by-field layout check needs.

## Tests/Editor/NuclearNativeTests.cs
**What it does:** Thirteen NUnit tests: interface version, layout of every struct field by field, nuclide parsing, masses and energies, decay data, neutron branching, fission and fusion queries, determinism by seed, saved generator state, reactions with their error cases, amounts, and disposed handles.

**Decisions:**
- The layout test asks the native library for a struct filled with known values (`nuc_layout_probe`) and reads every field through the C# declaration, ordered by offset. The k-th named field must hold k, or k + 0.25 for a double. That proves each offset and not only the total size.
- The file uses only classic NUnit assertions and no Unity types, so it compiles under plain .NET with the small NUnit stand-in in `Tests~/DotnetHarness`.
- The tests need the native library. In the Editor they run once the build workflow has committed the binaries for the Editor platform. The Linux binary has its importer's Editor switch off, so run the tests on macOS or Windows, or rely on CI.

## Native plugins and CI
- `Runtime/Plugins/Native/<platform>` holds one `NOTE.md` per platform so the folders exist. `.github/workflows/build-nuclear-rust-libs.yml` builds six platforms (macOS universal, Windows, Linux, Android arm64-v8a, armeabi-v7a and x86_64, iOS device and simulator, WebGL) and commits the binaries with their `.meta` files.
- The `ffi` job of `.github/workflows/nuclear-core.yml` builds the Linux release library, compiles `Runtime/Core` and the editor tests with .NET 8 at language version 9 (what Unity 2022.3 compiles), and runs them. It also checks that every `DllImport` resolves to a symbol and that their number equals the number of `nuc_` exports, so a function added in Rust and forgotten here fails the build.
- `Tests~/DotnetHarness` ends in a tilde, so Unity ignores it.

## Fixes and Problems

### Runtime/Core/NuclearNative.cs
- The native binaries were not built when this package was written. Until the build workflow has run and committed them, every call throws `DllNotFoundException`. The workflow is a like-for-like port of the chemistry one, and its first real run is its verification.
- An iOS or WebGL player that links both `libnuclear_core.a` and `libchemistry_core.a` can fail with duplicate Rust runtime symbols. Android and desktop use separate dynamic libraries and are not affected. If both packages must ship on those platforms, merge the two crates into one static library.

### Runtime/Core/NuclearStructs.cs
- Struct sizes and all field offsets are checked by the native layout probe on the build host (x86_64 Linux). Other platforms are covered by the explicit reserved fields, which make every offset independent of the target. They were not run on 32-bit ARM.

### Runtime/Core/NuclearContext.cs
- Handles have finalizers, so a forgotten `Dispose` frees the native object at some later collection. Dispose them to free memory when you want it freed.

### Tests~/DotnetHarness
- The root `.gitignore` ignores `*.csproj`, so `NuclearHarness.csproj` is not tracked by git commands that respect it. The file was added through the GitHub web UI, which does not read `.gitignore`. A local `git add` of an edited copy needs `-f`, or a negation line for this folder in `.gitignore`.
- The project builds with `dotnet build --source <empty folder>` where NuGet is unreachable. It needs no packages.

### Tests/Editor/NuclearNativeTests.cs
- The tests were not run inside the Unity Test Runner. They were run under .NET 8 against the native library, with a small stand-in for NUnit. A signature difference between that stand-in and the NUnit in Unity would show up as a compile error in the test assembly only. It cannot break the runtime assembly.
