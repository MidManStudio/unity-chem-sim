# Alembic

Real-time particle chemistry simulation for Unity. Rust simulation core
(spatial hash, Lennard-Jones forces, velocity Verlet integration), SIMD
math via `mid-math`, Burst-batched FFI marshaling, no-GameObject rendering.

Standalone and game-agnostic by design — Alembic does not know about
runes, spells, or any specific game. It simulates atoms; what a project
does with the output (a spell VFX, a crafting result, whatever) lives
outside this package.

## Requirements

This package needs `com.midmanstudio.mdix` for loading `.mdix` chemistry
data at runtime. Unity's Package Manager does not resolve git dependencies
declared inside another package's own `package.json` (only in a project's
own `Packages/manifest.json`), so add this to your project's manifest
directly, alongside Alembic's own entry:

```json
"com.midmanstudio.mdix": "https://github.com/Mid-D-Man/DixScript-Rust.git?path=com.midmanstudio.mdix"
```

Pin this to a tag once DixScript-Rust cuts one for the mdix Unity package —
tracking a branch head is fine for now, not for a shipped build.

## Status

Scaffold only. `Runtime/Core` and `Runtime/Adapters` are empty pending the
Rust simulation core (`rust_lib/chemistry_core`) and its FFI surface.
See `Documentation~/index.md` for the architecture this is built toward.
