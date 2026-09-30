// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "benches/chart_bench.rs"
// ============================================================================
//! Timing harness for the hot paths of nuclear_core.
//!
//! No dependencies: it uses `std::time::Instant` and `std::hint::black_box`, so
//! the crate keeps a zero-dependency test build. Run it with
//! `cargo bench -p nuclear_core`. The result is a Markdown table on stdout.

use std::hint::black_box;
use std::time::{Duration, Instant};

use nuclear_core::liquid_drop;
use nuclear_core::mass_table;
use nuclear_core::{binding_energy, q_value, Nuclide};

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

fn main() {
    let u235 = Nuclide::new(92, 143);
    let unlisted = Nuclide::new(50, 150);
    let all: Vec<Nuclide> = mass_table::entries().map(|entry| entry.nuclide).collect();
    let rows_per_sweep = all.len() as u64;

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

    let rows = vec![
        measure("lookup: listed nuclide (U-235)", 1, || {
            mass_table::lookup(black_box(u235))
        }),
        measure("lookup: unlisted nuclide (Sn-200)", 1, || {
            mass_table::lookup(black_box(unlisted))
        }),
        measure("lookup: every table row in turn (per row)", rows_per_sweep, || {
            for &nuclide in &all {
                black_box(mass_table::lookup(black_box(nuclide)));
            }
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
        measure("Nuclide::parse(\"U-235\")", 1, || {
            Nuclide::parse(black_box("U-235"))
        }),
        measure("Nuclide to_string (U-235)", 1, || black_box(u235).to_string()),
        measure("whole chart: binding per nucleon (per row)", rows_per_sweep, || {
            let mut sum = 0.0;
            for &nuclide in &all {
                if let Some(binding) = binding_energy(nuclide) {
                    sum += binding.per_nucleon_kev(nuclide);
                }
            }
            sum
        }),
    ];

    println!("### nuclear_core bench");
    println!();
    println!(
        "{SAMPLES} batches of about {} ms per benchmark. Read the median; min and max show the spread. Values under about 5 ns are mostly loop overhead.",
        BATCH.as_millis()
    );
    println!();
    println!("| benchmark | median | min | max | rate |");
    println!("|---|---:|---:|---:|---:|");
    for row in &rows {
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
