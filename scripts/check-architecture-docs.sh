#!/usr/bin/env bash
# Checks the docs without rendering anything:
#  1. every .svg in docs/architecture/out/ is linked at least once from docs/architecture/README.md;
#  2. docs/architecture/README.md does not reference a diagram that does not exist;
#  3. every .puml in docs/architecture/*/ includes the shared style (!include ../style.puml);
#  4. every relative link (and src/href) in the root README.md and in docs/**/*.md points to an existing file
#     (folders named node_modules, at any depth, are not scanned: a local docs/demo/node_modules is not documentation)
#     (the #anchor part is not checked);
#  5. no .svg in docs/architecture/out/ mentions DejaVu;
#  6. no .puml under docs/architecture/4plus1/ includes anything from C4/ (C4 includes belong in c4/);
#  7. the PlantUML aliases and section 12 of docs/architecture/inventory.md agree in both directions: every alias
#     drawn in a diagram is listed for that diagram, every listed alias is drawn there, and every id in that table
#     is an element of the inventory. Ids live in aliases, so label text is not checked.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
docs="$root/docs/architecture"
readme="$docs/README.md"
errors=0

fail() {
  echo "ERROR: $*" >&2
  errors=$((errors + 1))
}

[ -f "$readme" ] || { echo "Missing $readme" >&2; exit 1; }

# 1. Each SVG is linked at least once from the README.
shopt -s nullglob
svgs=("$docs"/out/*.svg)
for svg in "${svgs[@]}"; do
  name="$(basename "$svg")"
  count="$(grep -o "out/$name" "$readme" | wc -l | tr -d ' ')"
  if [ "$count" -lt 1 ]; then
    fail "out/$name is not linked from docs/architecture/README.md"
  fi
done

# 2. Every diagram the README names exists.
while IFS= read -r ref; do
  [ -f "$docs/$ref" ] || fail "docs/architecture/README.md references $ref, which does not exist"
done < <(grep -o 'out/[A-Za-z0-9._-]*\.svg' "$readme" | sort -u)

# 3. Every diagram includes the shared style.
for puml in "$docs"/*/*.puml; do
  grep -q '^!include \.\./style\.puml' "$puml" || fail "${puml#"$root"/} does not include ../style.puml"
done

# 4. Relative links resolve. Markdown links "](target)" and HTML src/href attributes.
files=("$root/README.md")
while IFS= read -r f; do files+=("$f"); done < <(find "$root/docs" -name node_modules -prune -o -name '*.md' -print | sort)
for file in "${files[@]}"; do
  dir="$(dirname "$file")"
  rel="${file#"$root"/}"
  while IFS= read -r target; do
    target="${target%%#*}"
    case "$target" in
      "" | http://* | https://* | mailto:*) continue ;;
    esac
    [ -e "$dir/$target" ] || fail "$rel: link to $target does not resolve"
  done < <(
    {
      grep -o ']([^)]*)' "$file" | sed -e 's/^](//' -e 's/)$//' -e 's/ ".*$//'
      grep -o -E '(src|href)="[^"]*"' "$file" | sed -e 's/^[a-z]*="//' -e 's/"$//'
    } || true
  )
done

# 5. The diagrams carry the vendored font stack, never the font of the PlantUML image.
for svg in "${svgs[@]}"; do
  if grep -q 'DejaVu' "$svg"; then
    fail "out/$(basename "$svg") mentions DejaVu; render with docs/architecture/render.sh"
  fi
done

# 6. C4 includes live only in c4/.
for puml in "$docs"/4plus1/*.puml; do
  if grep -q '^[[:space:]]*!include[[:space:]]*<C4/' "$puml"; then
    fail "${puml#"$root"/} includes C4; C4 includes belong in docs/architecture/c4/"
  fi
done

# 7. Aliases and the inventory's table agree.
inventory="$docs/inventory.md"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
macros='Person|Person_Ext|System|System_Ext|Container|Container_Ext|ContainerDb|Component|ComponentDb|Boundary|Container_Boundary|System_Boundary|Deployment_Node'
keywords='participant|actor|database|class|enum|package|folder|node|component|artifact|rectangle|usecase'
for puml in "$docs"/c4/*.puml "$docs"/4plus1/*.puml; do
  stem="$(basename "$puml" .puml)"
  sed -E -n \
    -e "s/^[[:space:]]*($macros)\(([A-Za-z0-9_]+),.*/\2 $stem/p" \
    -e "s/^[[:space:]]*($keywords)[[:space:]]+(\"[^\"]*\"|[^[:space:]]+)[[:space:]]+as[[:space:]]+([A-Za-z0-9_]+).*/\3 $stem/p" \
    "$puml"
done | LC_ALL=C sort -u > "$tmp/drawn"
awk '/^## 12\./ { in_table = 1; next } /^## / { in_table = 0 }
  in_table && /^\| `/ {
    split($0, cols, "|"); alias = cols[2]; gsub(/[ `]/, "", alias)
    count = split(cols[5], diagrams, ",")
    for (i = 1; i <= count; i++) { gsub(/ /, "", diagrams[i]); print alias, diagrams[i] }
  }' "$inventory" | LC_ALL=C sort -u > "$tmp/listed"
while read -r alias diagram; do
  fail "$diagram draws alias $alias, which docs/architecture/inventory.md section 12 does not list for it"
done < <(LC_ALL=C comm -23 "$tmp/drawn" "$tmp/listed")
while read -r alias diagram; do
  fail "docs/architecture/inventory.md section 12 lists alias $alias in $diagram, which does not draw it"
done < <(LC_ALL=C comm -13 "$tmp/drawn" "$tmp/listed")
awk '/^## 12\./ { exit } /^\| [A-Z]+(-[A-Z0-9]+)+ \|/ { split($0, cols, "|"); gsub(/ /, "", cols[2]); print cols[2] }' "$inventory" | LC_ALL=C sort -u > "$tmp/ids"
while read -r id; do
  fail "docs/architecture/inventory.md section 12 uses id $id, which is not an element of the inventory"
done < <(awk '/^## 12\./ { in_table = 1; next } /^## / { in_table = 0 }
  in_table && /^\| `/ { split($0, cols, "|"); id = cols[3]; gsub(/ /, "", id); if (id != "—") print id }' "$inventory" | LC_ALL=C sort -u | LC_ALL=C comm -23 - "$tmp/ids")

if [ "$errors" -ne 0 ]; then
  echo "$errors problem(s) in the docs." >&2
  exit 1
fi
echo "Docs OK: ${#svgs[@]} diagram(s), all linked from the architecture README, aliases match the inventory; ${#files[@]} Markdown file(s), links resolve."
