// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/nuclear_core.md, section "ffi/query.rs"
// ============================================================================
//! Data queries over the C interface: nuclide names, masses, binding energy,
//! reaction Q-values, decay data, neutron branching, fission outcomes and
//! fusion channels. None of these change a pile. The fission queries go
//! through a [`NucContext`] because they use its outcome cache.

use std::sync::OnceLock;

use super::*;
use crate::binding::{self, Source};
use crate::decay::{self, ChainEnd, DecayMode, HalfLifeState, Outcome};
use crate::elements;
use crate::fission::{self, Trigger, YieldSource};
use crate::fusion::{self, Fit};
use crate::mass_table;
use crate::neutron;
use crate::nuclide::Nuclide;
use crate::reaction::{q_value, ReactionError};

/// Codes of the decay modes without data, in order. Codes 29 and 30 are the
/// cluster modes.
const PLAIN_MODES: [DecayMode; 29] = [
    DecayMode::BetaMinus,
    DecayMode::BetaPlus,
    DecayMode::ElectronCapture,
    DecayMode::PositronEmission,
    DecayMode::Alpha,
    DecayMode::Proton,
    DecayMode::TwoProtons,
    DecayMode::ThreeProtons,
    DecayMode::Neutron,
    DecayMode::TwoNeutrons,
    DecayMode::ThreeNeutrons,
    DecayMode::SpontaneousFission,
    DecayMode::DoubleBetaMinus,
    DecayMode::DoubleBetaPlus,
    DecayMode::BetaMinusNeutron,
    DecayMode::BetaMinus2Neutrons,
    DecayMode::BetaMinus3Neutrons,
    DecayMode::BetaMinus4Neutrons,
    DecayMode::BetaMinusAlpha,
    DecayMode::BetaMinusDeuteron,
    DecayMode::BetaMinusTriton,
    DecayMode::BetaMinusProton,
    DecayMode::BetaMinusFission,
    DecayMode::BetaPlusProton,
    DecayMode::BetaPlus2Protons,
    DecayMode::BetaPlus3Protons,
    DecayMode::BetaPlusAlpha,
    DecayMode::BetaPlusProtonAlpha,
    DecayMode::BetaPlusFission,
];

/// The code of a decay mode and, for a cluster mode, the key of the cluster.
/// Codes 0 to 28 are the plain modes in declaration order, 29 is a cluster
/// mode and 30 is the mixture of two cluster modes.
pub(crate) fn mode_code(mode: DecayMode) -> (i32, u32) {
    match mode {
        DecayMode::Cluster(n) => (29, n.key()),
        DecayMode::ClusterMixture => (30, NUC_NO_NUCLIDE),
        other => {
            let index = PLAIN_MODES.iter().position(|m| *m == other);
            (index.map_or(-1, |i| i as i32), NUC_NO_NUCLIDE)
        }
    }
}

/// The inverse of [`mode_code`].
pub(crate) fn mode_from_code(code: i32, cluster: u32) -> Option<DecayMode> {
    match code {
        0..=28 => Some(PLAIN_MODES[code as usize]),
        29 if cluster != NUC_NO_NUCLIDE => Some(DecayMode::Cluster(Nuclide::from_key(cluster))),
        30 => Some(DecayMode::ClusterMixture),
        _ => None,
    }
}

fn source_code(source: Source) -> i32 {
    match source {
        Source::Measured => 0,
        Source::Estimated => 1,
        Source::LiquidDrop => 2,
    }
}

fn state_code(state: HalfLifeState) -> i32 {
    match state {
        HalfLifeState::Unknown => 0,
        HalfLifeState::Stable => 1,
        HalfLifeState::ParticleUnstable => 2,
        HalfLifeState::Exact => 3,
        HalfLifeState::Approximate => 4,
        HalfLifeState::AtLeast => 5,
        HalfLifeState::AtMost => 6,
    }
}

fn outcome_row(o: &Outcome) -> NucDecayOutcome {
    let (mode, cluster) = mode_code(o.mode);
    let mut emitted = [NUC_NO_NUCLIDE; NUC_MAX_EMITTED];
    for (slot, n) in emitted.iter_mut().zip(&o.emitted) {
        *slot = n.key();
    }
    NucDecayOutcome {
        percent: o.percent,
        mode,
        daughter: o.daughter.map_or(NUC_NO_NUCLIDE, |n| n.key()),
        cluster,
        emitted_count: o.emitted.len().min(NUC_MAX_EMITTED) as u8,
        assumed: u8::from(o.assumed),
        reserved: [0; 2],
        emitted,
    }
}

/// Reads `count` nuclide keys from `ptr`.
unsafe fn read_keys(ptr: *const u32, count: i32) -> Result<Vec<Nuclide>, i32> {
    if count < 0 {
        return Err(fail(NUC_ERR_INVALID, "a key count is negative"));
    }
    if count == 0 {
        return Ok(Vec::new());
    }
    if ptr.is_null() {
        return Err(fail(NUC_ERR_NULL, "a key array is null"));
    }
    Ok(std::slice::from_raw_parts(ptr, count as usize)
        .iter()
        .map(|&k| Nuclide::from_key(k))
        .collect())
}

fn valid_energy(energy_ev: f64) -> bool {
    energy_ev.is_finite() && energy_ev > 0.0
}

// ----------------------------------------------------------------------------
// Nuclides and masses
// ----------------------------------------------------------------------------

/// Parses a nuclide written as `Fe-56`, `U-235` or `n` from `len` bytes of
/// UTF-8 at `text`, and writes its key to `out_key`.
#[no_mangle]
pub unsafe extern "C" fn nuc_nuclide_parse(text: *const u8, len: i32, out_key: *mut u32) -> i32 {
    guard(|| {
        if len < 0 {
            return fail(NUC_ERR_INVALID, "the text length is negative");
        }
        if len > 0 && text.is_null() {
            return fail(NUC_ERR_NULL, "the text pointer is null");
        }
        let bytes = if len == 0 {
            &[][..]
        } else {
            std::slice::from_raw_parts(text, len as usize)
        };
        let Ok(s) = std::str::from_utf8(bytes) else {
            return fail(NUC_ERR_INVALID, "the text is not UTF-8");
        };
        match Nuclide::parse(s) {
            Some(n) => put(out_key, n.key()),
            None => fail(NUC_ERR_INVALID, format!("{s:?} is not a nuclide")),
        }
    })
}

/// Copies the name of a nuclide (`Fe-56`, or `n` for the neutron) as text and
/// returns its length.
#[no_mangle]
pub unsafe extern "C" fn nuc_nuclide_name(key: u32, buf: *mut u8, cap: i32) -> i32 {
    guard(|| put_text(&Nuclide::from_key(key).to_string(), buf, cap))
}

/// Copies the symbol of the element with `z` protons (1 to 118) as text and
/// returns its length.
#[no_mangle]
pub unsafe extern "C" fn nuc_element_symbol(z: i32, buf: *mut u8, cap: i32) -> i32 {
    guard(|| match u16::try_from(z).ok().and_then(elements::symbol) {
        Some(s) => put_text(s, buf, cap),
        None => fail(NUC_ERR_INVALID, format!("{z} is not an atomic number")),
    })
}

/// Number of nuclides in the AME2020 mass table.
#[no_mangle]
pub extern "C" fn nuc_table_count() -> i32 {
    mass_table::entries().count() as i32
}

/// Lists the keys of every nuclide in the mass table in ascending order.
#[no_mangle]
pub unsafe extern "C" fn nuc_table_keys(out: *mut u32, cap: i32, count: *mut i32) -> i32 {
    guard(|| {
        let keys: Vec<u32> = mass_table::entries().map(|e| e.nuclide.key()).collect();
        put_list(&keys, out, cap, count)
    })
}

/// Writes the atomic mass excess of a nuclide. Nuclides outside the table use
/// the liquid drop fallback, which `source` says.
#[no_mangle]
pub unsafe extern "C" fn nuc_mass_excess(key: u32, out: *mut NucEnergy) -> i32 {
    guard(|| match binding::mass_excess(Nuclide::from_key(key)) {
        Some(m) => put(
            out,
            NucEnergy {
                kev: m.kev,
                source: source_code(m.source),
                reserved: 0,
            },
        ),
        None => fail(NUC_ERR_NOT_IN_TABLE, "no mass data for this nuclide"),
    })
}

/// Writes the total and per-nucleon binding energy of a nuclide.
#[no_mangle]
pub unsafe extern "C" fn nuc_binding_energy(key: u32, out: *mut NucBinding) -> i32 {
    guard(|| {
        let n = Nuclide::from_key(key);
        match binding::binding_energy(n) {
            Some(b) => put(
                out,
                NucBinding {
                    total_kev: b.total_kev,
                    per_nucleon_kev: b.per_nucleon_kev(n),
                    source: source_code(b.source),
                    reserved: 0,
                },
            ),
            None => fail(NUC_ERR_NOT_IN_TABLE, "no mass data for this nuclide"),
        }
    })
}

/// Writes the energy released by a reaction given as lists of nuclide keys:
/// mass excess in minus mass excess out. Protons and nucleons must balance.
#[no_mangle]
pub unsafe extern "C" fn nuc_q_value(
    reactants: *const u32,
    reactant_count: i32,
    products: *const u32,
    product_count: i32,
    out: *mut NucEnergy,
) -> i32 {
    guard(|| {
        let r = match read_keys(reactants, reactant_count) {
            Ok(v) => v,
            Err(code) => return code,
        };
        let p = match read_keys(products, product_count) {
            Ok(v) => v,
            Err(code) => return code,
        };
        match q_value(&r, &p) {
            Ok(q) => put(
                out,
                NucEnergy {
                    kev: q.kev,
                    source: source_code(q.source),
                    reserved: 0,
                },
            ),
            Err(e @ ReactionError::NoMassData(_)) => fail(NUC_ERR_NOT_IN_TABLE, e.to_string()),
            Err(e) => fail(NUC_ERR_INVALID, e.to_string()),
        }
    })
}

// ----------------------------------------------------------------------------
// Decay
// ----------------------------------------------------------------------------

/// Writes the ground-state half-life of a nuclide. Fails with
/// `NUC_ERR_NOT_IN_TABLE` for a nuclide with no decay data.
#[no_mangle]
pub unsafe extern "C" fn nuc_half_life(key: u32, out: *mut NucHalfLife) -> i32 {
    guard(|| match decay::half_life(Nuclide::from_key(key)) {
        Some(h) => put(
            out,
            NucHalfLife {
                seconds: h.seconds,
                state: state_code(h.state),
                estimated: u8::from(h.estimated),
                reserved: [0; 3],
            },
        ),
        None => fail(NUC_ERR_NOT_IN_TABLE, "no decay data for this nuclide"),
    })
}

/// Writes the natural isotopic abundance in percent, or 0 when the nuclide
/// is not found in nature or NUBASE2020 gives none.
#[no_mangle]
pub unsafe extern "C" fn nuc_abundance(key: u32, out: *mut f64) -> i32 {
    guard(|| {
        put(
            out,
            decay::isotopic_abundance_percent(Nuclide::from_key(key)).unwrap_or(0.0),
        )
    })
}

/// Lists the ways a nuclide decays, as a distribution that sums to 100
/// percent. A stable nuclide has none.
#[no_mangle]
pub unsafe extern "C" fn nuc_decay_outcomes(
    key: u32,
    out: *mut NucDecayOutcome,
    cap: i32,
    count: *mut i32,
) -> i32 {
    guard(|| {
        let rows: Vec<NucDecayOutcome> = decay::outcomes(Nuclide::from_key(key))
            .iter()
            .map(outcome_row)
            .collect();
        put_list(&rows, out, cap, count)
    })
}

/// Writes the energy released by one decay mode of a nuclide. `cluster` is the
/// key of the emitted cluster when `mode` is the cluster mode, else ignored.
#[no_mangle]
pub unsafe extern "C" fn nuc_decay_q_value(
    key: u32,
    mode: i32,
    cluster: u32,
    out: *mut NucEnergy,
) -> i32 {
    guard(|| {
        let Some(m) = mode_from_code(mode, cluster) else {
            return fail(NUC_ERR_INVALID, format!("{mode} is not a decay mode code"));
        };
        match decay::decay_q_value(Nuclide::from_key(key), m) {
            Some(q) => put(
                out,
                NucEnergy {
                    kev: q.kev,
                    source: source_code(q.source),
                    reserved: 0,
                },
            ),
            None => fail(NUC_ERR_NOT_IN_TABLE, "no energy for this decay mode"),
        }
    })
}

/// Follows the most likely decay of a nuclide until it is stable or the chain
/// cannot go on. Lists the steps and writes where the chain ended.
#[no_mangle]
pub unsafe extern "C" fn nuc_chain_to_stability(
    key: u32,
    out: *mut NucChainStep,
    cap: i32,
    count: *mut i32,
    end: *mut NucChainEnd,
) -> i32 {
    guard(|| {
        if end.is_null() {
            return fail(NUC_ERR_NULL, "end pointer is null");
        }
        let chain = decay::chain_to_stability(Nuclide::from_key(key));
        let steps: Vec<NucChainStep> = chain
            .steps
            .iter()
            .map(|s| NucChainStep {
                parent: s.parent.key(),
                reserved: 0,
                outcome: outcome_row(&s.outcome),
            })
            .collect();
        let (reason, mode, cluster) = match chain.reason {
            ChainEnd::Stable => (0, 0, NUC_NO_NUCLIDE),
            ChainEnd::NoDaughter(m) => {
                let (code, cluster) = mode_code(m);
                (1, code, cluster)
            }
            ChainEnd::NoData => (2, 0, NUC_NO_NUCLIDE),
            ChainEnd::NotInTable => (3, 0, NUC_NO_NUCLIDE),
            ChainEnd::Loop => (4, 0, NUC_NO_NUCLIDE),
            ChainEnd::TooLong => (5, 0, NUC_NO_NUCLIDE),
        };
        let code = put_list(&steps, out, cap, count);
        if code == NUC_OK {
            end.write(NucChainEnd {
                end: chain.end.key(),
                reason,
                mode,
                cluster,
            });
        }
        code
    })
}

// ----------------------------------------------------------------------------
// Neutron absorption
// ----------------------------------------------------------------------------

/// Writes the authored probability that a neutron of `energy_ev` absorbed by
/// the target ends in fission instead of capture. Fails with
/// `NUC_ERR_NOT_IN_TABLE` for a target outside the small table.
#[no_mangle]
pub unsafe extern "C" fn nuc_neutron_branching(key: u32, energy_ev: f64, out: *mut f64) -> i32 {
    guard(|| {
        if !valid_energy(energy_ev) {
            return fail(NUC_ERR_INVALID, "the neutron energy must be positive");
        }
        match neutron::branching(Nuclide::from_key(key), energy_ev) {
            Some(b) => put(out, b.fission),
            None => fail(NUC_ERR_NOT_IN_TABLE, "no branching for this target"),
        }
    })
}

/// Writes the energy in keV released when the target captures a neutron.
#[no_mangle]
pub unsafe extern "C" fn nuc_capture_q_kev(key: u32, out: *mut f64) -> i32 {
    guard(|| match neutron::capture_q_kev(Nuclide::from_key(key)) {
        Some(q) => put(out, q),
        None => fail(NUC_ERR_NOT_IN_TABLE, "no mass data for this capture"),
    })
}

/// Lists the keys of the targets in the branching table.
#[no_mangle]
pub unsafe extern "C" fn nuc_neutron_table(out: *mut u32, cap: i32, count: *mut i32) -> i32 {
    guard(|| {
        let keys: Vec<u32> = neutron::tabulated().iter().map(|n| n.key()).collect();
        put_list(&keys, out, cap, count)
    })
}

// ----------------------------------------------------------------------------
// Fission
// ----------------------------------------------------------------------------

/// Energy 0 means spontaneous fission, a positive finite energy in eV means a
/// neutron of that energy absorbed by the target.
fn trigger(energy_ev: f64) -> Result<Trigger, i32> {
    if energy_ev == 0.0 {
        Ok(Trigger::Spontaneous)
    } else if valid_energy(energy_ev) {
        Ok(Trigger::Neutron { energy_ev })
    } else {
        Err(fail(
            NUC_ERR_INVALID,
            "the neutron energy must be 0 (spontaneous) or positive",
        ))
    }
}

/// Key of the nucleus that splits when `key` waits on the pending fission
/// list: the daughter for a fission that follows a beta decay, else `key`.
#[no_mangle]
pub extern "C" fn nuc_fissioning_nucleus(key: u32) -> u32 {
    fission::fissioning_nucleus(Nuclide::from_key(key)).key()
}

/// Writes the summary of the fission outcomes of a nucleus. `energy_ev` is 0
/// for spontaneous fission of `key`, or the energy of a neutron absorbed by
/// the target `key`.
#[no_mangle]
pub unsafe extern "C" fn nuc_fission_summary(
    ctx: *mut NucContext,
    key: u32,
    energy_ev: f64,
    out: *mut NucFissionSummary,
) -> i32 {
    guard(|| {
        let c = match context(ctx) {
            Ok(c) => c,
            Err(code) => return code,
        };
        let t = match trigger(energy_ev) {
            Ok(t) => t,
            Err(code) => return code,
        };
        match c.fission.outcomes(Nuclide::from_key(key), t) {
            Ok(o) => put(
                out,
                NucFissionSummary {
                    reference_energy_ev: o.reference_energy_ev,
                    mean_neutrons: o.mean_neutrons,
                    mean_q_kev: o.mean_q_kev,
                    compound: o.compound.key(),
                    reference: o.reference.key(),
                    channel_count: i32::try_from(o.channels.len()).unwrap_or(i32::MAX),
                    source: match o.source {
                        YieldSource::Evaluated => 0,
                        YieldSource::Extended => 1,
                    },
                },
            ),
            Err(e) => fission_error(&e),
        }
    })
}

/// Lists the channels of the fission outcomes, ordered by light fragment and
/// then heavy fragment. Their probabilities sum to 1.
#[no_mangle]
pub unsafe extern "C" fn nuc_fission_channels(
    ctx: *mut NucContext,
    key: u32,
    energy_ev: f64,
    out: *mut NucFissionChannel,
    cap: i32,
    count: *mut i32,
) -> i32 {
    guard(|| {
        let c = match context(ctx) {
            Ok(c) => c,
            Err(code) => return code,
        };
        let t = match trigger(energy_ev) {
            Ok(t) => t,
            Err(code) => return code,
        };
        match c.fission.outcomes(Nuclide::from_key(key), t) {
            Ok(o) => {
                let rows: Vec<NucFissionChannel> = o
                    .channels
                    .iter()
                    .map(|ch| NucFissionChannel {
                        probability: ch.probability,
                        q_kev: ch.q_kev,
                        light: ch.light.key(),
                        heavy: ch.heavy.key(),
                        neutrons: ch.neutrons,
                        q_known: u8::from(ch.q_known),
                        reserved: [0; 6],
                    })
                    .collect();
                put_list(&rows, out, cap, count)
            }
            Err(e) => fission_error(&e),
        }
    })
}

/// Lists the yield of each fission product per fission, in ascending nuclide
/// order. The yields sum to 2, one per fragment.
#[no_mangle]
pub unsafe extern "C" fn nuc_fission_yields(
    ctx: *mut NucContext,
    key: u32,
    energy_ev: f64,
    out: *mut NucAmount,
    cap: i32,
    count: *mut i32,
) -> i32 {
    guard(|| {
        let c = match context(ctx) {
            Ok(c) => c,
            Err(code) => return code,
        };
        let t = match trigger(energy_ev) {
            Ok(t) => t,
            Err(code) => return code,
        };
        match c.fission.outcomes(Nuclide::from_key(key), t) {
            Ok(o) => {
                let rows: Vec<NucAmount> = o
                    .yields()
                    .into_iter()
                    .map(|(n, y)| NucAmount {
                        key: n.key(),
                        reserved: 0,
                        amount: y,
                    })
                    .collect();
                put_list(&rows, out, cap, count)
            }
            Err(e) => fission_error(&e),
        }
    })
}

// ----------------------------------------------------------------------------
// Fusion
// ----------------------------------------------------------------------------

fn reactions() -> &'static [fusion::Channel] {
    static REACTIONS: OnceLock<Vec<fusion::Channel>> = OnceLock::new();
    REACTIONS.get_or_init(fusion::reactions)
}

fn channel_at(index: i32) -> Result<&'static fusion::Channel, i32> {
    usize::try_from(index)
        .ok()
        .and_then(|i| reactions().get(i))
        .ok_or_else(|| fail(NUC_ERR_INVALID, format!("{index} is not a fusion channel")))
}

/// Number of fusion channels. Channel indices run from 0 to one less.
#[no_mangle]
pub extern "C" fn nuc_fusion_reaction_count() -> i32 {
    reactions().len() as i32
}

/// Writes the description of one fusion channel.
#[no_mangle]
pub unsafe extern "C" fn nuc_fusion_reaction(index: i32, out: *mut NucFusionChannel) -> i32 {
    guard(|| {
        let ch = match channel_at(index) {
            Ok(c) => c,
            Err(code) => return code,
        };
        let mut product_keys = [NUC_NO_NUCLIDE; 3];
        let mut product_counts = [0u8; 3];
        for (i, &(n, k)) in ch.products.iter().take(3).enumerate() {
            product_keys[i] = n.key();
            product_counts[i] = k;
        }
        let (fit_kind, fit_low_kev, fit_high_kev) = match ch.fit {
            Fit::BoschHale {
                low_kev, high_kev, ..
            } => (0, low_kev, high_kev),
            Fit::Gamow { .. } => (1, 0.0, 0.0),
        };
        put(
            out,
            NucFusionChannel {
                q_kev: ch.q_kev,
                fit_low_kev,
                fit_high_kev,
                reactant_a: ch.reactants[0].key(),
                reactant_b: ch.reactants[1].key(),
                product_keys,
                product_counts,
                product_len: ch.products.len().min(3) as u8,
                fit_kind,
                reserved: [0; 7],
            },
        )
    })
}

/// Copies the name of one fusion channel, such as `D + T -> He-4 + n`, as text
/// and returns its length.
#[no_mangle]
pub unsafe extern "C" fn nuc_fusion_name(index: i32, buf: *mut u8, cap: i32) -> i32 {
    guard(|| match channel_at(index) {
        Ok(c) => put_text(c.name, buf, cap),
        Err(code) => code,
    })
}

/// Writes the thermal reactivity of one channel in cm^3/s at a temperature in
/// keV. Outside the published range of a Bosch and Hale fit it is an
/// extrapolation.
#[no_mangle]
pub unsafe extern "C" fn nuc_fusion_reactivity(index: i32, t_kev: f64, out: *mut f64) -> i32 {
    guard(|| match channel_at(index) {
        Ok(c) => put(out, c.reactivity(t_kev)),
        Err(code) => code,
    })
}

/// Lists the indices of the channels for a pair of nuclei, in either order.
#[no_mangle]
pub unsafe extern "C" fn nuc_fusion_channels_for(
    a: u32,
    b: u32,
    out: *mut i32,
    cap: i32,
    count: *mut i32,
) -> i32 {
    guard(|| {
        let pair = {
            let (x, y) = (Nuclide::from_key(a), Nuclide::from_key(b));
            if x <= y {
                [x, y]
            } else {
                [y, x]
            }
        };
        let indices: Vec<i32> = reactions()
            .iter()
            .enumerate()
            .filter(|(_, c)| c.reactants == pair)
            .map(|(i, _)| i as i32)
            .collect();
        put_list(&indices, out, cap, count)
    })
}

/// Lists the share of fusion events that take each channel of a pair at a
/// temperature in keV, in the order of [`nuc_fusion_channels_for`]. The shares
/// sum to 1, or are all 0 when no channel runs.
#[no_mangle]
pub unsafe extern "C" fn nuc_fusion_branching(
    a: u32,
    b: u32,
    t_kev: f64,
    out: *mut f64,
    cap: i32,
    count: *mut i32,
) -> i32 {
    guard(|| {
        let channels = fusion::channels_for(Nuclide::from_key(a), Nuclide::from_key(b));
        put_list(&fusion::branching(&channels, t_kev), out, cap, count)
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mode_codes_round_trip_for_every_mode_in_the_data() {
        let mut seen = std::collections::BTreeSet::new();
        for n in decay::nuclides() {
            for o in decay::outcomes(n) {
                let (code, cluster) = mode_code(o.mode);
                assert!((0..=30).contains(&code), "{:?}", o.mode);
                assert_eq!(mode_from_code(code, cluster), Some(o.mode));
                seen.insert(code);
            }
        }
        // Every code from 0 to 30 is a mode the chart really uses, apart from
        // modes that NUBASE2020 lists only as unquantified branches.
        assert!(seen.len() >= 25, "{} codes used", seen.len());
        assert_eq!(mode_from_code(31, 0), None);
        assert_eq!(mode_from_code(-1, 0), None);
        assert_eq!(mode_from_code(29, NUC_NO_NUCLIDE), None);
        for (i, m) in PLAIN_MODES.iter().enumerate() {
            assert_eq!(mode_code(*m).0, i as i32);
        }
    }

    #[test]
    fn no_decay_outcome_lists_more_nuclei_than_a_row_holds() {
        for n in decay::nuclides() {
            for o in decay::outcomes(n) {
                assert!(
                    o.emitted.len() <= NUC_MAX_EMITTED,
                    "{n}: {} emitted",
                    o.emitted.len()
                );
            }
            for step in decay::chain_to_stability(n).steps {
                assert!(step.outcome.emitted.len() <= NUC_MAX_EMITTED);
            }
        }
    }

    #[test]
    fn no_fusion_channel_has_more_products_than_a_row_holds() {
        for c in fusion::reactions() {
            assert!(c.products.len() <= 3, "{}", c.name);
        }
    }
}
