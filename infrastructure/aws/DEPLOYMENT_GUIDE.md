# AWS Production Infrastructure & Managed Secrets Deployment Guide

This guide details the step-by-step procedure for deploying SperoFlow's managed secrets infrastructure to an AWS EC2 instance using AWS Secrets Manager (SM), Parameter Store (SSM), KMS Customer Managed Keys (CMK), and IAM Instance Profiles.

---

## Prerequisites

1. **AWS CLI v2** installed and configured on your admin machine with administrator privileges (`aws sts get-caller-identity`).
2. **PowerShell 7+** (pwsh) installed.
3. Target **EC2 instance** running Ubuntu/Debian or Amazon Linux with Docker and Docker Compose installed.

---

## Step 1: Deploy CloudFormation Stack (KMS + IAM Roles)

Run the CloudFormation deployment to create the KMS Customer Managed Key, KMS Key Alias (`alias/speroflow-secrets-prod`), and EC2 IAM Instance Profile:

```bash
aws cloudformation deploy \
  --template-file infrastructure/aws/speroflow-secrets-stack.yaml \
  --stack-name speroflow-secrets-prod \
  --parameter-overrides EnvironmentName=prod \
  --capabilities CAPABILITY_NAMED_IAM \
  --region us-east-1
```

Verify stack outputs:
```bash
aws cloudformation describe-stacks \
  --stack-name speroflow-secrets-prod \
  --region us-east-1 \
  --query "Stacks[0].Outputs"
```

---

## Step 2: Populate AWS Secrets Manager (Admin Workstation)

From your local repository root on your admin workstation, generate/bootstrap local secrets or use your production credentials, then push them to Secrets Manager:

```powershell
# 1. Bootstrap local secrets (if starting fresh)
powershell -ExecutionPolicy Bypass -File scripts/bootstrap-secrets.ps1

# 2. Validate secrets catalog against Compose definition
powershell -ExecutionPolicy Bypass -File scripts/validate-secrets-catalog.ps1

# 3. Push catalog secrets to AWS Secrets Manager
powershell -ExecutionPolicy Bypass -File scripts/aws-secrets-push.ps1 -Environment prod -Region us-east-1 -KmsKeyId alias/speroflow-secrets-prod
```

---

## Step 3: Attach IAM Instance Profile to EC2

Attach the generated `SperoFlow-Ec2InstanceProfile-prod` to your EC2 instance:

```bash
# Get your EC2 Instance ID
INSTANCE_ID=$(aws ec2 describe-instances --filters "Name=tag:Name,Values=speroflow-prod" --query "Reservations[0].Instances[0].InstanceId" --output text)

# Attach Instance Profile
aws ec2 associate-iam-instance-profile \
  --instance-id $INSTANCE_ID \
  --iam-instance-profile Name=SperoFlow-Ec2InstanceProfile-prod \
  --region us-east-1
```

---

## Step 4: Configure Boot-Time Secret Pull on EC2 Host

On the target EC2 host, copy and enable the systemd service to automatically materialize secrets from AWS Secrets Manager before Docker Compose starts:

```bash
# 1. Copy pull script and systemd unit
sudo cp infrastructure/aws/speroflow-secrets-pull.sh /usr/local/bin/speroflow-secrets-pull.sh
sudo chmod +x /usr/local/bin/speroflow-secrets-pull.sh

sudo cp infrastructure/aws/speroflow-secrets-pull.service /etc/systemd/system/speroflow-secrets-pull.service

# 2. Enable systemd unit
sudo systemctl daemon-reload
sudo systemctl enable speroflow-secrets-pull.service

# 3. Perform first manual secret pull test
sudo /usr/local/bin/speroflow-secrets-pull.sh
```

---

## Step 5: Start SperoFlow Stacks

Once secrets are materialized into `infrastructure/secrets/`, launch the production Compose stacks:

```bash
# Launch Knowledge Platform and Main App Stacks
docker compose -f knowledge-platform/compose.yaml up -d
docker compose -f compose.yaml -f compose.prod.yaml up -d

# Optional: Launch Monitoring Profile (Prometheus + Grafana on 127.0.0.1)
docker compose -f compose.yaml -f compose.prod.yaml -f compose.monitoring.yaml --profile monitoring up -d
```

---

## Step 6: Post-Deployment Smoke Checks

Run post-deployment validation:

```powershell
# 1. Container Health Audit
powershell -ExecutionPolicy Bypass -File scripts/stack-status.ps1 -FailOnUnhealthy

# 2. Release Smoke Probes
powershell -ExecutionPolicy Bypass -File scripts/smoke-release.ps1

# 3. Browser E2E Live Test
$env:E2E_BASE_URL="https://app.your-domain.com"
$env:E2E_EMAIL="admin@your-domain.com"
$env:E2E_PASSWORD="YourPasswordHere"
cd e2e
npm run test:live
```
