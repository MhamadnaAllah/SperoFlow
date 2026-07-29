<#
.SYNOPSIS
  Ensure secrets-catalog.json covers every Compose secret file reference.

.DESCRIPTION
  Parses compose.yaml, knowledge-platform/compose.yaml, and compose.admin-bootstrap.yaml
  for `file: .../infrastructure/secrets/<name>` entries and compares them to the catalog.
#>
param()

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "aws-secrets-lib.ps1")

$root = Get-SperoFlowRepoRoot
$catalog = Get-SperoFlowSecretsCatalog -Root $root
$catalogNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($s in $catalog.secrets) {
    [void]$catalogNames.Add([string]$s.name)
}

$composeFiles = @(
    (Join-Path $root "compose.yaml"),
    (Join-Path $root "compose.admin-bootstrap.yaml"),
    (Join-Path $root "knowledge-platform\compose.yaml")
)

$composeNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$fileRefPattern = 'file:\s*(?:\./|\.\./)?infrastructure/secrets/([A-Za-z0-9_.-]+)'

foreach ($cf in $composeFiles) {
    if (-not (Test-Path -LiteralPath $cf)) {
        throw "Compose file missing: $cf"
    }
    $text = Get-Content -LiteralPath $cf -Raw
    $matches = [regex]::Matches($text, $fileRefPattern)
    foreach ($m in $matches) {
        [void]$composeNames.Add($m.Groups[1].Value)
    }
}

$missingInCatalog = @($composeNames | Where-Object { -not $catalogNames.Contains($_) } | Sort-Object)
$extraInCatalog = @($catalogNames | Where-Object { -not $composeNames.Contains($_) } | Sort-Object)

Write-Host "Compose secret file refs: $($composeNames.Count)" -ForegroundColor Cyan
Write-Host "Catalog entries: $($catalogNames.Count)" -ForegroundColor Cyan

$failed = $false
if ($missingInCatalog.Count -gt 0) {
    $failed = $true
    Write-Host "Missing from catalog (referenced by Compose):" -ForegroundColor Red
    $missingInCatalog | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}

# Optional extras in catalog are OK only if marked optional or documented; warn otherwise.
$warnExtras = @()
foreach ($extra in $extraInCatalog) {
    $entry = $catalog.secrets | Where-Object { $_.name -eq $extra } | Select-Object -First 1
    if ($entry -and $entry.optional) {
        Write-Host "OK optional catalog-only: $extra" -ForegroundColor DarkYellow
    } else {
        $warnExtras += $extra
    }
}
if ($warnExtras.Count -gt 0) {
    Write-Host "In catalog but not referenced by Compose (review):" -ForegroundColor Yellow
    $warnExtras | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}

if ($failed) {
    throw "Secrets catalog is out of sync with Compose."
}

Write-Host "Secrets catalog matches Compose file secret references." -ForegroundColor Green
