#!/usr/bin/env bash
# Renders every diagram in docs/architecture/*/ to SVG in docs/architecture/out/.
# Uses the plantuml/plantuml Docker image whose tag is pinned in style.puml (line "' plantuml-version: X.Y.Z"), so
# the same input gives the same bytes on every machine and in the CI. The font (Liberation Sans, see style.puml) is
# vendored in fonts/ and mounted read-only: no host font and no network are involved. postprocess-svg.sh then widens
# the font name in every SVG to a stack that starts with Arial.
# Works in Git Bash on Windows, macOS and Linux. Run from anywhere: bash docs/architecture/render.sh
set -euo pipefail

dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

version="$(sed -n "s/^' plantuml-version: \([0-9][0-9.]*\)\r\{0,1\}$/\1/p" "$dir/style.puml" | head -n 1)"
if [ -z "$version" ]; then
  echo "$dir/style.puml: missing the line \"' plantuml-version: X.Y.Z\"" >&2
  exit 1
fi

# Git Bash needs a Windows path for the Docker volume and must not rewrite the container paths.
to_host_path() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
export MSYS_NO_PATHCONV=1

docker run --rm -v "$(to_host_path "$dir"):/data" \
  -v "$(to_host_path "$dir/fonts"):/usr/share/fonts/truetype/liberation-vendored:ro" -w /data \
  "plantuml/plantuml:$version" -tsvg -failonerror "/data/*/*.puml" -o ../out
docker run --rm -v "$(to_host_path "$dir"):/data" -w /data --entrypoint sh \
  "plantuml/plantuml:$version" /data/postprocess-svg.sh
echo "Rendered to docs/architecture/out/ with plantuml/plantuml:$version"
