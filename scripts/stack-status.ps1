<#
.SYNOPSIS
  Aggregate Docker Compose health for main + knowledge stacks.

.DESCRIPTION
  Prints container health, restart counts, and a simple pass/fail summary for ops.
  Safe to run without application credentials.
#>
param(
    [switch]$FailOnUnhealthy
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker is required for stack-status.ps1."
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

Write-Host "=== SperoFlow stack status ===" -ForegroundColor Cyan
Write-Host ("Timestamp: {0}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss K"))

$rows = @()
$json = docker ps -a --format "{{json .}}" 2>$null
if (-not $json) {
    Write-Warning "No containers found (docker ps empty)."
    if ($FailOnUnhealthy) { throw "No containers running." }
    return
}

foreach ($line in $json) {
    try {
        $c = $line | ConvertFrom-Json
    } catch {
        continue
    }
    $name = [string]$c.Names
    if ($name -notmatch '(?i)speroflow|knowledge') {
        continue
    }
    $health = "none"
    if ($c.Status -match '\((healthy)\)') { $health = "healthy" }
    elseif ($c.Status -match '\((unhealthy)\)') { $health = "unhealthy" }
    elseif ($c.Status -match '\((health: starting)\)') { $health = "starting" }
    elseif ($c.Status -match '^Up') { $health = "up" }
    elseif ($c.Status -match 'Exited') { $health = "exited" }

    $rows += [pscustomobject]@{
        Name    = $name
        Health  = $health
        Status  = $c.Status
        Ports   = $c.Ports
    }
}

if ($rows.Count -eq 0) {
    Write-Warning "No SperoFlow/knowledge containers matched."
} else {
    $rows | Sort-Object Name | Format-Table -AutoSize Name, Health, Status | Out-String | Write-Host
}

$unhealthy = @($rows | Where-Object { $_.Health -eq "unhealthy" -or $_.Health -eq "exited" })
$starting = @($rows | Where-Object { $_.Health -eq "starting" })
$healthyish = @($rows | Where-Object { $_.Health -in @("healthy", "up") })

Write-Host ("Summary: {0} ok/up, {1} starting, {2} unhealthy/exited, {3} total matched" -f `
    $healthyish.Count, $starting.Count, $unhealthy.Count, $rows.Count)

# Host port audit (same idea as smoke-release)
$published = docker ps --format "{{.Names}} {{.Ports}}" 2>$null
$badPorts = @()
foreach ($line in $published) {
    if ($line -match '0\.0\.0\.0:(5432|6379|7474|7687|9000|9001|8000|8080)->') {
        $badPorts += $line
    }
}
if ($badPorts.Count -gt 0) {
    Write-Host "WARNING: private services appear published on the host:" -ForegroundColor Yellow
    $badPorts | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
} else {
    Write-Host "OK  no forbidden private host ports detected" -ForegroundColor Green
}

if ($FailOnUnhealthy -and ($unhealthy.Count -gt 0 -or $badPorts.Count -gt 0)) {
    throw "Stack status check failed."
}

Write-Host "stack-status complete." -ForegroundColor Green
