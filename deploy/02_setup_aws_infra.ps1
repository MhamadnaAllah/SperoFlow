# PowerShell Deployment Script: Step 2 - AWS Infrastructure & Route 53 Hosted Zone
# Target Domain: speroflow.space

$ErrorActionPreference = "Stop"

$AWS_REGION = "us-east-1"
$DOMAIN_NAME = "speroflow.space"
$S3_BUCKET = "speroflow-prod-datasets-564371180494"

Write-Host "==> 1. Creating AWS Route 53 Hosted Zone for '$DOMAIN_NAME'..." -ForegroundColor Cyan
$callerRef = "speroflow-hz-" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

$hzJson = aws route53 create-hosted-zone --name $DOMAIN_NAME --caller-reference $callerRef --hosted-zone-config Comment="SperoFlow Production Domain" --output json | ConvertFrom-Json

$hostedZoneId = $hzJson.HostedZone.Id
$nameServers = $hzJson.DelegationSet.NameServers

Write-Host "==> Hosted Zone Created! ID: $hostedZoneId" -ForegroundColor Green
Write-Host "==> Route 53 Nameservers:" -ForegroundColor Yellow
foreach ($ns in $nameServers) {
    Write-Host "    - $ns" -ForegroundColor Yellow
}

# Save Nameservers to text file for reference
$nsFilePath = Join-Path $PSScriptRoot "route53_nameservers.txt"
$nameServers | Out-File -FilePath $nsFilePath -Encoding utf8
Write-Host "==> Saved Nameservers to '$nsFilePath'" -ForegroundColor Green

Write-Host "==> 2. Creating Amazon S3 Bucket '$S3_BUCKET'..." -ForegroundColor Cyan
try {
    aws s3api head-bucket --bucket $S3_BUCKET 2>$null
    Write-Host "   - S3 Bucket '$S3_BUCKET' already exists." -ForegroundColor Green
} catch {
    Write-Host "   - Creating S3 Bucket '$S3_BUCKET'..." -ForegroundColor Yellow
    aws s3api create-bucket --bucket $S3_BUCKET --region $AWS_REGION
    aws s3api put-bucket-encryption --bucket $S3_BUCKET --server-side-encryption-configuration '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}'
}

Write-Host "==> 3. Allocating AWS Elastic IP..." -ForegroundColor Cyan
try {
    $eipJson = aws ec2 allocate-address --domain vpc --output json | ConvertFrom-Json
    $publicIp = $eipJson.PublicIp
    $allocationId = $eipJson.AllocationId
    Write-Host "   - Allocated Elastic IP: $publicIp (Allocation ID: $allocationId)" -ForegroundColor Green
    
    $eipFilePath = Join-Path $PSScriptRoot "elastic_ip.txt"
    "PublicIp: $publicIp`nAllocationId: $allocationId" | Out-File -FilePath $eipFilePath -Encoding utf8
} catch {
    Write-Host "   - Note: Could not allocate new Elastic IP (or limit reached). Will use standard EC2 public IP." -ForegroundColor Yellow
}

Write-Host "==> Step 2 Complete!" -ForegroundColor Green
