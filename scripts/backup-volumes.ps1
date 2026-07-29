<#
.SYNOPSIS
  Creates timestamped logical backups of SperoFlow data stores from running Compose stacks.

.DESCRIPTION
  Dumps main and knowledge PostgreSQL databases when those containers are healthy.
  Optionally archives Neo4j and MinIO data directories when Docker named volumes are accessible.
  Writes output under ./backups/<timestamp>/ (gitignored).

.PARAMETER OutputRoot
  Directory for backup artifacts. Default: <repo>/backups

.PARAMETER IncludeObjectStores
  Also attempt tar of MinIO volume data via temporary alpine containers.

.PARAMETER IncludeNeo4j
  Also attempt neo4j-admin dump when neo4j containers are running.
#>
param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\backups"),
    [switch]$IncludeObjectStores,
    [switch]$IncludeNeo4j
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker is required for backup-volumes.ps1."
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outDir = Join-Path $OutputRoot $stamp
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Test-ContainerRunning {
    param([string]$Name)
    $id = docker ps --filter "name=$Name" --filter "status=running" --format "{{.ID}}" 2>$null
    return -not [string]::IsNullOrWhiteSpace($id)
}

function Backup-Postgres {
    param(
        [string]$ContainerName,
        [string]$User,
        [string]$Database,
        [string]$OutFile
    )

    if (-not (Test-ContainerRunning $ContainerName)) {
        Write-Warning "Skip Postgres backup: container '$ContainerName' is not running."
        return $false
    }

    Write-Output "Dumping PostgreSQL $Database from $ContainerName..."
    docker exec $ContainerName pg_dump -U $User -d $Database --no-owner --format=custom -f "/tmp/speroflow-backup.dump"
    if ($LASTEXITCODE -ne 0) {
        throw "pg_dump failed inside $ContainerName."
    }
    docker cp "${ContainerName}:/tmp/speroflow-backup.dump" $OutFile
    if ($LASTEXITCODE -ne 0) {
        throw "docker cp failed for $ContainerName dump."
    }
    docker exec $ContainerName rm -f /tmp/speroflow-backup.dump | Out-Null
    Write-Output "Wrote $OutFile"
    return $true
}

Push-Location $root
try {
    $manifest = New-Object System.Collections.Generic.List[string]
    $manifest.Add("# SperoFlow backup $stamp")
    $manifest.Add("")
    $manifest.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K')")
    $manifest.Add("Host: $env:COMPUTERNAME")
    $manifest.Add("")

    # Main stack container names from compose project "speroflow"
    $mainPg = Backup-Postgres -ContainerName "speroflow-postgres-1" -User "speroflow_app" -Database "speroflow" -OutFile (Join-Path $outDir "main-postgres.dump")
    if (-not $mainPg) {
        # Fallback: match any running container with postgres service label-ish name
        $candidates = docker ps --format "{{.Names}}" | Where-Object { $_ -match "postgres" -and $_ -notmatch "knowledge" }
        foreach ($c in $candidates) {
            if (Backup-Postgres -ContainerName $c -User "speroflow_app" -Database "speroflow" -OutFile (Join-Path $outDir "main-postgres.dump")) {
                $mainPg = $true
                break
            }
        }
    }
    $manifest.Add("- main-postgres: $(if ($mainPg) { 'ok' } else { 'skipped' })")

    $knowledgePg = Backup-Postgres -ContainerName "knowledge-platform-knowledge-postgres-1" -User "speroflow_knowledge" -Database "speroflow_knowledge" -OutFile (Join-Path $outDir "knowledge-postgres.dump")
    if (-not $knowledgePg) {
        $candidates = docker ps --format "{{.Names}}" | Where-Object { $_ -match "knowledge" -and $_ -match "postgres" }
        foreach ($c in $candidates) {
            if (Backup-Postgres -ContainerName $c -User "speroflow_knowledge" -Database "speroflow_knowledge" -OutFile (Join-Path $outDir "knowledge-postgres.dump")) {
                $knowledgePg = $true
                break
            }
        }
    }
    $manifest.Add("- knowledge-postgres: $(if ($knowledgePg) { 'ok' } else { 'skipped' })")

    if ($IncludeNeo4j) {
        $neoContainers = docker ps --format "{{.Names}}" | Where-Object { $_ -match "neo4j" }
        foreach ($neo in $neoContainers) {
            $safe = ($neo -replace '[^a-zA-Z0-9_-]', '_')
            $dumpPath = Join-Path $outDir "$safe-neo4j.dump"
            Write-Output "Attempting Neo4j dump from $neo (requires neo4j-admin support)..."
            docker exec $neo neo4j-admin database dump neo4j --to-path=/tmp 2>$null
            if ($LASTEXITCODE -eq 0) {
                docker cp "${neo}:/tmp/neo4j.dump" $dumpPath 2>$null
                if (Test-Path $dumpPath) {
                    $manifest.Add("- ${safe}-neo4j: ok")
                    Write-Output "Wrote $dumpPath"
                } else {
                    $manifest.Add("- ${safe}-neo4j: failed-copy")
                    Write-Warning "Neo4j dump copy failed for $neo."
                }
            } else {
                $manifest.Add("- ${safe}-neo4j: skipped-or-failed")
                Write-Warning "Neo4j dump failed for $neo. Prefer volume snapshot or official backup procedure."
            }
        }
    } else {
        $manifest.Add("- neo4j: skipped (pass -IncludeNeo4j)")
    }

    if ($IncludeObjectStores) {
        Write-Warning "Object-store backup uses docker volume mount snapshots; verify volume names for your host."
        $volumes = docker volume ls --format "{{.Name}}" | Where-Object { $_ -match "minio" }
        foreach ($vol in $volumes) {
            $safe = ($vol -replace '[^a-zA-Z0-9_-]', '_')
            $tarPath = Join-Path $outDir "$safe-minio.tar"
            Write-Output "Archiving volume $vol..."
            docker run --rm -v "${vol}:/data:ro" -v "${outDir}:/backup" alpine tar cf "/backup/$safe-minio.tar" -C /data .
            if ($LASTEXITCODE -eq 0 -and (Test-Path $tarPath)) {
                $manifest.Add("- ${safe}-minio: ok")
            } else {
                $manifest.Add("- ${safe}-minio: failed")
            }
        }
    } else {
        $manifest.Add("- minio: skipped (pass -IncludeObjectStores)")
    }

    $manifest.Add("")
    $manifest.Add("## Restore outline")
    $manifest.Add("")
    $manifest.Add("1. Stop writers (api, workers) before restore.")
    $manifest.Add("2. Main Postgres: `docker exec -i <postgres> pg_restore -U speroflow_app -d speroflow --clean --if-exists < main-postgres.dump`")
    $manifest.Add("3. Knowledge Postgres: same pattern with knowledge credentials/database.")
    $manifest.Add("4. Neo4j/MinIO: restore from dumps/tars on an isolated host first; validate GraphRAG queries.")
    $manifest.Add("5. Rotate secrets if the backup medium was ever exposed.")
    $manifest.Add("")
    $manifest.Add("Encrypt and copy this directory off-host. Do not commit backups/.")

    $manifestPath = Join-Path $outDir "MANIFEST.md"
    [System.IO.File]::WriteAllText($manifestPath, ($manifest -join [Environment]::NewLine) + [Environment]::NewLine)
    Write-Output "Backup complete: $outDir"
    Write-Output "Manifest: $manifestPath"
}
finally {
    Pop-Location
}
