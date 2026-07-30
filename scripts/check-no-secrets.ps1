<#
.SYNOPSIS
  Fail if likely secret material appears in git-tracked (or scanned) files.

.DESCRIPTION
  Used locally and in CI to catch accidental commits of passwords, private keys,
  Bedrock-style keys, and plaintext credential dumps.
#>
param(
    [switch]$StagedOnly,
    [switch]$AllFiles
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

$patterns = @(
    @{ Name = "AWS Bedrock-style key"; Regex = 'ABSKT[A-Za-z0-9+/=]{20,}' },
    @{ Name = "Private RSA PEM"; Regex = '-----BEGIN (RSA |OPENSSH |EC )?PRIVATE KEY-----' },
    @{ Name = "Plaintext credentials dump heading"; Regex = '(?m)^# SperoFlow Credentials & Secrets Backup' },
    @{ Name = "Hardcoded demo password"; Regex = 'SperoFlowUser2026!|KnowledgeAdmin2026!|KnowledgeOwner2026!' },
    # Require a clear assignment boundary so concurrencyToken: "x" in tests does not match.
    @{
        Name = "Generic high-entropy assignment"
        Regex = '(?i)(?:^|[^A-Za-z0-9_])(?:password|secret|api[_-]?key)\s*[:=]\s*["''][A-Za-z0-9+/=_\-]{24,}["'']'
    }
)

# Large curated corpora, build artifacts, locks, and media are out of scope.
$excludePathRegex = '(?i)(node_modules|/bin/|/obj/|\.next/|__pycache__|package-lock\.json|yarn\.lock|^knowledge-base/|/backups/|\.(dll|pdb|png|jpg|jpeg|gif|ico|webp|woff2?|eot|ttf|pdf|exe|so|dylib|pyc)$)'

# Scripts/docs that legitimately mention scrubbing CREDENTIALS_SUMMARY or secret file names.
$allowPathRegex = '(?i)^(\.github/.*|scripts/(check-no-secrets|bootstrap-secrets|bootstrap-secrets-v2|reset-secrets-before-git|init-docker-secrets|aws-secrets-.*|validate-secrets-catalog|retire-legacy-knowledge-tables|rotate-backups)\.(ps1|sql)|infrastructure/secrets/README\.md|infrastructure/(OPERATIONS|DEPLOYMENT|MANAGED_SECRETS)\.md|infrastructure/aws/.*|e2e/.*|PRODUCT_OBJECTIVE_STATUS\.md|README\.md|grok_session\.md|implementation_plan.*\.md)$'

function Get-ScanTargets {
    if ($AllFiles) {
        return Get-ChildItem -Recurse -File |
            Where-Object { $_.FullName -notmatch $excludePathRegex } |
            ForEach-Object { $_.FullName.Substring($root.Length).TrimStart('\', '/') -replace '\\', '/' }
    }

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "git is required unless -AllFiles is specified."
    }

    if ($StagedOnly) {
        return @(git diff --cached --name-only --diff-filter=ACMR)
    }

    return @(git ls-files)
}

$targets = Get-ScanTargets |
    ForEach-Object { $_ -replace '\\', '/' } |
    Where-Object {
        $_ -and
        ($_ -notmatch $excludePathRegex) -and
        ($_ -notmatch $allowPathRegex)
    }

$violations = New-Object System.Collections.Generic.List[string]

foreach ($rel in $targets) {
    $path = Join-Path $root ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }

    $info = Get-Item -LiteralPath $path
    if ($info.Length -gt 1MB) {
        continue
    }

    # Skip unit/integration fixtures that use short fake tokens
    if ($rel -match '(?i)\.(test|spec)\.(js|jsx|ts|tsx|cs|py)$') {
        # Still scan tests for real Bedrock keys / private PEMs / demo passwords
        $testPatterns = $patterns | Where-Object { $_.Name -ne "Generic high-entropy assignment" }
    } else {
        $testPatterns = $patterns
    }

    try {
        $content = Get-Content -LiteralPath $path -Raw -ErrorAction Stop
    } catch {
        continue
    }

    if ([string]::IsNullOrEmpty($content)) {
        continue
    }

    foreach ($pattern in $testPatterns) {
        if ($content -match $pattern.Regex) {
            $violations.Add("$($pattern.Name): $rel")
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Secret-pattern scan FAILED:" -ForegroundColor Red
    $violations | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw "Refusing to proceed: potential secrets detected in scanned files."
}

Write-Host "Secret-pattern scan passed ($($targets.Count) files)." -ForegroundColor Green
