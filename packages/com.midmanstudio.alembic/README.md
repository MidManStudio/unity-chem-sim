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

`Runtime/Core` (the full `ChemistryLib` FFI layer), `Runtime/Adapters`
(Burst-batched bond marshaling), and `Runtime/Rendering` (atom/bond/angle
renderers, dual instanced/combined-mesh paths, the shared
`InstancedPrimitive_URP` shader) are all implemented against
`chemistry_core`'s current FFI surface — this is no longer scaffold-only,
that note is stale relative to what's actually in this folder now. Native
plugin binaries for Windows/macOS/Linux/Android/iOS/WebGL are committed
under `Runtime/Plugins/Native/`.

What's still open: `Runtime/Core` doesn't yet load chemistry data from
`.mdix` through `com.midmanstudio.mdix` at runtime the way
`Documentation~/index.md`'s own pipeline diagram describes —
`chemistry_core`'s element table is still a hand-synced Rust-side copy
(see `element_data.rs`'s own doc for why), so that diagram is closer to
the target architecture than to what ships today.

See `Documentation~/index.md` for the architecture this is built toward,
and `Samples~/Playground/README.md` for a drop-in Play Mode test scene
that exercises the full spawn/step/render loop end-to-end — the fastest
way to see whether any of the above actually works.
