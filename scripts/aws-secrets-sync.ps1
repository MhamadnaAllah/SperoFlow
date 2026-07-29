<#
.SYNOPSIS
  Pull secrets from AWS Secrets Manager and optionally validate Compose.

.DESCRIPTION
  Deploy-host helper: materialize infrastructure/secrets, then run compose config.
  Requires AWS CLI + instance role or profile for a real pull (use -DryRun offline).
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9-]+$')]
    [string]$Environment,

    [string]$Region = "",
    [switch]$AlsoRuntimeSecrets,
    [switch]$SkipComposeValidate,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$pullArgs = @{
    Environment = $Environment
}
if ($Region) { $pullArgs.Region = $Region }
if ($AlsoRuntimeSecrets) { $pullArgs.AlsoRuntimeSecrets = $true }
if ($DryRun) { $pullArgs.DryRun = $true }

& (Join-Path $PSScriptRoot "aws-secrets-pull.ps1") @pullArgs
if ($LASTEXITCODE -ne 0) {
    throw "aws-secrets-pull.ps1 failed."
}

if (-not $SkipComposeValidate -and -not $DryRun) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "validate-compose.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Compose validation failed after secrets pull."
    }
}

Write-Host "aws-secrets-sync complete for env=$Environment" -ForegroundColor Green
