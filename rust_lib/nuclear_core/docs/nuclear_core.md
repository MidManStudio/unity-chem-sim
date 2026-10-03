# nuclear_core

## Overview

nuclear_core is a Rust crate for event-level nuclear physics in MidManStudio games. It works with whole nuclei, not particles. A nuclide is a pair of counts, protons and neutrons. Energies come from the AME2020 atomic mass table, and a liquid drop formula covers nuclides the table does not list. Every number carries a tag that says which of the two produced it. Decay data (half-lives, decay modes and their intensities) comes from the NUBASE2020 table, which lists the same 3,558 nuclides.

The crate sits in this workspace next to chemistry_core and shares no code with it. chemistry_core models electron clouds and chemical bonds at angstrom scale and eV energies. Nuclear reactions happen inside the nucleus, at femtometer scale and MeV energies, so the two need different data models. The only planned connection is at the gameplay layer, where an authored hazard record can name a nuclear hazard type.

Version 0.0.1 covers milestones M1 to M3: the nuclide type, mass excess and binding energy for the whole chart, Q-values for reactions written as lists of nuclides, decay (half-lives, decay outcomes as a distribution that sums to 100 percent, decay Q-values, and chains to a stable nuclide), and time evolution: a seeded random number generator, exact decay propagation over any interval, and samples that decay over time, either as whole atoms with counting noise or as fractional amounts. Fission and fusion outcomes, and the Unity layer, come in later milestones. Particle-level dynamics, and bulk neutron transport or assembly criticality, are out of scope for now because they are a different kind of simulation.

Units: energies are in keV unless a name says otherwise. Masses are atomic masses, so electron masses are inside every mass excess.

## Modules

### `lib.rs`
**What it does:** Crate root. Declares the modules, keeps the generated tables private, and re-exports the main types.

**Decisions:**
- `ame2020_data` and `nubase2020_data` are private. The rest of the crate reaches them through `mass_table` and `decay`, so the row layouts can change without touching callers.
- Energies stay in keV inside the crate. Helpers such as `QValue::mev` convert at the edge.

**Tests:** the crate-level example in the module docs (deuterium plus tritium, and the carbon-14 chain) runs as a doctest.

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
**What it does:** Generated file holding the AME2020 rows. Not edited by hand.

**Decisions:**
- Source is the AME2020 atomic mass table (Wang et al., Chinese Phys. C 45, 030003, 2021, with the input-data paper by Huang et al., Chinese Phys. C 45, 030002, 2021). The original text file is kept in the repository as `data/mass.mas20`, and `scripts/gen_ame2020.py` reads it. The script needs only the Python standard library.
- The file has 3,558 rows: every nuclide from the neutron to element 118, 1,008 of them marked estimated. `estimated` means the file writes a `#` in place of the decimal point, which AME2020 uses for values taken from systematic trends instead of measurement.
- The neutron and hydrogen-1 mass excesses come straight from their rows (8071.31806 keV and 7288.971064 keV).
- The mass excess of carbon-12 is exactly zero, which is how the atomic mass unit is defined, and the generated file reproduces that.
- Every row is checked by the script: A equals N plus Z, and the element symbol matches the symbol list in `elements.rs`.
- For all rows the mass excess column agrees with the atomic mass column of the same file to 1.4 eV for measured rows and to under 1 keV for estimated rows (the estimated rows are rounded to the stated uncertainty).

**Tests:** `tests/chart_sweep.rs` pins the row counts, so regenerating from a different file fails loudly until the counts are updated on purpose. `tests/known_values.rs` compares binding energies and Q-values against published numbers. The `data` job in CI regenerates the file and compares it with the checked-in copy.

### `liquid_drop.rs`
**What it does:** Semi-empirical binding energy (volume, surface, Coulomb, asymmetry and pairing terms) as a fallback for nuclides the table does not list.

**Decisions:**
- The five coefficients are a common textbook set. Nothing was fitted to AME2020, so the accuracy numbers below are out-of-sample numbers.
- The table always wins where it has a row. The formula is much less accurate: against measured rows the mean error is about 70 keV per nucleon for A from 41 to 80, and about 43 keV per nucleon for A from 181 to 300. Below A = 20 it is poor (worst case above 6 MeV per nucleon), and it means nothing for A under 5.
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

### `decay.rs`
**What it does:** Half-lives, decay modes and decay outcomes from NUBASE2020. `half_life` and `isotopic_abundance_percent` look up one nuclide. `branches` returns the raw mode rows. `outcomes` turns them into a distribution of results that sums to 100 percent, most likely first, each with its daughter and emitted nuclei. `decay_q_value` gives the energy of a mode from the AME2020 masses. `chain_to_stability` follows the most likely outcome until it reaches a stable nuclide.

**Decisions:**
- Intensities in NUBASE2020 are per 100 decays of the parent, and a delayed emission such as `B-n` is a part of the `B-` intensity, not an addition to it. So the plain beta share is the beta intensity minus its delayed emissions. Helium-8 lists `B-=100`, `B-n=16` and `B-t=0.9`, which gives 83.1, 16 and 0.9 percent. Lithium-8 lists `B-=100` and `B-A=100`, so no plain beta share is left.
- `B+` in NUBASE2020 means beta-plus decay including electron capture. The `EC` and `e+` rows only split a `B+` row into its two parts, so they are skipped when a `B+` row exists, and counted when it does not.
- Rows with a limit (`<`) carry no usable number and are ignored. Rows with a value or `~` or `>` are used as written, and the shares are scaled to sum to 100. NUBASE2020 only normalizes when all competing modes were measured, so the raw sum can be a little off.
- Nuclides with no measured intensity fall back in two steps. Rows marked as observed with unknown intensity (`=?`) share 100 percent equally. If there are none, the first primary mode marked as possible (`?`, energetically allowed but not observed) takes 100 percent. These outcomes carry `assumed = true`. This covers 475 nuclides, mostly very neutron-rich or proton-rich ones, such as lithium-3 emitting a proton.
- Q-values are the mass excess of the parent minus those of the daughter and the emitted nuclei. For `BetaPlus`, `ElectronCapture` and the delayed beta-plus modes that is the electron-capture energy, and a positron can only come out when it exceeds twice the electron rest energy (1021.998 keV). `PositronEmission` has that amount taken off. Modes with no single daughter (spontaneous fission, delayed fission and the two-cluster mixture) have no Q-value.
- A chain follows only the residual nucleus. Emitted nuclei are listed in each step and are not decayed further.
- A chain stops at a stable nuclide, at a mode with no single daughter (fission), at a nuclide without usable decay data, or when the daughter is not in the table. A repeated nuclide and a length above 500 steps are also caught, though neither happens in the data.
- The year in half-lives is the tropical year (31,556,926 s), the value NUBASE2020 uses.

**Tests:** unit tests in the file cover the mode table (mass number and charge conserved for every mode), labels, families, and the outcome rules on named nuclides. `tests/decay_known.rs` and `tests/decay_sweep.rs` cover the published values and the whole chart.

### `nubase2020_data.rs`
**What it does:** Generated file holding the NUBASE2020 ground-state rows: half-life, isotopic abundance, and the list of decay modes with intensities. Not edited by hand.

**Decisions:**
- Source is the NUBASE2020 evaluation (Kondev et al., Chinese Phys. C 45, 030001, 2021). The original text file is kept in the repository as `data/nubase_1.mas20`, and `scripts/gen_nubase2020.py` reads it, using only the Python standard library.
- Only ground states are kept (3,558 rows, the same nuclides as the AME2020 table). Isomers are not modelled. The script checks that the two tables list the same nuclides, and that every mass excess in NUBASE2020 agrees with AME2020 to NUBASE2020's own rounding.
- Half-lives are stored in seconds with a state: exact, approximate (`~`), lower limit (`>`), upper limit (`<`), stable, particle-unstable (no half-life given) or unknown. 253 nuclides are stable, 46 have no half-life, and 3 are particle-unstable.
- All 5,784 mode rows are kept, including the ones marked possible. The mode names in the file map to the `DecayMode` enum through a table in the script, and the script stops with an error on any name it does not know.
- The file spells a few things in irregular ways: two possible modes with no semicolon between them (`B+p ? 2p ?`) and `=<` for an upper limit. The script handles these.
- The file gives an isotopic abundance of 8.84 percent for tellurium-126, which makes tellurium sum to 90 percent. The published value is 18.84 percent (the tellurium abundances then sum to 100). The script corrects it, and only while the file still holds the wrong value.

**Tests:** `tests/decay_sweep.rs` pins the counts and checks that abundances sum to 100 percent for every element. The `data` job in CI regenerates the file and compares it with the checked-in copy.

### `rng.rs`
**What it does:** A seeded random number generator, `Rng`, with no dependencies and no global state. It gives uniform integers and floats, a normal variate, a binomial sampler and a multinomial sampler. The state is four `u64` words that can be saved and restored.

**Decisions:**
- The generator is xoshiro256++, seeded by running SplitMix64 on the seed. Five seeds are tested against the output of the `rand_xoshiro` crate, so the stream is the standard one.
- Every simulation step takes the generator as an argument. Nothing draws from a global, so a run repeats exactly from its seed, and a save file only needs the four state words.
- `binomial` picks a method by size. Below a mean of 10 it walks the probability table, which is exact for any n. From a mean of 10 up to n = 2^40 it uses transformed rejection with squeeze (Hormann 1993), which is exact. Above n = 2^40 with a larger mean it falls back to a normal approximation, because the log-factorial differences the exact method relies on lose their precision at that size. A pile that large should use `Amounts`.
- `multinomial` draws one binomial per category from the remaining count and the remaining probability weight, and gives the last category the remainder. The weights are summed from the end so the tails stay accurate, and they need not sum to 1.

**Tests:** unit tests cover the reference streams, state save and restore, the ranges of the float and bounded-integer methods, log-factorial values, the edge cases of `binomial`, mean and variance for six (n, p) cases that reach every method, and the totals and weights of `multinomial`. The sampler was also checked once, outside the test suite, against `scipy.stats.binom` with chi-square tests on 200,000 draws for nine cases. The p-values ran from 0.10 to 0.93.

### `matrix.rs`
**What it does:** `expm` and `expm_minus_identity`, the matrix exponential of a small dense square matrix held as a row-major `Vec<f64>`.

**Decisions:**
- The algorithm is scaling and squaring with a degree-13 Pade approximant (Higham 2005), which stays stable when the entries of a decay generator span twenty orders of magnitude.
- The decay code uses `expm_minus_identity`, which returns `exp(A) - I`. A slow decay is a probability like 1e-24, and plain `exp(A)` rounds it into the neighbouring entry of 1. Working with `E = exp(A) - I` avoids that: the Pade step gives `E = 2 (V - U)^-1 U` with no subtraction, and each squaring uses `exp(2A) - I = E^2 + 2E`, which never adds 1 to a small number.
- A matrix with a non-finite entry returns NaN, and a wrong length returns an empty vector, instead of panicking.

**Tests:** unit tests cover the zero, diagonal and nilpotent cases, the two-member and three-member Bateman solutions, a generator with rates from 1e-12 to 1e12 per second (the 1e-6 slow entry keeps a relative error under 1e-12), and bad input. Sixty random stiff generators (3 to 28 states, rates from 1e-18 to 1e5 per second, intervals up to 1e12 seconds) were also compared once, outside the test suite, with 80-digit `mpmath`. The largest absolute error was 5.6e-16, and entries above 1e-30 had a relative error under 6.3e-9.

### `lineage.rs`
**What it does:** Exact decay propagation for one atom over any interval. `Propagator::transition(nuclide, seconds)` returns every state the atom can be in after the interval, with its probability and the energy released on the way. It costs the same for one second and for 1e30 seconds.

**Decisions:**
- A state is the residual nucleus together with the nuclei emitted so far. Two routes to the same residual stay distinct when they threw off different particles, which matters for delayed neutron emitters and rare cluster decays. A fission state keeps the nucleus that fissioned and is absorbing until fission products are modelled.
- The graph of all states reachable from a nuclide is built from `decay::outcomes`, with a branch rate of `ln 2 / half-life * share`. The rates go into a generator matrix `Q`, and the probabilities are row 0 of `exp(Q t)`. Atoms are independent, so this is exact: there is no time step, and no limit on the number of generations.
- Graphs are small. Over the whole chart the median has 6 states, the mean 10.3, the 95th percentile 39, and the largest has 79 (gold-170). One transition takes about 12 microseconds for krypton-92 (9 states), 0.4 milliseconds for uranium-238 (40 states) and 1.6 milliseconds for the largest graph.
- Particle-unstable nuclides and nuclides with no known half-life get a half-life of 1e-21 seconds, a nuclear timescale, so they decay at once.
- The energy of a state is the mass excess of the starting nuclide minus those of the residual and the emitted nuclei. It telescopes along any path, so it needs no per-decay bookkeeping. For the uranium series it comes to 51.695 MeV, the textbook total. It is the full Q, with neutrinos included.
- `Propagator` caches graphs per nuclide and transitions per (nuclide, interval). It keeps up to 4096 transitions and drops them all when full. There is no global cache. Outcomes with a probability under 1e-16 are dropped and the rest are rescaled.

**Tests:** unit tests cover stable nuclides, zero time, one half-life of carbon-14, the strontium-90 chain against the Bateman solution at four intervals, the uranium series ending in lead-206 with eight alphas and 51.695 MeV, mass-number conservation, fission states, caching, input checks and the cache limit.

### `sample.rs`
**What it does:** `Sample` holds whole-number atom counts and advances them with exact multinomial draws, so it shows real counting noise. `Amounts` holds fractional amounts and advances them with the exact expectation, for piles too big to count. Both use a `Propagator`, so one step of any length takes one lookup per nuclide.

**Decisions:**
- A step draws, for each nuclide, how its atoms spread over the states of its transition. Nuclides are processed in ascending order, so the same seed always gives the same result. A failed step leaves the sample unchanged.
- Emitted nuclei (alphas, neutrons, protons) join the pile at the end of the step that produced them, so an unstable emitted nucleus starts to decay on the next step. Use shorter steps when that matters.
- Atoms that end in a mode with no single daughter (spontaneous fission and delayed fission) move to a pending list. They still count in the nucleon total, until fission products are modelled.
- Nucleons are conserved exactly in `Sample`, which uses integer arithmetic, and to rounding error in `Amounts`.
- Counts are `u64`, and a count that would pass `u64::MAX` is an error. The binomial draws are exact up to 2^40 atoms per nuclide and approximate above that, so a larger pile should use `Amounts`.
- Each step reports the atoms changed, the energy released in keV (total Q), the new pending fissions, and whether every mass was known.

**Tests:** unit tests cover nucleon conservation over six steps from 1 second to 1e30 seconds, determinism by seed, the counting noise of a half-life step of carbon-14, alphas joining the pile, failed steps leaving the pile unchanged, and exact amounts for carbon-14 and californium-252.

### `tests/known_values.rs`
**What it does:** Checks the public API against published numbers: binding energies of light nuclei, binding energy per nucleon for iron-56, nickel-62, lead-208 and both uranium isotopes, and the Q-values listed under `reaction.rs`.

**Decisions:** Expected values were checked against published figures to the precision those figures give, and the remaining digits were taken from the generated table. The tests catch regressions and conversion mistakes. They cannot catch an error that is already in the source table. Tolerances are 0.05 keV for binding energies, 0.1 keV for the fusion Q-values and 0.1 MeV for the fission Q-value.

### `tests/chart_sweep.rs`
**What it does:** Whole-chart checks: table order and uniqueness, golden row counts, a text round trip for every row, nickel-62 as the most bound nucleus, source tags, the liquid drop accuracy bands, and the valley of stability check.

**Decisions:** The golden counts (3,558 rows, 1,008 estimated) must be updated together with this file when `scripts/gen_ame2020.py` is rerun against a different file.

### `tests/decay_known.rs`
**What it does:** Checks decay data against published figures: the four decay series (uranium, thorium, actinium and neptunium) nuclide by nuclide, fission-product and medical-isotope chains, fifteen half-lives, fourteen decay energies, branching shares for nuclides that split, and abundances of familiar isotopes.

**Decisions:** Expected values are typed in by hand from standard references, with a relative tolerance of 1 percent on half-lives. The neptunium series ends in thallium-205 because NUBASE2020 lists bismuth-209 as radioactive. The abundance test lists only isotopes where NUBASE2020 and the current IUPAC table agree. NUBASE2020 gives carbon-12 as 98.94 percent where IUPAC has 98.93, and deuterium as 0.0145 percent where IUPAC has 0.0115, so those are left out.

### `tests/decay_sweep.rs`
**What it does:** Whole-chart checks for decay: the decay table and the mass table list the same nuclides, golden counts, every unstable nuclide has outcomes that sum to 100 percent and are sorted, every outcome conserves mass number and charge, the main decay of every fully measured nuclide releases energy, every chain ends in a known way, and abundances sum to 100 percent per element.

**Decisions:** The check that the main decay releases energy compares two independent evaluations (AME2020 masses against NUBASE2020 decay modes) for the 2,251 main decays whose masses are all measured, and finds no exception. That also checks every entry of the mode table. A further 525 main decays involve an estimated mass and are not held to the check. The chain endings are pinned: 3,365 stable, 175 spontaneous fission, 1 beta-plus fission, 17 where the daughter is not in the table (lithium-3, and 16 superheavy nuclides whose alpha chains end at dubnium-272, dubnium-273, seaborgium-275 or seaborgium-276). The longest chain has 25 steps. These counts must be updated together with this file when `scripts/gen_nubase2020.py` is rerun against a different file.

### `tests/lineage_sweep.rs`
**What it does:** Whole-chart checks for the propagator. Every nuclide has a finite graph, with 253 one-state graphs (the stable nuclides) and a largest graph of 79 states (gold-170). Transitions for every 11th nuclide at 1 second, 1e6 seconds and 1e12 seconds are probability distributions that conserve mass number. After 1e60 seconds every atom of every 53rd nuclide has stopped decaying. A transition does not depend on what was cached before.

**Decisions:** The golden numbers must be updated together with this file when `scripts/gen_nubase2020.py` is rerun against a different file. The strides keep the debug-mode run near two seconds.

### `tests/sample_stats.rs`
**What it does:** Counting statistics and closed-form checks. Strontium-90 chain counts stay within five standard deviations of the Bateman expectation at 5, 20 and 80 years. Radon-222 growth from radium-226 matches the exact two-member formula at four times, and reaches secular equilibrium. A uranium-238 sample releases 51.695 MeV per atom. Many small steps agree with one big step. A saved generator state continues the same run.

**Decisions:** The fixed seeds make the outcomes repeatable, and the five-sigma limits are the ones the counting statistics allow.

### `benches/chart_bench.rs`
**What it does:** A timing harness with about fifty benchmarks in seven groups, printed as one Markdown table per group. The groups are nuclides and the mass table, binding energies and Q-values, decay data, random numbers, the matrix exponential, decay propagation, and samples. Benchmarks that sweep the chart report time per row or per nuclide.

**Decisions:**
- No criterion. A dev-dependency is built for every `cargo test`. In a trial run, `cargo test` on Rust 1.75.0 failed with criterion 0.5 added, because `clap_lex` 1.1.1 in its dependency tree needs the `edition2024` Cargo feature and cargo 1.75 cannot read that manifest. The harness uses `std::time::Instant` and `std::hint::black_box` instead, which keeps the zero-dependency build and the 1.75 floor.
- Each benchmark first finds how many calls fill a 25 ms batch, runs one untimed batch, then times 21 batches and reports the median with the minimum and maximum.
- The propagation group separates three costs: building a lineage graph, solving it for a new interval, and a fully cached lookup. A game that steps by a fixed interval pays only the lookup, so the difference between them is the reason the cache exists.
- The matrix group runs a stiff decay chain over 1e9 seconds at 10, 40 and 80 states, which brackets the graph sizes the chart produces.
- The whole run takes under a minute on a development machine. Progress goes to stderr and the tables go to stdout, so the CI job can capture only the tables.
- Run it with `cargo bench -p nuclear_core --bench chart_bench`. The `--bench` flag matters: without it cargo also runs the library's unit tests in bench mode and prints their output ahead of the tables.
- Results from shared CI runners show a big change between two runs and prove nothing about absolute speed. Values under about 5 ns are mostly loop overhead.

**Tests:** none of its own. The `test` job builds all targets, so the harness cannot stop compiling unnoticed.

## CI and Workflows

- `.github/workflows/nuclear-core.yml` - tests and benchmarks for this crate alone. The `test` job builds all targets, runs `cargo test -p nuclear_core`, and builds the docs with broken links denied, all on stable Rust. The `floor` job copies the crate into a workspace of its own and runs its tests on Rust 1.75.0, the `rust-version` in `Cargo.toml`. Copying it out means no other crate's dependencies are resolved. The `data` job regenerates both generated tables from the files in `data/` and fails if either differs from the checked-in copy. The `bench` job runs only from a manual run with the `bench` box ticked, and writes the results table to the run's Job Summary. The workflow also runs on pushes that touch `rust_lib/nuclear_core/**`.
- `.github/workflows/rust-rust-ci.yml` - the existing workspace CI. It builds the whole workspace with `--all-targets` and runs `cargo test --workspace` on stable Rust, so it covers nuclear_core with no change. It runs on pushes and pull requests that touch `rust_lib/**` or the root `Cargo.toml`.
- `.github/workflows/build-rust-lib.yml` - builds the native libraries for Unity. It builds chemistry_core only, and nuclear_core has no FFI layer yet, so it is not part of that build. Its trigger path is `rust_lib/**`, so a push that touches this crate also starts that build.
- `scripts/gen_ame2020.py` and `scripts/gen_nubase2020.py` - regenerate `src/ame2020_data.rs` and `src/nubase2020_data.rs` from the original text files in `rust_lib/nuclear_core/data/`. Run them from the repository root with `python scripts/gen_ame2020.py` and `python scripts/gen_nubase2020.py`. Both need only Python 3. After a regeneration, update the golden counts in `tests/chart_sweep.rs` and `tests/decay_sweep.rs` and the numbers in this file.

The workspace as a whole needs Rust 1.83 or newer because of the vendored mid-math. The `floor` job checks this crate only.

## Fixes and Problems

### `.github/workflows/nuclear-core.yml`
- `cargo test --workspace` stops at the first failing doc-test. In a local run on Rust 1.85, five doc-tests in mid-math fail, so a workspace run ends before nuclear_core's own doc-test. The workflow gives this crate a result of its own.

### `ame2020_data.rs`
- The first version of this table was built from the rounded AME2020 masses in the `periodictable` package. That copy had 2,940 rows, 618 fewer than the full table, and it rounded each mass to its stated uncertainty, which moved about 1,300 values by more than 0.1 keV. Every difference was under 0.34 times the stated uncertainty. It was replaced by a table generated from the original file.
- The data files are the copies bundled in the `nuclearmasses` 0.5.0 package on PyPI: `mass.mas20` (the AME2020 release dated 3 March 2021) and `nubase_1.mas20`. The Atomic Mass Data Center now names its current files `mass_1.mas20` and `nubase_4.mas20`, which are later revisions. The build sandbox could not reach the IAEA host, so the files were not compared with the current revisions and the size of any differences is not known. To refresh, download the current files into `data/`, rerun both scripts, and update the golden counts.

### `nubase2020_data.rs`
- Isomers are not modelled. A nuclide is its ground state, and where NUBASE2020 lists a long-lived isomer as the dominant decaying state, the ground-state row is used.
- NUBASE2020 abundances can differ from the current IUPAC table in the last digits (carbon-12, deuterium). The values are used as the file gives them, apart from the tellurium-126 correction.
- 475 nuclides have an assumed outcome, because NUBASE2020 gives no measured intensity for any of their modes.

### `decay.rs`
- `BetaPlus` Q-values are electron-capture energies. Whether a positron can come out is a separate question of whether Q exceeds 1021.998 keV, which this module does not decide for the caller.
- Chains do not follow emitted nuclei. A neutron or triton emitted along the way is listed and stays as it is.

### `rng.rs`
- `binomial` is approximate above n = 2^40 when the mean is 10 or more. Use `Amounts` for piles that large.

### `lineage.rs`
- Emitted nuclei do not decay inside the step that emits them. A neutron emitted early in a long step stays a neutron until the next step.
- Rare cluster decays (neon-24 or magnesium-28 emission by uranium-234, for example) end in the same residual as the main route with a different set of emitted nuclei, so the uranium-238 propagation has more than one lead-206 state. Take the most probable one when only one is needed.
- A graph of 80 states takes about 5 milliseconds to exponentiate, so the first step over a large and unusual sample costs a few milliseconds per species until the cache is warm.

### `sample.rs`
- Pending fissions are counted but not resolved. A fissioning atom leaves the pile for the pending list, and fission products arrive in a later milestone.
- `Sample` counts above 2^40 atoms for one nuclide use the approximate binomial.

### `liquid_drop.rs`
- Worst-case error is above 6 MeV per nucleon below A = 20, so anything labelled `LiquidDrop` should not be trusted for light nuclides.

### `Cargo.toml`
- The root manifest has no `[workspace.lints]` table, so the lints (`unsafe_code = "deny"`, `missing_docs = "warn"`) are declared in the crate manifest instead of inherited.
