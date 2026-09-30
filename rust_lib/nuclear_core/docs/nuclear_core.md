# nuclear_core

## Overview

nuclear_core is a Rust crate for event-level nuclear physics in MidManStudio games. It works with whole nuclei, not particles. A nuclide is a pair of counts, protons and neutrons. Energies come from the AME2020 atomic mass table, and a liquid drop formula covers nuclides the table does not list. Every number carries a tag that says which of the two produced it.

The crate sits in this workspace next to chemistry_core and shares no code with it. chemistry_core models electron clouds and chemical bonds at angstrom scale and eV energies. Nuclear reactions happen inside the nucleus, at femtometer scale and MeV energies, so the two need different data models. The only planned connection is at the gameplay layer, where an authored hazard record can name a nuclear hazard type.

Version 0.0.1 is milestone M1: the nuclide type, mass excess and binding energy for the whole known chart, and Q-values for reactions written as lists of nuclides. Decay, time evolution of a sample, fission and fusion outcomes, and the Unity layer come in later milestones. Particle-level dynamics, and bulk neutron transport or assembly criticality, are out of scope for now because they are a different kind of simulation.

Units: energies are in keV unless a name says otherwise. Masses are atomic masses, so electron masses are inside every mass excess.

## Modules

### `lib.rs`
**What it does:** Crate root. Declares the modules, keeps the generated table private, and re-exports the main types.

**Decisions:**
- `ame2020_data` is private. The rest of the crate reaches the table through `mass_table`, so the row layout can change without touching callers.
- Energies stay in keV inside the crate. Helpers such as `QValue::mev` convert at the edge.

**Tests:** the crate-level example in the module docs (deuterium plus tritium) runs as a doctest.

### `nuclide.rs`
**What it does:** The `Nuclide` type, a nucleus identified by proton count `z` and neutron count `n`, with named constants for the free neutron and the light nuclei that show up in fusion.

**Decisions:**
- Two `u16` counts instead of `(z, a)`. Mass number is derived, and the neutron is `(0, 1)`.
- `H1` stands in for the free proton. All masses are atomic, so a proton in a reaction is written as a hydrogen-1 atom and the electron count balances.
- Fields are private and `new` is a `const fn`, so the representation can change later (an isomer index, for example) without breaking callers.
- `key` packs a nuclide into one `u32`, z in the high half, for FFI and hash keys.
- No energy-level data. AME2020 lists ground states, so isomers of one (z, n) pair are one `Nuclide`.
- `parse` is an inherent method that returns `Option`. A `FromStr` impl would need an error type that nothing uses yet.

**Tests:** unit tests in the file cover counts, `from_za`, the key round trip, and display and parse. `tests/chart_sweep.rs` round-trips every row of the table through text.

### `elements.rs`
**What it does:** Element symbols for atomic numbers 1 to 118, with lookup in both directions.

**Decisions:**
- A plain array indexed by `z - 1`. The reverse lookup is a linear scan over 118 entries, which is fine because it only runs when parsing text.
- Symbols are case sensitive.

**Tests:** unit tests check known symbols, the bounds, and a round trip for every element.

### `mass_table.rs`
**What it does:** Lookup over the generated AME2020 rows. `lookup` finds one nuclide by binary search, `entries` walks the whole table, and two constants hold the mass excess of hydrogen-1 and the neutron.

**Decisions:**
- Rows are `(u8, u16, f64, f64, bool)` tuples in one static array, sorted by `(z, a)`. Binary search needs that order, and a test enforces it.
- Mass excess is atomic, in keV. Binding energy is built from it in `binding.rs`.
- `MassEntry::estimated` keeps the AME2020 flag for values taken from systematic trends instead of measurement.

**Tests:** unit tests for hits, misses and the constants. `tests/chart_sweep.rs` checks ordering, uniqueness and lookup for every row.

### `ame2020_data.rs`
**What it does:** Generated file holding the rows. Not edited by hand.

**Decisions:**
- Source is the AME2020 atomic mass evaluation (Wang et al., Chinese Phys. C 45, 030003, 2021, with the input-data paper by Huang et al., Chinese Phys. C 45, 030002, 2021). Rows come from the public-domain `periodictable` package, version 2.1.0, which carries the IAEA massround.mas20 table. The build sandbox could not reach the IAEA host, so this is a second-hand copy.
- `scripts/gen_ame2020.py` turns atomic mass in u into mass excess in keV with decimal arithmetic, using 931494.10242 keV per u, so no float rounding enters the conversion.
- The file has 2,940 rows: 2,939 nuclides with Z from 1 to 118, plus the neutron. 453 rows are marked estimated.
- The neutron row uses the CODATA neutron mass that `periodictable` ships. It is about 1 eV away from the AME2020 neutron value, which does not matter at keV precision.
- The mass excess of carbon-12 is exactly zero, which is how the atomic mass unit is defined, and the generated file reproduces that.

**Tests:** `tests/chart_sweep.rs` pins the row counts, so regenerating from a different table fails loudly until the counts are updated on purpose. `tests/known_values.rs` compares binding energies and Q-values against published numbers.

### `liquid_drop.rs`
**What it does:** Semi-empirical binding energy (volume, surface, Coulomb, asymmetry and pairing terms) as a fallback for nuclides the table does not list.

**Decisions:**
- The five coefficients are a common textbook set. Nothing was fitted to AME2020, so the accuracy numbers below are out-of-sample numbers.
- The table always wins where it has a row. The formula is much less accurate: against measured rows the mean error is about 70 keV per nucleon for A from 41 to 80, and about 42 keV per nucleon for A from 181 to 300. Below A = 20 it is poor (worst case above 6 MeV per nucleon), and it means nothing for A under 5.
- Near stability it cannot be trusted for decay energies. For carbon-14 and potassium-40 it gives a negative beta-minus Q-value where the real one is positive, so both would look stable. This is why the project uses the full table instead of the formula plus a short list of measured overrides.
- Along the valley of stability it lands within two protons of the table's most bound Z at every mass number from 40 to 250, and exactly on it for 150 of 211. That is enough for a fallback to lean the right way.

**Tests:** unit tests for sanity and pairing. `tests/chart_sweep.rs` pins the accuracy bands and the valley check, so a coefficient change is noticed.

### `binding.rs`
**What it does:** `mass_excess` and `binding_energy` for any nuclide, each returned with a `Source` tag: `Measured`, `Estimated`, or `LiquidDrop`.

**Decisions:**
- Binding energy from the table is `z * dH + n * dn - d`, all mass excesses in keV, where `dH` and `dn` are the mass excesses of hydrogen-1 and the neutron.
- Sources are ordered so the larger value is the weaker one. Code that combines several numbers takes the maximum, so a result is never labelled better than its worst input.
- The functions return `None` only when neither the table nor the formula applies: no protons, or no neutrons outside the table.
- A negative binding energy means the nucleus is unbound in the model. It is returned as is instead of being clamped, so Q-value arithmetic stays consistent.

**Tests:** unit tests for free nucleons, source ordering and fallback consistency. `tests/known_values.rs` covers light nuclei and per-nucleon anchors. `tests/chart_sweep.rs` checks that nickel-62 has the highest binding energy per nucleon in the whole table, a known fact that only holds if the table and the conversion are both right.

### `reaction.rs`
**What it does:** `q_value` for a reaction written as two lists of nuclides.

**Decisions:**
- Q is mass excess in minus mass excess out, so a positive Q releases energy.
- Both sides must conserve mass number and proton count. Atomic masses carry their own electrons, so a balanced reaction needs no electron bookkeeping. Beta decay and electron capture change the proton count and need lepton handling, so they get their own module in the decay milestone.
- The result carries the weakest source of any participant.
- Errors are a small enum with `Display` and `std::error::Error`, so callers can tell a bad reaction from missing data.

**Tests:** unit tests for D+T and the error cases. `tests/known_values.rs` checks five fusion Q-values and one fission channel (U-235 plus a neutron giving Ba-141, Kr-92 and three neutrons, about 173 MeV), and checks that a participant outside the table downgrades the source.

### `tests/known_values.rs`
**What it does:** Checks the public API against published numbers: binding energies of light nuclei, binding energy per nucleon for iron-56, nickel-62, lead-208 and both uranium isotopes, and the Q-values listed under `reaction.rs`.

**Decisions:** Expected values were checked against published figures to the precision those figures give, and the remaining digits were taken from the generated table. The tests catch regressions and conversion mistakes. They cannot catch an error that is already in the source table. Tolerances are 0.05 keV for binding energies, 0.1 keV for the fusion Q-values and 0.1 MeV for the fission Q-value.

### `tests/chart_sweep.rs`
**What it does:** Whole-chart checks: table order and uniqueness, golden row counts, a text round trip for every row, nickel-62 as the most bound nucleus, source tags, the liquid drop accuracy bands, and the valley of stability check.

**Decisions:** The golden counts (2,940 rows, 453 estimated) must be updated together with this file when `scripts/gen_ame2020.py` is rerun against a different table.

### `benches/chart_bench.rs`
**What it does:** A timing harness for the hot paths: table lookup (listed, unlisted, and every row in turn), `binding_energy` (table row and fallback), the liquid drop formula, `q_value` for D+T and for a fission channel, `Nuclide` parse and display, and a sweep of binding energy per nucleon over the whole chart. It prints a Markdown table of median, minimum and maximum time per operation.

**Decisions:**
- No criterion. A dev-dependency is built for every `cargo test`. In a trial run, `cargo test` on Rust 1.75.0 failed with criterion 0.5 added, because `clap_lex` 1.1.1 in its dependency tree needs the `edition2024` Cargo feature and cargo 1.75 cannot read that manifest. The harness uses `std::time::Instant` and `std::hint::black_box` instead, which keeps the zero-dependency build and the 1.75 floor.
- Each benchmark first finds how many calls fill a 25 ms batch, runs one untimed batch, then times 21 batches and reports the median with the minimum and maximum. Benchmarks that loop over the chart report time per row.
- Run it with `cargo bench -p nuclear_core --bench chart_bench`. The `--bench` flag matters: without it cargo also runs the library's unit tests in bench mode and prints their output ahead of the table.
- Results from shared CI runners show a big change between two runs and prove nothing about absolute speed. Values under about 5 ns are mostly loop overhead.

**Tests:** none of its own. The `test` job builds all targets, so the harness cannot stop compiling unnoticed.

## CI and Workflows

- `.github/workflows/nuclear-core.yml` - tests and benchmarks for this crate alone. The `test` job builds all targets, runs `cargo test -p nuclear_core`, and builds the docs with broken links denied, all on stable Rust. The `floor` job copies the crate into a workspace of its own and runs its tests on Rust 1.75.0, the `rust-version` in `Cargo.toml`. Copying it out means no other crate's dependencies are resolved. The `bench` job runs only from a manual run with the `bench` box ticked, and writes the results table to the run's Job Summary. The workflow also runs on pushes that touch `rust_lib/nuclear_core/**`.
- `.github/workflows/rust-rust-ci.yml` - the existing workspace CI. It builds the whole workspace with `--all-targets` and runs `cargo test --workspace` on stable Rust, so it covers nuclear_core with no change. It runs on pushes and pull requests that touch `rust_lib/**` or the root `Cargo.toml`.
- `.github/workflows/build-rust-lib.yml` - builds the native libraries for Unity. It builds chemistry_core only, and nuclear_core has no FFI layer yet, so it is not part of that build. Its trigger path is `rust_lib/**`, so a push that touches this crate also starts that build.
- `scripts/gen_ame2020.py` - regenerates `src/ame2020_data.rs`. Needs `pip install periodictable`. Run it locally or in a scratch workflow, then update the golden counts in `tests/chart_sweep.rs` if the row counts changed.

The workspace as a whole needs Rust 1.83 or newer because of the vendored mid-math. The `floor` job checks this crate only.

## Fixes and Problems

### `.github/workflows/nuclear-core.yml`
- `cargo test --workspace` stops at the first failing doc-test. In a local run on Rust 1.85, five doc-tests in mid-math fail, so a workspace run ends before nuclear_core's own doc-test. The workflow gives this crate a result of its own.

### `ame2020_data.rs`
- The rows are a second-hand copy of AME2020 (through `periodictable` 2.1.0). They have not been diffed against the primary IAEA file, which the build sandbox could not reach, so the row count may be below the full AME2020 listing. A CI job that downloads the primary file and compares it with the checked-in table would close this.
- The neutron row differs from the AME2020 neutron value by about 1 eV.

### `liquid_drop.rs`
- Worst-case error is above 6 MeV per nucleon below A = 20, so anything labelled `LiquidDrop` should not be trusted for light nuclides.

### `Cargo.toml`
- The root manifest has no `[workspace.lints]` table, so the lints (`unsafe_code = "deny"`, `missing_docs = "warn"`) are declared in the crate manifest instead of inherited.
