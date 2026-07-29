<#
.SYNOPSIS
  Run pilot/release preflight gates before deploy or push.

.DESCRIPTION
  Chains secret scanning, Compose validation, and optional unit tests.
  Does not start containers or require live secrets.
#>
param(
    [switch]$SkipTests,
    [switch]$SkipFrontend,
    [switch]$SkipAi
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )
    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
        throw "Preflight step failed: $Name (exit $LASTEXITCODE)"
    }
    Write-Host "OK  $Name" -ForegroundColor Green
}

Write-Host "SperoFlow preflight starting in $root" -ForegroundColor Yellow

Invoke-Step "Secret pattern guard" {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts\check-no-secrets.ps1")
}

Invoke-Step "Secrets catalog vs Compose" {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts\validate-secrets-catalog.ps1")
}

Invoke-Step "Managed secrets dry-run" {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts\aws-secrets-push.ps1") -Environment prod -Region us-east-1 -DryRun
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts\aws-secrets-pull.ps1") -Environment prod -Region us-east-1 -DryRun
}

Invoke-Step "Compose validation" {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts\validate-compose.ps1")
}

if (-not $SkipTests) {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        Invoke-Step "Backend domain tests" {
            & dotnet test (Join-Path $root "backend\tests\SperoFlow.Domain.Tests\SperoFlow.Domain.Tests.csproj") --configuration Release --nologo
        }
        Invoke-Step "Knowledge infrastructure tests" {
            & dotnet test (Join-Path $root "knowledge-platform\backend\tests\SperoFlow.Knowledge.Infrastructure.Tests\SperoFlow.Knowledge.Infrastructure.Tests.csproj") --configuration Release --nologo
        }
    } else {
        Write-Warning "dotnet not found; skipping .NET tests."
    }

    if (-not $SkipAi) {
        if (Get-Command python -ErrorAction SilentlyContinue) {
            Invoke-Step "AI-core offline unit tests" {
                $env:PYTHONPATH = Join-Path $root "ai-core\src"
                $env:APP_ENV = "development"
                $env:LLM_PROVIDER = "keyword"
                $env:ROUTER_PROVIDER = "keyword"
                Push-Location (Join-Path $root "ai-core")
                try {
                    & python -m unittest discover -s tests -p "test_*.py" -q
                } finally {
                    Pop-Location
                }
            }
        } else {
            Write-Warning "python not found; skipping AI-core tests."
        }
    }

    if (-not $SkipFrontend) {
        $frontend = Join-Path $root "frontend"
        if ((Test-Path (Join-Path $frontend "package.json")) -and (Get-Command npm -ErrorAction SilentlyContinue)) {
            Invoke-Step "Frontend unit tests" {
                Push-Location $frontend
                try {
                    if (-not (Test-Path "node_modules")) {
                        & npm ci
                    }
                    & npm test
                } finally {
                    Pop-Location
                }
            }
        } else {
            Write-Warning "frontend npm toolchain not available; skipping frontend tests."
        }
    }
}

Write-Host ""
Write-Host "Preflight passed. Next on a running host:" -ForegroundColor Green
Write-Host "  powershell -ExecutionPolicy Bypass -File scripts/stack-status.ps1"
Write-Host "  powershell -ExecutionPolicy Bypass -File scripts/smoke-release.ps1"
Write-Host "  powershell -ExecutionPolicy Bypass -File scripts/e2e-smoke.ps1 -RequireStack"
Write-Host "  powershell -ExecutionPolicy Bypass -File scripts/backup-volumes.ps1"
Write-Host "  # optional: compose ... -f compose.monitoring.yaml --profile monitoring up -d"
