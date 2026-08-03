# PowerShell Deployment Script: Step 4 - AWS ACM SSL Certificate & ALB Setup
# Domain: speroflow.space | Account ID: 564371180494

$ErrorActionPreference = "Stop"

$AWS_REGION = "us-east-1"
$DOMAIN_NAME = "speroflow.space"
$HOSTED_ZONE_ID = "Z096280732MZXG4UN1KDI"

Write-Host "==> 1. Requesting AWS ACM Certificate for '$DOMAIN_NAME' and '*.$DOMAIN_NAME'..." -ForegroundColor Cyan

$certJson = aws acm request-certificate `
    --domain-name $DOMAIN_NAME `
    --validation-method DNS `
    --subject-alternative-names "*.$DOMAIN_NAME" `
    --idempotency-token "speroflowcert2026" `
    --region $AWS_REGION `
    --output json | ConvertFrom-Json

$certArn = $certJson.CertificateArn
Write-Host "==> Certificate Requested! ARN: $certArn" -ForegroundColor Green

# Save Cert ARN to file
$certFilePath = Join-Path $PSScriptRoot "acm_certificate_arn.txt"
$certArn | Out-File -FilePath $certFilePath -Encoding utf8

Write-Host "==> 2. Setting up Route 53 DNS Validation for ACM Certificate..." -ForegroundColor Cyan
Start-Sleep -Seconds 5

$certDesc = aws acm describe-certificate --certificate-arn $certArn --region $AWS_REGION --output json | ConvertFrom-Json

foreach ($options in $certDesc.Certificate.DomainValidationOptions) {
    if ($options.ResourceRecord) {
        $rName = $options.ResourceRecord.Name
        $rType = $options.ResourceRecord.Type
        $rValue = $options.ResourceRecord.Value
        
        Write-Host "   - Creating Validation Record: $rName -> $rValue" -ForegroundColor Yellow
        
        $changeBatch = @"
{
  "Comment": "ACM DNS Validation Record",
  "Changes": [
    {
      "Action": "UPSERT",
      "ResourceRecordSet": {
        "Name": "$rName",
        "Type": "$rType",
        "TTL": 300,
        "ResourceRecords": [
          {
            "Value": "$rValue"
          }
        ]
      }
    }
  ]
}
"@
        $tmpBatchFile = [System.IO.Path]::GetTempFileName()
        $changeBatch | Out-File -FilePath $tmpBatchFile -Encoding utf8
        aws route53 change-resource-record-sets --hosted-zone-id $HOSTED_ZONE_ID --change-batch "file://$tmpBatchFile" 2>$null
        Remove-Item -Path $tmpBatchFile -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "==> 3. Creating Route 53 A-Records for 'speroflow.space' pointing to Elastic IP (100.59.55.87)..." -ForegroundColor Cyan
$elasticIp = "100.59.55.87"
$subdomains = @("speroflow.space", "app.speroflow.space", "api.speroflow.space", "ai.speroflow.space")

foreach ($sub in $subdomains) {
    $dnsBatch = @"
{
  "Comment": "A Record for $sub",
  "Changes": [
    {
      "Action": "UPSERT",
      "ResourceRecordSet": {
        "Name": "$sub",
        "Type": "A",
        "TTL": 300,
        "ResourceRecords": [
          {
            "Value": "$elasticIp"
          }
        ]
      }
    }
  ]
}
"@
    $tmpBatchFile = [System.IO.Path]::GetTempFileName()
    $dnsBatch | Out-File -FilePath $tmpBatchFile -Encoding utf8
    aws route53 change-resource-record-sets --hosted-zone-id $HOSTED_ZONE_ID --change-batch "file://$tmpBatchFile" 2>$null
    Remove-Item -Path $tmpBatchFile -Force -ErrorAction SilentlyContinue
    Write-Host "   - Created A-Record for $sub -> $elasticIp" -ForegroundColor Green
}

Write-Host "==> Step 4 Complete: ACM Certificate requested & Route 53 DNS records created!" -ForegroundColor Green
