# Nuclear - Event-Level Nuclear Physics

`com.midmanstudio.nuclear` brings the `nuclear_core` Rust library into Unity.
It answers questions a game asks about nuclei, and it moves piles of atoms
through time and reactions:

- the nuclide chart: masses, binding energy, reaction energies (AME2020)
- decay: half-lives, outcomes, chains to a stable nuclide (NUBASE2020)
- time evolution of a pile of atoms, as exact draws or exact expectations
- fission products from ENDF/B-VIII.0 yields, neutron capture, and fusion of
  light nuclei at a temperature

It is event-level: you say how many events happen. Rates, fluxes, geometry and
chain reactions are for the game to build on top. Reactions stay at the level of
a sandbox, not weapon design.

The C# layer has no Unity engine references, so the same files compile and run
under plain .NET. That is how CI tests them.

## Install

Add the package from the repository with Package Manager, "Add package from git
URL":

    https://github.com/MidManStudio/unity-chem-sim.git?path=packages/com.midmanstudio.nuclear

The native libraries under `Runtime/Plugins/Native` are built and committed by
the `build-nuclear-rust-libs` workflow. Until it has run, the package has no
binaries and the first call throws `DllNotFoundException`.

## Use

```csharp
using MidManStudio.Nuclear;

using (var context = new NuclearContext(seed: 42))
using (var pile = new NuclearSample())
{
    pile.Add(Nuclide.Parse("Cs-137"), 1_000_000);
    pile.Advance(context, 30.0 * 365.25 * 86400.0);   // about one half-life
    foreach (NuclideCount c in pile.GetCounts())
        UnityEngine.Debug.Log(c.Nuclide + ": " + c.Count);
}
```

A context holds the random stream and the caches and is shared between piles.
Neither a context nor a pile is thread safe.

## Tests

`Tests/Editor/NuclearNativeTests.cs` runs in the Unity Test Runner (Edit Mode).
CI runs the same file under .NET 8 against the freshly built library, which also
checks every struct offset and every exported function.

See `docs/com.midmanstudio.nuclear.md` for the design and the known problems.
