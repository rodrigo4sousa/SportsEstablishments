#!/usr/bin/env bash
# Checks the docs without rendering anything:
#  1. every .svg in docs/architecture/out/ is referenced exactly once in docs/architecture/README.md;
#  2. docs/architecture/README.md does not reference a diagram that does not exist;
#  3. every .puml in docs/architecture/*/ includes the shared style (!include ../style.puml);
#  4. every relative link (and src/href) in the root README.md and in docs/**/*.md points to an existing file
#     (the #anchor part is not checked).
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

# 1. Each SVG appears exactly once in the README.
shopt -s nullglob
svgs=("$docs"/out/*.svg)
for svg in "${svgs[@]}"; do
  name="$(basename "$svg")"
  count="$(grep -o "out/$name" "$readme" | wc -l | tr -d ' ')"
  if [ "$count" -ne 1 ]; then
    fail "out/$name is referenced $count times in docs/architecture/README.md (expected exactly 1)"
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
while IFS= read -r f; do files+=("$f"); done < <(find "$root/docs" -name '*.md' | sort)
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

if [ "$errors" -ne 0 ]; then
  echo "$errors problem(s) in the docs." >&2
  exit 1
fi
echo "Docs OK: ${#svgs[@]} diagram(s), all referenced once; ${#files[@]} Markdown file(s), links resolve."
