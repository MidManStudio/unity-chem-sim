# Alembic — Architecture

```
.mdix chemistry_db  ->  chemistry_core (Rust)  ->  FFI bridge  ->  Presentation
  moddable data         spatial hash, LJ,          Burst-batched,   no GameObjects
  (com.midmanstudio.       Verlet, SIMD             zero-copy       (base tier);
   mdix, Dix.LoadStr)      (mid-math wide types)     NativeArray     VFX Graph /
                                                                     GPU instancing
                                                                     (high tier)
```

Alembic is the Rust-core + Unity-package half of that pipeline. It owns
the simulation and the base rendering path; it does not own chemistry
data (that's `.mdix` files loaded through `com.midmanstudio.mdix`) and
it does not own what a specific game does with simulation output.

## Layout

- `Runtime/Core` — atom state, simulation bindings, FFI calls into
  `chemistry_core`.
- `Runtime/Adapters` — Burst-batched marshaling. Same threshold pattern
  as `MidManStudio_Unity`'s `ProjectileSystem/Runtime/Adapters/BatchSpawnHelper.cs`:
  small batches filled on the managed side, larger ones filled by an
  `IJobParallelFor` before crossing the FFI boundary.
- `Runtime/Plugins/Native` — compiled `chemistry_core` binaries per platform.
- `Editor` — import/authoring tooling.

## Rust core

Lives at `rust_lib/chemistry_core` in this same repo, workspace-linked
to a vendored copy of `mid-math` (`rust_lib/vendor/mid-math`, refreshed
via `.github/workflows/vendor-mid-math.yml`). Zero third-party
dependencies by design.
