$ErrorActionPreference = "Stop"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker Desktop or Docker Engine with Compose is required."
}

Push-Location (Join-Path $PSScriptRoot "..")
try {
    docker compose -f compose.yaml config | Out-Null
    docker compose -f compose.yaml -f compose.gpu.yaml --profile gpu config | Out-Null
    Write-Output "Default and GPU Compose configurations are valid."
}
finally {
    Pop-Location
}