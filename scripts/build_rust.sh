#!/usr/bin/env bash
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$REPO/Assets/Plugins"
mkdir -p "$OUT"

echo "→ Building chemistry_core (release)..."
cargo build -p chemistry_core --release

echo "→ Copying to Assets/Plugins/..."
if [[ "$OSTYPE" == msys* || "$OSTYPE" == cygwin* || "$OSTYPE" == win32* ]]; then
    cp "$REPO/target/release/chemistry_core.dll" "$OUT/"
elif [[ "$OSTYPE" == darwin* ]]; then
    cp "$REPO/target/release/libchemistry_core.dylib" "$OUT/"
else
    cp "$REPO/target/release/libchemistry_core.so" "$OUT/"
fi

echo "✓ Done — DLL in Assets/Plugins/"
