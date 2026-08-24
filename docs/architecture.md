# Architecture

*Last synced against `chemistry_core` bench #11 (bonding system + mixed-element
benches). The previous version of this doc described a pre-refactor design —
C# owning a `NativeArray` and passing it into Rust by pointer each call. That
is no longer how this works; see "Why this changed" at the bottom.*

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
- **Pairwise bonding, new as of bench #11** (`compute_bonds`, additive on
  top of LJ, not a replacement for it): two atoms bond when they're within
  `1.15 × r_min` of each other (`r_min = σ · 2^(1/6)`, LJ's own equilibrium
  separation — reused, not reinvented) and their combined reactivity
  clears a threshold between He's exact `0.0` and the lowest nonzero
  `reactivity_index` in the current element table. A formed bond adds a
  harmonic spring (`spring_k`, eV/Å²) pulling toward that equilibrium
  distance, on top of whatever LJ already computed. Breaks once stretched
  past `1.8×` equilibrium.
  **MVP restriction, worth knowing before relying on it for game recipes:
  each atom can hold at most one bond at a time.** This is fine for
  diatomic pairs (H₂, HCl) but means nothing resembling a real multi-atom
  molecule — water, saltpeter, sulfuric acid, anything in the gameplay
  doc's predefined-compound list — can form yet. Generalizing this is a
  bigger lift than adding element data: it changes `BondInfo` from "one
  slot per atom" to something that can actually represent a small
  molecule's bond graph.

## Element data — where the numbers come from

`element_data.rs`'s `TABLE` is **hand-transcribed** from DixScript-Rust's
`mdix_files/chemistry_db/elements_database.mdix`, not loaded at runtime by
either side. There is no `ChemDataLoader`/boot-time registration step —
that was the old design. Re-syncing this table when the source `.mdix`
grows is a manual, by-eye pass (noted directly in the file's own module
doc), and `reactivity_index()`/`bond_strength()` reproduce
`core/physics.mdix`'s `calculateReactivityIndex`/`calculateBondStrength`
formulas exactly rather than reinventing them.

**Current coverage: 5 elements (H, He, Li, Be, B — Z=1 through 5),
transcribed correctly** — cross-checked field-by-field against the actual
`.mdix` source and the physics formulas; no discrepancies found. This
matches the `.mdix` source's own `total_elements = 5` — the table isn't
behind the source, the source itself only has 5 elements yet.

**Gap worth being explicit about:** the gameplay nomenclature system
(`grand-theft-grimoire-gameplay-reference.md` §1) names ~15 elements
directly (Au, Ag, Cu, Fe, Sn, Pb, Hg, S, C, N, As, Sb, Zn, P) plus H/O via
procedural roots, and lists 18 predefined historic compounds (Aqua Fortis,
Saltpeter, Black Powder, …). None of those 18 compounds can be represented
yet — none of their constituent elements beyond H are in the table. This
isn't a Rust-side backlog; it's upstream `.mdix` data that doesn't exist
yet. Worth sequencing element additions by what unlocks the most
predefined compounds first (O and N alone would unlock several of the
nitrogen/oxygen-based ones).

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
