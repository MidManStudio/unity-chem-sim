#!/usr/bin/env python3
"""Summarize criterion benchmark results into a Markdown table.

Reads target/criterion/**/new/estimates.json (criterion's own machine-
readable output — more robust than parsing its human-readable terminal
text, which line-wraps differently depending on benchmark name length,
as seen firsthand in an actual run of this bench). Prints Markdown to
stdout; the workflow redirects that into $GITHUB_STEP_SUMMARY.

Never raises past main() — a parse problem becomes a row saying so, not
a failed CI step, since the raw output is still captured separately as
a fallback.

Stdlib only, no pip install.
"""
import json
import sys
from pathlib import Path

CRITERION_DIR = Path("target/criterion")


def fmt_ns(ns: float) -> str:
    if ns >= 1_000_000:
        return f"{ns / 1_000_000:.3f} ms"
    if ns >= 1_000:
        return f"{ns / 1_000:.3f} \u00b5s"
    return f"{ns:.1f} ns"


def main() -> None:
    if not CRITERION_DIR.exists():
        print(f"_No `{CRITERION_DIR}` directory — did the bench step run?_")
        return

    rows = []
    for est_path in sorted(CRITERION_DIR.glob("**/new/estimates.json")):
        # Label = path relative to target/criterion, minus the trailing
        # "new/estimates.json" — works regardless of how deeply criterion
        # nests group/function/value directories for this benchmark.
        rel = est_path.relative_to(CRITERION_DIR)
        label = "/".join(rel.parts[:-2])
        try:
            data = json.loads(est_path.read_text())
            mean_ns = data["mean"]["point_estimate"]
            median_ns = data["median"]["point_estimate"]
            rows.append((label, fmt_ns(mean_ns), fmt_ns(median_ns)))
        except Exception as e:  # noqa: BLE001 — deliberately broad, see docstring
            rows.append((label, f"(parse error: {e})", "-"))

    if not rows:
        print("_No criterion `estimates.json` files found under "
              f"`{CRITERION_DIR}`._")
        return

    print("| Benchmark | Mean | Median |")
    print("|---|---|---|")
    for label, mean, median in rows:
        print(f"| `{label}` | {mean} | {median} |")


if __name__ == "__main__":
    main()
