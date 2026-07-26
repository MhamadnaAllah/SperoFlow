$ErrorActionPreference = "Stop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 10 SDK is required. Install it before creating EF Core migrations."
}

Push-Location (Join-Path $PSScriptRoot "..\backend")
try {
    dotnet tool install --global dotnet-ef --version 10.0.0
    dotnet ef migrations add InitialCreate --project src/SperoFlow.Infrastructure --startup-project src/SperoFlow.Api --output-dir Persistence/Migrations
}
finally {
    Pop-Location
}
