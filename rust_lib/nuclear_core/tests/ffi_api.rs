// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "tests/ffi_api.rs"
// ============================================================================
//! The C interface, called the way a host calls it: through raw pointers, with
//! the results compared against the Rust API.
#![allow(unsafe_code)]

use core::ptr::{null, null_mut};

use nuclear_core::ffi::pile::*;
use nuclear_core::ffi::query::*;
use nuclear_core::ffi::*;
use nuclear_core::{binding, decay, Nuclide, Propagator, Rng, Sample};

fn nuc(text: &str) -> Nuclide {
    Nuclide::parse(text).unwrap()
}

fn key(text: &str) -> u32 {
    nuc(text).key()
}

/// Runs a list function twice, once to size the buffer and once to fill it.
fn list<T: Default + Clone>(f: impl Fn(*mut T, i32, *mut i32) -> i32) -> Vec<T> {
    let mut count = 0;
    assert_eq!(f(null_mut(), 0, &mut count), NUC_OK);
    let mut out = vec![T::default(); count as usize];
    let mut again = 0;
    assert_eq!(f(out.as_mut_ptr(), count, &mut again), NUC_OK);
    assert_eq!(again, count);
    out
}

fn text(f: impl Fn(*mut u8, i32) -> i32) -> String {
    let n = f(null_mut(), 0);
    assert!(n >= 0);
    let mut buf = vec![0u8; n as usize + 1];
    assert_eq!(f(buf.as_mut_ptr(), n + 1), n);
    String::from_utf8(buf[..n as usize].to_vec()).unwrap()
}

fn last_error() -> String {
    text(|b, c| unsafe { nuc_last_error(b, c) })
}

#[test]
fn versions_and_handle_lifecycle() {
    assert_eq!(nuc_abi_version(), NUC_ABI_VERSION);
    assert_eq!(
        text(|b, c| unsafe { nuc_library_version(b, c) }),
        env!("CARGO_PKG_VERSION")
    );
    unsafe {
        let ctx = nuc_context_create(1);
        let sample = nuc_sample_create();
        let amounts = nuc_amounts_create();
        assert!(!ctx.is_null() && !sample.is_null() && !amounts.is_null());
        let copy = nuc_sample_clone(sample);
        assert!(!copy.is_null() && copy != sample);
        nuc_sample_destroy(copy);
        nuc_sample_destroy(sample);
        nuc_amounts_destroy(amounts);
        nuc_context_destroy(ctx);
        // Null handles are refused or ignored, never dereferenced.
        nuc_sample_destroy(null_mut());
        nuc_amounts_destroy(null_mut());
        assert!(nuc_sample_clone(null_mut()).is_null());
        assert!(nuc_amounts_clone(null_mut()).is_null());
        let mut report = NucStepReport::default();
        assert_eq!(
            nuc_sample_advance(null_mut(), null_mut(), 1.0, &mut report),
            NUC_ERR_NULL
        );
        assert_eq!(nuc_sample_add(null_mut(), 0, 1), NUC_ERR_NULL);
        assert_eq!(nuc_amounts_add(null_mut(), 0, 1.0), NUC_ERR_NULL);
    }
}

#[test]
fn names_symbols_and_the_table() {
    unsafe {
        let mut k = 0u32;
        assert_eq!(nuc_nuclide_parse(b"U-235".as_ptr(), 5, &mut k), NUC_OK);
        assert_eq!(k, key("U-235"));
        assert_eq!(text(|b, c| nuc_nuclide_name(k, b, c)), "U-235");
        assert_eq!(nuc_nuclide_parse(b"n".as_ptr(), 1, &mut k), NUC_OK);
        assert_eq!(k, Nuclide::NEUTRON.key());
        assert_eq!(
            nuc_nuclide_parse(b"Xx-1".as_ptr(), 4, &mut k),
            NUC_ERR_INVALID
        );
        assert!(last_error().contains("Xx-1"));
        assert_eq!(
            nuc_nuclide_parse(b"\xff\xfe".as_ptr(), 2, &mut k),
            NUC_ERR_INVALID
        );
        assert_eq!(nuc_nuclide_parse(null(), 3, &mut k), NUC_ERR_NULL);
        assert_eq!(text(|b, c| nuc_element_symbol(92, b, c)), "U");
        assert_eq!(nuc_element_symbol(0, null_mut(), 0), NUC_ERR_INVALID);
        assert_eq!(nuc_element_symbol(119, null_mut(), 0), NUC_ERR_INVALID);
        assert_eq!(nuc_table_count(), 3558);
        let keys: Vec<u32> = list(|o, c, n| nuc_table_keys(o, c, n));
        assert_eq!(keys.len(), 3558);
        assert!(keys.windows(2).all(|w| w[0] < w[1]));
        assert!(keys.contains(&key("Fe-56")));
    }
}

#[test]
fn masses_binding_and_q_values_match_the_rust_api() {
    unsafe {
        let u = key("U-235");
        let mut e = NucEnergy::default();
        assert_eq!(nuc_mass_excess(u, &mut e), NUC_OK);
        let direct = binding::mass_excess(nuc("U-235")).unwrap();
        assert_eq!(e.kev, direct.kev);
        assert_eq!(e.source, 0);
        let mut b = NucBinding::default();
        assert_eq!(nuc_binding_energy(u, &mut b), NUC_OK);
        let direct = binding::binding_energy(nuc("U-235")).unwrap();
        assert_eq!(b.total_kev, direct.total_kev);
        assert_eq!(b.per_nucleon_kev, direct.per_nucleon_kev(nuc("U-235")));
        // Deuterium plus tritium to helium-4 and a neutron.
        let r = [Nuclide::H2.key(), Nuclide::H3.key()];
        let p = [Nuclide::HE4.key(), Nuclide::NEUTRON.key()];
        assert_eq!(nuc_q_value(r.as_ptr(), 2, p.as_ptr(), 2, &mut e), NUC_OK);
        assert!((e.kev - 17_589.0).abs() < 5.0);
        // Unbalanced reactions are refused with the reason.
        assert_eq!(
            nuc_q_value(r.as_ptr(), 2, p.as_ptr(), 1, &mut e),
            NUC_ERR_INVALID
        );
        assert!(!last_error().is_empty());
        assert_eq!(nuc_q_value(null(), 2, p.as_ptr(), 2, &mut e), NUC_ERR_NULL);
    }
}

#[test]
fn decay_queries_match_the_rust_api() {
    unsafe {
        let cs = key("Cs-137");
        let mut h = NucHalfLife::default();
        assert_eq!(nuc_half_life(cs, &mut h), NUC_OK);
        let direct = decay::half_life(nuc("Cs-137")).unwrap();
        assert_eq!(h.seconds, direct.seconds);
        assert_eq!(h.state, 3);
        assert_eq!(
            nuc_half_life(Nuclide::new(200, 200).key(), &mut h),
            NUC_ERR_NOT_IN_TABLE
        );

        let rows: Vec<NucDecayOutcome> = list(|o, c, n| nuc_decay_outcomes(key("U-238"), o, c, n));
        let direct = decay::outcomes(nuc("U-238"));
        assert_eq!(rows.len(), direct.len());
        for (row, d) in rows.iter().zip(&direct) {
            assert_eq!(row.percent, d.percent);
            assert_eq!(row.daughter, d.daughter.map_or(NUC_NO_NUCLIDE, |n| n.key()));
        }
        assert!(
            rows.iter().any(|r| r.daughter == NUC_NO_NUCLIDE),
            "spontaneous fission branch"
        );
        let stable: Vec<NucDecayOutcome> =
            list(|o, c, n| nuc_decay_outcomes(key("Fe-56"), o, c, n));
        assert!(stable.is_empty());

        // The energy of the alpha decay of U-238 is 4.27 MeV.
        let (mode, cluster) = (4, NUC_NO_NUCLIDE);
        let mut q = NucEnergy::default();
        assert_eq!(
            nuc_decay_q_value(key("U-238"), mode, cluster, &mut q),
            NUC_OK
        );
        assert!((q.kev - 4270.0).abs() < 10.0, "{}", q.kev);
        assert_eq!(
            nuc_decay_q_value(key("U-238"), 99, cluster, &mut q),
            NUC_ERR_INVALID
        );

        let mut end = NucChainEnd::default();
        let end_ptr: *mut NucChainEnd = &mut end;
        let steps: Vec<NucChainStep> =
            list(|o, c, n| nuc_chain_to_stability(key("U-238"), o, c, n, end_ptr));
        let chain = decay::chain_to_stability(nuc("U-238"));
        assert_eq!(steps.len(), chain.steps.len());
        assert_eq!(end.end, chain.end.key());
        assert_eq!(end.reason, 0);
        assert_eq!(steps[0].parent, key("U-238"));
        let mut abundance = 0.0;
        assert_eq!(nuc_abundance(key("U-238"), &mut abundance), NUC_OK);
        assert!((abundance - 99.27).abs() < 0.01);
    }
}

#[test]
fn neutron_fission_and_fusion_queries() {
    unsafe {
        let mut p = 0.0;
        assert_eq!(nuc_neutron_branching(key("U-235"), 0.0253, &mut p), NUC_OK);
        assert!((p - 0.855).abs() < 1e-9);
        assert_eq!(
            nuc_neutron_branching(key("Fe-56"), 0.0253, &mut p),
            NUC_ERR_NOT_IN_TABLE
        );
        assert_eq!(
            nuc_neutron_branching(key("U-235"), -1.0, &mut p),
            NUC_ERR_INVALID
        );
        let table: Vec<u32> = list(|o, c, n| nuc_neutron_table(o, c, n));
        assert_eq!(table.len(), 8);
        let mut q = 0.0;
        assert_eq!(nuc_capture_q_kev(key("U-238"), &mut q), NUC_OK);
        assert!((q / 1000.0 - 4.806).abs() < 0.01);

        let ctx = nuc_context_create(1);
        let mut s = NucFissionSummary::default();
        assert_eq!(
            nuc_fission_summary(ctx, key("U-235"), 0.0253, &mut s),
            NUC_OK
        );
        assert_eq!(s.compound, key("U-236"));
        assert_eq!(s.reference, key("U-235"));
        assert!(s.mean_neutrons > 2.3 && s.mean_neutrons < 2.7);
        assert_eq!(s.source, 0);
        let channels: Vec<NucFissionChannel> =
            list(|o, c, n| nuc_fission_channels(ctx, key("U-235"), 0.0253, o, c, n));
        assert_eq!(channels.len() as i32, s.channel_count);
        let sum: f64 = channels.iter().map(|c| c.probability).sum();
        assert!((sum - 1.0).abs() < 1e-9);
        for c in &channels {
            let (l, h) = (Nuclide::from_key(c.light), Nuclide::from_key(c.heavy));
            assert_eq!(l.z() + h.z(), 92);
            assert_eq!(l.a() + h.a() + u16::from(c.neutrons), 236);
        }
        let yields: Vec<NucAmount> =
            list(|o, c, n| nuc_fission_yields(ctx, key("U-235"), 0.0253, o, c, n));
        assert!((yields.iter().map(|y| y.amount).sum::<f64>() - 2.0).abs() < 1e-9);
        // Spontaneous fission of an unevaluated nucleus is extended.
        assert_eq!(nuc_fission_summary(ctx, key("Cf-254"), 0.0, &mut s), NUC_OK);
        assert_eq!(s.source, 1);
        assert_eq!(
            nuc_fission_summary(ctx, key("Fe-56"), 0.0, &mut s),
            NUC_ERR_NOT_FISSIONABLE
        );
        assert_eq!(
            nuc_fission_summary(ctx, key("U-235"), -5.0, &mut s),
            NUC_ERR_INVALID
        );
        let mut sizes = [0i32; 2];
        assert_eq!(nuc_context_cache_sizes(ctx, sizes.as_mut_ptr()), NUC_OK);
        assert_eq!(sizes[1], 2);
        assert_eq!(nuc_context_clear_caches(ctx), NUC_OK);
        nuc_context_cache_sizes(ctx, sizes.as_mut_ptr());
        assert_eq!(sizes, [0, 0]);
        nuc_context_destroy(ctx);
        assert_eq!(nuc_fissioning_nucleus(key("Bk-240")), key("Cm-240"));

        assert_eq!(nuc_fusion_reaction_count(), 7);
        for i in 0..7 {
            let mut c = NucFusionChannel::default();
            assert_eq!(nuc_fusion_reaction(i, &mut c), NUC_OK);
            assert!(c.q_kev > 0.0 && c.product_len >= 1);
            assert!(!text(|b, n| nuc_fusion_name(i, b, n)).is_empty());
        }
        let idx: Vec<i32> =
            list(|o, c, n| nuc_fusion_channels_for(key("H-3"), key("H-2"), o, c, n));
        assert_eq!(idx.len(), 1);
        let mut c = NucFusionChannel::default();
        nuc_fusion_reaction(idx[0], &mut c);
        assert_eq!((c.fit_kind, c.fit_low_kev, c.fit_high_kev), (0, 0.2, 100.0));
        let mut r = 0.0;
        assert_eq!(nuc_fusion_reactivity(idx[0], 10.0, &mut r), NUC_OK);
        assert!((r - 1.1362e-16).abs() < 1.0e-19);
        assert_eq!(nuc_fusion_reactivity(99, 10.0, &mut r), NUC_ERR_INVALID);
        let dd: Vec<f64> =
            list(|o, c, n| nuc_fusion_branching(key("H-2"), key("H-2"), 10.0, o, c, n));
        assert_eq!(dd.len(), 2);
        assert!((dd.iter().sum::<f64>() - 1.0).abs() < 1e-12);
    }
}

#[test]
fn advancing_through_the_interface_matches_the_rust_api() {
    unsafe {
        let ctx = nuc_context_create(5);
        let sample = nuc_sample_create();
        for (name, n) in [
            ("C-14", 1_000_000u64),
            ("Cf-252", 200_000),
            ("I-131", 50_000),
        ] {
            assert_eq!(nuc_sample_add(sample, key(name), n), NUC_OK);
        }
        let mut direct = Sample::new();
        for (name, n) in [
            ("C-14", 1_000_000u64),
            ("Cf-252", 200_000),
            ("I-131", 50_000),
        ] {
            direct.add(nuc(name), n).unwrap();
        }
        let (mut rng, mut prop) = (Rng::new(5), Propagator::new());
        for seconds in [3.0e5, 1.0e9] {
            let mut report = NucStepReport::default();
            assert_eq!(
                nuc_sample_advance(ctx, sample, seconds, &mut report),
                NUC_OK
            );
            let want = direct.advance(seconds, &mut rng, &mut prop).unwrap();
            assert_eq!(report.atoms_changed, want.atoms_changed);
            assert_eq!(report.new_pending_fissions, want.new_pending_fissions);
            assert_eq!(report.energy_released_kev, want.energy_released_kev);
            assert_eq!(report.energy_known, u8::from(want.energy_known));
        }
        let counts: Vec<NucCount> = list(|o, c, n| nuc_sample_counts(sample, o, c, n));
        let want: Vec<(u32, u64)> = direct.iter().map(|(n, c)| (n.key(), c)).collect();
        assert_eq!(
            counts.iter().map(|c| (c.key, c.count)).collect::<Vec<_>>(),
            want
        );
        let pending: Vec<NucCount> = list(|o, c, n| nuc_sample_pending(sample, o, c, n));
        assert_eq!(pending.len(), direct.pending_fissions().count());
        let mut totals = NucTotals::default();
        assert_eq!(nuc_sample_totals(sample, &mut totals), NUC_OK);
        assert_eq!(u128::from(totals.baryons), direct.baryon_number());
        assert_eq!(u128::from(totals.atoms), direct.total_atoms());
        let mut elapsed = 0.0;
        nuc_sample_elapsed_seconds(sample, &mut elapsed);
        assert_eq!(elapsed, 3.0e5 + 1.0e9);
        nuc_sample_destroy(sample);
        nuc_context_destroy(ctx);
    }
}

#[test]
fn saved_generator_state_continues_the_same_sequence() {
    unsafe {
        let ctx = nuc_context_create(77);
        let a = nuc_sample_create();
        nuc_sample_add(a, key("C-14"), 5_000_000);
        let mut report = NucStepReport::default();
        nuc_sample_advance(ctx, a, 1.0e11, &mut report);
        let mut state = [0u64; 4];
        nuc_context_rng_state(ctx, state.as_mut_ptr());
        let b = nuc_sample_clone(a);
        nuc_sample_advance(ctx, a, 1.0e11, &mut report);
        let first: Vec<NucCount> = list(|o, c, n| nuc_sample_counts(a, o, c, n));
        assert_eq!(nuc_context_set_rng_state(ctx, state.as_ptr()), NUC_OK);
        nuc_sample_advance(ctx, b, 1.0e11, &mut report);
        let second: Vec<NucCount> = list(|o, c, n| nuc_sample_counts(b, o, c, n));
        assert_eq!(first, second);
        nuc_sample_destroy(a);
        nuc_sample_destroy(b);
        nuc_context_destroy(ctx);
    }
}

#[test]
fn reactions_through_the_interface() {
    unsafe {
        let ctx = nuc_context_create(9);
        let sample = nuc_sample_create();
        nuc_sample_add(sample, key("U-235"), 100_000);
        nuc_sample_add(sample, key("H-2"), 40_000);
        nuc_sample_add(sample, key("H-3"), 40_000);
        let mut report = NucReactionReport::default();
        let mut p = 0.0;
        nuc_neutron_branching(key("U-235"), 0.0253, &mut p);
        assert_eq!(
            nuc_sample_irradiate(ctx, sample, key("U-235"), 50_000, 0.0253, p, &mut report),
            NUC_OK
        );
        assert_eq!(report.fissions + report.captures, 50_000);
        assert!(report.neutrons_released > 100_000);
        assert_eq!(report.energy_known, 1);
        assert_eq!(
            nuc_sample_fuse(
                ctx,
                sample,
                key("H-2"),
                key("H-3"),
                30_000,
                10.0,
                &mut report
            ),
            NUC_OK
        );
        assert_eq!(report.fusions, 30_000);
        assert_eq!(report.neutrons_released, 30_000);
        // Errors leave the pile alone and name the problem.
        let before: Vec<NucCount> = list(|o, c, n| nuc_sample_counts(sample, o, c, n));
        assert_eq!(
            nuc_sample_irradiate(ctx, sample, key("U-235"), 1_000_000, 0.0253, p, &mut report),
            NUC_ERR_NOT_ENOUGH
        );
        assert_eq!(
            nuc_sample_irradiate(ctx, sample, key("U-235"), 1, 0.0253, 1.5, &mut report),
            NUC_ERR_INVALID
        );
        assert_eq!(
            nuc_sample_fuse(ctx, sample, key("H-3"), key("H-3"), 1, 10.0, &mut report),
            NUC_ERR_NO_REACTION
        );
        assert!(last_error().contains("fusion"));
        assert_eq!(
            nuc_sample_advance(ctx, sample, -1.0, &mut NucStepReport::default()),
            NUC_ERR_INVALID
        );
        assert_eq!(list(|o, c, n| nuc_sample_counts(sample, o, c, n)), before);
        assert_eq!(
            nuc_sample_add(sample, key("U-235"), u64::MAX),
            NUC_ERR_OVERFLOW
        );

        // Pending spontaneous fissions become fission products.
        let waiting = nuc_sample_create();
        nuc_sample_add(waiting, key("Cf-252"), 2_000_000);
        nuc_sample_advance(ctx, waiting, 1.0e10, &mut NucStepReport::default());
        let mut t = NucTotals::default();
        nuc_sample_totals(waiting, &mut t);
        let baryons = t.baryons;
        assert_eq!(
            nuc_sample_resolve_fissions(ctx, waiting, &mut report),
            NUC_OK
        );
        assert!(report.fissions > 0);
        nuc_sample_totals(waiting, &mut t);
        assert_eq!(t.baryons, baryons);
        let pending: Vec<NucCount> = list(|o, c, n| nuc_sample_pending(waiting, o, c, n));
        assert!(pending.is_empty());
        nuc_sample_destroy(waiting);
        nuc_sample_destroy(sample);
        nuc_context_destroy(ctx);
    }
}

#[test]
fn amounts_through_the_interface() {
    unsafe {
        let ctx = nuc_context_create(1);
        let amounts = nuc_amounts_create();
        assert_eq!(nuc_amounts_add(amounts, key("U-235"), 100.0), NUC_OK);
        assert_eq!(
            nuc_amounts_add(amounts, key("U-235"), -1.0),
            NUC_ERR_INVALID
        );
        assert_eq!(
            nuc_amounts_add(amounts, key("U-235"), f64::NAN),
            NUC_ERR_INVALID
        );
        let mut report = NucAmountsReactionReport::default();
        assert_eq!(
            nuc_amounts_irradiate(ctx, amounts, key("U-235"), 60.0, 0.0253, 0.855, &mut report),
            NUC_OK
        );
        assert!((report.fissions - 51.3).abs() < 1e-9);
        let mut v = 0.0;
        nuc_amounts_amount(amounts, key("U-235"), &mut v);
        assert!((v - 40.0).abs() < 1e-9);
        nuc_amounts_baryon_number(amounts, &mut v);
        assert!((v - (100.0 * 235.0 + 60.0)).abs() < 1e-6);
        let mut step = NucAmountsStepReport::default();
        assert_eq!(nuc_amounts_advance(ctx, amounts, 1.0e5, &mut step), NUC_OK);
        let listed: Vec<NucAmount> = list(|o, c, n| nuc_amounts_list(amounts, o, c, n));
        assert!(listed.len() > 50, "fission products are listed");
        assert_eq!(nuc_amounts_add(amounts, key("H-2"), 2.0), NUC_OK);
        assert_eq!(nuc_amounts_add(amounts, key("H-3"), 2.0), NUC_OK);
        assert_eq!(
            nuc_amounts_fuse(amounts, key("H-2"), key("H-3"), 1.0, 10.0, &mut report),
            NUC_OK
        );
        assert_eq!(report.fusions, 1.0);
        assert_eq!(
            nuc_amounts_resolve_fissions(ctx, amounts, &mut report),
            NUC_OK
        );
        let copy = nuc_amounts_clone(amounts);
        assert!(!copy.is_null());
        nuc_amounts_destroy(copy);
        nuc_amounts_destroy(amounts);
        nuc_context_destroy(ctx);
    }
}
