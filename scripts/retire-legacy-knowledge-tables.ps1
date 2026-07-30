[CmdletBinding()]
param(
    [switch]$ConfirmRetirement,
    [string]$DatabaseUser = "speroflow",
    [string]$DatabaseName = "speroflow"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

Write-Host "=== Knowledge Legacy Table Retirement Helper ===" -ForegroundColor Cyan

$sqlFile = Join-Path $PSScriptRoot "retire-legacy-knowledge-tables.sql"
if (-not (Test-Path $sqlFile)) {
    Write-Error "Script file not found: $sqlFile"
}

if (-not $ConfirmRetirement) {
    Write-Host "[DRY-RUN] Legacy knowledge tables scheduled for retirement:" -ForegroundColor Yellow
    Write-Host "  - app.dataset_ingestion_jobs"
    Write-Host "  - app.knowledge_source_files"
    Write-Host "  - app.knowledge_datasets"
    Write-Host ""
    Write-Host "To execute retirement on host PostgreSQL, run:" -ForegroundColor Yellow
    Write-Host "  powershell -File scripts/retire-legacy-knowledge-tables.ps1 -ConfirmRetirement"
    exit 0
}

Write-Host "Executing legacy table retirement against $DatabaseName PostgreSQL database..." -ForegroundColor Cyan

if (Get-Command docker -ErrorAction SilentlyContinue) {
    Get-Content $sqlFile | docker compose exec -T postgres psql -U $DatabaseUser -d $DatabaseName
    if ($LASTEXITCODE -eq 0) {
        Write-Host "SUCCESS: Legacy knowledge tables retired cleanly from main database." -ForegroundColor Green
    } else {
        Write-Error "Failed to execute retirement script via docker compose exec."
    }
} else {
    Write-Error "Docker CLI not found; execute $sqlFile manually against PostgreSQL."
}
