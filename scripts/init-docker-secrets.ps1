# Thin wrapper that delegates to bootstrap-secrets.ps1.
# Prefer: powershell -ExecutionPolicy Bypass -File scripts/bootstrap-secrets.ps1
Param(
    # Never embed live keys here. Use env BEDROCK_API_KEY or pass -BedrockApiKey explicitly.
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
    -BedrockApiKey $BedrockApiKey `
    -Rotate:$Rotate `
    -WritePlaintextSummary:$WritePlaintextSummary `
    -WriteLegacyTxtCopies:$WriteLegacyTxtCopies
