#!/usr/bin/env python3
# ============================================================================
# NOTICE: Full documentation, design decisions, and fix history for this file
# live in rust_lib/nuclear_core/docs/nuclear_core.md, section "CI and Workflows"
# ============================================================================
"""Generate rust_lib/nuclear_core/src/nubase2020_data.rs from the NUBASE2020 table.

Reads the original NUBASE2020 text file (rust_lib/nuclear_core/data/nubase_1.mas20)
and keeps the ground-state rows (isomer index 0). For each nuclide it writes the
half-life, the isotopic abundance and the list of decay modes with intensities.
Only the Python standard library is needed.

Every row is cross-checked against the AME2020 table in data/mass.mas20: the
same set of nuclides, and mass excess values that agree to NUBASE's rounding.

Usage, from the repository root:
    python scripts/gen_nubase2020.py
"""
import argparse
import re
import sys
from decimal import Decimal
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gen_ame2020 as ame  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
CRATE = ROOT / "rust_lib" / "nuclear_core"

# 1 y = 365.2422 d = 31 556 926 s, the tropical year NUBASE2020 uses.
YEAR = Decimal(31556926)
UNITS = {
    "ys": Decimal("1e-24"), "zs": Decimal("1e-21"), "as": Decimal("1e-18"),
    "fs": Decimal("1e-15"), "ps": Decimal("1e-12"), "ns": Decimal("1e-9"),
    "us": Decimal("1e-6"), "ms": Decimal("1e-3"), "s": Decimal(1),
    "m": Decimal(60), "h": Decimal(3600), "d": Decimal(86400), "y": YEAR,
    "ky": YEAR * Decimal("1e3"), "My": YEAR * Decimal("1e6"),
    "Gy": YEAR * Decimal("1e9"), "Ty": YEAR * Decimal("1e12"),
    "Py": YEAR * Decimal("1e15"), "Ey": YEAR * Decimal("1e18"),
    "Zy": YEAR * Decimal("1e21"), "Yy": YEAR * Decimal("1e24"),
}

MODE_NAMES = {
    "B-": "BetaMinus", "B+": "BetaPlus", "EC+B+": "BetaPlus", "EC": "ElectronCapture",
    "e+": "PositronEmission", "A": "Alpha",
    "p": "Proton", "2p": "TwoProtons", "3p": "ThreeProtons",
    "n": "Neutron", "2n": "TwoNeutrons", "3n": "ThreeNeutrons",
    "SF": "SpontaneousFission", "2B-": "DoubleBetaMinus", "2B+": "DoubleBetaPlus",
    "B-n": "BetaMinusNeutron", "B-2n": "BetaMinus2Neutrons",
    "B-3n": "BetaMinus3Neutrons", "B-4n": "BetaMinus4Neutrons",
    "B-A": "BetaMinusAlpha", "B-d": "BetaMinusDeuteron", "B-t": "BetaMinusTriton",
    "B-p": "BetaMinusProton", "B-SF": "BetaMinusFission",
    "B+p": "BetaPlusProton", "B+2p": "BetaPlus2Protons", "B+3p": "BetaPlus3Protons",
    "B+A": "BetaPlusAlpha", "B+pA": "BetaPlusProtonAlpha", "B+SF": "BetaPlusFission",
    "24Ne+26Ne": "ClusterMixture", "28Mg+30Mg": "ClusterMixture",
}
# Known errors in the source file. Each fix applies only while the file still
# has the wrong value, so it switches itself off if a corrected file is used.
# 126Te: the file says 8.84, which makes tellurium sum to 90 percent. The
# published natural abundance is 18.84 (IUPAC; also in standard isotope tables).
ABUNDANCE_FIXES = {(52, 126): (Decimal("8.84"), Decimal("18.84"))}

QUALIFIERS = {"=": "Exact", "~": "Approximate", "<": "AtMost", ">": "AtLeast"}
HL_QUALIFIERS = {"<": "AtMost", ">": "AtLeast", "~": "Approximate"}

TOKEN = re.compile(
    r"^(?P<mode>[A-Za-z0-9+\-]+?)\s*"
    r"(?:(?P<op>[=~<>])\s*(?P<val>\?|[0-9]*\.?[0-9]+(?:[eE][+\-]?\d+)?)(?P<est>#)?"
    r"(?:\s+\S+)?|(?P<possible>\?))\s*(?:\[.*\])?\s*$"
)
LIMIT = re.compile(r"^(?P<op>[<>~])\s*(?P<val>[0-9]*\.?[0-9]+)\s*(?P<unit>[a-zA-Z]+)$")


def mode_expr(name, symbols):
    if name in MODE_NAMES:
        return "DecayMode::" + MODE_NAMES[name]
    m = re.match(r"^(\d+)([A-Z][a-z]?)$", name)
    if m:
        a, z = int(m.group(1)), symbols.index(m.group(2))
        return f"DecayMode::Cluster(Nuclide::new({z}, {a - z}))"
    raise ValueError(f"unknown decay mode {name!r}")


def parse_half_life(value, unit, limit):
    """Return (state, seconds, estimated)."""
    value, unit, limit = value.strip(), unit.strip(), limit.strip()
    if value == "stbl":
        return "Stable", Decimal(0), False
    if value == "p-unst":
        return "ParticleUnstable", Decimal(0), False
    if value:
        estimated = value.endswith("#")
        value = value.rstrip("#")
        state = "Exact"
        if value[0] in HL_QUALIFIERS:
            state, value = HL_QUALIFIERS[value[0]], value[1:].strip()
        return state, Decimal(value) * UNITS[unit], estimated
    m = LIMIT.match(limit)
    if m:
        return HL_QUALIFIERS[m.group("op")], Decimal(m.group("val")) * UNITS[m.group("unit")], False
    return "Unknown", Decimal(0), False


def parse_branches(text, symbols):
    """Return (abundance or None, [(mode_expr, qualifier, percent, estimated)])."""
    abundance, branches = None, []
    # The file has a few irregular spellings: two "possible" modes with no
    # semicolon between them ("B+p ? 2p ?") and "=<" for an upper limit.
    text = re.sub(r"\?\s+(?=[A-Za-z0-9])", "?;", text)
    text = re.sub(r"=\s*([<>~])", r"\1", text)
    for token in (t.strip() for t in text.split(";")):
        if not token:
            continue
        m = TOKEN.match(token)
        if not m:
            raise ValueError(f"cannot parse decay token {token!r}")
        mode = m.group("mode")
        if mode == "IS":
            abundance = Decimal(m.group("val"))
            continue
        expr = mode_expr(mode, symbols)
        if m.group("possible"):
            branches.append((expr, "Possible", Decimal(0), False))
        elif m.group("val") == "?":
            branches.append((expr, "Unquantified", Decimal(0), False))
        else:
            branches.append((expr, QUALIFIERS[m.group("op")], Decimal(m.group("val")), bool(m.group("est"))))
    return abundance, branches


def rust_float(value):
    text = repr(float(value))
    return text if ("." in text or "e" in text) else text + ".0"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", default=str(CRATE / "data" / "nubase_1.mas20"))
    ap.add_argument("--ame", default=str(CRATE / "data" / "mass.mas20"))
    ap.add_argument("--out", default=str(CRATE / "src" / "nubase2020_data.rs"))
    args = ap.parse_args()

    symbols = ame.load_symbols()
    ame_rows = {(r[0], r[1]): r for r in ame.parse(Path(args.ame), symbols)}

    modes, rows, branches, fixes_applied = [], [], [], []
    worst_mass = (0.0, None)
    for line in Path(args.input).read_text(encoding="utf-8").split("\n"):
        if not line or line.startswith("#") or line[7] != "0":
            continue
        a, z = int(line[0:3]), int(line[4:7])
        label = line[11:16].strip()
        assert label == f"{a}{symbols[z]}", f"label {label!r} != {a}{symbols[z]} (Z={z})"
        assert (z, a) in ame_rows, f"{label} is not in the AME table"

        mass_text = line[18:31].strip()
        if mass_text not in ("non-exist",):
            dec = len(mass_text.split(".")[1].rstrip("#")) if "." in mass_text else 0
            nub = Decimal(mass_text.replace("#", ""))
            # NUBASE rounds more coarsely than AME (to tens of keV for large
            # uncertainties). Allow one unit of the last printed digit, or 5 keV
            # for values printed as whole tens.
            tol = Decimal(10) ** -dec if dec else (Decimal(5) if nub % 10 == 0 else Decimal(1))
            diff = abs(nub - ame_rows[(z, a)][2])
            assert diff <= tol + Decimal("1e-6"), f"{label}: NUBASE mass {nub} vs AME {ame_rows[(z, a)][2]}"
            worst_mass = max(worst_mass, (float(diff / tol), label))

        state, seconds, hl_est = parse_half_life(line[69:78], line[78:80], line[81:88])
        abundance, parsed = parse_branches(line[119:].strip(), symbols)
        if (z, a) in ABUNDANCE_FIXES and abundance == ABUNDANCE_FIXES[(z, a)][0]:
            abundance = ABUNDANCE_FIXES[(z, a)][1]
            fixes_applied.append(label)
        start = len(branches)
        for expr, qualifier, percent, est in parsed:
            if expr not in modes:
                modes.append(expr)
            branches.append((modes.index(expr), percent, qualifier, est))
        rows.append((z, a, state, seconds, hl_est, abundance, start, len(parsed)))

    rows.sort(key=lambda r: (r[0], r[1]))
    assert {(r[0], r[1]) for r in rows} == set(ame_rows), "NUBASE and AME list different nuclides"
    assert len(modes) < 256

    out = []
    w = out.append
    w("// ============================================================================")
    w("// NOTICE: Full documentation, design decisions, and fix history for this file")
    w('// live in docs/nuclear_core.md, section "nubase2020_data.rs"')
    w("// ============================================================================")
    w("// GENERATED FILE. Do not edit by hand.")
    w("// Regenerate from the repository root with: python scripts/gen_nubase2020.py")
    w("//")
    w("// Source: the NUBASE2020 table (data/nubase_1.mas20), ground states only.")
    w("// F.G. Kondev, M. Wang, W.J. Huang, S. Naimi, G. Audi, Chinese Phys. C 45,")
    w("// 030001 (2021).")
    w("//")
    w("// ROWS: (z, a, half-life state, half-life in seconds, half-life estimated,")
    w("//        isotopic abundance in percent or -1 when none, first BRANCHES index,")
    w("//        number of branches).")
    w("// BRANCHES: (index into MODES, percent, qualifier, estimated). Percent is per")
    w("// 100 decays of the parent, as NUBASE2020 gives it.")
    w("")
    w("// A data value can land close to a math constant by chance. That is not a bug.")
    w("#![allow(clippy::approx_constant)]")
    w("")
    w("use crate::decay::{DecayMode, HalfLifeState, Qualifier};")
    w("use crate::nuclide::Nuclide;")
    w("")
    w(f"pub(crate) const ROW_COUNT: usize = {len(rows)};")
    w(f"pub(crate) const BRANCH_COUNT: usize = {len(branches)};")
    w("")
    w("pub(crate) type Row = (u8, u16, HalfLifeState, f64, bool, f64, u16, u8);")
    w("pub(crate) type BranchRow = (u8, f64, Qualifier, bool);")
    w("")
    w(f"pub(crate) static MODES: [DecayMode; {len(modes)}] = [")
    for expr in modes:
        w(f"    {expr},")
    w("];")
    w("")
    w("#[rustfmt::skip]")
    w("pub(crate) static ROWS: [Row; ROW_COUNT] = [")
    for z, a, state, seconds, hl_est, abundance, start, count in rows:
        ab = rust_float(abundance) if abundance is not None else "-1.0"
        w(f"    ({z}, {a}, HalfLifeState::{state}, {rust_float(seconds)}, {'true' if hl_est else 'false'}, {ab}, {start}, {count}),")
    w("];")
    w("")
    w("#[rustfmt::skip]")
    w("pub(crate) static BRANCHES: [BranchRow; BRANCH_COUNT] = [")
    for mode_index, percent, qualifier, est in branches:
        w(f"    ({mode_index}, {rust_float(percent)}, Qualifier::{qualifier}, {'true' if est else 'false'}),")
    w("];")
    Path(args.out).write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")

    by_state = {}
    for r in rows:
        by_state[r[2]] = by_state.get(r[2], 0) + 1
    by_qual = {}
    for b in branches:
        by_qual[b[2]] = by_qual.get(b[2], 0) + 1
    print(f"wrote {len(rows)} rows, {len(branches)} branches, {len(modes)} modes to {args.out}")
    print("half-life states:", by_state)
    print("branch qualifiers:", by_qual)
    print("worst NUBASE-vs-AME mass agreement (fraction of allowed rounding):", worst_mass)
    print("source errors corrected:", fixes_applied)


if __name__ == "__main__":
    sys.exit(main())
