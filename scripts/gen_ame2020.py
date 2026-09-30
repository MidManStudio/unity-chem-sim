#!/usr/bin/env python3
# ============================================================================
# NOTICE: Full documentation, design decisions, and fix history for this file
# live in rust_lib/nuclear_core/docs/nuclear_core.md, section "CI and Workflows"
# ============================================================================
"""Generate rust_lib/nuclear_core/src/ame2020_data.rs from the AME2020 mass table.

The table is read from the public-domain `periodictable` package, which carries
the AME2020 atomic masses published by the IAEA Atomic Mass Data Center. Atomic
mass (u) is converted to mass excess (keV) with decimal arithmetic, so no float
rounding enters the conversion. Output is plain ASCII.

Usage:
    pip install periodictable
    python scripts/gen_ame2020.py --out rust_lib/nuclear_core/src/ame2020_data.rs
"""
import argparse
import re
import sys
from decimal import Decimal, getcontext

import periodictable
import periodictable.mass as pt_mass
from periodictable import constants

getcontext().prec = 40

# keV per atomic mass unit, the conversion factor AME2020 uses.
U_KEV = Decimal("931494.10242")

# value(unc) with an optional trailing '#'. '#' marks a value taken from
# systematic trends of the mass surface instead of measurement.
MASS_RE = re.compile(r"^(?P<val>-?\d+(?:\.\d+)?)\((?P<unc>\d+)\)(?P<est>#)?\??$")


def parse_mass(text):
    m = MASS_RE.match(text.strip())
    if not m:
        raise ValueError(f"unexpected mass field: {text!r}")
    val = m.group("val")
    decimals = len(val.split(".")[1]) if "." in val else 0
    unc = Decimal(m.group("unc")) * (Decimal(10) ** -decimals)
    return Decimal(val), unc, m.group("est") is not None


def rows():
    """Return (z, a, mass_excess_kev, uncertainty_kev, estimated) sorted by (z, a)."""
    out = []
    for line in pt_mass.isotope_mass.split("\n"):
        iso, iso_mass = line.split(",")[:2]
        z_str, _symbol, a_str = iso.split("-")
        z, a = int(z_str), int(a_str)
        mass_u, unc_u, est = parse_mass(iso_mass)
        out.append((z, a, (mass_u - a) * U_KEV, unc_u * U_KEV, est))
    # The neutron is element 0 in the AME table (N=1, Z=0).
    n_mass = Decimal(repr(constants.neutron_mass))
    n_unc = Decimal(repr(constants.neutron_mass_unc))
    out.append((0, 1, (n_mass - 1) * U_KEV, n_unc * U_KEV, False))
    out.sort(key=lambda r: (r[0], r[1]))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    data = rows()
    keys = [(r[0], r[1]) for r in data]
    assert len(keys) == len(set(keys)), "duplicate (z, a) key"
    by_key = {(r[0], r[1]): r for r in data}
    h1 = by_key[(1, 1)][2]
    nn = by_key[(0, 1)][2]

    out = []
    w = out.append
    w("// ============================================================================")
    w("// NOTICE: Full documentation, design decisions, and fix history for this file")
    w('// live in docs/nuclear_core.md, section "ame2020_data.rs"')
    w("// ============================================================================")
    w("// GENERATED FILE. Do not edit by hand.")
    w("// Regenerate with:")
    w("//   python scripts/gen_ame2020.py --out rust_lib/nuclear_core/src/ame2020_data.rs")
    w("//")
    w("// Source: AME2020 atomic mass evaluation. M. Wang, W.J. Huang, F.G. Kondev,")
    w("// G. Audi, S. Naimi, Chinese Phys. C 45, 030003 (2021), and W.J. Huang et al.,")
    w("// Chinese Phys. C 45, 030002 (2021). Values come from the public-domain")
    w(f"// periodictable package {periodictable.__version__}, which carries the IAEA AMDC")
    w("// massround.mas20 table. The neutron row is the exception: it comes from the")
    w("// CODATA neutron mass that periodictable ships, about 1 eV above the AME value.")
    w("//")
    w("// Each row is (z, a, mass excess in keV, uncertainty in keV, estimated).")
    w("// `estimated` is true where AME2020 marks a value as taken from systematic")
    w("// trends of the mass surface instead of measurement.")
    w("")
    w(f"pub(crate) const H1_MASS_EXCESS_KEV: f64 = {h1:.6f};")
    w(f"pub(crate) const NEUTRON_MASS_EXCESS_KEV: f64 = {nn:.6f};")
    w(f"pub(crate) const ENTRY_COUNT: usize = {len(data)};")
    w("")
    w("#[rustfmt::skip]")
    w("pub(crate) static RAW: [(u8, u16, f64, f64, bool); ENTRY_COUNT] = [")
    for z, a, dm, sig, est in data:
        w(f"    ({z}, {a}, {dm:.6f}, {sig:.6f}, {'true' if est else 'false'}),")
    w("];")
    with open(args.out, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(out) + "\n")
    n_est = sum(1 for r in data if r[4])
    print(f"wrote {len(data)} rows ({n_est} estimated) to {args.out}")


if __name__ == "__main__":
    sys.exit(main())
