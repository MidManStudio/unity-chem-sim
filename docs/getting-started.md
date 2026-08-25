# Getting Started

*This doc previously described calling into Rust with a C#-owned
`NativeArray<AtomData>` pointer (`Chem_Init(ptr, atomCount, ...)`,
`Chem_Step(ptr, atoms.Length, ...)`). That API doesn't exist — it never
shipped past the design stage. What's below is the FFI surface that
actually exists in `chemistry_core` today. See `docs/architecture.md` for
why this changed.*

## What you can do today

Build and exercise `chemistry_core` on its own — spawn atoms, step the
simulation, query bonds, all from Rust (tests) or from any C caller that
links the compiled library. There is **no Unity-side integration yet**:
`packages/com.midmanstudio.alembic/Runtime/Core` and `Runtime/Adapters`
are both scaffold placeholders (see that package's own README "Status"
section), and no platform's native plugin binary has been built/committed
under `Runtime/Plugins/Native/*` yet. If you're picking up the C# bindings
next, this doc is the FFI surface to write against.

## Prerequisites

- Rust, stable toolchain, **1.83 or newer** — not 1.80 as an earlier note
  in `Cargo.toml` claimed. The floor comes from the vendored `mid-math`
  crate: its `sse2` module uses `f32::from_bits` in a `const` context,
  which only became a stable `const fn` in Rust 1.83. That module is
  compiled unconditionally on any x86/x86_64 target — `--features
  force-scalar` does **not** route around it, it only changes which impl
  gets re-exported as the canonical `Vec3`/`Vec4`/etc, not whether the
  `sse2` module itself gets compiled and type-checked. Confirmed directly:
  `cargo build -p mid-math --features force-scalar` still hits the same 20
  errors on Rust 1.75. CI has never surfaced this because
  `dtolnay/rust-toolchain@stable` always installs whatever's newest, so
  it's never actually built against the documented floor.
- Unity 2022.3 LTS or later, once the C# side exists — not required to
  build/test `chemistry_core` on its own.
- Unity packages `com.unity.burst`, `com.unity.mathematics`,
  `com.unity.collections` — same caveat, needed once `Runtime/Adapters`
  is written, not before.

## Build the Rust library

```bash
# Linux / macOS
./scripts/build_rust.sh

# Windows (PowerShell)
.\scripts\build_rust.ps1
```

Compiles `chemistry_core` and copies the platform DLL to `Assets/Plugins/`
for whenever a Unity project actually consumes it. For just building and
testing the crate itself:

```bash
cargo build --workspace
cargo test --workspace
```

## The FFI surface, as it exists today

This is `chemistry_core/src/lib.rs`'s own usage example, current as of
the unbounded-bonding + 20-element pass:

```csharp
FFIBridge.ValidateStructSizes();               // call once in Awake — checks
                                                // chem_struct_size()/chem_handle_size()
                                                // against the C# struct layout
IntPtr ctx = FFIBridge.chem_context_create(10.0f);   // cutoff_hint; 0.0 falls back to 10.0
AtomHandle h = FFIBridge.chem_spawn_atom(ctx, 1, 0f, 0f, 0f); // Z=1 (hydrogen) at origin
FFIBridge.chem_init(ctx, 300.0f, 42UL);        // Maxwell-Boltzmann velocities at 300K

// per frame:
FFIBridge.chem_step(ctx, Time.deltaTime, 10.0f);   // see the femtoseconds note below
IntPtr atoms = FFIBridge.chem_atoms_ptr(ctx);       // re-fetch every frame, don't cache —
                                                     // spawn can reallocate, despawn reorders
int n = FFIBridge.chem_atom_count(ctx);
// ... draw n AtomState structs starting at `atoms` ...

FFIBridge.chem_despawn_atom(ctx, h);

// on teardown (e.g. OnDestroy):
FFIBridge.chem_context_destroy(ctx);
```

**Before wiring `Time.deltaTime` straight into `chem_step`'s `dt`:** the
simulation's internal units require `dt` in femtoseconds, not seconds (see
`docs/architecture.md`'s "Energy units" section) — `lib.rs`'s own comment
on `chem_step` currently says "seconds," which is a documentation bug, not
the real contract. Worth resolving what `dt` should actually mean
gameplay-side (real elapsed time scaled into fs, a fixed timestep
independent of frame rate, etc.) before this becomes the convention baked
into `Runtime/Core`.

Full FFI surface, for reference:

| Function | Does |
|---|---|
| `chem_context_create(cutoff_hint)` → ctx | Create a persistent `SimContext` |
| `chem_context_destroy(ctx)` | Free it and every atom still alive in it |
| `chem_spawn_atom(ctx, z, x, y, z)` → `AtomHandle` | Spawn one atom, mass/radius sourced from `element_data` |
| `chem_despawn_atom(ctx, handle)` → `bool` | `false` if the handle is already stale |
| `chem_atom_count(ctx)` → `i32` | Current live atom count |
| `chem_get_atom(ctx, handle, out)` → `bool` | Copy one atom's state out by handle |
| `chem_atoms_ptr(ctx)` → `*const AtomState` | Zero-copy read into the dense array — re-fetch every frame |
| `chem_init(ctx, temperature_k, seed)` | Maxwell-Boltzmann velocity init, zeroes forces |
| `chem_step(ctx, dt, cutoff)` | LJ + bonding + velocity Verlet, one step |
| `chem_kinetic_energy(ctx)` → `f32` | Total KE in eV |
| `chem_temperature(ctx)` → `f32` | Temperature estimate in Kelvin, from equipartition |
| `chem_is_bonded(ctx, handle)` → `bool` | `false` for unbonded or stale, never crashes |
| `chem_bond_count(ctx, handle)` → `i32` | How many bonds this atom currently holds — 0 for stale/unbonded |
| `chem_bond_partner_at(ctx, handle, index, out)` → `bool` | The `index`-th partner (`0..chem_bond_count(...)`) — iterate to walk all of them |
| `chem_struct_size()` / `chem_handle_size()` | Layout validation — should return 48 / 8 |

## Current simulation limits, worth knowing before building recipes on top

- **20 elements** — see `docs/architecture.md`'s "Element data" section
  for exactly which, where the 15 newest ones' LJ parameters come from,
  and what that combination does and doesn't unlock yet.
- **Bonds are unbounded per atom**, but capped to one *new* edge per atom
  per `compute_bonds()` call — see `docs/architecture.md`'s bonding
  section for why. A multi-atom compound assembles over a few steps, not
  instantly.

Neither is a Rust-side bug; both are documented, deliberate scope.
