<#
.SYNOPSIS
  Restore a PostgreSQL custom-format dump produced by backup-volumes.ps1.

.DESCRIPTION
  Destructive. Requires -ConfirmRestore. Stops are NOT automatic — scale down
  writers yourself before running. Prefer an isolated host for the first drill.

.PARAMETER DumpFile
  Path to a pg_dump --format=custom file (e.g. backups/.../main-postgres.dump).

.PARAMETER Target
  main | knowledge

.PARAMETER ContainerName
  Optional explicit container name. Auto-detected when omitted.

.PARAMETER ConfirmRestore
  Required safety latch.
#>
param(
    [Parameter(Mandatory = $true)][string]$DumpFile,
    [Parameter(Mandatory = $true)][ValidateSet("main", "knowledge")][string]$Target,
    [string]$ContainerName = "",
    [switch]$ConfirmRestore
)

$ErrorActionPreference = "Stop"

if (-not $ConfirmRestore) {
    throw "Refusing to restore without -ConfirmRestore. This overwrites database contents."
}

if (-not (Test-Path -LiteralPath $DumpFile)) {
    throw "Dump file not found: $DumpFile"
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker is required."
}

$resolved = (Resolve-Path -LiteralPath $DumpFile).Path

if ($Target -eq "main") {
    $user = "speroflow_app"
    $database = "speroflow"
    $defaultMatch = { param($n) $n -match "postgres" -and $n -notmatch "knowledge" }
} else {
    $user = "speroflow_knowledge"
    $database = "speroflow_knowledge"
    $defaultMatch = { param($n) $n -match "knowledge" -and $n -match "postgres" }
}

if (-not $ContainerName) {
    $names = @(docker ps --format "{{.Names}}" | Where-Object { & $defaultMatch $_ })
    if ($names.Count -eq 0) {
        throw "No running Postgres container matched target '$Target'. Pass -ContainerName."
    }
    if ($names.Count -gt 1) {
        throw "Multiple containers matched: $($names -join ', '). Pass -ContainerName."
    }
    $ContainerName = $names[0]
}

Write-Warning "About to restore $Target database '$database' in container '$ContainerName' from:"
Write-Warning "  $resolved"
Write-Warning "Ensure API/workers are stopped. Prefer testing on an isolated host first."

$remote = "/tmp/speroflow-restore.dump"
docker cp $resolved "${ContainerName}:${remote}"
if ($LASTEXITCODE -ne 0) {
    throw "docker cp failed."
}

try {
    # --clean --if-exists drops objects before recreate; still requires exclusive access for best results.
    docker exec $ContainerName pg_restore -U $user -d $database --clean --if-exists --no-owner --no-acl $remote
    $code = $LASTEXITCODE
    # pg_restore often exits 1 with non-fatal warnings; treat only hard failures specially.
    if ($code -gt 1) {
        throw "pg_restore failed with exit code $code."
    }
    if ($code -eq 1) {
        Write-Warning "pg_restore exited 1 (often warnings). Verify application readiness."
    } else {
        Write-Output "pg_restore completed successfully."
    }
}
finally {
    docker exec $ContainerName rm -f $remote 2>$null | Out-Null
}

Write-Output "Restore finished for $Target. Run scripts/stack-status.ps1 and smoke-release.ps1 next."
