[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$networks = @(
    "speroflow_knowledge_edge",
    "speroflow_knowledge_grant_bridge",
    "speroflow_knowledge_read_bridge",
    "speroflow_knowledge_storage_bridge"
)

foreach ($network in $networks) {
    & docker network inspect $network *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Network already exists: $network"
        continue
    }

    & docker network create --driver bridge $network | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to create Docker network: $network"
    }

    Write-Host "Created network: $network"
}