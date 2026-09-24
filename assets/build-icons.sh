#!/usr/bin/env bash
# Draws the icon at every size the three platforms want and packs it three ways.
# Run from the repository root; needs a Mac, because the drawing is CoreGraphics and
# .icns is made by iconutil. The three files it writes are committed, so nobody else
# has to have one.
set -euo pipefail
cd "$(dirname "$0")/.."

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

swift assets/draw-icon.swift "$work"

# Linux, and the window icon inside the app.
cp "$work/icon-1024.png" assets/yourstruly.png
cp "$work/icon-256.png"  src/YoursTruly.App/app-icon.png

# The mark on its own, transparent, for a page or a README.
cp "$work/mark.png" assets/mark.png

# macOS.
set=$work/YoursTruly.iconset
mkdir -p "$set"
for s in 16 32 128 256 512; do
  cp "$work/icon-$s.png" "$set/icon_${s}x${s}.png"
  cp "$work/icon-$((s*2)).png" "$set/icon_${s}x${s}@2x.png"
done
iconutil -c icns "$set" -o assets/yourstruly.icns

# Windows. An .ico is a header, one directory entry per size, then the PNGs
# themselves — small enough to write out rather than take a dependency for.
python3 - "$work" <<'PY'
import struct, sys, pathlib
work = pathlib.Path(sys.argv[1])
sizes = [16, 32, 48, 64, 128, 256]
blobs = [(s, (work / f"icon-{s}.png").read_bytes()) for s in sizes]
out = bytearray(struct.pack("<HHH", 0, 1, len(blobs)))
offset = 6 + 16 * len(blobs)
for s, b in blobs:
    out += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(b), offset)
    offset += len(b)
for _, b in blobs:
    out += b
pathlib.Path("assets/yourstruly.ico").write_bytes(out)
PY

echo "wrote assets/yourstruly.{png,ico,icns} and src/YoursTruly.App/app-icon.png"
