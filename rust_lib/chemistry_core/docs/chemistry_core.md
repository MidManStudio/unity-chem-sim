# chemistry_core

## Overview

Rust FFI simulation core behind com.midmanstudio.alembic: Lennard-Jones
pairwise forces, velocity Verlet integration, and harmonic bond/angle
springs, exposed to Unity through a small `extern "C"` surface in `lib.rs`.

## Modules

### `lib.rs`
**What it does:** The FFI boundary. Every function Unity calls into lives
here: context lifecycle, atom spawn/despawn, stepping, bond/angle
accessors, custom element registration.

**Decisions:**
- Added `chem_bond_r_min(atomic_number_a, atomic_number_b)`. Pure function
  of two atomic numbers, no `SimContext` needed. Returns the same LJ
  equilibrium separation (`sigma * 2^(1/6)`) that `compute_bonds`'s own
  Pass 2 uses as a fresh bond's rest length. Added so a caller (the Alembic
  Playground sample) can check a candidate spawn position against the real
  bonding-distance math before anything is spawned, instead of guessing a
  spacing constant that could drift out of sync with `compute_bonds`
  itself.

### `simulation.rs`
**What it does:** Core physics. LJ pairwise forces, velocity Verlet
integration, bond formation and breaking, angle-bend forces.

**Decisions:**
- `BondParams` gained a `min_range_factor` field, default `1.0`. Pass 2 of
  `compute_bonds` (new-bond formation) only checked an upper distance bound
  before this (`r > bond_range`), with no lower bound at all. A pair
  already crushed together well inside the LJ repulsive core (`r` much
  less than `r_min`) could still be proposed and bonded, with
  `equilibrium_length` set to `r_min` regardless of the actual, much
  shorter separation. That mismatch adds an outward spring force on top of
  whatever LJ repulsion is already dominating at that distance, sharpening
  the explosion instead of damping it. The new floor
  (`r < r_min * min_range_factor`) restricts bond formation to the
  attractive side of the pair's own LJ curve, at or beyond its equilibrium
  point, never mid-collision. Default `1.0` puts the floor exactly at
  `r_min`.

## CI and Workflows

- `.github/workflows/rust-rust-ci.yml` - build/test on push to `rust_lib/**`
- `.github/workflows/build-rust-lib.yml` - builds native plugin binaries
  for every platform on push to `rust_lib/**` or the root `Cargo.toml`.
  This is what picks up both changes above automatically — no manual Rust
  build needed before the Unity side sees them, once this lands on `main`.
- `.github/workflows/rust-bench.yml` - benchmark suite

## Fixes and Problems

### `simulation.rs`
- Pass 2 bond formation had no lower distance bound, letting an
  already-colliding pair (deep inside the LJ repulsive core) get proposed
  and bonded with `equilibrium_length` set to `r_min` regardless of actual
  separation. This was the dominant contributor to explosive first-frame
  behavior when the Unity-side random cloud spawn placed atoms without any
  minimum spacing (see com.midmanstudio.alembic.md's own Fixes and
  Problems section for that half of the fix). Fixed with the new
  `BondParams.min_range_factor` floor, default `1.0` (`r_min` itself).
