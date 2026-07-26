# Script to initialize Docker secrets and backup credentials
Param(
    [string]$BedrockApiKey = "ABSKTWFudGxlQXBpS2V5LTdvaXExbm80LWF0LTU2NDM3MTE4MDQ5NDpPaWwvNy8rL3VIeUR2OW02ZjJFajZPYllJT2FDL1J6QVZrQ0dYaUJwZFpNNTArSjZjRlVvY25yUmZpVT0="
)

$ErrorActionPreference = "Stop"

$bootstrapScript = Join-Path $PSScriptRoot "bootstrap-secrets.ps1"
if (Test-Path $bootstrapScript) {
    & $bootstrapScript -BedrockApiKey $BedrockApiKey
} else {
    throw "bootstrap-secrets.ps1 not found."
}
