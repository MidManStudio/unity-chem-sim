# Changelog

All notable changes to this package are documented here.
Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [0.1.0] - Unreleased

### Added
- Package skeleton: `Runtime`, `Editor`, `Tests/Runtime`, `Tests/Editor`,
  `Documentation~`, assembly definitions, native plugin folder layout.
- `Samples~/Playground` — a drop-in Play Mode test scene (config
  ScriptableObject, sim-driving controller, on-screen HUD) for watching
  the full spawn/step/render loop and tweaking it live. See its own
  README for setup.

### Notes
- No simulation logic yet — `Runtime/Core` and `Runtime/Adapters` are
  placeholders pending the Rust core.
  **Stale as of the Playground sample above** — `Runtime/Core`,
  `Runtime/Adapters`, and `Runtime/Rendering` are all implemented now
  (see `README.md`'s Status section). Left this line as-is rather than
  rewriting history for work this pass didn't do; flagging it here so
  it doesn't get taken at face value.
