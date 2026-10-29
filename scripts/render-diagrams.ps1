# Renders docs/architecture/diagrams/*.puml to .svg next to each source, with the PlantUML Docker image whose tag is
# pinned in the header of every .puml (line "' plantuml-version: X.Y.Z"). Same input, same bytes.
$ErrorActionPreference = 'Stop'

$dir = Join-Path $PSScriptRoot '..\docs\architecture\diagrams' | Resolve-Path | Select-Object -ExpandProperty Path
$sources = Get-ChildItem -Path $dir -Filter '*.puml'
if ($sources.Count -eq 0) { throw "No .puml files in $dir" }

$version = $null
foreach ($file in $sources) {
    $line = Get-Content -Path $file.FullName | Where-Object { $_ -match "^' plantuml-version: (\d[\d.]*)\s*$" } | Select-Object -First 1
    if (-not $line) { throw "$($file.FullName): missing the header line `"' plantuml-version: X.Y.Z`"" }
    $found = $Matches[1]
    if ($version -and $found -ne $version) { throw "$($file.FullName): PlantUML version $found differs from $version; all diagrams must use one version" }
    $version = $found
}

docker run --rm -v "${dir}:/data" "plantuml/plantuml:$version" -tsvg -failonerror '/data/*.puml'
if ($LASTEXITCODE -ne 0) { throw "PlantUML failed with exit code $LASTEXITCODE" }
Write-Host "Rendered $($sources.Count) diagram(s) with plantuml/plantuml:$version"
