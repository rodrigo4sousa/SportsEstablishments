# Checks the docs without rendering anything:
#  1. every .svg in docs/architecture/out/ is referenced exactly once in docs/architecture/README.md;
#  2. docs/architecture/README.md does not reference a diagram that does not exist;
#  3. every .puml in docs/architecture/*/ includes the shared style (!include ../style.puml);
#  4. every relative link (and src/href) in the root README.md and in docs/**/*.md points to an existing file
#     (the #anchor part is not checked).
$ErrorActionPreference = 'Stop'

$root = Join-Path $PSScriptRoot '..' | Resolve-Path | Select-Object -ExpandProperty Path
$docs = Join-Path $root 'docs\architecture'
$readme = Join-Path $docs 'README.md'
if (-not (Test-Path $readme)) { throw "Missing $readme" }
$readmeText = Get-Content -Path $readme -Raw -Encoding UTF8
$errors = New-Object System.Collections.Generic.List[string]

# 1. Each SVG appears exactly once in the README.
$svgs = @(Get-ChildItem -Path (Join-Path $docs 'out') -Filter '*.svg')
foreach ($svg in $svgs) {
    $count = [regex]::Matches($readmeText, [regex]::Escape("out/$($svg.Name)")).Count
    if ($count -ne 1) {
        $errors.Add("out/$($svg.Name) is referenced $count times in docs/architecture/README.md (expected exactly 1)")
    }
}

# 2. Every diagram the README names exists.
$refs = [regex]::Matches($readmeText, 'out/[A-Za-z0-9._-]*\.svg') | ForEach-Object { $_.Value } | Sort-Object -Unique
foreach ($ref in $refs) {
    if (-not (Test-Path (Join-Path $docs $ref))) {
        $errors.Add("docs/architecture/README.md references $ref, which does not exist")
    }
}

# 3. Every diagram includes the shared style.
foreach ($puml in Get-ChildItem -Path $docs -Filter '*.puml' -Recurse | Where-Object { $_.Directory.Parent.FullName -eq $docs }) {
    if (-not (Select-String -Path $puml.FullName -Pattern '^!include \.\./style\.puml' -Quiet)) {
        $errors.Add("$($puml.FullName.Substring($root.Length + 1)) does not include ../style.puml")
    }
}

# 4. Relative links resolve. Markdown links "](target)" and HTML src/href attributes.
$files = @(Get-Item -Path (Join-Path $root 'README.md')) + @(Get-ChildItem -Path (Join-Path $root 'docs') -Filter '*.md' -Recurse)
foreach ($file in $files) {
    $text = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    $targets = @()
    $targets += [regex]::Matches($text, '\]\(([^)]*)\)') | ForEach-Object { ($_.Groups[1].Value -replace ' ".*$', '') }
    $targets += [regex]::Matches($text, '(?:src|href)="([^"]*)"') | ForEach-Object { $_.Groups[1].Value }
    foreach ($target in $targets) {
        $path = ($target -split '#')[0]
        if ($path -eq '' -or $path -match '^(https?:|mailto:)') { continue }
        if (-not (Test-Path -LiteralPath (Join-Path $file.DirectoryName $path))) {
            $relative = $file.FullName.Substring($root.Length + 1)
            $errors.Add("${relative}: link to $path does not resolve")
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { [Console]::Error.WriteLine("ERROR: $_") }
    [Console]::Error.WriteLine("$($errors.Count) problem(s) in the docs.")
    exit 1
}
Write-Host "Docs OK: $($svgs.Count) diagram(s), all referenced once; $($files.Count) Markdown file(s), links resolve."
