// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "tests/fission_sweep.rs"
// ============================================================================
//! Sweeps over every evaluated fission table, checked against the data file.

use std::collections::BTreeMap;

use nuclear_core::fission::{self, Fission, Trigger, YieldSource};
use nuclear_core::Nuclide;

/// Independent yields of one table as read from the data file, with isomers
/// folded into the ground state and yields of 1e-9 or less dropped, as the
/// generator does.
type Table = BTreeMap<Nuclide, f64>;

fn read_tables() -> Vec<((Nuclide, bool, f64), Table)> {
    let path = concat!(env!("CARGO_MANIFEST_DIR"), "/data/fission_yields.tsv");
    let text = std::fs::read_to_string(path).unwrap();
    let mut out: BTreeMap<(String, bool, u64), Table> = BTreeMap::new();
    for line in text.lines() {
        if line.starts_with('#') || line.is_empty() {
            continue;
        }
        let f: Vec<&str> = line.split('\t').collect();
        if f[2] != "independent" || f[0].contains("_m") {
            continue;
        }
        let name = |g: &str| -> Option<Nuclide> {
            let digits = g.find(|c: char| c.is_ascii_digit())?;
            let end = g.find('_').unwrap_or(g.len());
            Nuclide::parse(&format!("{}-{}", &g[..digits], &g[digits..end]))
        };
        let (Some(_), Some(product)) = (name(f[0]), name(f[4])) else {
            continue;
        };
        let value: f64 = f[5].parse().unwrap();
        let energy: f64 = f[3].parse().unwrap();
        let table = out
            .entry((f[0].to_string(), f[1] == "sf", energy.to_bits()))
            .or_default();
        *table.entry(product).or_insert(0.0) += value;
    }
    out.into_iter()
        .map(|((parent, sf, bits), mut t)| {
            let (digits, end) = (
                parent.find(|c: char| c.is_ascii_digit()).unwrap(),
                parent.len(),
            );
            let n =
                Nuclide::parse(&format!("{}-{}", &parent[..digits], &parent[digits..end])).unwrap();
            t.retain(|_, v| *v > 1e-9);
            ((n, sf, f64::from_bits(bits)), t)
        })
        .collect()
}

#[test]
fn every_evaluated_table_conserves_and_normalizes() {
    let mut f = Fission::new();
    let sets = fission::evaluated();
    assert_eq!(sets.len(), 60);
    for (nuclide, trigger) in sets {
        let o = f.outcomes(nuclide, trigger).unwrap();
        assert_eq!(o.source, YieldSource::Evaluated);
        assert_eq!(o.reference, nuclide);
        let extra = u16::from(matches!(trigger, Trigger::Neutron { .. }));
        assert_eq!(o.compound, Nuclide::new(nuclide.z(), nuclide.n() + extra));
        let sum: f64 = o.channels.iter().map(|c| c.probability).sum();
        assert!((sum - 1.0).abs() < 1e-9, "{nuclide} {trigger:?}: sum {sum}");
        let yields: f64 = o.yields().iter().map(|(_, y)| y).sum();
        assert!((yields - 2.0).abs() < 1e-9);
        for c in &o.channels {
            assert_eq!(c.light.z() + c.heavy.z(), o.compound.z());
            assert_eq!(
                c.light.a() + c.heavy.a() + u16::from(c.neutrons),
                o.compound.a(),
                "{nuclide} {trigger:?}: {} {} {}",
                c.light,
                c.heavy,
                c.neutrons
            );
        }
        assert!(
            o.mean_neutrons > 0.8 && o.mean_neutrons < 6.5,
            "{nuclide} {trigger:?}: {} neutrons",
            o.mean_neutrons
        );
    }
}

#[test]
fn model_yields_stay_close_to_the_data_file() {
    let tables = read_tables();
    assert_eq!(tables.len(), 60);
    let mut f = Fission::new();
    let (mut worst_tv, mut worst_nu, mut worst_median): (f64, f64, f64) = (0.0, 0.0, 0.0);
    let mut worst_nu_at = String::new();
    for ((parent, sf, energy), table) in &tables {
        let trigger = if *sf {
            Trigger::Spontaneous
        } else {
            Trigger::Neutron { energy_ev: *energy }
        };
        let o = f.outcomes(*parent, trigger).unwrap();
        let model: BTreeMap<Nuclide, f64> = o.yields().into_iter().collect();
        let total: f64 = table.values().sum();
        // Total variation distance between the two yield distributions.
        let mut tv = 0.0;
        for (n, y) in table {
            tv += (model.get(n).copied().unwrap_or(0.0) - y).abs() / total;
        }
        for (n, y) in &model {
            if !table.contains_key(n) {
                tv += y / total;
            }
        }
        let tv = tv / 2.0;
        // Median relative error of the products with a share above 0.1 percent.
        let mut errors: Vec<f64> = table
            .iter()
            .filter(|(_, y)| **y / total > 1.0e-3)
            .map(|(n, y)| (model.get(n).copied().unwrap_or(0.0) - y).abs() / y)
            .collect();
        errors.sort_by(|a, b| a.total_cmp(b));
        let median = errors[errors.len() / 2];
        // Mean neutron count against the one the table implies.
        let mean_a: f64 = table.iter().map(|(n, y)| f64::from(n.a()) * y).sum::<f64>() / total;
        let implied = f64::from(o.compound.a()) - 2.0 * mean_a;
        let gap = (o.mean_neutrons - implied).abs();
        if gap > worst_nu {
            worst_nu = gap;
            worst_nu_at = format!(
                "{parent} {trigger:?}: model {:.2}, table {implied:.2}",
                o.mean_neutrons
            );
        }
        worst_tv = worst_tv.max(tv);
        worst_median = worst_median.max(median);
        assert!(tv < 0.04, "{parent} {trigger:?}: TV distance {tv:.4}");
        assert!(
            median < 0.10,
            "{parent} {trigger:?}: median error {median:.3}"
        );
    }
    eprintln!("worst TV {worst_tv:.4}, worst median {worst_median:.4}, worst neutron gap {worst_nu:.4} at {worst_nu_at}");
    assert!(
        worst_nu < 0.35,
        "worst mean-neutron gap {worst_nu} at {worst_nu_at}"
    );
}
