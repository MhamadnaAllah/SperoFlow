# PowerShell Deployment Script: Step 5 - AWS SSM Secrets & Production Container Launch
# Account ID: 564371180494 | Region: us-east-1

$ErrorActionPreference = "Stop"

$AWS_REGION = "us-east-1"
$PREFIX = "/speroflow/prod"

Write-Host "==> 1. Storing Production Secrets into AWS SSM Parameter Store..." -ForegroundColor Cyan

$ssmParameters = @{
    "$PREFIX/POSTGRES_USER" = "speroflow_app"
    "$PREFIX/POSTGRES_DB" = "speroflow"
    "$PREFIX/POSTGRES_PASSWORD" = "speroflow_secure_prod_db_pass_2026"
    "$PREFIX/REDIS_PASSWORD" = "speroflow_secure_redis_pass_2026"
    "$PREFIX/NEO4J_USER" = "neo4j"
    "$PREFIX/NEO4J_PASSWORD" = "speroflow_secure_neo4j_pass_2026"
    "$PREFIX/SERVICE_JWT_ISSUER" = "SperoFlow.Api"
    "$PREFIX/SERVICE_JWT_AUDIENCE" = "speroflow-ai-api"
    "$PREFIX/BEDROCK_REGION" = "us-east-1"
    "$PREFIX/BEDROCK_MODEL_ID" = "google.gemma-4-31b"
    "$PREFIX/EMBEDDING_MODEL_ID" = "cohere.embed-v4:0"
}

foreach ($key in $ssmParameters.Keys) {
    $val = $ssmParameters[$key]
    Write-Host "   - Putting Parameter '$key'..." -ForegroundColor Yellow
    aws ssm put-parameter --name $key --value $val --type "SecureString" --overwrite --region $AWS_REGION
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
