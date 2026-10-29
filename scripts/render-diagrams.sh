#!/usr/bin/env bash
# Renders docs/architecture/diagrams/*.puml to .svg next to each source, with the PlantUML Docker image whose tag is
# pinned in the header of every .puml (line "' plantuml-version: X.Y.Z"). Same input, same bytes.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dir="$root/docs/architecture/diagrams"

shopt -s nullglob
sources=("$dir"/*.puml)
if [ "${#sources[@]}" -eq 0 ]; then
  echo "No .puml files in $dir" >&2
  exit 1
fi

version=""
for file in "${sources[@]}"; do
  found="$(sed -n "s/^' plantuml-version: \([0-9][0-9.]*\)\r\{0,1\}$/\1/p" "$file" | head -n 1)"
  if [ -z "$found" ]; then
    echo "$file: missing the header line \"' plantuml-version: X.Y.Z\"" >&2
    exit 1
  fi
  if [ -n "$version" ] && [ "$found" != "$version" ]; then
    echo "$file: PlantUML version $found differs from $version; all diagrams must use one version" >&2
    exit 1
  fi
  version="$found"
done

# MSYS_NO_PATHCONV stops Git Bash on Windows from rewriting the container path "/data".
mount="$dir"
if command -v cygpath >/dev/null 2>&1; then
  mount="$(cygpath -w "$dir")"
fi
MSYS_NO_PATHCONV=1 docker run --rm -v "$mount:/data" "plantuml/plantuml:$version" -tsvg -failonerror "/data/*.puml"
echo "Rendered ${#sources[@]} diagram(s) with plantuml/plantuml:$version"
