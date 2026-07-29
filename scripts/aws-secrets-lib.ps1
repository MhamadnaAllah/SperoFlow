# Shared helpers for AWS Secrets Manager push/pull (dot-source only).

function Get-SperoFlowRepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-SperoFlowSecretsCatalog {
    param([string]$Root = (Get-SperoFlowRepoRoot))
    $path = Join-Path $Root "infrastructure\aws\secrets-catalog.json"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Secrets catalog not found: $path"
    }
    return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
}

function Get-SecretArnName {
    param(
        [Parameter(Mandatory = $true)][string]$Environment,
        [Parameter(Mandatory = $true)][string]$Name
    )
    return "speroflow/$Environment/$Name"
}

function Test-AwsCli {
    if (-not (Get-Command aws -ErrorAction SilentlyContinue)) {
        throw "AWS CLI v2 is required (aws). Configure instance role or SSO profile."
    }
}

function Get-SecretEncoding {
    param($CatalogEntry)
    if ($CatalogEntry.encoding) {
        return [string]$CatalogEntry.encoding
    }
    if ($CatalogEntry.kind -eq "pfx") {
        return "binary"
    }
    return "text"
}

function Write-Utf8NoBomFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Value
    )
    $text = $Value.TrimEnd("`r", "`n") + [Environment]::NewLine
    [System.IO.File]::WriteAllText($Path, $text, [System.Text.UTF8Encoding]::new($false))
}
