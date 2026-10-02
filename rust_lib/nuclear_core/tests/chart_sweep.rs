// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "tests/chart_sweep.rs"
// ============================================================================
//! Whole-chart checks: table integrity, the shape of the binding energy curve,
//! and the accuracy bands of the liquid drop fallback.

use nuclear_core::mass_table::{self, MassEntry};
use nuclear_core::{binding_energy, elements, liquid_drop, Nuclide, Source};

fn per_nucleon_kev(entry: &MassEntry) -> f64 {
    binding_energy(entry.nuclide)
        .unwrap()
        .per_nucleon_kev(entry.nuclide)
}

#[test]
fn table_is_strictly_sorted_and_self_consistent() {
    let mut previous: Option<(u16, u16)> = None;
    let mut count = 0;
    for entry in mass_table::entries() {
        let key = (entry.nuclide.z(), entry.nuclide.a());
        if let Some(prev) = previous {
            assert!(prev < key, "rows out of order: {prev:?} then {key:?}");
        }
        previous = Some(key);
        assert!(entry.mass_excess_kev.is_finite(), "{}", entry.nuclide);
        assert!(entry.uncertainty_kev >= 0.0, "{}", entry.nuclide);
        assert!(entry.nuclide.z() <= elements::MAX_Z, "{}", entry.nuclide);
        assert!(entry.nuclide.a() >= 1, "{}", entry.nuclide);
        assert_eq!(mass_table::lookup(entry.nuclide), Some(entry));
        count += 1;
    }
    assert_eq!(count, mass_table::ENTRY_COUNT);
}

/// Golden counts for the checked-in data. Update these, and the doc file, when
/// scripts/gen_ame2020.py is rerun against a different source file.
#[test]
fn row_counts_match_the_generated_file() {
    assert_eq!(mass_table::ENTRY_COUNT, 3558);
    let estimated = mass_table::entries().filter(|e| e.estimated).count();
    assert_eq!(estimated, 1008);
}

#[test]
fn every_row_survives_a_text_round_trip() {
    for entry in mass_table::entries() {
        let text = entry.nuclide.to_string();
        assert_eq!(Nuclide::parse(&text), Some(entry.nuclide), "{text}");
    }
}

#[test]
fn nickel_62_is_the_most_bound_nucleus_per_nucleon() {
    let best = mass_table::entries()
        .filter(|e| e.nuclide.a() >= 2)
        .max_by(|x, y| per_nucleon_kev(x).total_cmp(&per_nucleon_kev(y)))
        .unwrap();
    assert_eq!(
        best.nuclide,
        Nuclide::new(28, 34),
        "expected Ni-62, got {}",
        best.nuclide
    );
}

#[test]
fn binding_source_follows_the_estimated_flag() {
    for entry in mass_table::entries() {
        let expected = if entry.estimated {
            Source::Estimated
        } else {
            Source::Measured
        };
        assert_eq!(
            binding_energy(entry.nuclide).unwrap().source,
            expected,
            "{}",
            entry.nuclide
        );
    }
}

/// Pins the known accuracy of the fallback so a coefficient change is noticed.
/// Values are keV per nucleon against measured (not estimated) rows.
#[test]
fn liquid_drop_stays_inside_its_documented_bands() {
    // (lowest A, highest A, allowed mean error, allowed worst error)
    let bands = [(41_u16, 120_u16, 90.0, 220.0), (121, 300, 60.0, 100.0)];
    for (lo, hi, mean_limit, worst_limit) in bands {
        let mut sum = 0.0;
        let mut worst = 0.0_f64;
        let mut count = 0_u32;
        for entry in mass_table::entries().filter(|e| !e.estimated) {
            let nuc = entry.nuclide;
            if nuc.z() == 0 || nuc.n() == 0 || nuc.a() < lo || nuc.a() > hi {
                continue;
            }
            let measured = binding_energy(nuc).unwrap().total_kev;
            let model = liquid_drop::binding_energy_kev(nuc.z(), nuc.n()).unwrap();
            let error = (model - measured).abs() / f64::from(nuc.a());
            sum += error;
            worst = worst.max(error);
            count += 1;
        }
        let mean = sum / f64::from(count);
        assert!(
            mean < mean_limit,
            "A {lo}-{hi}: mean error {mean:.1} keV/nucleon"
        );
        assert!(
            worst < worst_limit,
            "A {lo}-{hi}: worst error {worst:.1} keV/nucleon"
        );
    }
}

/// The fallback must lean the right way along the valley of stability, since
/// later decay chains rely on that direction.
#[test]
fn liquid_drop_picks_the_right_side_of_the_valley() {
    let all: Vec<MassEntry> = mass_table::entries().collect();
    let mut checked = 0;
    for a in 40_u16..=250 {
        let rows: Vec<&MassEntry> = all
            .iter()
            .filter(|e| e.nuclide.a() == a && e.nuclide.z() >= 1)
            .collect();
        if rows.len() < 5 {
            continue;
        }
        let table_z = rows
            .iter()
            .max_by(|x, y| {
                let bx = binding_energy(x.nuclide).unwrap().total_kev;
                let by = binding_energy(y.nuclide).unwrap().total_kev;
                bx.total_cmp(&by)
            })
            .unwrap()
            .nuclide
            .z();
        let model_z = (1..a)
            .max_by(|&zx, &zy| {
                let bx = liquid_drop::binding_energy_kev(zx, a - zx).unwrap();
                let by = liquid_drop::binding_energy_kev(zy, a - zy).unwrap();
                bx.total_cmp(&by)
            })
            .unwrap();
        let gap = i32::from(model_z) - i32::from(table_z);
        assert!(
            gap.abs() <= 2,
            "A={a}: table Z {table_z}, liquid drop Z {model_z}"
        );
        checked += 1;
    }
    assert!(
        checked >= 200,
        "only {checked} mass numbers had enough rows"
    );
}

#[test]
fn nuclides_outside_the_table_fall_back_and_say_so() {
    let unlisted = Nuclide::new(50, 150);
    assert_eq!(mass_table::lookup(unlisted), None);
    assert_eq!(binding_energy(unlisted).unwrap().source, Source::LiquidDrop);
    assert_eq!(binding_energy(Nuclide::new(0, 2)), None);
    assert_eq!(binding_energy(Nuclide::new(30, 0)), None);
}
