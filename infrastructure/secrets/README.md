# Docker Secrets

This directory is ignored by Git (except this README and `.gitignore`).

## Production (recommended): AWS Secrets Manager

Source of truth should be AWS Secrets Manager. See
[`infrastructure/MANAGED_SECRETS.md`](../MANAGED_SECRETS.md).

```powershell
# Admin host: generate once, push, scrub
powershell -ExecutionPolicy Bypass -File scripts/bootstrap-secrets.ps1
powershell -ExecutionPolicy Bypass -File scripts/aws-secrets-push.ps1 -Environment prod -Region us-east-1 -KmsKeyId alias/speroflow-secrets
powershell -ExecutionPolicy Bypass -File scripts/reset-secrets-before-git.ps1

# Deploy host (instance role): pull before compose up
powershell -ExecutionPolicy Bypass -File scripts/aws-secrets-pull.ps1 -Environment prod -Region us-east-1
```

## Local / air-gapped generate

```powershell
powershell -ExecutionPolicy Bypass -File scripts/bootstrap-secrets.ps1
```

The script preserves existing material by default. Use `-Rotate` only in a
planned rotation with a validated rollback.

## Safe inventory (default)

Bootstrap writes a **non-secret** inventory to `secrets_backup/SECRETS_INVENTORY.md`:

- which secret files are present or empty
- RSA public key SHA-256 fingerprints
- certificate subject / serial / fingerprint / validity

It does **not** write passwords, tokens, private keys, or API keys by default.

## Plaintext summary (opt-in only)

```powershell
powershell -ExecutionPolicy Bypass -File scripts/bootstrap-secrets.ps1 -WritePlaintextSummary
```

Use only on an air-gapped or single-operator machine. Never commit
`CREDENTIALS_SUMMARY.md`. Prefer a secrets manager for production.

## Bedrock API key

Do not hardcode Bedrock credentials in scripts. Provide them when generating:

```powershell
$env:BEDROCK_API_KEY = "..."   # or use IAM/workload identity instead
powershell -ExecutionPolicy Bypass -File scripts/bootstrap-secrets.ps1
```

Or pass `-BedrockApiKey`. Prefer least-privilege VM/workload identity over static keys.

## Main Application

- postgres_password, redis_password, neo4j_password
- minio_access_key, minio_secret_key
- admin_bootstrap_token
- service_jwt_private_key, service_jwt_public_key
- oidc_signing_certificate, oidc_signing_certificate_password
- oidc_encryption_certificate, oidc_encryption_certificate_password
- smtp_password only when SMTP is configured
- bedrock_api_key when static Bedrock keys are required

## Knowledge Platform

- knowledge_postgres_password, knowledge_redis_password
- knowledge_minio_access_key, knowledge_minio_secret_key
- knowledge_neo4j_password
- knowledge_neo4j_writer_password for knowledge-worker only
- knowledge_neo4j_reader_password for main ai-api only
- knowledge_service_jwt_private_key, knowledge_service_jwt_public_key
- knowledge_grant_private_key, knowledge_grant_public_key
- knowledge_portal_data_protection_certificate
- knowledge_portal_data_protection_certificate_password

The main API receives its own service private key to request grants. It never
receives knowledge PostgreSQL, Redis, MinIO, graph-writer, grant-signing, or
portal data-protection secrets.

## Before git push

```powershell
powershell -ExecutionPolicy Bypass -File scripts/reset-secrets-before-git.ps1
```

This deletes local secret files and fails if secret paths are still staged.
