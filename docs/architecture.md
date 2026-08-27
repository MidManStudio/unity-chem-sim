# Architecture

*Last synced against the unbounded-bonding + 20-element pass (post bench
#11). The previous version of this doc described a pre-refactor design —
C# owning a `NativeArray` and passing it into Rust by pointer each call.
That is no longer how this works; see "Why this changed" at the bottom.*

## Layers, as they exist today

```
[DixScript-Rust: mdix_files/chemistry_db/elements_database.mdix]
        │  source of truth for per-element physics — hand-synced, not loaded at runtime
        ▼
[chemistry_core/src/element_data.rs]   ← TABLE: transcribed by hand, cross-checked
        │                                  against the .mdix source and core/physics.mdix's
        │                                  own formulas (see "Element data" below)
        ▼
[chemistry_core::SimContext]           ← Rust OWNS the atom array (see below)
        │  spatial hash → LJ forces → bonding (form/break/spring) → velocity Verlet
        ▼
[FFI surface (lib.rs, extern "C")]     ← chem_context_create, chem_spawn_atom,
        │                                  chem_step, chem_get_atom, chem_is_bonded, …
        ▼
[C# bindings — NOT YET WRITTEN]        ← packages/com.midmanstudio.alembic/Runtime/Core
        │                                  is scaffold-only right now; see package README
        ▼
[Rendering — NOT YET WRITTEN]          ← Runtime/Adapters (Burst batching) is also scaffold-only
```

The bottom two layers are the actual next milestone, not a "someday." The
Rust FFI surface below is stable enough now (spawn/despawn, step, bond
queries, struct-size validation) that there's no remaining reason to treat
it as a moving target blocking the C# side.

## Rust owns the atom array

Unlike the old design, `SimContext` owns atom storage itself — atoms are
spawned and despawned *through* the FFI (`chem_spawn_atom`/`chem_despawn_atom`),
not passed in as a `NativeArray<AtomData>` pointer from C# each frame. This
is deliberate: for Rust to actually *enforce* atom identity (not just book-
keep it) and for `chemistry_core` to work unmodified in a non-DOTS engine
with zero concept of a stable entity ID, the array has to live on the Rust
side. Identity crossing the FFI boundary is a `chemistry_core::AtomHandle`
(`{ index: u32, generation: u32 }`, 8 bytes, `#[repr(C)]`) — not
`mid_collections::GenerationalIndex` directly, which has no public
constructor from raw parts by design, so nothing on the C# side can forge a
handle.

`chem_atoms_ptr`'s array order is **not stable across despawns** — a
despawn swap-removes to keep the live set dense. Fine for "draw N points
somewhere," not fine for tracking "array position 47 is always this atom."
Use `AtomHandle` + `chem_get_atom` for that.

## AtomState struct (48 bytes, both sides must agree)

| Field | Type | Offset | Unit |
|---|---|---|---|
| position | float3 / `[f32; 3]` | 0 | Ångström |
| velocity | float3 / `[f32; 3]` | 12 | Å / fs |
| force | float3 / `[f32; 3]` | 24 | eV / Å (accumulated this frame, cleared per step) |
| mass | float / `f32` | 36 | amu |
| radius | float / `f32` | 40 | pm (van der Waals) |
| atomicNumber | int / `i32` | 44 | element Z — the `element_data` lookup key |

Verified two ways: a `const _: () = assert!(size_of::<AtomState>() == 48);`
at compile time in `lib.rs`, and `chem_struct_size()` at runtime for
whichever language is on the other side of the FFI to call once at startup.

**Energy units are eV, not kcal/mol, and time is femtoseconds, not
picoseconds** — worth being explicit since the previous version of this doc
had both wrong. The conversion constant that makes this internally
consistent is `AMU_TO_EFF_MASS` in `simulation.rs` (amu → eV·fs²/Å²,
≈103.64269, the standard "real units" LAMMPS-style conversion for this unit
system) — `F = ma` only balances dimensionally if `dt` is in femtoseconds.
**`chem_step`'s own doc comment in `lib.rs` currently says "seconds" —
that's wrong, worth fixing before the C# side gets written against it** (see
`simulation.rs::step`'s comment, which correctly says femtoseconds, and
matches what `AMU_TO_EFF_MASS` requires). Passing an actual `Time.deltaTime`
in seconds (~0.016) as `dt` would run the simulation at 0.016 fs per frame —
stable, but nowhere near what "step by one frame's worth of real time" was
probably meant to do. Flagging this now so it gets caught before, not after,
`Runtime/Core`'s FFI bindings get written against the current wording.

## Rust simulation kernel

- Spatial hash grid: uniform cells sized to the cutoff radius, rebuilt each
  step from current positions. Any pair within `cutoff` lands in the same
  cell or one of its 26 neighbors, so only 27 cells are ever checked per
  atom (`spatial_hash.rs`). Uses an in-house `FxHasher` instead of std's
  SipHash — cell keys are simulation-derived `(i32,i32,i32)`, not
  attacker-supplied, so HashDoS resistance is overhead with nothing to
  defend against.
- LJ force: `24ε/r · (2(σ/r)¹² − (σ/r)⁶)`, standard derivative form, cutoff
  at 10 Å by default. Two implementations exist — `compute_forces_scalar`
  (default) and `compute_forces_simd` (behind the `scalar-math` feature
  flag's *inverse* — see Cargo.toml). SIMD was benched as a genuine
  regression at n=256/1024 and kept anyway, feature-gated off, rather than
  deleted — see the flag's own comment for the numbers.
- Velocity Verlet integration: position update from the old force, force
  recompute at the new positions, then a velocity half-step blending old
  and new acceleration.
- **Pairwise bonding** (`compute_bonds`, additive on top of LJ, not a
  replacement for it): two atoms bond when they're within `1.15 × r_min`
  of each other (`r_min = σ · 2^(1/6)`, LJ's own equilibrium separation —
  reused, not reinvented) and their combined reactivity clears a
  threshold between He's exact `0.0` and the lowest nonzero
  `reactivity_index` in the current element table. A formed bond adds a
  harmonic spring (`spring_k`, eV/Å²) pulling toward that equilibrium
  distance, on top of whatever LJ already computed. Breaks once stretched
  past `1.8×` equilibrium.
  **Bonds are unbounded per atom** — the earlier one-bond-per-atom MVP
  restriction is gone, so multi-atom molecules (water, saltpeter,
  sulfuric acid — anything in the gameplay doc's predefined-compound
  list) can actually form now, not just diatomic pairs. What *is* still
  capped: an atom can pick up at most **one new edge per `compute_bonds`
  call**, not an unlimited number in a single frame — a deliberate choice
  so a compound assembles visibly over a few steps instead of an atom
  suddenly surrounded by reactive neighbors bonding to all of them at
  once. Total bonds held stays unbounded; only the growth rate is capped.
  `ctx.bonds` is `SparseSet<GenerationalIndex, MidVec<BondInfo, 6>>` now
  (a small-vec per atom — 6 edges inline before a real heap allocation,
  not a hard cap), not one `BondInfo` per atom — `chem_bond_partner`
  (single-partner) is gone from the FFI, replaced by `chem_bond_count` +
  `chem_bond_partner_at(index)` to actually express "bonded to more than
  one thing." `6` comfortably clears every predefined compound in the
  gameplay doc (nothing there exceeds 4) with headroom to spare, and
  matches octahedral coordination — the single most common real packing
  for the ore/salt compounds in that same list (Litharge, Cinnabar,
  Crocus of Iron, Natron), which is a more realistic worst case for this
  sim's actual proximity-based bonding than a small molecule's central
  atom is. `MidVec` has no `retain` (unlike `Vec`) — `retain_bond`, a
  small local helper, covers the gap `break_one_bond`/`break_all_bonds`
  need. Safe to break the FFI's old single-partner shape outright rather
  than deprecate it: nothing outside this crate consumed it yet
  (`Runtime/Core` is still scaffold — see below), so there's no real
  caller to migrate.
  `chem_bond_geometry_at(index)` sits alongside `chem_bond_partner_at`,
  same indexing — returns a `BondGeometry { equilibrium_length,
  current_length }` per edge. `current_length` reads straight off each
  atom's live `AtomState.position`, not the `positions` scratch buffer
  used internally by the force kernels, so it's correct even before any
  `chem_step` has run. Strain (`(current_length - equilibrium_length) /
  equilibrium_length`) is deliberately left for the caller to compute —
  one line either side of the FFI boundary, and it keeps this accessor
  from baking in an opinion about how a straining bond should be
  visualized.

## Element data — where the numbers come from

`element_data.rs`'s `TABLE` is **hand-transcribed** from DixScript-Rust's
`mdix_files/chemistry_db/elements_database.mdix`, not loaded at runtime by
either side. There is no `ChemDataLoader`/boot-time registration step —
that was the old design. Re-syncing this table when the source `.mdix`
grows is a manual, by-eye pass (noted directly in the file's own module
doc), and `reactivity_index()`/`bond_strength()` reproduce
`core/physics.mdix`'s `calculateReactivityIndex`/`calculateBondStrength`
formulas exactly rather than reinventing them.

**Coverage: 20 elements** — the original H, He, Li, Be, B, plus the full
gameplay-doc §1.1 alchemical-naming set (C, N, O, P, S, As, Sb, Zn, Cu,
Fe, Sn, Pb, Hg, Ag, Au). The original 5's sigma/epsilon are real
transport-property-literature LJ values; the 15 new elements' come from a
different, broader-coverage source instead — Rappé et al.'s Universal
Force Field (UFF), which is the only real, citable, peer-reviewed source
that actually covers metals (most metals don't occur as simple monatomic
gases, so the transport-property route that worked for H/He simply
doesn't exist for them). Worth knowing before tuning game feel against
these numbers: UFF is a generic molecular-mechanics force field, not a
high-precision fit the way the original two entries are — see
`element_data.rs`'s own module doc for the full sourcing writeup and a
concrete spot-check (UFF's own He parameters vs. the real ones already in
this table) showing the gap.

**What this unlocks:** combined with bonds now being unbounded per atom
(above), several of the gameplay doc's 18 predefined compounds
(`grand-theft-grimoire-gameplay-reference.md` §1.3) can actually be
represented for the first time — anything built from this element set,
e.g. Cinnabar (HgS), Litharge (PbO), Crocus of Iron (Fe₂O₃), Fixed Air
(CO₂), Wood Spirit (CH₃OH). Still missing: Na, K, Ca, Cl, Ba, Al, Ag's
counterpart anion chemistry for a few of the salts (Saltpeter, Natron,
Alum, Baryte, Sal Ammoniac, Lunar Caustic all need at least one of
those) — none of those elements are in the `.mdix` source yet either, so
same as before, this is upstream data, not a Rust-side backlog.

## Rendering

Not implemented. `Runtime/Adapters` (Burst-batched marshaling) and
`Runtime/Core` (atom bindings, FFI calls) are both scaffold-only — see
`packages/com.midmanstudio.alembic/README.md`'s own "Status" section. The
originally-planned design (`AtomRenderer` reading positions each
`chem_step`, uploading to a `GraphicsBuffer`, `Graphics.DrawMesh` billboard
quads, no per-atom GameObjects) is still the intended shape; none of it
exists as code yet.

## Why this changed

The version of this doc before this pass described atoms as C#-owned
(`NativeArray<AtomData>`, `Allocator.Persistent`) with Rust operating on a
pointer passed in each call, and chemistry data loaded at boot through a
`ChemDataLoader`/registration call. Neither of those is how the current
`chemistry_core` (as of the atom-ownership restructure, well before bench
#11) actually works — see `lib.rs`'s own module doc for the up-to-date
version of "why Rust owns the array" in more detail than this file
duplicates. This doc was out of sync with the code for long enough that it
was actively describing a different architecture; flagged here rather than
silently rewritten so the gap is visible in history.
