<#
.SYNOPSIS
  Run dependency-light Node e2e smoke against a running edge (or skip if down).
#>
param(
    [string]$AppBaseUrl = "",
    [string]$KnowledgeBaseUrl = "",
    [switch]$RequireStack
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "Node.js is required for e2e-smoke.ps1"
}

if ($AppBaseUrl) { $env:APP_BASE_URL = $AppBaseUrl }
if ($KnowledgeBaseUrl) { $env:KNOWLEDGE_BASE_URL = $KnowledgeBaseUrl }
if ($RequireStack) { $env:REQUIRE_STACK = "1" }

Push-Location $root
try {
    & node (Join-Path $root "scripts\e2e-smoke.mjs")
    if ($LASTEXITCODE -ne 0) {
        throw "e2e-smoke failed with exit $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
