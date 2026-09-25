# Alembic Playground (sample)

A drop-in Play Mode test scene for `com.midmanstudio.alembic` — watch
atoms spawn, move, and bond, tweak sim parameters live, spawn your own
atoms at will. Exists to answer "does this actually look right," not as
a shipping-game reference implementation.

Not registered as a UPM "sample" in `package.json` (this repo's other
packages — Questly, HudMan, GenSettings — don't register theirs either,
their `Samples~` folders are copy-in-by-hand reference content too, same
pattern followed here) — copy this folder's contents into your project's
own `Assets/` manually, per the steps below.

## Prerequisites

- Unity **2022.3 LTS** or later, **Universal Render Pipeline** project
  (the sample material uses `MidManStudio/Alembic/InstancedPrimitive_URP`
  — a Built-in-RP project will show pink/missing-shader atoms).
- `com.unity.burst`, `com.unity.collections`, `com.unity.mathematics` —
  same requirement Alembic's own README already states.
- `com.midmanstudio.mdix` added to your project's own
  `Packages/manifest.json` **directly** (Unity won't resolve a git
  dependency declared inside another package's `package.json`):
  ```json
  "com.midmanstudio.mdix": "https://github.com/Mid-D-Man/DixScript-Rust.git?path=com.midmanstudio.mdix"
  ```
  `MidManStudio.Alembic`'s own Runtime asmdef references
  `MidManStudio.Mdix.Runtime` — without this, the whole Alembic package
  (not just this sample) fails to compile.
- Alembic itself added to your manifest, e.g.
  `"com.midmanstudio.alembic": "https://github.com/MidManStudio/unity-chem-sim.git?path=packages/com.midmanstudio.alembic"`.
  Native plugin binaries for Windows/macOS/Linux/Android/iOS/WebGL are
  already committed under `Runtime/Plugins/Native/` — no Rust build step
  needed just to run this sample in the Editor.

## Setup (~2 minutes)

1. Copy this whole `Playground` folder into your project, e.g.
   `Assets/AlembicPlayground/`.
2. In your test scene: **GameObject > Create Empty**, name it
   `AlembicPlayground`. Add three components to it:
   `AtomRenderer`, `BondRenderer`, `AlembicPlaygroundController`
   (`AngleRenderer` too, optionally). Add `AlembicPlaygroundHud` as a
   fourth — it auto-finds `AlembicPlaygroundController` on the same
   GameObject via its own `Reset()`.
3. Create a material for the atoms/bonds: **Assets > Create > Material**,
   set its shader to `MidManStudio/Alembic/InstancedPrimitive_URP`. One
   material works for both — assign it to `AtomRenderer`'s and
   `BondRenderer`'s `_material` field. (Two separate materials if you
   want independently-tunable `_LightDir`/`_AmbientLevel`.)
4. On `AlembicPlaygroundController`, drag the `AtomRenderer` and
   `BondRenderer` components (from the same GameObject) into its
   `_atomRenderer`/`_bondRenderer` fields.
5. *(Optional — the controller runs a small built-in H/O cloud with
   zero setup)* **Assets > Create > MidManStudio > Alembic > Playground
   Config**, tweak it, drag it onto the controller's `_config` field.
6. Make sure a `Camera` tagged `MainCamera` exists and can actually see
   the origin (or wherever your config's `ExplicitAtoms`/cloud center
   sits) — atoms render at real Ångström-scale positions, a few units
   across at most, so a camera sitting far away or pointed the wrong way
   sees nothing. Press Play.

## While it's running

| Key | Does |
|---|---|
| `Space` | Pause / resume stepping |
| `.` | Single-step while paused |
| `R` | Reset (fresh context, re-spawns from config) |
| `T` | Reheat (re-randomize velocities at target temperature, keeps positions) |
| `1`-`9` | Spawn the config's `QuickSpawnElements[i]` at the camera's forward point |

The on-screen panel (top-left) shows live atom/bond count, temperature,
kinetic energy, and sliders for `dt`, steps/frame, and target
temperature — all mutate a runtime copy, never the config asset itself,
so nothing gets permanently changed by testing.

## Known rough edges, worth knowing before filing a bug against Alembic itself

- **Only 20 elements have a defined color** in
  `Rendering.AlembicElementColors` (the original gameplay-doc alchemical
  set: H, He, Li, Be, B, C, N, O, P, S, Fe, Cu, Zn, As, Ag, Sn, Sb, Au,
  Hg, Pb) even though `chemistry_core`'s own element table now covers
  all 118 — anything else (including a custom-registered reagent)
  renders `AlembicElementColors.DefaultColor`, a deliberate eye-catching
  magenta, not a bug in this sample.
- `docs/getting-started.md` and `docs/architecture.md` at the repo root
  still describe Alembic's Unity side as unwritten scaffold — that's
  stale relative to `Runtime/Core`/`Adapters`/`Rendering` as they
  actually stand now (this sample is proof they work end-to-end).
  Flagging it here rather than rewriting those docs myself in the same
  pass as an unrelated sample addition.
