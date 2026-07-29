<#
.SYNOPSIS
  Materialize Docker secret files from AWS Secrets Manager.

.DESCRIPTION
  Downloads speroflow/{Environment}/{name} into infrastructure/secrets (and
  optionally ./secrets) so existing Compose mounts keep working. Intended for
  EC2 boot with an instance role. Never prints secret values.

.PARAMETER Environment
  Logical environment (prod, staging, dev).

.PARAMETER SecretsDirectory
  Output directory for Compose file secrets.

.PARAMETER AlsoRuntimeSecrets
  Also mirror into ./secrets for local dual-path setups.

.PARAMETER Region
  AWS region.

.PARAMETER FailIfOptionalMissing
  Treat optional catalog secrets as required.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9-]+$')]
    [string]$Environment,

    [string]$SecretsDirectory = "",
    [string]$Region = "",
    [switch]$AlsoRuntimeSecrets,
    [switch]$FailIfOptionalMissing,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "aws-secrets-lib.ps1")

if (-not $DryRun) {
    Test-AwsCli
}

$root = Get-SperoFlowRepoRoot
if (-not $SecretsDirectory) {
    $SecretsDirectory = Join-Path $root "infrastructure\secrets"
}
$runtimeDir = Join-Path $root "secrets"
if (-not $Region) {
    $Region = if ($env:AWS_REGION) { $env:AWS_REGION } elseif ($env:AWS_DEFAULT_REGION) { $env:AWS_DEFAULT_REGION } else { "" }
}
if (-not $Region) {
    if ($DryRun) {
        $Region = "us-east-1"
    } else {
        throw "Pass -Region or set AWS_REGION."
    }
}

New-Item -ItemType Directory -Force -Path $SecretsDirectory | Out-Null
if ($AlsoRuntimeSecrets) {
    New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
}

$catalog = Get-SperoFlowSecretsCatalog -Root $root
$ok = 0
$skipped = 0

Write-Host "Pulling secrets from Secrets Manager env=$Environment region=$Region" -ForegroundColor Cyan

foreach ($entry in $catalog.secrets) {
    $name = [string]$entry.name
    $secretId = Get-SecretArnName -Environment $Environment -Name $name
    $encoding = Get-SecretEncoding -CatalogEntry $entry
    $optional = [bool]$entry.optional -and -not $FailIfOptionalMissing
    $dest = Join-Path $SecretsDirectory $name

    if ($DryRun) {
        Write-Host "DRY-RUN would fetch $secretId -> $dest" -ForegroundColor DarkCyan
        continue
    }

    $outJson = aws secretsmanager get-secret-value --secret-id $secretId --region $Region --output json 2>$null
    if ($LASTEXITCODE -ne 0) {
        if ($optional) {
            Write-Host "SKIP missing optional: $name" -ForegroundColor DarkYellow
            $skipped++
            continue
        }
        throw "Failed to get secret $secretId (required)."
    }

    $obj = $outJson | ConvertFrom-Json
    if ($encoding -eq "binary" -or $obj.SecretBinary) {
        # AWS CLI JSON returns SecretBinary as base64 string
        $b64 = [string]$obj.SecretBinary
        if ([string]::IsNullOrWhiteSpace($b64) -and $obj.SecretString) {
            # Some tools store binary as base64 in SecretString
            $b64 = [string]$obj.SecretString
        }
        if ([string]::IsNullOrWhiteSpace($b64)) {
            throw "Secret $secretId has no binary payload."
        }
        $bytes = [Convert]::FromBase64String($b64.Trim())
        [System.IO.File]::WriteAllBytes($dest, $bytes)
    } else {
        $text = [string]$obj.SecretString
        if ($null -eq $text) {
            throw "Secret $secretId has no SecretString."
        }
        Write-Utf8NoBomFile -Path $dest -Value $text
    }

    if ($AlsoRuntimeSecrets) {
        Copy-Item -LiteralPath $dest -Destination (Join-Path $runtimeDir $name) -Force
    }

    # Restrict ACL best-effort on Windows
    try {
        icacls $dest /inheritance:r /grant:r "$env:USERNAME:(R)" 2>$null | Out-Null
    } catch { }

    $ok++
    Write-Host "OK $name" -ForegroundColor Green
}

Write-Host "Pull complete. written=$ok skipped=$skipped dir=$SecretsDirectory" -ForegroundColor Cyan
Write-Host "Next: docker compose -f knowledge-platform/compose.yaml up -d ; docker compose -f compose.yaml -f compose.prod.yaml up -d" -ForegroundColor DarkCyan
