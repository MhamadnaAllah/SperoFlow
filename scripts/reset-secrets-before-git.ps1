# Scrub local secret material and fail if git would stage secret paths.
# Run from the repository root before committing when secrets may have been generated locally.
$ErrorActionPreference = "Stop"

$WorkspaceDir = if ($PSScriptRoot) {
    (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
} else {
    (Get-Location).Path
}

$InfraSecretsDir = Join-Path $WorkspaceDir "infrastructure\secrets"
$SecretsDir = Join-Path $WorkspaceDir "secrets"
$BackupDir = Join-Path $WorkspaceDir "secrets_backup"

Write-Host "Scrubbing generated secrets and credential backups before Git commit..." -ForegroundColor Yellow

# 1. Remove infrastructure secrets directory files (preserve .gitignore and README.md)
if (Test-Path $InfraSecretsDir) {
    Get-ChildItem -Path $InfraSecretsDir -File |
        Where-Object { $_.Name -ne ".gitignore" -and $_.Name -ne "README.md" } |
        Remove-Item -Force
    Write-Host "Scrubbed directory: $InfraSecretsDir (preserved .gitignore & README.md)" -ForegroundColor Green
}

# 2. Remove runtime secrets directory files
if (Test-Path $SecretsDir) {
    Get-ChildItem -Path $SecretsDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Write-Host "Scrubbed directory: $SecretsDir" -ForegroundColor Green
}

# 3. Remove backup credentials directory files (including CREDENTIALS_SUMMARY.md / SECRETS_INVENTORY.md)
if (Test-Path $BackupDir) {
    Get-ChildItem -Path $BackupDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Write-Host "Scrubbed directory: $BackupDir" -ForegroundColor Green
}

# 4. Clean untracked local .env files
$LocalEnvFiles = @(".env", ".env.local", ".env.production", "aura.env")
foreach ($envFile in $LocalEnvFiles) {
    $envPath = Join-Path $WorkspaceDir $envFile
    if (Test-Path $envPath) {
        Remove-Item -Path $envPath -Force
        Write-Host "Removed local $envFile file." -ForegroundColor Green
    }
}

# 5. Unstage any accidentally tracked secret paths (best-effort)
Push-Location $WorkspaceDir
try {
    if (Get-Command git -ErrorAction SilentlyContinue) {
        git rm --cached -r --ignore-unmatch secrets/ secrets_backup/ infrastructure/secrets/ .env .env.local .env.production aura.env 2>$null | Out-Null

        $forbiddenPatterns = @(
            '^secrets/',
            '^secrets_backup/',
            '^infrastructure/secrets/',
            '^\.env$',
            '^\.env\.',
            '^aura\.env$'
        )
        $staged = @(git diff --cached --name-only 2>$null)
        $violations = @()
        foreach ($path in $staged) {
            $normalized = ($path -replace '\\', '/').Trim()
            foreach ($pattern in $forbiddenPatterns) {
                if ($normalized -match $pattern) {
                    # Allow the documented exceptions under infrastructure/secrets
                    if ($normalized -eq 'infrastructure/secrets/.gitignore' -or $normalized -eq 'infrastructure/secrets/README.md') {
                        continue
                    }
                    $violations += $normalized
                    break
                }
            }
        }

        if ($violations.Count -gt 0) {
            Write-Host "ERROR: Secret-related paths are still staged for commit:" -ForegroundColor Red
            $violations | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
            throw "Refusing to leave secret material staged. Unstage these paths and re-run."
        }

        Write-Host "Git index check passed (no secret paths staged)." -ForegroundColor Green
    }
}
finally {
    Pop-Location
}

Write-Host "All secrets, credentials, and API key files scrubbed." -ForegroundColor Green
Write-Host "Workspace is clean for Git commit (re-bootstrap secrets on the deploy host after push)." -ForegroundColor Green
