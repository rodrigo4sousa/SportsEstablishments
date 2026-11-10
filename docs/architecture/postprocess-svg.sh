#!/bin/sh
# Runs inside the plantuml container (render.sh and render.ps1 call it after PlantUML), so the tools are the same
# everywhere. PlantUML measures the text with the vendored Liberation Sans and writes that one name in the SVG; this
# widens it to a font stack, so a browser uses Arial (same metrics) when it has it. Writes in place through a temporary
# file outside the mounted folder: PlantUML only needs to be able to write the .svg, and so does this.
set -eu

tmp="$(mktemp)"
for svg in /data/out/*.svg; do
  sed 's/font-family="Liberation Sans"/font-family="Arial, Liberation Sans, Helvetica, sans-serif"/g' "$svg" > "$tmp"
  cat "$tmp" > "$svg"
done
rm -f "$tmp"
