# Delegate to bootstrap-secrets.ps1 for complete secrets generation
param(
    [string]$SecretsDirectory = (Join-Path $PSScriptRoot "..\infrastructure\secrets"),
    [string]$RuntimeSecretsDirectory = (Join-Path $PSScriptRoot "..\secrets"),
    [string]$BackupDirectory = (Join-Path $PSScriptRoot "..\secrets_backup"),
    [switch]$Rotate
)

$ErrorActionPreference = "Stop"

$bootstrapScript = Join-Path $PSScriptRoot "bootstrap-secrets.ps1"
if (Test-Path $bootstrapScript) {
    & $bootstrapScript -SecretsDirectory $SecretsDirectory -RuntimeSecretsDirectory $RuntimeSecretsDirectory -BackupDirectory $BackupDirectory -Rotate:$Rotate
} else {
    throw "bootstrap-secrets.ps1 not found."
}
