#!/usr/bin/env python3
"""Strip [dev-dependencies] and [[bench]] tables from a vendored crate's
Cargo.toml, leaving only [package], [lib], [dependencies], and [features].

Used by .github/workflows/vendor-mid-math.yml so the vendored copy of
mid-math never pulls in mid-math's own benchmark-only dependencies
(glam, nalgebra, criterion, etc). Standard library only, no pip installs.

Usage: strip_vendor_manifest.py <path-to-Cargo.toml>
"""
import re
import sys


def strip(text: str) -> str:
    # Matches a table header (regular or array-of-tables) plus everything
    # up to the next line that starts a new table, i.e. the next "[".
    pattern = re.compile(
        r"^\[dev-dependencies\].*?(?=^\[|\Z)"
        r"|^\[target\.'[^']*'\.dev-dependencies\].*?(?=^\[|\Z)"
        r"|^\[\[bench\]\].*?(?=^\[|\Z)",
        re.MULTILINE | re.DOTALL,
    )
    stripped = pattern.sub("", text)
    # Collapse runs of 3+ blank lines left behind by the removals.
    stripped = re.sub(r"\n{3,}", "\n\n", stripped)
    return stripped.strip() + "\n"


def main() -> None:
    if len(sys.argv) != 2:
        print("usage: strip_vendor_manifest.py <path-to-Cargo.toml>", file=sys.stderr)
        sys.exit(1)

    path = sys.argv[1]
    with open(path, "r", encoding="utf-8") as f:
        original = f.read()

    result = strip(original)

    with open(path, "w", encoding="utf-8") as f:
        f.write(result)

    print(f"Stripped dev-dependencies and bench targets from {path}")


if __name__ == "__main__":
    main()
