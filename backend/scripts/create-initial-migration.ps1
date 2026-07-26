[CmdletBinding()]
param(
    [string]$MigrationName = "InitialCreate"
)

$ErrorActionPreference = "Stop"

$backendRoot = Split-Path -Parent $PSScriptRoot
$toolPath = Join-Path $backendRoot ".tools"
$tool = Join-Path $toolPath "dotnet-ef.exe"

if (-not (Test-Path -LiteralPath $tool)) {
    dotnet tool install dotnet-ef --tool-path $toolPath --version 10.0.10
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to install dotnet-ef."
    }
}

& $tool migrations add $MigrationName `
    --project (Join-Path $backendRoot "src/SperoFlow.Infrastructure/SperoFlow.Infrastructure.csproj") `
    --startup-project (Join-Path $backendRoot "src/SperoFlow.Api/SperoFlow.Api.csproj") `
    --context SperoFlow.Infrastructure.AppDbContext `
    --output-dir Migrations

if ($LASTEXITCODE -ne 0) {
    throw "EF Core migration creation failed."
}