param(
    [string]$SecretsDirectory = (Join-Path $PSScriptRoot "..\infrastructure\secrets"),
    [string]$RuntimeSecretsDirectory = (Join-Path $PSScriptRoot "..\secrets"),
    [string]$BackupDirectory = (Join-Path $PSScriptRoot "..\secrets_backup"),
    [string]$BedrockApiKey = "ABSKTWFudGxlQXBpS2V5LTdvaXExbm80LWF0LTU2NDM3MTE4MDQ5NDpPaWwvNy8rL3VIeUR2OW02ZjJFajZPYllJT2FDL1J6QVZrQ0dYaUJwZFpNNTArSjZjRlVvY25yUmZpVT0=",
    [switch]$Rotate
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
    $gitOpenSsl = "C:\Program Files\Git\usr\bin"
    if (Test-Path $gitOpenSsl) {
        $env:PATH = "$gitOpenSsl;$env:PATH"
    }
}

$openssl = Get-Command openssl -ErrorAction SilentlyContinue
if ($null -eq $openssl) {
    throw "OpenSSL is required to generate RSA keys and certificate secrets."
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Value
    )

    [System.IO.File]::WriteAllText($Path, $Value.TrimEnd("`r", "`n") + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function New-HexSecret {
    param([int]$Bytes = 32)

    $buffer = New-Object byte[] $Bytes
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($buffer)
    $rng.Dispose()
    return ([BitConverter]::ToString($buffer).Replace("-", "")).ToLowerInvariant()
}

function Ensure-RandomSecret {
    param([Parameter(Mandatory = $true)][string]$Name)

    $path = Join-Path $SecretsDirectory $Name
    if ((Test-Path -LiteralPath $path) -and -not $Rotate) {
        Write-Output "Preserving existing secret: $Name"
        return
    }

    Write-Utf8NoBom -Path $path -Value (New-HexSecret)
    Write-Output "Generated secret: $Name"
}

function Ensure-FixedSecret {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $path = Join-Path $SecretsDirectory $Name
    if ((Test-Path -LiteralPath $path) -and -not $Rotate) {
        Write-Output "Preserving existing secret: $Name"
        return
    }

    Write-Utf8NoBom -Path $path -Value $Value
    Write-Output "Generated secret: $Name"
}

function Invoke-OpenSsl {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & $openssl.Source @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "OpenSSL failed while executing: openssl $($Arguments -join ' ')"
    }
}

function Ensure-RsaKeyPair {
    param(
        [Parameter(Mandatory = $true)][string]$PrivateName,
        [Parameter(Mandatory = $true)][string]$PublicName
    )

    $privatePath = Join-Path $SecretsDirectory $PrivateName
    $publicPath = Join-Path $SecretsDirectory $PublicName
    $privateExists = Test-Path -LiteralPath $privatePath
    $publicExists = Test-Path -LiteralPath $publicPath
    if ($privateExists -and $publicExists -and -not $Rotate) {
        Write-Output "Preserving existing key pair: $PrivateName"
        return
    }
    if (($privateExists -xor $publicExists) -and -not $Rotate) {
        throw "Refusing to replace an incomplete key pair. Restore or rotate $PrivateName and $PublicName together."
    }

    Invoke-OpenSsl -Arguments @("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:3072", "-out", $privatePath)
    Invoke-OpenSsl -Arguments @("rsa", "-pubout", "-in", $privatePath, "-out", $publicPath)
    Write-Output "Generated key pair: $PrivateName"
}

function Ensure-PfxCertificate {
    param(
        [Parameter(Mandatory = $true)][string]$CertificateName,
        [Parameter(Mandatory = $true)][string]$PasswordName,
        [Parameter(Mandatory = $true)][string]$Subject
    )

    $certificatePath = Join-Path $SecretsDirectory $CertificateName
    $passwordPath = Join-Path $SecretsDirectory $PasswordName
    $certificateExists = Test-Path -LiteralPath $certificatePath
    $passwordExists = Test-Path -LiteralPath $passwordPath
    if ($certificateExists -and $passwordExists -and -not $Rotate) {
        Write-Output "Preserving existing certificate: $CertificateName"
        return
    }
    if (($certificateExists -xor $passwordExists) -and -not $Rotate) {
        throw "Refusing to replace an incomplete certificate pair. Restore or rotate $CertificateName and $PasswordName together."
    }

    $password = New-HexSecret
    $temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("speroflow-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $temporaryDirectory | Out-Null
    $keyPath = Join-Path $temporaryDirectory "key.pem"
    $certificatePemPath = Join-Path $temporaryDirectory "certificate.pem"
    try {
        Invoke-OpenSsl -Arguments @("req", "-x509", "-newkey", "rsa:3072", "-sha256", "-days", "730", "-nodes", "-keyout", $keyPath, "-out", $certificatePemPath, "-subj", $Subject)
        Invoke-OpenSsl -Arguments @("pkcs12", "-export", "-out", $certificatePath, "-inkey", $keyPath, "-in", $certificatePemPath, "-passout", "pass:$password")
        Write-Utf8NoBom -Path $passwordPath -Value $password
    }
    finally {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Output "Generated certificate: $CertificateName"
}

New-Item -ItemType Directory -Force -Path $SecretsDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $RuntimeSecretsDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $BackupDirectory | Out-Null

@(
    "postgres_password",
    "redis_password",
    "neo4j_password",
    "minio_access_key",
    "minio_secret_key",
    "admin_bootstrap_token",
    "knowledge_postgres_password",
    "knowledge_redis_password",
    "knowledge_neo4j_password",
    "knowledge_neo4j_reader_password",
    "knowledge_neo4j_writer_password",
    "knowledge_minio_access_key",
    "knowledge_minio_secret_key",
    "smtp_password"
) | ForEach-Object { Ensure-RandomSecret -Name $_ }

Ensure-FixedSecret -Name "bedrock_api_key" -Value $BedrockApiKey

Ensure-RsaKeyPair -PrivateName "service_jwt_private_key" -PublicName "service_jwt_public_key"
Ensure-RsaKeyPair -PrivateName "knowledge_service_jwt_private_key" -PublicName "knowledge_service_jwt_public_key"
Ensure-RsaKeyPair -PrivateName "knowledge_grant_private_key" -PublicName "knowledge_grant_public_key"

Ensure-PfxCertificate -CertificateName "oidc_signing_certificate" -PasswordName "oidc_signing_certificate_password" -Subject "/CN=SperoFlow OIDC Signing"
Ensure-PfxCertificate -CertificateName "oidc_encryption_certificate" -PasswordName "oidc_encryption_certificate_password" -Subject "/CN=SperoFlow OIDC Encryption"
Ensure-PfxCertificate -CertificateName "knowledge_portal_data_protection_certificate" -PasswordName "knowledge_portal_data_protection_certificate_password" -Subject "/CN=SperoFlow Knowledge Portal Data Protection"

# Copy secret files to secrets_backup and runtime secrets directory
Get-ChildItem -Path $SecretsDirectory -File | Where-Object { $_.Name -ne ".gitignore" -and $_.Name -ne "README.md" } | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $BackupDirectory $_.Name) -Force
    Copy-Item -Path $_.FullName -Destination (Join-Path $RuntimeSecretsDirectory $_.Name) -Force
}

# Ensure .txt copies in runtime secrets directory for legacy compatibility
$legacyTxtMapping = @{
    "admin_bootstrap_token" = "admin_bootstrap_token.txt"
    "bedrock_api_key" = "bedrock_api_key.txt"
    "minio_access_key" = "minio_access_key.txt"
    "minio_secret_key" = "minio_secret_key.txt"
    "neo4j_password" = "neo4j_password.txt"
    "postgres_password" = "postgres_password.txt"
    "redis_password" = "redis_password.txt"
    "oidc_signing_certificate_password" = "cert_password.txt"
}
foreach ($key in $legacyTxtMapping.Keys) {
    $srcFile = Join-Path $SecretsDirectory $key
    if (Test-Path $srcFile) {
        Copy-Item -Path $srcFile -Destination (Join-Path $RuntimeSecretsDirectory $legacyTxtMapping[$key]) -Force
    }
}

# Generate CREDENTIALS_SUMMARY.md
function Get-RsaKeyFingerprint {
    param([string]$PublicPath)
    $lines = & $openssl.Source rsa -pubin -in $PublicPath -outform DER | & $openssl.Source dgst -sha256
    $lineStr = $lines -join " "
    if ($lineStr -match "=\s*([a-fA-F0-9]+)") {
        return $matches[1].ToLowerInvariant()
    }
    return $lineStr.Trim()
}

function Get-PfxCertSummary {
    param([string]$PfxPath, [string]$Password)
    $subjectRaw = (& $openssl.Source pkcs12 -in $PfxPath -nodes -passin "pass:$Password" | & $openssl.Source x509 -noout -subject) -join " "
    $fpRaw = (& $openssl.Source pkcs12 -in $PfxPath -nodes -passin "pass:$Password" | & $openssl.Source x509 -noout -fingerprint -sha256) -join " "
    $datesRaw = (& $openssl.Source pkcs12 -in $PfxPath -nodes -passin "pass:$Password" | & $openssl.Source x509 -noout -dates) -join " "
    $serialRaw = (& $openssl.Source pkcs12 -in $PfxPath -nodes -passin "pass:$Password" | & $openssl.Source x509 -noout -serial) -join " "

    $subjectVal = if ($subjectRaw -match "subject=\s*(.*)") { $matches[1].Trim() } else { $subjectRaw.Trim() }
    $fpVal = if ($fpRaw -match "Fingerprint=\s*(.*)") { $matches[1].Trim() } else { $fpRaw.Trim() }
    $serialVal = if ($serialRaw -match "serial=\s*(.*)") { $matches[1].Trim() } else { $serialRaw.Trim() }

    return @{
        Subject = $subjectVal
        Fingerprint = $fpVal
        Serial = $serialVal
        Dates = $datesRaw.Trim()
    }
}

$pgPass = (Get-Content (Join-Path $SecretsDirectory "postgres_password") -Raw).Trim()
$redisPass = (Get-Content (Join-Path $SecretsDirectory "redis_password") -Raw).Trim()
$neo4jPass = (Get-Content (Join-Path $SecretsDirectory "neo4j_password") -Raw).Trim()
$minioAccess = (Get-Content (Join-Path $SecretsDirectory "minio_access_key") -Raw).Trim()
$minioSecret = (Get-Content (Join-Path $SecretsDirectory "minio_secret_key") -Raw).Trim()
$adminToken = (Get-Content (Join-Path $SecretsDirectory "admin_bootstrap_token") -Raw).Trim()
$kPgPass = (Get-Content (Join-Path $SecretsDirectory "knowledge_postgres_password") -Raw).Trim()
$kRedisPass = (Get-Content (Join-Path $SecretsDirectory "knowledge_redis_password") -Raw).Trim()
$kNeo4jPass = (Get-Content (Join-Path $SecretsDirectory "knowledge_neo4j_password") -Raw).Trim()
$kNeo4jReaderPass = (Get-Content (Join-Path $SecretsDirectory "knowledge_neo4j_reader_password") -Raw).Trim()
$kNeo4jWriterPass = (Get-Content (Join-Path $SecretsDirectory "knowledge_neo4j_writer_password") -Raw).Trim()
$kMinioAccess = (Get-Content (Join-Path $SecretsDirectory "knowledge_minio_access_key") -Raw).Trim()
$kMinioSecret = (Get-Content (Join-Path $SecretsDirectory "knowledge_minio_secret_key") -Raw).Trim()
$smtpPass = (Get-Content (Join-Path $SecretsDirectory "smtp_password") -Raw).Trim()
$bedrockKey = (Get-Content (Join-Path $SecretsDirectory "bedrock_api_key") -Raw).Trim()

$oidcSignPass = (Get-Content (Join-Path $SecretsDirectory "oidc_signing_certificate_password") -Raw).Trim()
$oidcEncPass = (Get-Content (Join-Path $SecretsDirectory "oidc_encryption_certificate_password") -Raw).Trim()
$kDpPass = (Get-Content (Join-Path $SecretsDirectory "knowledge_portal_data_protection_certificate_password") -Raw).Trim()

$serviceJwtFp = Get-RsaKeyFingerprint (Join-Path $SecretsDirectory "service_jwt_public_key")
$kServiceJwtFp = Get-RsaKeyFingerprint (Join-Path $SecretsDirectory "knowledge_service_jwt_public_key")
$kGrantFp = Get-RsaKeyFingerprint (Join-Path $SecretsDirectory "knowledge_grant_public_key")

$oidcSignCert = Get-PfxCertSummary -PfxPath (Join-Path $SecretsDirectory "oidc_signing_certificate") -Password $oidcSignPass
$oidcEncCert = Get-PfxCertSummary -PfxPath (Join-Path $SecretsDirectory "oidc_encryption_certificate") -Password $oidcEncPass
$kDpCert = Get-PfxCertSummary -PfxPath (Join-Path $SecretsDirectory "knowledge_portal_data_protection_certificate") -Password $kDpPass

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"

$template = @'
# SperoFlow Credentials & Secrets Backup

Generated At: {TIMESTAMP}

> WARNING: This file contains private production & development secrets. DO NOT commit to Git.

## 1. Cloud & AI Service Keys
- **Amazon Bedrock API Key** (`bedrock_api_key`): {BEDROCK_KEY}

## 2. Main Application Accounts
- **Admin Bootstrap User**: admin@speroflow.local
  - Bootstrap Token (`admin_bootstrap_token`): {ADMIN_TOKEN}
- **Default Application User**: user@speroflow.local
  - Default Password: SperoFlowUser2026!

## 3. Knowledge Platform Accounts
- **Knowledge Platform Admin**: knowledge_admin@speroflow.local
  - Role: KnowledgeAdmin
  - Password: KnowledgeAdmin2026!
- **Knowledge Platform Owner**: knowledge_owner@speroflow.local
  - Role: KnowledgeOwner
  - Password: KnowledgeOwner2026!

## 4. Infrastructure Passwords
- **Main PostgreSQL User (`speroflow_app`)** (`postgres_password`): {PG_PASS}
- **Main Neo4j User (`neo4j`)** (`neo4j_password`): {NEO4J_PASS}
- **Main Redis Password** (`redis_password`): {REDIS_PASS}
- **Main MinIO Access Key** (`minio_access_key`): {MINIO_ACCESS}
- **Main MinIO Secret Key** (`minio_secret_key`): {MINIO_SECRET}
- **Knowledge PostgreSQL Owner (`speroflow_knowledge`)** (`knowledge_postgres_password`): {K_PG_PASS}
- **Knowledge Redis Password** (`knowledge_redis_password`): {K_REDIS_PASS}
- **Knowledge Neo4j Admin (`neo4j`)** (`knowledge_neo4j_password`): {K_NEO4J_PASS}
- **Knowledge Neo4j Reader (`knowledge_reader`)** (`knowledge_neo4j_reader_password`): {K_NEO4J_READER_PASS}
- **Knowledge Neo4j Writer (`knowledge_writer`)** (`knowledge_neo4j_writer_password`): {K_NEO4J_WRITER_PASS}
- **Knowledge MinIO Access Key** (`knowledge_minio_access_key`): {K_MINIO_ACCESS}
- **Knowledge MinIO Secret Key** (`knowledge_minio_secret_key`): {K_MINIO_SECRET}
- **SMTP Password** (`smtp_password`): {SMTP_PASS}

## 5. RSA 3072 Key Pairs & Fingerprints
- **Main Service JWT (`service_jwt_private_key`, `service_jwt_public_key`)**:
  - Key Type: RSA 3072-bit
  - Public Key SHA256 Fingerprint: {SERVICE_JWT_FP}
- **Knowledge Service JWT (`knowledge_service_jwt_private_key`, `knowledge_service_jwt_public_key`)**:
  - Key Type: RSA 3072-bit
  - Public Key SHA256 Fingerprint: {K_SERVICE_JWT_FP}
- **Knowledge Grant (`knowledge_grant_private_key`, `knowledge_grant_public_key`)**:
  - Key Type: RSA 3072-bit
  - Public Key SHA256 Fingerprint: {K_GRANT_FP}

## 6. PKCS#12 (PFX) X.509 Certificates
- **OIDC Signing Certificate (`oidc_signing_certificate`)**:
  - Subject: {OIDC_SIGN_SUBJECT}
  - Password File: `oidc_signing_certificate_password`
  - Serial Number: {OIDC_SIGN_SERIAL}
  - Validity: {OIDC_SIGN_DATES}
  - SHA256 Fingerprint: {OIDC_SIGN_FP}
- **OIDC Encryption Certificate (`oidc_encryption_certificate`)**:
  - Subject: {OIDC_ENC_SUBJECT}
  - Password File: `oidc_encryption_certificate_password`
  - Serial Number: {OIDC_ENC_SERIAL}
  - Validity: {OIDC_ENC_DATES}
  - SHA256 Fingerprint: {OIDC_ENC_FP}
- **Knowledge Portal Data Protection Certificate (`knowledge_portal_data_protection_certificate`)**:
  - Subject: {K_DP_SUBJECT}
  - Password File: `knowledge_portal_data_protection_certificate_password`
  - Serial Number: {K_DP_SERIAL}
  - Validity: {K_DP_DATES}
  - SHA256 Fingerprint: {K_DP_FP}

## 7. Active Docker Endpoints
- Web Frontend: http://localhost:3000
- Main API (ASP.NET Core): http://localhost:8080
- AI API (FastAPI): http://localhost:8000
- Knowledge Portal: http://localhost:3001
- MinIO Console: http://localhost:9001
'@

$summaryMarkdown = $template
$summaryMarkdown = $summaryMarkdown.Replace('{TIMESTAMP}', $timestamp)
$summaryMarkdown = $summaryMarkdown.Replace('{BEDROCK_KEY}', $bedrockKey)
$summaryMarkdown = $summaryMarkdown.Replace('{ADMIN_TOKEN}', $adminToken)
$summaryMarkdown = $summaryMarkdown.Replace('{PG_PASS}', $pgPass)
$summaryMarkdown = $summaryMarkdown.Replace('{NEO4J_PASS}', $neo4jPass)
$summaryMarkdown = $summaryMarkdown.Replace('{REDIS_PASS}', $redisPass)
$summaryMarkdown = $summaryMarkdown.Replace('{MINIO_ACCESS}', $minioAccess)
$summaryMarkdown = $summaryMarkdown.Replace('{MINIO_SECRET}', $minioSecret)
$summaryMarkdown = $summaryMarkdown.Replace('{K_PG_PASS}', $kPgPass)
$summaryMarkdown = $summaryMarkdown.Replace('{K_REDIS_PASS}', $kRedisPass)
$summaryMarkdown = $summaryMarkdown.Replace('{K_NEO4J_PASS}', $kNeo4jPass)
$summaryMarkdown = $summaryMarkdown.Replace('{K_NEO4J_READER_PASS}', $kNeo4jReaderPass)
$summaryMarkdown = $summaryMarkdown.Replace('{K_NEO4J_WRITER_PASS}', $kNeo4jWriterPass)
$summaryMarkdown = $summaryMarkdown.Replace('{K_MINIO_ACCESS}', $kMinioAccess)
$summaryMarkdown = $summaryMarkdown.Replace('{K_MINIO_SECRET}', $kMinioSecret)
$summaryMarkdown = $summaryMarkdown.Replace('{SMTP_PASS}', $smtpPass)
$summaryMarkdown = $summaryMarkdown.Replace('{SERVICE_JWT_FP}', $serviceJwtFp)
$summaryMarkdown = $summaryMarkdown.Replace('{K_SERVICE_JWT_FP}', $kServiceJwtFp)
$summaryMarkdown = $summaryMarkdown.Replace('{K_GRANT_FP}', $kGrantFp)
$summaryMarkdown = $summaryMarkdown.Replace('{OIDC_SIGN_SUBJECT}', $oidcSignCert["Subject"])
$summaryMarkdown = $summaryMarkdown.Replace('{OIDC_SIGN_SERIAL}', $oidcSignCert["Serial"])
$summaryMarkdown = $summaryMarkdown.Replace('{OIDC_SIGN_DATES}', $oidcSignCert["Dates"])
$summaryMarkdown = $summaryMarkdown.Replace('{OIDC_SIGN_FP}', $oidcSignCert["Fingerprint"])
$summaryMarkdown = $summaryMarkdown.Replace('{OIDC_ENC_SUBJECT}', $oidcEncCert["Subject"])
$summaryMarkdown = $summaryMarkdown.Replace('{OIDC_ENC_SERIAL}', $oidcEncCert["Serial"])
$summaryMarkdown = $summaryMarkdown.Replace('{OIDC_ENC_DATES}', $oidcEncCert["Dates"])
$summaryMarkdown = $summaryMarkdown.Replace('{OIDC_ENC_FP}', $oidcEncCert["Fingerprint"])
$summaryMarkdown = $summaryMarkdown.Replace('{K_DP_SUBJECT}', $kDpCert["Subject"])
$summaryMarkdown = $summaryMarkdown.Replace('{K_DP_SERIAL}', $kDpCert["Serial"])
$summaryMarkdown = $summaryMarkdown.Replace('{K_DP_DATES}', $kDpCert["Dates"])
$summaryMarkdown = $summaryMarkdown.Replace('{K_DP_FP}', $kDpCert["Fingerprint"])

Write-Utf8NoBom -Path (Join-Path $BackupDirectory "CREDENTIALS_SUMMARY.md") -Value $summaryMarkdown
Write-Output "Deployment secrets are ready in $SecretsDirectory, copied to $RuntimeSecretsDirectory, and backed up to $BackupDirectory (including CREDENTIALS_SUMMARY.md)."
