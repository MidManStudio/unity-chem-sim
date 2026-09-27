# com.midmanstudio.alembic

## Overview

Unity chemistry simulation package. A thin C# layer over the
chemistry_core Rust FFI: P/Invoke bindings, renderers, and a Playground
sample scene for testing scenarios end to end.

## Modules

### `Runtime/Core/ChemistryLib.cs`
**What it does:** Every P/Invoke binding to chemistry_core. Nothing else
in the package uses DllImport directly.

**Decisions:**
- Added `chem_bond_r_min(int atomicNumberA, int atomicNumberB)`. A direct
  extern binding (plain float return, no byte/bool marshaling needed,
  unlike most of the rest of this file). Lets a caller ask chemistry_core
  what its own bonding math considers the equilibrium separation for a
  pair, before either atom is spawned.
- Added `BondEvent` (12-byte explicit-layout struct mirroring Rust's
  `repr(C)`, plus an `EventKind` enum for the `Kind` byte) and
  `chem_take_bond_events`, registered in `ValidateStructSizes()` the same
  way every other FFI struct already is. Raw `IntPtr` + count, same as
  `chem_bonds_ptr` -- marshaling the array into managed data is left to
  the caller (`Adapters.BondBatchAdapter` is the existing precedent for
  that; no consumer built for `BondEvent` yet, this batch only adds the
  binding itself).
- Added the containment-bubble bindings: `SuppressAtom`,
  `ClearSuppression`, `ClearSuppressionInRadius` (the last one takes a
  `Vector3` for convenience -- not FFI-facing itself, unpacked into three
  floats before the actual `DllImport` call, consistent with this file's
  own stated reasons for keeping `Vector3`/`float3` out of the
  explicit-layout structs themselves).
- Added `chem_set_max_bond_order`.

### `Samples~/Playground/AlembicPlaygroundConfig.cs`
**What it does:** ScriptableObject scenario data for the Playground sample
controller.

**Decisions:**
- Added `CloudMinSpacingFactor`, default `1`. Multiplies chemistry_core's
  own `chem_bond_r_min` result to get the minimum enforced spacing between
  any two atoms placed by the random-cloud fallback. Only affects the
  cloud path, not `ExplicitAtoms` — those are already placed by hand.

### `Samples~/Playground/AlembicPlaygroundController.cs`
**What it does:** Owns the live `SimContext`, spawns the configured
scenario, steps the sim, hands the context to whichever renderers are
assigned.

**Decisions:**
- The random-cloud fallback (`SpawnRandomCloud`, split out of the old
  `SpawnInitialAtoms`) previously placed every atom via plain
  `UnityEngine.Random.insideUnitSphere`, with no minimum spacing check at
  all. At the sample's own defaults (40 atoms in a 6 Angstrom radius), most
  pairs started out closer together than chemistry_core's LJ equilibrium
  separation for H/O — deep inside the repulsive core, which reads as a
  violent explosion the moment stepping starts rather than a molecule
  assembling.
- Rewrote it as seeded rejection sampling. A candidate position is only
  accepted once it clears `chem_bond_r_min(candidateElement,
  alreadyPlacedElement) * CloudMinSpacingFactor` from every atom already
  placed this pass. Pairwise r_min for the pool's distinct elements is
  precomputed once (a handful of P/Invoke calls total) instead of queried
  per rejection attempt.
- Replaced `UnityEngine.Random` with a local `System.Random` seeded from
  `AlembicPlaygroundConfig.Seed` for spawn positions specifically. Before
  this, only `chem_init`'s velocity draw was ever tied to Seed — cloud
  positions came from Unity's own global RNG regardless. Reset (R) with
  the same Seed now reproduces the same starting layout every time.
- An atom that can't find a valid slot within 64 attempts is skipped, not
  force-placed into an overlap. One aggregated warning reports how many
  were skipped, naming `CloudRadius`/`CloudAtomCount`/
  `CloudMinSpacingFactor` as the knobs to adjust.

## CI and Workflows

Nothing specific to this package beyond the repo-root Rust build/test
workflows already covered in chemistry_core's own doc file — this
package's C# has no separate CI of its own yet.

## Fixes and Problems

### `Samples~/Playground/AlembicPlaygroundController.cs`
- Random-cloud spawn had no minimum inter-atom spacing and used unseeded
  `UnityEngine.Random` for positions. Combined, this caused near-guaranteed
  LJ-core overlap (a violent explosion on the first simulation step for
  most runs) and a non-reproducible Reset (same Seed, different layout
  every time). Fixed with seeded rejection sampling against
  chemistry_core's own `chem_bond_r_min`, described above. Companion fix
  on the Rust side in chemistry_core.md's own Fixes and Problems section —
  the two together closed both the spawn-time overlap and the
  already-colliding-pair bonding bug it was triggering.
