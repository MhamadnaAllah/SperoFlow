# PowerShell Deployment Script: Step 1 - AWS CLI Auth & ECR Provisioning
# Account ID: 564371180494 | Region: us-east-1

$ErrorActionPreference = "Stop"

$csvPath = "C:\Users\fal\Desktop\aws\mhamadnaallah@gmail.com_accessKeys.csv"
if (Test-Path $csvPath) {
    $csv = Import-Csv $csvPath
    $AWS_ACCESS_KEY_ID = $csv.'Access key ID'
    $AWS_SECRET_ACCESS_KEY = $csv.'Secret access key'
} else {
    $AWS_ACCESS_KEY_ID = $env:AWS_ACCESS_KEY_ID
    $AWS_SECRET_ACCESS_KEY = $env:AWS_SECRET_ACCESS_KEY
}
$AWS_REGION = "us-east-1"
$ACCOUNT_ID = "564371180494"

Write-Host "==> 1. Configuring AWS CLI credentials..." -ForegroundColor Cyan
$env:AWS_ACCESS_KEY_ID = $AWS_ACCESS_KEY_ID
$env:AWS_SECRET_ACCESS_KEY = $AWS_SECRET_ACCESS_KEY
$env:AWS_DEFAULT_REGION = $AWS_REGION

aws configure set aws_access_key_id $AWS_ACCESS_KEY_ID
aws configure set aws_secret_access_key $AWS_SECRET_ACCESS_KEY
aws configure set default.region $AWS_REGION

Write-Host "==> Verifying AWS STS Caller Identity..." -ForegroundColor Cyan
aws sts get-caller-identity

Write-Host "==> 2. Logging into Amazon ECR..." -ForegroundColor Cyan
$ecrRegistry = "$ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com"
aws ecr get-login-password --region $AWS_REGION | docker login --username AWS --password-stdin $ecrRegistry

Write-Host "==> 3. Creating ECR Repositories..." -ForegroundColor Cyan
$repos = @("speroflow/web", "speroflow/api", "speroflow/api-worker", "speroflow/ai-api", "speroflow/ai-worker")

foreach ($repo in $repos) {
    try {
        aws ecr describe-repositories --repository-names $repo --region $AWS_REGION 2>$null
        Write-Host "   - ECR Repository '$repo' already exists." -ForegroundColor Green
    } catch {
        Write-Host "   - Creating ECR Repository '$repo'..." -ForegroundColor Yellow
        aws ecr create-repository --repository-name $repo --region $AWS_REGION
    }
}

Write-Host "==> Step 1 Complete: AWS CLI Auth & ECR Repositories ready!" -ForegroundColor Green
