# Vendored copy — do not hand-edit

Re-run the **Vendor mid-arena** workflow to refresh this instead of
editing files under this directory directly.

- Source: https://github.com/Mid-D-Man/mid-engine
- Path: `crates/mid-arena`
- Ref requested: `main`
- Commit: `744118c38846e6a68d7bd994ade830aebf30cf6d`
- Synced: 2026-09-07T18:58:50Z

Vendored with the `bump` feature in mind (see chemistry_core's own
Cargo.toml [dev-dependencies] entry) — this copies the whole `src/`
tree regardless, same as mid-math/mid-collections, so `compact_slot_arena.rs`
lands here too but stays uncompiled unless a future Cargo.toml change
adds the `compact` feature.
