#!/usr/bin/env python3
# ============================================================================
# NOTICE: Full documentation, design decisions, and fix history for this file
# live in rust_lib/nuclear_core/docs/nuclear_core.md, section "CI and Workflows"
# ============================================================================
"""Generate rust_lib/nuclear_core/src/ame2020_data.rs from the AME2020 mass table.

Reads the original AME2020 text file (rust_lib/nuclear_core/data/mass.mas20) and
writes one row per nuclide: (z, a, mass excess in keV, uncertainty in keV,
estimated). Only the Python standard library is needed.

AME marks values that come from systematic trends instead of measurement with
a '#' in place of the decimal point. Those rows get estimated = true.

Usage, from the repository root:
    python scripts/gen_ame2020.py
"""
import argparse
import re
import sys
from decimal import Decimal
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CRATE = ROOT / "rust_lib" / "nuclear_core"


def load_symbols():
    """Element symbols from elements.rs, so there is one list in the repository."""
    src = (CRATE / "src" / "elements.rs").read_text(encoding="utf-8")
    block = re.search(r"const SYMBOLS: \[&str; 118\] = \[(.*?)\];", src, re.S).group(1)
    symbols = re.findall(r'"([A-Za-z]+)"', block)
    assert len(symbols) == 118, len(symbols)
    return ["n"] + symbols  # index = Z, with the neutron at Z = 0


def number(token):
    """Decimal value of an AME token; returns (value, estimated)."""
    estimated = "#" in token
    return Decimal(token.replace("#", ".")), estimated


def parse(path, symbols):
    lines = path.read_text(encoding="utf-8").split("\n")
    start = next(i for i, line in enumerate(lines) if line.startswith("1N-Z"))
    rows = []
    for line in lines[start + 2:]:
        if not line.strip():
            continue
        n, z, a = int(line[4:9]), int(line[9:14]), int(line[14:19])
        element = line[20:23].strip()
        assert a == n + z, f"A != N + Z in: {line[:30]}"
        assert element == symbols[z], f"symbol {element!r} != {symbols[z]!r} for Z={z}"
        tokens = line[28:].split()
        mass, estimated = number(tokens[0])
        sigma, _ = number(tokens[1])
        rows.append((z, a, mass, sigma, estimated))
    rows.sort(key=lambda r: (r[0], r[1]))
    keys = [(r[0], r[1]) for r in rows]
    assert len(keys) == len(set(keys)), "duplicate (z, a) key"
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", default=str(CRATE / "data" / "mass.mas20"))
    ap.add_argument("--out", default=str(CRATE / "src" / "ame2020_data.rs"))
    args = ap.parse_args()

    rows = parse(Path(args.input), load_symbols())
    by_key = {(r[0], r[1]): r for r in rows}
    h1, neutron = by_key[(1, 1)][2], by_key[(0, 1)][2]

    out = []
    w = out.append
    w("// ============================================================================")
    w("// NOTICE: Full documentation, design decisions, and fix history for this file")
    w('// live in docs/nuclear_core.md, section "ame2020_data.rs"')
    w("// ============================================================================")
    w("// GENERATED FILE. Do not edit by hand.")
    w("// Regenerate from the repository root with: python scripts/gen_ame2020.py")
    w("//")
    w("// Source: the AME2020 atomic mass table (data/mass.mas20). M. Wang, W.J. Huang,")
    w("// F.G. Kondev, G. Audi, S. Naimi, Chinese Phys. C 45, 030003 (2021), and")
    w("// W.J. Huang et al., Chinese Phys. C 45, 030002 (2021).")
    w("//")
    w("// Each row is (z, a, mass excess in keV, uncertainty in keV, estimated).")
    w("// `estimated` is true where AME2020 marks a value as taken from systematic")
    w("// trends of the mass surface instead of measurement.")
    w("")
    w("// A data value can land close to a math constant by chance. That is not a bug.")
    w("#![allow(clippy::approx_constant)]")
    w("")
    w(f"pub(crate) const H1_MASS_EXCESS_KEV: f64 = {h1};")
    w(f"pub(crate) const NEUTRON_MASS_EXCESS_KEV: f64 = {neutron};")
    w(f"pub(crate) const ENTRY_COUNT: usize = {len(rows)};")
    w("")
    w("#[rustfmt::skip]")
    w("pub(crate) static RAW: [(u8, u16, f64, f64, bool); ENTRY_COUNT] = [")
    for z, a, mass, sigma, estimated in rows:
        w(f"    ({z}, {a}, {mass:.6f}, {sigma:.6f}, {'true' if estimated else 'false'}),")
    w("];")
    Path(args.out).write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")
    n_est = sum(1 for r in rows if r[4])
    print(f"wrote {len(rows)} rows ({n_est} estimated) to {args.out}")


if __name__ == "__main__":
    sys.exit(main())
