#!/usr/bin/env python3
# ============================================================================
# NOTICE: Full documentation, design decisions, and fix history for this file
# live in rust_lib/nuclear_core/docs/nuclear_core.md, section "CI and Workflows"
# ============================================================================
"""Generate rust_lib/nuclear_core/src/fission_yields_data.rs from the ENDF/B-VIII.0
fission product yields.

Reads rust_lib/nuclear_core/data/fission_yields.tsv, keeps the independent
yields, and writes one block of rows per (parent, origin, energy) set. Only the
Python standard library is needed.

Choices made here, all documented in docs/nuclear_core.md:
  * Only kind == independent rows are kept. Cumulative yields are not used.
  * A parent or product in an isomeric state (a _m1 suffix) is not modelled.
    A parent in an isomeric state is skipped, and a product in an isomeric
    state adds its yield to the ground state of the same (z, a).
  * Yields of MIN_YIELD or less are dropped. The yield dropped from a set is
    reported when the script runs.

Usage, from the repository root:
    python scripts/gen_fission_yields.py
"""
import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CRATE = ROOT / "rust_lib" / "nuclear_core"

MIN_YIELD = 1e-9
GNDS = re.compile(r"^([A-Z][a-z]?)(\d+)(?:_m(\d+))?$")


def load_symbols():
    """Element symbols from elements.rs, so there is one list in the repository."""
    src = (CRATE / "src" / "elements.rs").read_text(encoding="utf-8")
    block = re.search(r"const SYMBOLS: \[&str; 118\] = \[(.*?)\];", src, re.S).group(1)
    symbols = re.findall(r'"([A-Za-z]+)"', block)
    assert len(symbols) == 118, len(symbols)
    return ["n"] + symbols  # index = Z, with the neutron at Z = 0


def split_gnds(name, index):
    """(z, a, isomer) of a GNDS nuclide name such as Cs137 or Ba137_m1."""
    m = GNDS.match(name)
    assert m, f"unreadable nuclide name: {name!r}"
    return index[m.group(1)], int(m.group(2)), m.group(3)


def parse(path, symbols):
    index = {s: z for z, s in enumerate(symbols)}
    sets = {}
    for line in path.read_text(encoding="utf-8").split("\n"):
        if not line or line.startswith("#"):
            continue
        fields = line.split("\t")
        assert len(fields) == 7, f"expected 7 columns: {line[:60]!r}"
        parent, origin, kind, energy, daughter, value, _sigma = fields
        if kind != "independent":
            continue
        pz, pa, pm = split_gnds(parent, index)
        if pm is not None:
            continue
        assert origin in ("n", "sf"), origin
        key = (pz, pa, origin == "sf", float(energy))
        z, a, _ = split_gnds(daughter, index)
        rows = sets.setdefault(key, {})
        rows[(z, a)] = rows.get((z, a), 0.0) + float(value)
    return sets


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", default=str(CRATE / "data" / "fission_yields.tsv"))
    ap.add_argument("--out", default=str(CRATE / "src" / "fission_yields_data.rs"))
    args = ap.parse_args()

    sets = parse(Path(args.input), load_symbols())
    keys = sorted(sets)
    rows_out = []
    set_rows = []
    worst_loss = 0.0
    for key in keys:
        start = len(rows_out)
        total = sum(sets[key].values())
        kept = 0.0
        for (z, a), value in sorted(sets[key].items()):
            if value > MIN_YIELD:
                rows_out.append((z, a, value))
                kept += value
        worst_loss = max(worst_loss, (total - kept) / total)
        set_rows.append((key, start, len(rows_out)))

    out = []
    w = out.append
    w("// ============================================================================")
    w("// NOTICE: Full documentation, design decisions, and fix history for this file")
    w('// live in docs/nuclear_core.md, section "fission_yields_data.rs"')
    w("// ============================================================================")
    w("// GENERATED FILE. Do not edit by hand.")
    w("// Regenerate from the repository root with: python scripts/gen_fission_yields.py")
    w("//")
    w("// Source: the ENDF/B-VIII.0 neutron-induced (nfy) and spontaneous (sfy) fission")
    w("// yield sublibraries, as bundled in data/fission_yields.tsv (the copy in the")
    w("// crates.io package nucleide-nuclei 0.16.0, BSD-2-Clause). Both sublibraries")
    w("// are unchanged carries of ENDF/B-VII.1, the England et al. ENDF-349")
    w("// evaluations, apart from Pu-239. ENDF/B-VIII.0 is a US government work")
    w("// (NNDC, Brookhaven National Laboratory).")
    w("//")
    w("// Independent yields only: the yield of a product per fission, after prompt")
    w("// neutron emission and before any beta decay. The yields of one set sum to 2.")
    w("// Isomeric parents are skipped and isomeric products are folded into the")
    w("// ground state. Yields of 1e-9 or less are dropped.")
    w("//")
    w("// SETS: (parent z, parent a, spontaneous, incident neutron energy in eV or 0.0,")
    w("//        first ROWS index, one past the last ROWS index). For a neutron-induced")
    w("// set the parent is the target nucleus.")
    w("// ROWS: (product z, product a, yield per fission).")
    w("")
    w("// A data value can land close to a math constant by chance. That is not a bug.")
    w("#![allow(clippy::approx_constant)]")
    w("")
    w(f"pub(crate) const SET_COUNT: usize = {len(set_rows)};")
    w(f"pub(crate) const ROW_COUNT: usize = {len(rows_out)};")
    w("")
    w("pub(crate) type SetRow = (u8, u16, bool, f64, u32, u32);")
    w("pub(crate) type YieldRow = (u8, u16, f32);")
    w("")
    w("#[rustfmt::skip]")
    w("pub(crate) static SETS: [SetRow; SET_COUNT] = [")
    for (pz, pa, spontaneous, energy), start, end in set_rows:
        w(f"    ({pz}, {pa}, {'true' if spontaneous else 'false'}, {energy!r}, {start}, {end}),")
    w("];")
    w("")
    w("#[rustfmt::skip]")
    w("pub(crate) static ROWS: [YieldRow; ROW_COUNT] = [")
    for z, a, value in rows_out:
        w(f"    ({z}, {a}, {value:.5e}),")
    w("];")
    Path(args.out).write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")
    print(
        f"wrote {len(set_rows)} sets and {len(rows_out)} rows to {args.out} "
        f"(largest dropped share of a set: {worst_loss:.1e})"
    )


if __name__ == "__main__":
    sys.exit(main())
