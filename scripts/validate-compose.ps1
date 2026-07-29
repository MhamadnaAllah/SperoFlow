$ErrorActionPreference = "Stop"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker Desktop or Docker Engine with Compose is required."
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $root
try {
    $checks = @(
        @{ Name = "main"; Args = @("-f", "compose.yaml", "config") },
        @{ Name = "main+prod"; Args = @("-f", "compose.yaml", "-f", "compose.prod.yaml", "config") },
        @{ Name = "main+gpu"; Args = @("-f", "compose.yaml", "-f", "compose.gpu.yaml", "--profile", "gpu", "config") },
        @{ Name = "main+monitoring"; Args = @("-f", "compose.yaml", "-f", "compose.monitoring.yaml", "--profile", "monitoring", "config") },
        @{ Name = "knowledge"; Args = @("-f", "knowledge-platform/compose.yaml", "config") }
    )

    foreach ($check in $checks) {
        Write-Output "Validating Compose configuration: $($check.Name)..."
        & docker compose @($check.Args) | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Compose validation failed for $($check.Name)."
        }
        Write-Output "OK: $($check.Name)"
    }

    Write-Output "All Compose configurations are valid (main, prod, gpu profile, knowledge)."
}
finally {
    Pop-Location
}
