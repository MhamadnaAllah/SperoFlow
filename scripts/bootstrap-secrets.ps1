param(
    [string]$SecretsDirectory = (Join-Path $PSScriptRoot "..\infrastructure\secrets"),
    [string]$RuntimeSecretsDirectory = (Join-Path $PSScriptRoot "..\secrets"),
    [string]$BackupDirectory = (Join-Path $PSScriptRoot "..\secrets_backup"),
    # Prefer env BEDROCK_API_KEY. Never embed a real key as a script default.
    [string]$BedrockApiKey = $(if ($env:BEDROCK_API_KEY) { $env:BEDROCK_API_KEY } else { "" }),
    [switch]$Rotate,
    # Dev-only: write CREDENTIALS_SUMMARY.md with raw secret values. Never use on shared hosts.
    [switch]$WritePlaintextSummary,
    # Legacy dual .txt copies under secrets/. Compose does not use these.
    [switch]$WriteLegacyTxtCopies
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

    if ([string]::IsNullOrWhiteSpace($Value)) {
        if (-not (Test-Path -LiteralPath $path)) {
            Write-Utf8NoBom -Path $path -Value ""
            Write-Output "Created empty placeholder for $Name (set via -BedrockApiKey or BEDROCK_API_KEY, or mount from a secrets manager)."
        }
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

function Get-RsaKeyFingerprint {
    param([string]$PublicPath)
    if (-not (Test-Path -LiteralPath $PublicPath)) {
        return "(missing)"
    }
    $lines = & $openssl.Source rsa -pubin -in $PublicPath -outform DER 2>$null | & $openssl.Source dgst -sha256
    $lineStr = $lines -join " "
    if ($lineStr -match "=\s*([a-fA-F0-9]+)") {
        return $matches[1].ToLowerInvariant()
    }
    return $lineStr.Trim()
}

function Get-PfxCertSummary {
    param([string]$PfxPath, [string]$Password)
    if (-not (Test-Path -LiteralPath $PfxPath) -or [string]::IsNullOrWhiteSpace($Password)) {
        return @{
            Subject = "(missing)"
            Fingerprint = "(missing)"
            Serial = "(missing)"
            Dates = "(missing)"
        }
    }
    $subjectRaw = (& $openssl.Source pkcs12 -in $PfxPath -nodes -passin "pass:$Password" 2>$null | & $openssl.Source x509 -noout -subject) -join " "
    $fpRaw = (& $openssl.Source pkcs12 -in $PfxPath -nodes -passin "pass:$Password" 2>$null | & $openssl.Source x509 -noout -fingerprint -sha256) -join " "
    $datesRaw = (& $openssl.Source pkcs12 -in $PfxPath -nodes -passin "pass:$Password" 2>$null | & $openssl.Source x509 -noout -dates) -join " "
    $serialRaw = (& $openssl.Source pkcs12 -in $PfxPath -nodes -passin "pass:$Password" 2>$null | & $openssl.Source x509 -noout -serial) -join " "

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

function Test-SecretPresent {
    param([string]$Name)
    $path = Join-Path $SecretsDirectory $Name
    if (-not (Test-Path -LiteralPath $path)) {
        return "missing"
    }
    $raw = (Get-Content -LiteralPath $path -Raw -ErrorAction SilentlyContinue)
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return "empty"
    }
    return "present"
}

New-Item -ItemType Directory -Force -Path $SecretsDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $RuntimeSecretsDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $BackupDirectory | Out-Null

$randomSecretNames = @(
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
)
$randomSecretNames | ForEach-Object { Ensure-RandomSecret -Name $_ }

Ensure-FixedSecret -Name "bedrock_api_key" -Value $BedrockApiKey

Ensure-RsaKeyPair -PrivateName "service_jwt_private_key" -PublicName "service_jwt_public_key"
Ensure-RsaKeyPair -PrivateName "knowledge_service_jwt_private_key" -PublicName "knowledge_service_jwt_public_key"
Ensure-RsaKeyPair -PrivateName "knowledge_grant_private_key" -PublicName "knowledge_grant_public_key"

Ensure-PfxCertificate -CertificateName "oidc_signing_certificate" -PasswordName "oidc_signing_certificate_password" -Subject "/CN=SperoFlow OIDC Signing"
Ensure-PfxCertificate -CertificateName "oidc_encryption_certificate" -PasswordName "oidc_encryption_certificate_password" -Subject "/CN=SperoFlow OIDC Encryption"
Ensure-PfxCertificate -CertificateName "knowledge_portal_data_protection_certificate" -PasswordName "knowledge_portal_data_protection_certificate_password" -Subject "/CN=SperoFlow Knowledge Portal Data Protection"

# Mirror secret files to runtime mount dir and optional offline backup (file copies, not markdown dumps).
Get-ChildItem -Path $SecretsDirectory -File | Where-Object { $_.Name -ne ".gitignore" -and $_.Name -ne "README.md" } | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $BackupDirectory $_.Name) -Force
    Copy-Item -Path $_.FullName -Destination (Join-Path $RuntimeSecretsDirectory $_.Name) -Force
}

if ($WriteLegacyTxtCopies) {
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
    Write-Output "Wrote legacy .txt secret copies under $RuntimeSecretsDirectory (opt-in)."
}

$oidcSignPass = if (Test-Path (Join-Path $SecretsDirectory "oidc_signing_certificate_password")) {
    (Get-Content (Join-Path $SecretsDirectory "oidc_signing_certificate_password") -Raw).Trim()
} else { "" }
$oidcEncPass = if (Test-Path (Join-Path $SecretsDirectory "oidc_encryption_certificate_password")) {
    (Get-Content (Join-Path $SecretsDirectory "oidc_encryption_certificate_password") -Raw).Trim()
} else { "" }
$kDpPass = if (Test-Path (Join-Path $SecretsDirectory "knowledge_portal_data_protection_certificate_password")) {
    (Get-Content (Join-Path $SecretsDirectory "knowledge_portal_data_protection_certificate_password") -Raw).Trim()
} else { "" }

$serviceJwtFp = Get-RsaKeyFingerprint (Join-Path $SecretsDirectory "service_jwt_public_key")
$kServiceJwtFp = Get-RsaKeyFingerprint (Join-Path $SecretsDirectory "knowledge_service_jwt_public_key")
$kGrantFp = Get-RsaKeyFingerprint (Join-Path $SecretsDirectory "knowledge_grant_public_key")

$oidcSignCert = Get-PfxCertSummary -PfxPath (Join-Path $SecretsDirectory "oidc_signing_certificate") -Password $oidcSignPass
$oidcEncCert = Get-PfxCertSummary -PfxPath (Join-Path $SecretsDirectory "oidc_encryption_certificate") -Password $oidcEncPass
$kDpCert = Get-PfxCertSummary -PfxPath (Join-Path $SecretsDirectory "knowledge_portal_data_protection_certificate") -Password $kDpPass

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"

$inventoryLines = New-Object System.Collections.Generic.List[string]
$inventoryLines.Add("# SperoFlow Secrets Inventory")
$inventoryLines.Add("")
$inventoryLines.Add("Generated At: $timestamp")
$inventoryLines.Add("")
$inventoryLines.Add("> Safe by default: this file lists presence and public fingerprints only.")
$inventoryLines.Add("> It does **not** contain passwords, tokens, private keys, or API keys.")
$inventoryLines.Add("> Raw values live only in Docker secret files under infrastructure/secrets (gitignored).")
$inventoryLines.Add("")
$inventoryLines.Add("## Secret file status")
$inventoryLines.Add("")
$allSecretNames = $randomSecretNames + @(
    "bedrock_api_key",
    "service_jwt_private_key",
    "service_jwt_public_key",
    "knowledge_service_jwt_private_key",
    "knowledge_service_jwt_public_key",
    "knowledge_grant_private_key",
    "knowledge_grant_public_key",
    "oidc_signing_certificate",
    "oidc_signing_certificate_password",
    "oidc_encryption_certificate",
    "oidc_encryption_certificate_password",
    "knowledge_portal_data_protection_certificate",
    "knowledge_portal_data_protection_certificate_password"
)
foreach ($name in $allSecretNames) {
    $status = Test-SecretPresent -Name $name
    $inventoryLines.Add("- ``$name``: **$status**")
}
$inventoryLines.Add("")
$inventoryLines.Add("## RSA public key fingerprints (SHA-256)")
$inventoryLines.Add("")
$inventoryLines.Add("- service_jwt_public_key: ``$serviceJwtFp``")
$inventoryLines.Add("- knowledge_service_jwt_public_key: ``$kServiceJwtFp``")
$inventoryLines.Add("- knowledge_grant_public_key: ``$kGrantFp``")
$inventoryLines.Add("")
$inventoryLines.Add("## Certificate inventory")
$inventoryLines.Add("")
$inventoryLines.Add("### OIDC signing")
$inventoryLines.Add("- Subject: $($oidcSignCert['Subject'])")
$inventoryLines.Add("- Serial: $($oidcSignCert['Serial'])")
$inventoryLines.Add("- Validity: $($oidcSignCert['Dates'])")
$inventoryLines.Add("- SHA256: $($oidcSignCert['Fingerprint'])")
$inventoryLines.Add("")
$inventoryLines.Add("### OIDC encryption")
$inventoryLines.Add("- Subject: $($oidcEncCert['Subject'])")
$inventoryLines.Add("- Serial: $($oidcEncCert['Serial'])")
$inventoryLines.Add("- Validity: $($oidcEncCert['Dates'])")
$inventoryLines.Add("- SHA256: $($oidcEncCert['Fingerprint'])")
$inventoryLines.Add("")
$inventoryLines.Add("### Knowledge portal data protection")
$inventoryLines.Add("- Subject: $($kDpCert['Subject'])")
$inventoryLines.Add("- Serial: $($kDpCert['Serial'])")
$inventoryLines.Add("- Validity: $($kDpCert['Dates'])")
$inventoryLines.Add("- SHA256: $($kDpCert['Fingerprint'])")
$inventoryLines.Add("")
$inventoryLines.Add("## How to read a secret on the host")
$inventoryLines.Add("")
$inventoryLines.Add('```powershell')
$inventoryLines.Add('Get-Content .\infrastructure\secrets\postgres_password -Raw')
$inventoryLines.Add('```')
$inventoryLines.Add("")
$inventoryLines.Add("## Rotation")
$inventoryLines.Add("")
$inventoryLines.Add("After any suspected exposure, rotate on the deploy host:")
$inventoryLines.Add("")
$inventoryLines.Add('```powershell')
$inventoryLines.Add('powershell -ExecutionPolicy Bypass -File .\scripts\bootstrap-secrets.ps1 -Rotate')
$inventoryLines.Add('```')
$inventoryLines.Add("")
$inventoryLines.Add("Do not commit this directory. Prefer a secrets manager for production.")

$inventoryPath = Join-Path $BackupDirectory "SECRETS_INVENTORY.md"
Write-Utf8NoBom -Path $inventoryPath -Value ($inventoryLines -join [Environment]::NewLine)
Write-Output "Wrote non-secret inventory: $inventoryPath"

# Remove stale plaintext summary unless explicitly requested again.
$plaintextSummaryPath = Join-Path $BackupDirectory "CREDENTIALS_SUMMARY.md"
if (-not $WritePlaintextSummary -and (Test-Path -LiteralPath $plaintextSummaryPath)) {
    Remove-Item -LiteralPath $plaintextSummaryPath -Force
    Write-Output "Removed stale plaintext CREDENTIALS_SUMMARY.md (default is inventory-only)."
}

if ($WritePlaintextSummary) {
    Write-Warning "WritePlaintextSummary is enabled. The output file contains live secrets. Keep it offline and never commit it."

    function Read-SecretRaw {
        param([string]$Name)
        $path = Join-Path $SecretsDirectory $Name
        if (-not (Test-Path -LiteralPath $path)) { return "(missing)" }
        return (Get-Content -LiteralPath $path -Raw).Trim()
    }

    $template = @'
# SperoFlow Credentials & Secrets Backup (PLAINTEXT — DEV ONLY)

Generated At: {TIMESTAMP}

> DANGER: This file contains private secrets. DO NOT commit to Git.
> Prefer SECRETS_INVENTORY.md. Regenerate without -WritePlaintextSummary for safe defaults.

## Cloud & AI
- bedrock_api_key: {BEDROCK_KEY}

## Bootstrap
- admin_bootstrap_token: {ADMIN_TOKEN}

## Main infrastructure
- postgres_password: {PG_PASS}
- neo4j_password: {NEO4J_PASS}
- redis_password: {REDIS_PASS}
- minio_access_key: {MINIO_ACCESS}
- minio_secret_key: {MINIO_SECRET}
- smtp_password: {SMTP_PASS}

## Knowledge infrastructure
- knowledge_postgres_password: {K_PG_PASS}
- knowledge_redis_password: {K_REDIS_PASS}
- knowledge_neo4j_password: {K_NEO4J_PASS}
- knowledge_neo4j_reader_password: {K_NEO4J_READER_PASS}
- knowledge_neo4j_writer_password: {K_NEO4J_WRITER_PASS}
- knowledge_minio_access_key: {K_MINIO_ACCESS}
- knowledge_minio_secret_key: {K_MINIO_SECRET}

## RSA fingerprints
- service_jwt_public_key: {SERVICE_JWT_FP}
- knowledge_service_jwt_public_key: {K_SERVICE_JWT_FP}
- knowledge_grant_public_key: {K_GRANT_FP}

## Certificates
- OIDC signing subject: {OIDC_SIGN_SUBJECT} serial={OIDC_SIGN_SERIAL} fp={OIDC_SIGN_FP}
- OIDC encryption subject: {OIDC_ENC_SUBJECT} serial={OIDC_ENC_SERIAL} fp={OIDC_ENC_FP}
- Knowledge DP subject: {K_DP_SUBJECT} serial={K_DP_SERIAL} fp={K_DP_FP}
'@

    $summaryMarkdown = $template
    $summaryMarkdown = $summaryMarkdown.Replace('{TIMESTAMP}', $timestamp)
    $summaryMarkdown = $summaryMarkdown.Replace('{BEDROCK_KEY}', (Read-SecretRaw "bedrock_api_key"))
    $summaryMarkdown = $summaryMarkdown.Replace('{ADMIN_TOKEN}', (Read-SecretRaw "admin_bootstrap_token"))
    $summaryMarkdown = $summaryMarkdown.Replace('{PG_PASS}', (Read-SecretRaw "postgres_password"))
    $summaryMarkdown = $summaryMarkdown.Replace('{NEO4J_PASS}', (Read-SecretRaw "neo4j_password"))
    $summaryMarkdown = $summaryMarkdown.Replace('{REDIS_PASS}', (Read-SecretRaw "redis_password"))
    $summaryMarkdown = $summaryMarkdown.Replace('{MINIO_ACCESS}', (Read-SecretRaw "minio_access_key"))
    $summaryMarkdown = $summaryMarkdown.Replace('{MINIO_SECRET}', (Read-SecretRaw "minio_secret_key"))
    $summaryMarkdown = $summaryMarkdown.Replace('{K_PG_PASS}', (Read-SecretRaw "knowledge_postgres_password"))
    $summaryMarkdown = $summaryMarkdown.Replace('{K_REDIS_PASS}', (Read-SecretRaw "knowledge_redis_password"))
    $summaryMarkdown = $summaryMarkdown.Replace('{K_NEO4J_PASS}', (Read-SecretRaw "knowledge_neo4j_password"))
    $summaryMarkdown = $summaryMarkdown.Replace('{K_NEO4J_READER_PASS}', (Read-SecretRaw "knowledge_neo4j_reader_password"))
    $summaryMarkdown = $summaryMarkdown.Replace('{K_NEO4J_WRITER_PASS}', (Read-SecretRaw "knowledge_neo4j_writer_password"))
    $summaryMarkdown = $summaryMarkdown.Replace('{K_MINIO_ACCESS}', (Read-SecretRaw "knowledge_minio_access_key"))
    $summaryMarkdown = $summaryMarkdown.Replace('{K_MINIO_SECRET}', (Read-SecretRaw "knowledge_minio_secret_key"))
    $summaryMarkdown = $summaryMarkdown.Replace('{SMTP_PASS}', (Read-SecretRaw "smtp_password"))
    $summaryMarkdown = $summaryMarkdown.Replace('{SERVICE_JWT_FP}', $serviceJwtFp)
    $summaryMarkdown = $summaryMarkdown.Replace('{K_SERVICE_JWT_FP}', $kServiceJwtFp)
    $summaryMarkdown = $summaryMarkdown.Replace('{K_GRANT_FP}', $kGrantFp)
    $summaryMarkdown = $summaryMarkdown.Replace('{OIDC_SIGN_SUBJECT}', $oidcSignCert["Subject"])
    $summaryMarkdown = $summaryMarkdown.Replace('{OIDC_SIGN_SERIAL}', $oidcSignCert["Serial"])
    $summaryMarkdown = $summaryMarkdown.Replace('{OIDC_SIGN_FP}', $oidcSignCert["Fingerprint"])
    $summaryMarkdown = $summaryMarkdown.Replace('{OIDC_ENC_SUBJECT}', $oidcEncCert["Subject"])
    $summaryMarkdown = $summaryMarkdown.Replace('{OIDC_ENC_SERIAL}', $oidcEncCert["Serial"])
    $summaryMarkdown = $summaryMarkdown.Replace('{OIDC_ENC_FP}', $oidcEncCert["Fingerprint"])
    $summaryMarkdown = $summaryMarkdown.Replace('{K_DP_SUBJECT}', $kDpCert["Subject"])
    $summaryMarkdown = $summaryMarkdown.Replace('{K_DP_SERIAL}', $kDpCert["Serial"])
    $summaryMarkdown = $summaryMarkdown.Replace('{K_DP_FP}', $kDpCert["Fingerprint"])

    Write-Utf8NoBom -Path $plaintextSummaryPath -Value $summaryMarkdown
    Write-Output "Wrote PLAINTEXT summary (opt-in only): $plaintextSummaryPath"
}

Write-Output "Deployment secrets are ready in $SecretsDirectory, mirrored to $RuntimeSecretsDirectory and $BackupDirectory."
Write-Output "Default inventory (no raw secrets): $inventoryPath"
if ($Rotate) {
    Write-Warning "Secrets were rotated. Redeploy services and invalidate any previously shared credential dumps."
}
