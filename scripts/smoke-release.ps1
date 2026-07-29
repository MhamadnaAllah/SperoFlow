<#
.SYNOPSIS
  Post-deploy smoke checks against a running SperoFlow stack.

.DESCRIPTION
  Verifies Caddy-facing health (or direct container health), that only expected
  host ports are published, and that private services are not exposed.
  Does not require application credentials.
#>
param(
    [string]$AppBaseUrl = "",
    [string]$KnowledgeBaseUrl = "",
    [switch]$SkipPortAudit
)

$ErrorActionPreference = "Stop"

function Get-EnvValue {
    param([string]$Name)
    if (Test-Path ".env") {
        $line = Get-Content ".env" | Where-Object { $_ -match "^\s*$Name\s*=" } | Select-Object -First 1
        if ($line -match "=(.*)$") {
            return $matches[1].Trim().Trim('"').Trim("'")
        }
    }
    return $null
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

if (-not $AppBaseUrl) {
    $domain = Get-EnvValue "APP_DOMAIN"
    $AppBaseUrl = if ($domain) { "https://$domain" } else { "http://127.0.0.1" }
}
if (-not $KnowledgeBaseUrl) {
    $domain = Get-EnvValue "KNOWLEDGE_DOMAIN"
    $KnowledgeBaseUrl = if ($domain) { "https://$domain" } else { "" }
}

$failures = New-Object System.Collections.Generic.List[string]

function Test-HttpOk {
    param(
        [string]$Name,
        [string]$Url,
        [int[]]$AcceptStatus = @(200)
    )
    try {
        # Prefer HttpClient for consistent status handling across Windows PowerShell 5 and 7.
        add-type -AssemblyName System.Net.Http | Out-Null
        $handler = [System.Net.Http.HttpClientHandler]::new()
        $handler.AllowAutoRedirect = $false
        # Pilot hosts may use staging certs; still validate connectivity/status codes.
        $handler.ServerCertificateCustomValidationCallback = { $true }
        $client = [System.Net.Http.HttpClient]::new($handler)
        $client.Timeout = [TimeSpan]::FromSeconds(20)
        $response = $client.GetAsync($Url).GetAwaiter().GetResult()
        $status = [int]$response.StatusCode
        $client.Dispose()
        $handler.Dispose()
        if ($AcceptStatus -notcontains $status) {
            $failures.Add("$Name returned $status for $Url (expected $($AcceptStatus -join ','))")
            return
        }
        Write-Output "OK  $Name ($status) $Url"
    } catch {
        $failures.Add("$Name failed for $Url : $($_.Exception.Message)")
    }
}

Write-Output "=== SperoFlow release smoke ==="
Write-Output "App base: $AppBaseUrl"

# Public health through edge (or localhost if DNS not set)
Test-HttpOk -Name "app-live" -Url "$AppBaseUrl/health/live" -AcceptStatus @(200)
Test-HttpOk -Name "app-ready" -Url "$AppBaseUrl/health/ready" -AcceptStatus @(200, 503)
# Metrics must stay off the public edge (Caddy 404); scrapers use private api:8080/metrics
Test-HttpOk -Name "metrics-hidden" -Url "$AppBaseUrl/metrics" -AcceptStatus @(404)

# Correlation header round-trip (API observability middleware)
try {
    $probeId = [guid]::NewGuid().ToString("N")
    add-type -AssemblyName System.Net.Http | Out-Null
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.ServerCertificateCustomValidationCallback = { $true }
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(20)
    $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$AppBaseUrl/health/live")
    $req.Headers.TryAddWithoutValidation("X-Request-Id", $probeId) | Out-Null
    $resp = $client.SendAsync($req).GetAwaiter().GetResult()
    $echo = $null
    if ($resp.Headers.Contains("X-Request-Id")) {
        $echo = ($resp.Headers.GetValues("X-Request-Id") | Select-Object -First 1)
    }
    $client.Dispose()
    $handler.Dispose()
    if ($echo -eq $probeId) {
        Write-Output "OK  request-id-echo ($echo)"
    } else {
        # Health probes skip some middleware paths; accept missing echo as soft warning.
        Write-Warning "X-Request-Id not echoed on /health/live (got '$echo'). Non-health API routes still set it."
    }
} catch {
    Write-Warning "request-id probe skipped: $($_.Exception.Message)"
}

if ($KnowledgeBaseUrl) {
    Write-Output "Knowledge base: $KnowledgeBaseUrl"
    # Knowledge health must NOT be exposed on the public portal host (Caddy returns 404).
    Test-HttpOk -Name "knowledge-health-hidden" -Url "$KnowledgeBaseUrl/health/live" -AcceptStatus @(404)
}

if (-not $SkipPortAudit) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Warning "Docker not available; skipping host port audit."
    } else {
        Write-Output "Auditing published host ports..."
        $published = docker ps --format "{{.Names}} {{.Ports}}" 2>$null
        $forbidden = @()
        foreach ($line in $published) {
            # Flag common private service host publishes
            if ($line -match '0\.0\.0\.0:(5432|6379|7474|7687|9000|9001|8000|8080)->') {
                # 80/443 on reverse-proxy are fine; 8080/8000 public is not
                $forbidden += $line
            }
        }
        if ($forbidden.Count -gt 0) {
            foreach ($f in $forbidden) {
                $failures.Add("Private service appears published on host: $f")
            }
        } else {
            Write-Output "OK  no forbidden DB/Redis/MinIO/AI host ports detected"
        }

        # Caddy should own 80/443
        $caddy = $published | Where-Object { $_ -match 'reverse-proxy|caddy' -and $_ -match ':80->|:443->' }
        if (-not $caddy) {
            Write-Warning "Could not confirm Caddy publishes 80/443 (stack may use different names)."
        } else {
            Write-Output "OK  edge proxy publishes HTTP(S)"
        }
    }
}

# Container-level health sample (best-effort)
if (Get-Command docker -ErrorAction SilentlyContinue) {
    $unhealthy = docker ps --filter "health=unhealthy" --format "{{.Names}}" 2>$null
    if ($unhealthy) {
        foreach ($name in $unhealthy) {
            $failures.Add("Unhealthy container: $name")
        }
    } else {
        Write-Output "OK  no unhealthy containers reported"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Smoke FAILED:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw "Release smoke checks failed."
}

Write-Output "All smoke checks passed."
