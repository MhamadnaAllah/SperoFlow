# PowerShell Deployment Script: Step 5 - AWS SSM Secrets & Production Container Launch
# Account ID: 564371180494 | Region: us-east-1

$ErrorActionPreference = "Stop"

$AWS_REGION = "us-east-1"
$PREFIX = "/speroflow/prod"

Write-Host "==> 1. Storing Production Secrets into AWS SSM Parameter Store..." -ForegroundColor Cyan

# Non-secret configuration can stay inline.
$ssmParameters = @{
    "$PREFIX/POSTGRES_USER" = "speroflow_app"
    "$PREFIX/POSTGRES_DB" = "speroflow"
    "$PREFIX/NEO4J_USER" = "neo4j"
    "$PREFIX/SERVICE_JWT_ISSUER" = "SperoFlow.Api"
    "$PREFIX/SERVICE_JWT_AUDIENCE" = "speroflow-ai-api"
    "$PREFIX/BEDROCK_REGION" = "us-east-1"
    "$PREFIX/BEDROCK_MODEL_ID" = "google.gemma-4-31b"
    "$PREFIX/EMBEDDING_MODEL_ID" = "cohere.embed-v4:0"
}

# Secret values are pulled at runtime from AWS Secrets Manager (strong random
# values pushed by scripts/aws-secrets-push.ps1) instead of being hardcoded
# here in plaintext. This keeps secrets out of source control.
function Get-SmValue {
    param([Parameter(Mandatory = $true)][string]$SecretId)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $v = & aws secretsmanager get-secret-value --secret-id $SecretId --region $AWS_REGION --query SecretString --output text 2>&1
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prev
    }
    if ($code -ne 0) { throw "Failed to read secret $SecretId from Secrets Manager: $v" }
    return ($v -join "").Trim()
}

$ssmParameters["$PREFIX/POSTGRES_PASSWORD"] = Get-SmValue "speroflow/prod/postgres_password"
$ssmParameters["$PREFIX/REDIS_PASSWORD"] = Get-SmValue "speroflow/prod/redis_password"
$ssmParameters["$PREFIX/NEO4J_PASSWORD"] = Get-SmValue "speroflow/prod/neo4j_password"

foreach ($key in $ssmParameters.Keys) {
    $val = $ssmParameters[$key]
    Write-Host "   - Putting Parameter '$key'..." -ForegroundColor Yellow
    aws ssm put-parameter --name $key --value $val --type "SecureString" --overwrite --region $AWS_REGION
    if ($LASTEXITCODE -ne 0) { throw "Failed to put SSM parameter $key" }
}

Write-Host "==> 2. Creating Production Caddyfile for automatic HTTPS on speroflow.space..." -ForegroundColor Cyan

$caddyfileContent = @"
{
    email mhamadnaallah@gmail.com
}

speroflow.space, app.speroflow.space {
    reverse_proxy web:3000
}

api.speroflow.space {
    reverse_proxy api:8080
}

ai.speroflow.space {
    reverse_proxy ai-api:8000
}
"@

$caddyProdPath = Join-Path $PSScriptRoot "Caddyfile.prod"
$caddyfileContent | Out-File -FilePath $caddyProdPath -Encoding utf8
Write-Host "==> Saved Production Caddyfile to '$caddyProdPath'" -ForegroundColor Green

Write-Host "==> Step 5 Complete: SSM Secrets stored & Caddyfile ready!" -ForegroundColor Green
