// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "benches/chart_bench.rs"
// ============================================================================
//! Timing harness for the hot paths of nuclear_core.
//!
//! No dependencies: it uses `std::time::Instant` and `std::hint::black_box`, so
//! the crate keeps a zero-dependency test build. Run it with
//! `cargo bench -p nuclear_core --bench chart_bench`. The result is a set of
//! Markdown tables on stdout, one per group; progress goes to stderr.

use std::hint::black_box;
use std::time::{Duration, Instant};

use nuclear_core::decay::{self, DecayMode};
use nuclear_core::elements;
use nuclear_core::lineage::Propagator;
use nuclear_core::liquid_drop;
use nuclear_core::mass_table;
use nuclear_core::matrix;
use nuclear_core::{
    binding_energy, chain_to_stability, decay_q_value, mass_excess, outcomes, q_value, Amounts,
    Nuclide, Rng, Sample,
};

/// Timed batches per benchmark. The median of these is the reported number.
const SAMPLES: usize = 21;

/// Target length of one timed batch.
const BATCH: Duration = Duration::from_millis(25);

struct Row {
    name: &'static str,
    median_ns: f64,
    min_ns: f64,
    max_ns: f64,
}

struct Group {
    title: &'static str,
    rows: Vec<Row>,
}

/// Times `work`. Each call counts as `ops_per_call` operations, so a benchmark
/// that loops over the whole chart reports time per row.
fn measure<R>(name: &'static str, ops_per_call: u64, mut work: impl FnMut() -> R) -> Row {
    // Double the call count until one batch takes a quarter of the target,
    // then scale it so a batch takes about the target.
    let mut calls: u64 = 1;
    let calibration = loop {
        let start = Instant::now();
        for _ in 0..calls {
            black_box(work());
        }
        let elapsed = start.elapsed();
        if elapsed >= BATCH / 4 || calls >= 1 << 40 {
            break elapsed;
        }
        calls *= 2;
    };
    let scale = BATCH.as_secs_f64() / calibration.as_secs_f64().max(1e-9);
    let calls = ((calls as f64 * scale).ceil() as u64).max(1);

    // One untimed batch warms caches and branch predictors.
    for _ in 0..calls {
        black_box(work());
    }

    let mut per_op: Vec<f64> = (0..SAMPLES)
        .map(|_| {
            let start = Instant::now();
            for _ in 0..calls {
                black_box(work());
            }
            start.elapsed().as_nanos() as f64 / (calls * ops_per_call) as f64
        })
        .collect();
    per_op.sort_by(f64::total_cmp);
    Row {
        name,
        median_ns: per_op[SAMPLES / 2],
        min_ns: per_op[0],
        max_ns: per_op[SAMPLES - 1],
    }
}

fn format_time(ns: f64) -> String {
    if ns >= 1_000_000.0 {
        format!("{:.2} ms", ns / 1_000_000.0)
    } else if ns >= 1_000.0 {
        format!("{:.2} us", ns / 1_000.0)
    } else {
        format!("{ns:.1} ns")
    }
}

fn format_rate(ns: f64) -> String {
    let per_second = 1e9 / ns;
    if per_second >= 1e6 {
        format!("{:.1} M/s", per_second / 1e6)
    } else if per_second >= 1e3 {
        format!("{:.1} k/s", per_second / 1e3)
    } else {
        format!("{per_second:.1} /s")
    }
}

/// Generator of a decay chain 0 -> 1 -> ... -> n-1 over `seconds`, with rates
/// spread over 18 orders of magnitude, like a real decay series.
fn chain_generator(n: usize, seconds: f64) -> Vec<f64> {
    let mut q = vec![0.0; n * n];
    for i in 0..n - 1 {
        let rate = 10f64.powf(-12.0 + 18.0 * i as f64 / (n - 1) as f64) * seconds;
        q[i * n + i] = -rate;
        q[i * n + i + 1] = rate;
    }
    q
}

fn nuclide(text: &str) -> Nuclide {
    Nuclide::parse(text).expect("benchmark nuclide")
}

fn mixed_sample() -> Sample {
    let mut sample = Sample::new();
    for (name, count) in [
        ("U-238", 100_000u64),
        ("Kr-92", 100_000),
        ("C-14", 100_000),
        ("Na-22", 100_000),
        ("Cf-252", 100_000),
        ("Li-8", 100_000),
        ("Fe-56", 100_000),
    ] {
        sample.add(nuclide(name), count).expect("benchmark sample");
    }
    sample
}

fn main() {
    let u235 = nuclide("U-235");
    let u238 = nuclide("U-238");
    let kr92 = nuclide("Kr-92");
    let c14 = nuclide("C-14");
    let au170 = nuclide("Au-170");
    let unlisted = Nuclide::new(50, 150);
    let all: Vec<Nuclide> = mass_table::entries().map(|entry| entry.nuclide).collect();
    let rows_per_sweep = all.len() as u64;
    let decaying: Vec<Nuclide> = decay::nuclides().collect();
    let decaying_per_sweep = decaying.len() as u64;
    let tenth: Vec<Nuclide> = decay::nuclides().step_by(10).collect();
    let fortieth: Vec<Nuclide> = decay::nuclides().step_by(40).collect();

    let fusion_in = [Nuclide::H2, Nuclide::H3];
    let fusion_out = [Nuclide::HE4, Nuclide::NEUTRON];
    let fission_in = [u235, Nuclide::NEUTRON];
    let fission_out = [
        Nuclide::new(56, 85),
        Nuclide::new(36, 56),
        Nuclide::NEUTRON,
        Nuclide::NEUTRON,
        Nuclide::NEUTRON,
    ];
    let unlisted_in = [unlisted];
    let unlisted_out = [Nuclide::new(50, 149), Nuclide::NEUTRON];

    let mut groups: Vec<Group> = Vec::new();

    // --- nuclides and the mass table ------------------------------------------
    eprintln!("group: nuclides and the mass table");
    groups.push(Group {
        title: "Nuclides and the mass table",
        rows: vec![
            measure("Nuclide::parse(\"U-235\")", 1, || {
                Nuclide::parse(black_box("U-235"))
            }),
            measure("Nuclide to_string (U-235)", 1, || {
                black_box(u235).to_string()
            }),
            measure("elements::symbol (Z=92)", 1, || {
                elements::symbol(black_box(92))
            }),
            measure("elements::z_from_symbol(\"U\")", 1, || {
                elements::z_from_symbol(black_box("U"))
            }),
            measure("lookup: listed nuclide (U-235)", 1, || {
                mass_table::lookup(black_box(u235))
            }),
            measure("lookup: unlisted nuclide (Sn-200)", 1, || {
                mass_table::lookup(black_box(unlisted))
            }),
            measure(
                "lookup: every table row in turn (per row)",
                rows_per_sweep,
                || {
                    for &nuclide in &all {
                        black_box(mass_table::lookup(black_box(nuclide)));
                    }
                },
            ),
            measure(
                "entries(): walk the whole table (per row)",
                rows_per_sweep,
                || {
                    let mut sum = 0.0;
                    for entry in mass_table::entries() {
                        sum += entry.mass_excess_kev;
                    }
                    sum
                },
            ),
        ],
    });

    // --- binding energies and reaction Q-values ----------------------------------
    eprintln!("group: binding energies and Q-values");
    groups.push(Group {
        title: "Binding energies and reaction Q-values",
        rows: vec![
            measure("mass_excess: table row (U-235)", 1, || {
                mass_excess(black_box(u235))
            }),
            measure("binding_energy: table row (U-235)", 1, || {
                binding_energy(black_box(u235))
            }),
            measure("binding_energy: fallback (Sn-200)", 1, || {
                binding_energy(black_box(unlisted))
            }),
            measure("liquid drop formula (U-235)", 1, || {
                liquid_drop::binding_energy_kev(black_box(92), black_box(143))
            }),
            measure("q_value: D + T -> He-4 + n", 1, || {
                q_value(black_box(&fusion_in), black_box(&fusion_out))
            }),
            measure("q_value: U-235 + n -> Ba-141 + Kr-92 + 3n", 1, || {
                q_value(black_box(&fission_in), black_box(&fission_out))
            }),
            measure("q_value: unlisted Sn-200 -> Sn-199 + n", 1, || {
                q_value(black_box(&unlisted_in), black_box(&unlisted_out))
            }),
            measure(
                "whole chart: binding per nucleon (per row)",
                rows_per_sweep,
                || {
                    let mut sum = 0.0;
                    for &nuclide in &all {
                        if let Some(binding) = binding_energy(nuclide) {
                            sum += binding.per_nucleon_kev(nuclide);
                        }
                    }
                    sum
                },
            ),
            measure(
                "whole chart: liquid drop formula (per row)",
                rows_per_sweep,
                || {
                    let mut sum = 0.0;
                    for &nuclide in &all {
                        sum += liquid_drop::binding_energy_kev(nuclide.z(), nuclide.n())
                            .unwrap_or(0.0);
                    }
                    sum
                },
            ),
        ],
    });

    // --- decay data -----------------------------------------------------------------
    eprintln!("group: decay data");
    groups.push(Group {
        title: "Decay data",
        rows: vec![
            measure("decay::half_life (U-238)", 1, || {
                decay::half_life(black_box(u238))
            }),
            measure("decay::branches: U-238 (raw rows)", 1, || {
                decay::branches(black_box(u238))
            }),
            measure("decay::outcomes: U-238 (alpha, SF, 2B-)", 1, || {
                outcomes(black_box(u238))
            }),
            measure("decay::outcomes: Kr-92 (B-, B-n)", 1, || {
                outcomes(black_box(kr92))
            }),
            measure("decay::dominant_outcome: Kr-92", 1, || {
                decay::dominant_outcome(black_box(kr92))
            }),
            measure("decay_q_value: C-14 beta-minus", 1, || {
                decay_q_value(black_box(c14), black_box(DecayMode::BetaMinus))
            }),
            measure("decay_q_value: U-238 alpha", 1, || {
                decay_q_value(black_box(u238), black_box(DecayMode::Alpha))
            }),
            measure("chain_to_stability: U-238 (14 steps)", 1, || {
                chain_to_stability(black_box(u238))
            }),
            measure("chain_to_stability: Kr-92 (4 steps)", 1, || {
                chain_to_stability(black_box(kr92))
            }),
            measure(
                "whole chart: outcomes (per nuclide)",
                decaying_per_sweep,
                || {
                    let mut count = 0;
                    for &nuclide in &decaying {
                        count += outcomes(nuclide).len();
                    }
                    count
                },
            ),
            measure(
                "whole chart: chain to stability (per nuclide)",
                decaying_per_sweep,
                || {
                    let mut steps = 0;
                    for &nuclide in &decaying {
                        steps += chain_to_stability(nuclide).steps.len();
                    }
                    steps
                },
            ),
            measure(
                "whole chart: Q of the main decay (per nuclide)",
                decaying_per_sweep,
                || {
                    let mut sum = 0.0;
                    for &nuclide in &decaying {
                        if let Some(outcome) = decay::dominant_outcome(nuclide) {
                            if let Some(q) = decay_q_value(nuclide, outcome.mode) {
                                sum += q.kev;
                            }
                        }
                    }
                    sum
                },
            ),
        ],
    });

    // --- random numbers ------------------------------------------------------------
    eprintln!("group: random numbers");
    let mut rng = Rng::new(1);
    let mut rng_b = Rng::new(2);
    let mut rng_c = Rng::new(3);
    let mut rng_d = Rng::new(4);
    let mut rng_e = Rng::new(5);
    let mut rng_f = Rng::new(6);
    let mut rng_g = Rng::new(7);
    let mut rng_h = Rng::new(8);
    let mut rng_i = Rng::new(9);
    let weights = [0.4, 0.25, 0.15, 0.1, 0.05, 0.03, 0.015, 0.005];
    let mut counts = [0u64; 8];
    groups.push(Group {
        title: "Random numbers",
        rows: vec![
            measure("Rng::next_u64", 1, || rng.next_u64()),
            measure("Rng::next_f64", 1, || rng_b.next_f64()),
            measure("Rng::below(1000)", 1, || rng_c.below(black_box(1000))),
            measure("Rng::next_normal", 1, || rng_d.next_normal()),
            measure("Rng::binomial: n=100, p=0.03 (inversion)", 1, || {
                rng_e.binomial(black_box(100), black_box(0.03))
            }),
            measure("Rng::binomial: n=1000, p=0.3 (BTRS)", 1, || {
                rng_f.binomial(black_box(1_000), black_box(0.3))
            }),
            measure("Rng::binomial: n=1e9, p=0.5 (BTRS)", 1, || {
                rng_g.binomial(black_box(1_000_000_000), black_box(0.5))
            }),
            measure("Rng::binomial: n=1e13, p=0.5 (normal approx)", 1, || {
                rng_h.binomial(black_box(10_000_000_000_000), black_box(0.5))
            }),
            measure("Rng::multinomial: 8 categories, n=1e6", 1, || {
                rng_i.multinomial(black_box(1_000_000), black_box(&weights), &mut counts);
                counts[0]
            }),
        ],
    });

    // --- matrix exponential --------------------------------------------------------
    eprintln!("group: matrix exponential");
    let q10 = chain_generator(10, 1e9);
    let q40 = chain_generator(40, 1e9);
    let q80 = chain_generator(80, 1e9);
    groups.push(Group {
        title: "Matrix exponential (stiff decay chain over 1e9 s)",
        rows: vec![
            measure("matrix::expm_minus_identity: 10 x 10", 1, || {
                matrix::expm_minus_identity(10, black_box(&q10))
            }),
            measure("matrix::expm_minus_identity: 40 x 40", 1, || {
                matrix::expm_minus_identity(40, black_box(&q40))
            }),
            measure("matrix::expm_minus_identity: 80 x 80", 1, || {
                matrix::expm_minus_identity(80, black_box(&q80))
            }),
        ],
    });

    // --- decay propagation -----------------------------------------------------------
    eprintln!("group: decay propagation");
    let mut warm = Propagator::new();
    warm.transition(u238, 1e9).expect("warm transition");
    let mut graph_only = Propagator::with_cache_limit(1);
    graph_only.transition(u238, 1.0).expect("warm graph");
    let mut interval = 1e9;
    let mut sweep_propagator = Propagator::new();
    groups.push(Group {
        title: "Decay propagation (exact, any interval)",
        rows: vec![
            measure(
                "graph: build the lineage graph (U-238, 40 states)",
                1,
                || Propagator::new().state_count(black_box(u238)),
            ),
            measure("transition, nothing cached: Kr-92 (9 states)", 1, || {
                Propagator::new().transition(black_box(kr92), black_box(1e9))
            }),
            measure("transition, nothing cached: U-238 (40 states)", 1, || {
                Propagator::new().transition(black_box(u238), black_box(1e9))
            }),
            measure("transition, nothing cached: Au-170 (79 states)", 1, || {
                Propagator::new().transition(black_box(au170), black_box(1e9))
            }),
            measure("transition, new interval, graph cached: U-238", 1, || {
                interval += 1.0;
                graph_only.transition(black_box(u238), black_box(interval))
            }),
            measure("transition, fully cached: U-238", 1, || {
                warm.transition(black_box(u238), black_box(1e9))
            }),
            measure(
                "whole chart: transitions, nothing cached (per nuclide)",
                tenth.len() as u64,
                || {
                    sweep_propagator.clear();
                    let mut outcomes_total = 0;
                    for &nuclide in &tenth {
                        if let Ok(t) = sweep_propagator.transition(nuclide, 1e9) {
                            outcomes_total += t.outcomes.len();
                        }
                    }
                    outcomes_total
                },
            ),
        ],
    });

    // --- samples ---------------------------------------------------------------------
    eprintln!("group: samples");
    let base_sample = mixed_sample();
    let mut cached = Propagator::new();
    let mut step_rng = Rng::new(42);
    let mut cached_amounts = Propagator::new();
    let mut base_amounts = Amounts::new();
    for (nuc, _) in base_sample.iter() {
        base_amounts.add(nuc, 1.0e5);
    }
    let mut chart_sample = Sample::new();
    for &nuclide in &fortieth {
        chart_sample.add(nuclide, 1_000).expect("benchmark sample");
    }
    let mut chart_propagator = Propagator::new();
    let mut chart_rng = Rng::new(43);
    chart_sample
        .clone()
        .advance(1e6, &mut chart_rng, &mut chart_propagator)
        .expect("warm chart sample");
    let mut cold_rng = Rng::new(44);
    groups.push(Group {
        title: "Samples (7 species of 100000 atoms unless noted)",
        rows: vec![
            measure("Sample::advance 1e6 s, transitions cached", 1, || {
                let mut sample = base_sample.clone();
                sample.advance(1e6, &mut step_rng, &mut cached)
            }),
            measure("Sample::advance 1e6 s, nothing cached", 1, || {
                let mut sample = base_sample.clone();
                sample.advance(1e6, &mut cold_rng, &mut Propagator::new())
            }),
            measure("Amounts::advance 1e6 s, transitions cached", 1, || {
                let mut amounts = base_amounts.clone();
                amounts.advance(1e6, &mut cached_amounts)
            }),
            measure(
                "Sample::advance 1e6 s, 89 species of 1000 atoms (per species)",
                fortieth.len() as u64,
                || {
                    let mut sample = chart_sample.clone();
                    sample.advance(1e6, &mut chart_rng, &mut chart_propagator)
                },
            ),
        ],
    });

    println!("### nuclear_core bench");
    println!();
    println!(
        "{SAMPLES} batches of about {} ms per benchmark. Read the median; min and max show the spread. Values under about 5 ns are mostly loop overhead.",
        BATCH.as_millis()
    );
    for group in &groups {
        println!();
        println!("#### {}", group.title);
        println!();
        println!("| benchmark | median | min | max | rate |");
        println!("|---|---:|---:|---:|---:|");
        for row in &group.rows {
            println!(
                "| {} | {} | {} | {} | {} |",
                row.name,
                format_time(row.median_ns),
                format_time(row.min_ns),
                format_time(row.max_ns),
                format_rate(row.median_ns),
            );
        }
    }
}
