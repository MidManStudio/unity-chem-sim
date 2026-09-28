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
- Added `BondEvent` (new `#[repr(C)]` struct, 12 bytes, own
  `chem_bond_event_size` validator) and `chem_take_bond_events` -- see
  `simulation.rs`'s own "Bond events" note above for the design reasoning,
  and that function's doc comment for why draining the same buffer it
  just returned a pointer into is safe (plain `Copy`, non-`Drop` data --
  `Vec::clear` here is only a length reset).
- Added the containment-bubble surface: `chem_suppress_atom`,
  `chem_clear_suppression`, `chem_clear_suppression_in_radius`. Thin
  wrappers straight over `simulation::suppress_atom`/
  `clear_suppression`/`clear_suppression_in_radius` -- see
  `simulation.rs`'s own note above for the mechanic itself.
- Added `chem_set_max_bond_order`. The one new externally-mutable knob on
  `SimContext.bond_params` -- everything else in `BondParams` is still
  fixed at its `Default` value for the context's lifetime, same as
  before (see that struct's own doc on why the rest wasn't exposed
  today).

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
- Multi-order bonds (single/double/triple). `BondInfo` gained an `order:
  u8` field (1-3). `BondParams` gained `max_bond_order` (default `1`,
  upgrades off by default -- every existing scenario/test keeps forming
  single bonds only unless a caller explicitly raises this via
  `chem_set_max_bond_order`), `order_length_factor` (default `[1.0, 0.87,
  0.78]`, mirroring real C-C single/double/triple bond length ratios --
  the one pair this sim has an actual reference point for, since it's the
  motivating case, acetylene's C-C triple bond), and
  `order_stiffness_factor` (default `[1.0, 1.89, 3.21]`, derived from real
  C-C stretching-vibration frequency ratios). Pass 2 now considers an
  upgrade candidate (not just a fresh-bond candidate) whenever it finds a
  pair already bonded to each other with order headroom left, gated by its
  own tighter distance window centered on the target order's shorter
  `equilibrium_length` -- capped to one upgrade per atom per call
  (`order_upgraded_this_pass`), independent of the existing one-new-edge
  cap. Pass 1 gained the matching downgrade path: an over-stretched
  multi-order bond now cascades down one order at a time (checked from one
  order below current, downward, taking the first that fits) before
  actually breaking, only snapping entirely once even a bare single bond's
  own break tolerance is exceeded. `r_min` for the cascade is back-derived
  from the current `equilibrium_length`/`order` rather than stored
  separately (`equilibrium_length == r_min * order_length_factor[order -
  1]` always, by construction). At `order == 1` the downgrade range is
  empty, falling straight through to the original break behavior
  unchanged -- same backward-compatible-default property everything else
  here already has. New `SimContext.order_downgrades_scratch` queues these
  the same way `broken_scratch` queues breaks, drained via
  `set_bond_order` right after `break_one_bond`'s own drain.
  `BondEvent.kind == 2` (OrderChanged) now covers both directions -- the
  event doesn't say which way on its own, a consumer that cares compares
  against a previously-seen order.
- Containment bubble (`suppressed` field on `SimContext`, keyed by
  `GenerationalIndex` -> remaining suppression femtoseconds,
  `f32::INFINITY` for "until explicitly cleared"). Either side of a
  Pass 2 candidate pair being suppressed excludes the whole pair from
  consideration this call -- fresh bond and order upgrade alike. Only
  gates *new* chemistry: LJ repulsion/attraction and any bond an atom
  already holds are completely unaffected, so a suppressed pair still
  visibly interacts, it just can't newly react. Ticks down once per
  `step()` call; `suppress_atom`/`clear_suppression`/
  `clear_suppression_in_radius` are the only ways in and out. This is the
  "electromagnetic stabilizing bubble" mechanic from the original design
  conversation -- keep two reactants apart until a designed trigger
  (a collision, a timer, an explicit game event) says otherwise.
- Bond events. New `SimContext.bond_events_scratch: Vec<BondEvent>`
  (`BondEvent` defined in `lib.rs`), appended to directly inside Pass 1
  (a break) and Pass 2's two drain loops (a fresh bond, an order upgrade)
  -- no separate diffing pass, since Pass 1/Pass 2 already identify
  exactly these transitions as part of their own normal work. Deliberately
  *not* cleared per `step()` call the way `new_bonds_scratch` is:
  `AlembicPlaygroundController.stepsPerFrame` can call `chem_step` several
  times before anything reads this, so events accumulate across every
  sub-step in between and are only drained by an explicit
  `chem_take_bond_events` call (see that function's own doc for why this
  makes it a fundamentally different access pattern from
  `chem_bonds_ptr`/`refresh_bonds_scratch`, not just a naming choice).
  Landed in Rust rather than as a frame-to-frame diff on the C# side
  because Pass 1/Pass 2 already walk exactly the transitions this needs
  to report as a normal part of their own work -- computing them again
  from a snapshot diff in C# would mean either a second full bond-list
  walk every frame, or Unity maintaining its own shadow copy of bond
  topology just to diff against, both strictly more work than appending
  three lines at the three places the transition is already being
  decided.

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
- No way to represent a double or triple bond (needed for e.g.
  acetylene's C-C triple bond from a calcium-carbide-plus-water
  scenario) -- every edge was implicitly single-order. Added
  `BondInfo.order` and the Pass 2 upgrade path described above, off by
  default (`max_bond_order = 1`) so nothing existing changes unless a
  caller opts in.
- No way to keep two reactants apart until a designed trigger (the
  "electromagnetic stabilizing bubble" from the original design
  conversation) -- anything in range and reactive enough would bond the
  moment Pass 2 saw it. Added the `suppressed` mechanic described above.
- No way for Unity to know a bond formed/broke/upgraded *this frame*
  without polling `chem_bonds_ptr` and diffing it by hand. Added
  `BondEvent`/`chem_take_bond_events`.
