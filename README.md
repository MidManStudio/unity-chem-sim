# unity-chem-sim

Rust FFI particle-chemistry simulation core (`chemistry_core`) plus a
standalone Unity package (`com.midmanstudio.alembic`) built on top of it.
Spatial-hashed Lennard-Jones dynamics, velocity Verlet integration, and
pairwise bonding, driven by real per-element physics data hand-synced from
DixScript-Rust's `elements_database.mdix` — not a lookup-table magic
system. Built for [Grand Theft Grimoire](https://github.com/MidManStudio),
but the package itself doesn't know that; it simulates atoms, nothing
game-specific.

## Status

`chemistry_core` (the Rust simulation core) is the mature half: spatial
hash, LJ forces, velocity Verlet, and pairwise bonding are all in and
tested, currently covering 5 elements (H, He, Li, Be, B). The Unity side
(`packages/com.midmanstudio.alembic`) is still scaffold — `Runtime/Core`
and `Runtime/Adapters` are placeholders pending C# FFI bindings against the
surface documented in `docs/getting-started.md`.

## Build

```bash
# Linux / macOS
./scripts/build_rust.sh

# Windows (PowerShell)
.\scripts\build_rust.ps1
```

This compiles `chemistry_core` and copies the platform DLL to `Assets/Plugins/`.

## Requirements

- Unity 2022.3 LTS or later
- Rust, stable toolchain, **1.83 or newer** (vendored `mid-math` needs a
  stable-const `f32::from_bits` — see `docs/getting-started.md` for why the
  older "1.80+" note was wrong)
- Unity packages: `com.unity.burst`, `com.unity.mathematics`, `com.unity.collections`

## Architecture

See `docs/architecture.md`.

## License

MIT
