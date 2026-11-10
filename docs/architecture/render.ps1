# Renders every diagram in docs/architecture/*/ to SVG in docs/architecture/out/ (Windows PowerShell).
# Same as render.sh: the plantuml/plantuml image tag is pinned in style.puml (line "' plantuml-version: X.Y.Z"), and
# the font (Liberation Sans) is vendored in fonts/ and mounted read-only, so no host font and no network are involved.
# postprocess-svg.sh then widens the font name in every SVG to a stack that starts with Arial.
# Run from anywhere: powershell -ExecutionPolicy Bypass -File docs\architecture\render.ps1
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot

$line = Get-Content -Path (Join-Path $dir 'style.puml') | Where-Object { $_ -match "^' plantuml-version: (\d[\d.]*)\s*$" } | Select-Object -First 1
if (-not $line) { throw "$dir\style.puml: missing the line `"' plantuml-version: X.Y.Z`"" }
$version = $Matches[1]

$fonts = Join-Path $dir 'fonts'
docker run --rm -v "${dir}:/data" -v "${fonts}:/usr/share/fonts/truetype/liberation-vendored:ro" -w /data "plantuml/plantuml:$version" -tsvg -failonerror '/data/*/*.puml' -o ../out
if ($LASTEXITCODE -ne 0) { throw "PlantUML failed with exit code $LASTEXITCODE" }
docker run --rm -v "${dir}:/data" -w /data --entrypoint sh "plantuml/plantuml:$version" /data/postprocess-svg.sh
if ($LASTEXITCODE -ne 0) { throw "postprocess-svg.sh failed with exit code $LASTEXITCODE" }
"Rendered to docs/architecture/out/ with plantuml/plantuml:$version"
