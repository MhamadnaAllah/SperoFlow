<#
.SYNOPSIS
  Upload local infrastructure/secrets files to AWS Secrets Manager.

.DESCRIPTION
  Creates or updates secrets named speroflow/{Environment}/{name} from the
  catalog. Uses the caller's AWS credentials (SSO profile or instance role).
  Does not print secret values.

.PARAMETER Environment
  Logical environment segment (prod, staging, dev).

.PARAMETER SecretsDirectory
  Local secret files directory (default infrastructure/secrets).

.PARAMETER Region
  AWS region. Defaults to AWS_REGION / AWS_DEFAULT_REGION.

.PARAMETER KmsKeyId
  Optional CMK id/arn/alias for CreateSecret.

.PARAMETER SkipOptionalMissing
  Skip optional catalog entries when the local file is missing or empty.

.PARAMETER DryRun
  Print actions without calling AWS.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9-]+$')]
    [string]$Environment,

    [string]$SecretsDirectory = "",
    [string]$Region = "",
    [string]$KmsKeyId = "",
    [switch]$SkipOptionalMissing,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "aws-secrets-lib.ps1")

if (-not $DryRun) {
    Test-AwsCli
}

$root = Get-SperoFlowRepoRoot
if (-not $SecretsDirectory) {
    $SecretsDirectory = Join-Path $root "infrastructure\secrets"
}
if (-not $Region) {
    $Region = if ($env:AWS_REGION) { $env:AWS_REGION } elseif ($env:AWS_DEFAULT_REGION) { $env:AWS_DEFAULT_REGION } else { "" }
}
if (-not $Region) {
    if ($DryRun) {
        $Region = "us-east-1"
    } else {
        throw "Pass -Region or set AWS_REGION."
    }
}

function Invoke-Aws {
    param([Parameter(Mandatory = $true)][string[]]$AwsArgs)
    & aws @AwsArgs
    if ($LASTEXITCODE -ne 0) {
        throw "AWS CLI failed: aws $($AwsArgs -join ' ')"
    }
}

function Test-SecretExists {
    param([string]$SecretId, [string]$AwsRegion)
    aws secretsmanager describe-secret --secret-id $SecretId --region $AwsRegion 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

$catalog = Get-SperoFlowSecretsCatalog -Root $root
$created = 0
$updated = 0
$skipped = 0

Write-Host "Pushing secrets to Secrets Manager env=$Environment region=$Region" -ForegroundColor Cyan

foreach ($entry in $catalog.secrets) {
    $name = [string]$entry.name
    $localPath = Join-Path $SecretsDirectory $name
    $secretId = Get-SecretArnName -Environment $Environment -Name $name
    $encoding = Get-SecretEncoding -CatalogEntry $entry
    $optional = [bool]$entry.optional

    if (-not (Test-Path -LiteralPath $localPath)) {
        if ($optional -or $SkipOptionalMissing) {
            Write-Host "SKIP missing optional: $name" -ForegroundColor DarkYellow
            $skipped++
            continue
        }
        throw "Required secret file missing: $localPath"
    }

    if ($encoding -eq "text") {
        $raw = Get-Content -LiteralPath $localPath -Raw -ErrorAction SilentlyContinue
        if ([string]::IsNullOrWhiteSpace($raw)) {
            if ($optional -or $SkipOptionalMissing) {
                Write-Host "SKIP empty optional: $name" -ForegroundColor DarkYellow
                $skipped++
                continue
            }
            throw "Required secret file is empty: $localPath"
        }
    }

    if ($DryRun) {
        Write-Host "DRY-RUN would upsert $secretId ($encoding)" -ForegroundColor DarkCyan
        continue
    }

    $exists = Test-SecretExists -SecretId $secretId -AwsRegion $Region
    $tagPairs = @(
        @{ Key = "Application"; Value = "SperoFlow" },
        @{ Key = "Environment"; Value = $Environment },
        @{ Key = "SecretName"; Value = $name },
        @{ Key = "Kind"; Value = [string]$entry.kind }
    )

    if ($encoding -eq "binary") {
        # fileb:// sends raw bytes (correct for PFX).
        $fileUri = "fileb://$localPath"
        if ($exists) {
            Invoke-Aws -AwsArgs @("secretsmanager", "put-secret-value", "--secret-id", $secretId, "--region", $Region, "--secret-binary", $fileUri) | Out-Null
            $updated++
            Write-Host "UPDATED $secretId (binary)" -ForegroundColor Green
        } else {
            $args = [System.Collections.Generic.List[string]]::new()
            $args.AddRange([string[]]@("secretsmanager", "create-secret", "--name", $secretId, "--region", $Region, "--secret-binary", $fileUri))
            foreach ($tag in $tagPairs) {
                $args.Add("--tags")
                $args.Add("Key=$($tag.Key),Value=$($tag.Value)")
            }
            if ($KmsKeyId) {
                $args.Add("--kms-key-id")
                $args.Add($KmsKeyId)
            }
            Invoke-Aws -AwsArgs $args.ToArray() | Out-Null
            $created++
            Write-Host "CREATED $secretId (binary)" -ForegroundColor Green
        }
    } else {
        $text = (Get-Content -LiteralPath $localPath -Raw).TrimEnd("`r", "`n")
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ("speroflow-secret-" + [guid]::NewGuid().ToString("N") + ".txt")
        try {
            Write-Utf8NoBomFile -Path $tmp -Value $text
            # file:// reads text content for --secret-string
            $fileUri = "file://$tmp"
            if ($exists) {
                Invoke-Aws -AwsArgs @("secretsmanager", "put-secret-value", "--secret-id", $secretId, "--region", $Region, "--secret-string", $fileUri) | Out-Null
                $updated++
                Write-Host "UPDATED $secretId" -ForegroundColor Green
            } else {
                $args = [System.Collections.Generic.List[string]]::new()
                $args.AddRange([string[]]@("secretsmanager", "create-secret", "--name", $secretId, "--region", $Region, "--secret-string", $fileUri))
                foreach ($tag in $tagPairs) {
                    $args.Add("--tags")
                    $args.Add("Key=$($tag.Key),Value=$($tag.Value)")
                }
                if ($KmsKeyId) {
                    $args.Add("--kms-key-id")
                    $args.Add($KmsKeyId)
                }
                Invoke-Aws -AwsArgs $args.ToArray() | Out-Null
                $created++
                Write-Host "CREATED $secretId" -ForegroundColor Green
            }
        }
        finally {
            Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host "Push complete. created=$created updated=$updated skipped=$skipped" -ForegroundColor Cyan
if ($DryRun) {
    Write-Host "Dry run only - no AWS changes." -ForegroundColor DarkYellow
} else {
    Write-Host "Scrub admin workstations after push: scripts/reset-secrets-before-git.ps1" -ForegroundColor DarkYellow
}
