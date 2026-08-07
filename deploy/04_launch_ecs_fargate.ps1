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
    log {
        output stdout
        format json
    }
}

speroflow.space, app.speroflow.space {
    encode zstd gzip

    header {
        -Server
        X-Content-Type-Options "nosniff"
        X-Frame-Options "DENY"
        Referrer-Policy "no-referrer"
        Permissions-Policy "camera=(), geolocation=(), microphone=()"
        Cross-Origin-Opener-Policy "same-origin"
        Cross-Origin-Resource-Policy "same-site"
        Strict-Transport-Security "max-age=31536000; includeSubDomains"
    }

    @metrics path /metrics /metrics/*
    respond @metrics "Not found" 404

    @api path /api/* /connect/* /.well-known/* /health/live /health/ready
    handle @api {
        reverse_proxy api:8080 {
            header_up -X-Forwarded-For
            header_up -X-Forwarded-Host
            header_up -X-Forwarded-Proto
            header_up X-Forwarded-For {remote_host}
            header_up X-Forwarded-Host {host}
            header_up X-Forwarded-Proto https
            header_up X-Request-Id {http.request.header.X-Request-Id}
            header_up X-Correlation-ID {http.request.header.X-Correlation-ID}
        }
    }

    handle {
        reverse_proxy web:3000 {
            header_up X-Request-Id {http.request.header.X-Request-Id}
        }
    }
}

api.speroflow.space {
    encode zstd gzip
    reverse_proxy api:8080 {
        header_up X-Forwarded-Host {host}
        header_up X-Forwarded-Proto https
        header_up X-Request-Id {http.request.header.X-Request-Id}
    }
}

ai.speroflow.space {
    encode zstd gzip
    reverse_proxy ai-api:8000 {
        header_up X-Forwarded-Host {host}
        header_up X-Forwarded-Proto https
        header_up X-Request-Id {http.request.header.X-Request-Id}
    }
}
"@

$caddyProdPath = Join-Path $PSScriptRoot "Caddyfile.prod"
$caddyfileContent | Out-File -FilePath $caddyProdPath -Encoding utf8
Write-Host "==> Saved Production Caddyfile to '$caddyProdPath'" -ForegroundColor Green

Write-Host "==> Step 5 Complete: SSM Secrets stored & Caddyfile ready!" -ForegroundColor Green
