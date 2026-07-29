# Delegate to bootstrap-secrets.ps1 for complete secrets generation
param(
    [string]$SecretsDirectory = (Join-Path $PSScriptRoot "..\infrastructure\secrets"),
    [string]$RuntimeSecretsDirectory = (Join-Path $PSScriptRoot "..\secrets"),
    [string]$BackupDirectory = (Join-Path $PSScriptRoot "..\secrets_backup"),
    [string]$BedrockApiKey = $(if ($env:BEDROCK_API_KEY) { $env:BEDROCK_API_KEY } else { "" }),
    [switch]$Rotate,
    [switch]$WritePlaintextSummary,
    [switch]$WriteLegacyTxtCopies
)

$ErrorActionPreference = "Stop"

$bootstrapScript = Join-Path $PSScriptRoot "bootstrap-secrets.ps1"
if (-not (Test-Path $bootstrapScript)) {
    throw "bootstrap-secrets.ps1 not found."
}

& $bootstrapScript `
    -SecretsDirectory $SecretsDirectory `
    -RuntimeSecretsDirectory $RuntimeSecretsDirectory `
    -BackupDirectory $BackupDirectory `
    -BedrockApiKey $BedrockApiKey `
    -Rotate:$Rotate `
    -WritePlaintextSummary:$WritePlaintextSummary `
    -WriteLegacyTxtCopies:$WriteLegacyTxtCopies
