[CmdletBinding()]
param(
    [int]$RetentionDays = 7,
    [switch]$ConfirmPurge
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$backupsDir = Join-Path $root "backups"

Write-Host "=== SperoFlow Backup Retention & Rotation ===" -ForegroundColor Cyan

if (-not (Test-Path $backupsDir)) {
    Write-Host "No backups directory found at $backupsDir." -ForegroundColor Yellow
    exit 0
}

$cutoffDate = (Get-Date).AddDays(-$RetentionDays)
Write-Host "Retention policy: $RetentionDays days. Cutoff threshold: $cutoffDate" -ForegroundColor Yellow

$backupItems = Get-ChildItem -Path $backupsDir -Directory | Where-Object { $_.CreationTime -lt $cutoffDate }

if ($backupItems.Count -eq 0) {
    Write-Host "No backup directories older than $RetentionDays days found." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($backupItems.Count) backup directories older than $RetentionDays days:" -ForegroundColor Red
foreach ($item in $backupItems) {
    Write-Host "  - $($item.Name) (Created: $($item.CreationTime))"
}

if (-not $ConfirmPurge) {
    Write-Host ""
    Write-Host "[DRY-RUN] To purge expired backups, run:" -ForegroundColor Yellow
    Write-Host "  powershell -File scripts/rotate-backups.ps1 -RetentionDays $RetentionDays -ConfirmPurge"
    exit 0
}

Write-Host "Purging expired backups..." -ForegroundColor Red
foreach ($item in $backupItems) {
    Remove-Item -Path $item.FullName -Recurse -Force
    Write-Host "Purged: $($item.Name)" -ForegroundColor Green
}

Write-Host "Backup rotation complete." -ForegroundColor Green
