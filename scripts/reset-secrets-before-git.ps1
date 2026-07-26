# PowerShell script to reset all secret files, backup credentials, and environment keys before pushing to Git.
$ErrorActionPreference = "Continue"

$WorkspaceDir = Get-Location
$InfraSecretsDir = Join-Path $WorkspaceDir "infrastructure\secrets"
$SecretsDir = Join-Path $WorkspaceDir "secrets"
$BackupDir = Join-Path $WorkspaceDir "secrets_backup"

Write-Host "Scrubbing all generated secrets and credential backups before Git commit..." -ForegroundColor Yellow

# 1. Remove infrastructure secrets directory files (preserve .gitignore and README.md)
if (Test-Path $InfraSecretsDir) {
    Get-ChildItem -Path $InfraSecretsDir -File | Where-Object { $_.Name -ne ".gitignore" -and $_.Name -ne "README.md" } | Remove-Item -Force
    Write-Host "Scrubbed directory: $InfraSecretsDir (preserved .gitignore & README.md)" -ForegroundColor Green
}

# 2. Remove runtime secrets directory files
if (Test-Path $SecretsDir) {
    Remove-Item -Path "$SecretsDir\*" -Recurse -Force
    Write-Host "Scrubbed directory: $SecretsDir" -ForegroundColor Green
}

# 3. Remove backup credentials directory files
if (Test-Path $BackupDir) {
    Remove-Item -Path "$BackupDir\*" -Recurse -Force
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

# 5. Check git status for uncommitted secret files
try {
    git rm --cached -r secrets/ secrets_backup/ infrastructure/secrets/ .env .env.local aura.env 2>$null
} catch {}

Write-Host "All secrets, credentials, and API keys scrubbed safely." -ForegroundColor Green
Write-Host "Workspace is clean and ready for Git commit and push!" -ForegroundColor Green

