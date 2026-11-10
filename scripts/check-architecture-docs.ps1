# Checks the docs without rendering anything:
#  1. every .svg in docs/architecture/out/ is linked at least once from docs/architecture/README.md;
#  2. docs/architecture/README.md does not reference a diagram that does not exist;
#  3. every .puml in docs/architecture/*/ includes the shared style (!include ../style.puml);
#  4. every relative link (and src/href) in the root README.md and in docs/**/*.md points to an existing file
#     (the #anchor part is not checked);
#  5. no .svg in docs/architecture/out/ mentions DejaVu;
#  6. no .puml under docs/architecture/4plus1/ includes anything from C4/ (C4 includes belong in c4/);
#  7. the PlantUML aliases and section 12 of docs/architecture/inventory.md agree in both directions: every alias
#     drawn in a diagram is listed for that diagram, every listed alias is drawn there, and every id in that table
#     is an element of the inventory. Ids live in aliases, so label text is not checked.
$ErrorActionPreference = 'Stop'

$root = Join-Path $PSScriptRoot '..' | Resolve-Path | Select-Object -ExpandProperty Path
$docs = Join-Path $root 'docs\architecture'
$readme = Join-Path $docs 'README.md'
if (-not (Test-Path $readme)) { throw "Missing $readme" }
$readmeText = Get-Content -Path $readme -Raw -Encoding UTF8
$errors = New-Object System.Collections.Generic.List[string]

# 1. Each SVG is linked at least once from the README.
$svgs = @(Get-ChildItem -Path (Join-Path $docs 'out') -Filter '*.svg')
foreach ($svg in $svgs) {
    $count = [regex]::Matches($readmeText, [regex]::Escape("out/$($svg.Name)")).Count
    if ($count -lt 1) {
        $errors.Add("out/$($svg.Name) is not linked from docs/architecture/README.md")
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

# 5. The diagrams carry the vendored font stack, never the font of the PlantUML image.
foreach ($svg in $svgs) {
    if (Select-String -Path $svg.FullName -Pattern 'DejaVu' -SimpleMatch -Quiet) {
        $errors.Add("out/$($svg.Name) mentions DejaVu; render with docs/architecture/render.sh")
    }
}

# 6. C4 includes live only in c4/.
foreach ($puml in Get-ChildItem -Path (Join-Path $docs '4plus1') -Filter '*.puml') {
    if (Select-String -Path $puml.FullName -Pattern '^\s*!include\s*<C4/' -Quiet) {
        $errors.Add("$($puml.FullName.Substring($root.Length + 1)) includes C4; C4 includes belong in docs/architecture/c4/")
    }
}

# 7. Aliases and the inventory's table agree.
$macros = 'Person|Person_Ext|System|System_Ext|Container|Container_Ext|ContainerDb|Component|ComponentDb|Boundary|Container_Boundary|System_Boundary|Deployment_Node'
$keywords = 'participant|actor|database|class|enum|package|folder|node|component|artifact|rectangle|usecase'
$drawn = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($puml in @(Get-ChildItem -Path (Join-Path $docs 'c4') -Filter '*.puml') + @(Get-ChildItem -Path (Join-Path $docs '4plus1') -Filter '*.puml')) {
    foreach ($line in Get-Content -Path $puml.FullName -Encoding UTF8) {
        if ($line -cmatch "^\s*($macros)\(([A-Za-z0-9_]+),") { [void]$drawn.Add("$($Matches[2]) $($puml.BaseName)") }
        elseif ($line -cmatch "^\s*($keywords)\s+(`"[^`"]*`"|\S+)\s+as\s+([A-Za-z0-9_]+)") { [void]$drawn.Add("$($Matches[3]) $($puml.BaseName)") }
    }
}
$inventoryLines = Get-Content -Path (Join-Path $docs 'inventory.md') -Encoding UTF8
$section = [array]::FindIndex([string[]]$inventoryLines, [Predicate[string]]{ param($l) $l -cmatch '^## 12\.' })
if ($section -lt 0) { $errors.Add('docs/architecture/inventory.md has no section 12') ; $section = $inventoryLines.Count }
$ids = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($line in $inventoryLines[0..($section - 1)]) {
    if ($line -cmatch '^\| ([A-Z]+(-[A-Z0-9]+)+) \|') { [void]$ids.Add($Matches[1]) }
}
$listed = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($line in $inventoryLines | Select-Object -Skip ($section + 1)) {
    if ($line -cmatch '^## ') { break }
    if ($line -cnotmatch '^\| `') { continue }
    $cols = $line -split '\|'
    $alias = $cols[1].Trim().Trim('`')
    $id = $cols[2].Trim()
    if ($id -ne [string][char]0x2014 -and -not $ids.Contains($id)) {
        $errors.Add("docs/architecture/inventory.md section 12 uses id $id, which is not an element of the inventory")
    }
    foreach ($diagram in $cols[4] -split ',') { [void]$listed.Add("$alias $($diagram.Trim())") }
}
foreach ($pair in $drawn) {
    if (-not $listed.Contains($pair)) {
        $parts = $pair -split ' '
        $errors.Add("$($parts[1]) draws alias $($parts[0]), which docs/architecture/inventory.md section 12 does not list for it")
    }
}
foreach ($pair in $listed) {
    if (-not $drawn.Contains($pair)) {
        $parts = $pair -split ' '
        $errors.Add("docs/architecture/inventory.md section 12 lists alias $($parts[0]) in $($parts[1]), which does not draw it")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { [Console]::Error.WriteLine("ERROR: $_") }
    [Console]::Error.WriteLine("$($errors.Count) problem(s) in the docs.")
    exit 1
}
Write-Host "Docs OK: $($svgs.Count) diagram(s), all linked from the architecture README, aliases match the inventory; $($files.Count) Markdown file(s), links resolve."
