# Operations Runbook

## Knowledge Base and Graphs

Verify curated assets before graph construction:

```powershell
python ai-worker/scripts/knowledge_manifest.py verify `
  --root knowledge-base `
  --layout canonical `
  --manifest knowledge-base/manifest.json
```

Curated sources enter the isolated platform through the Knowledge Portal: create
an owner-controlled private dataset, upload the verified roadmap or CBT source
files, wait for its complete release snapshot to validate, submit it for review,
and publish it as a KnowledgeAdmin. Upload original source material from
`knowledge-base/roadmaps` and `knowledge-base/cbt/source`; do not upload derived
`cbt/graph` artifacts or use the retired main-stack bootstrap path.

Do not add graph ingestion to the AI API, main AI worker startup, or a browser
path. The dedicated knowledge worker is the only graph writer. Neo4j remains
derived data and every production rebuild must flow through a validated release.

## Secrets

### Managed (AWS Secrets Manager) — preferred for pilot/production

Full design: [`MANAGED_SECRETS.md`](MANAGED_SECRETS.md). Catalog:
`aws/secrets-catalog.json`.

```powershell
# Materialize Compose secret files from SM (instance role / SSO)
powershell -ExecutionPolicy Bypass -File scripts/aws-secrets-pull.ps1 -Environment prod -Region us-east-1

# Admin upload after bootstrap or rotation
powershell -ExecutionPolicy Bypass -File scripts/aws-secrets-push.ps1 -Environment prod -Region us-east-1 -KmsKeyId alias/speroflow-secrets
```

- IAM templates: `aws/ec2-secrets-read-policy.json`, `aws/ec2-secrets-admin-policy.json`
- Optional non-secret config examples: `aws/ssm-config-examples.json` (SSM String only)
- Prefer **IAM roles** for Bedrock/S3; do not store long-lived AWS access keys in SM

### Local generation

- `scripts/bootstrap-secrets.ps1` for air-gapped/lab only.
- Default inventory is **non-secret** (`SECRETS_INVENTORY.md`).
- Do **not** use `-WritePlaintextSummary` on shared machines.
- Never commit `secrets/`, `secrets_backup/`, or `infrastructure/secrets/*` material.
- Before pushing from a workstation that generated secrets:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/reset-secrets-before-git.ps1
```

- After any suspected exposure, rotate, push to SM, pull on hosts, restart services:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/bootstrap-secrets.ps1 -Rotate
powershell -ExecutionPolicy Bypass -File scripts/aws-secrets-push.ps1 -Environment prod -Region us-east-1
```

Treat previously dumped values as burned.

## Data Protection

- Run logical backups while stacks are healthy:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/backup-volumes.ps1
# optional:
powershell -ExecutionPolicy Bypass -File scripts/backup-volumes.ps1 -IncludeNeo4j -IncludeObjectStores
```

- Copy `backups/<timestamp>/` to encrypted off-site storage. The directory is gitignored.
- Test a full restore on an isolated host before each production release and at
  a regular operational cadence.
- Use `docker compose exec` for emergency private-service operations; do not
  add host port mappings to convenience tools.

### Restore outline

1. Stop API and workers so writers are quiet.
2. Restore Postgres dumps with the guarded helper (destructive; requires `-ConfirmRestore`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/restore-postgres.ps1 `
  -Target main `
  -DumpFile .\backups\<timestamp>\main-postgres.dump `
  -ConfirmRestore

powershell -ExecutionPolicy Bypass -File scripts/restore-postgres.ps1 `
  -Target knowledge `
  -DumpFile .\backups\<timestamp>\knowledge-postgres.dump `
  -ConfirmRestore
```

3. Restore Neo4j / MinIO from their dump or volume archives on a non-production host first.
4. Run `scripts/stack-status.ps1` and `scripts/smoke-release.ps1`, then auth/GraphRAG smoke before cutting traffic back.

### Request correlation

Edge Caddy forwards `X-Request-Id` / `X-Correlation-ID` to API services. The main
API and private AI API generate an id when the client omits one and return
`X-Request-Id` on responses (health probes intentionally skip timing logs).

## AI Boundaries

The API signs short-lived service JWTs for the private AI API. AI services have
no PostgreSQL credentials and never accept browser requests. The scheduler
returns a proposal only; ASP.NET validates ownership and persists a schedule
change only after explicit user acceptance.

## Observability (pilot)

- ASP.NET and AI API emit `X-Request-Id` and structured request timing logs (health/metrics probes skipped).
- Process-local Prometheus text at **private** `http://api:8080/metrics` and `http://ai-api:8000/metrics`.
  Caddy returns **404** for public `/metrics` on the app host.
- Optional Prometheus:

```powershell
docker compose -f compose.yaml -f compose.prod.yaml -f compose.monitoring.yaml --profile monitoring up -d
# UI bound to 127.0.0.1:9090 only — do not open in a public SG
```

- Production Compose sets `LOG_FORMAT=json` and rotates container json-file logs.
- Example CloudWatch agent config: `infrastructure/monitoring/cloudwatch-agent-config.json`.
- Aggregate container health without credentials:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/stack-status.ps1 -FailOnUnhealthy
```

## Release Checks

Prefer green CI (`.github/workflows/ci.yml`) plus one local preflight:

```powershell
# secrets + compose + unit tests (skip pieces with -SkipTests / -SkipFrontend / -SkipAi)
powershell -ExecutionPolicy Bypass -File scripts/preflight.ps1
```

After the stacks are up, run non-credential smoke checks:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/stack-status.ps1
powershell -ExecutionPolicy Bypass -File scripts/smoke-release.ps1
# Node e2e smoke (health, metrics hidden, security headers):
powershell -ExecutionPolicy Bypass -File scripts/e2e-smoke.ps1 -AppBaseUrl https://app.example.com -KnowledgeBaseUrl https://knowledge.example.com -RequireStack
# or: node scripts/e2e-smoke.mjs

# Playwright browser E2E (login, CSRF, proposal approve) — mocked (CI default):
#   cd e2e; npm install; npx playwright install chromium; npm run test:mocked
# Live edge:
#   $env:E2E_BASE_URL="https://app.example.com"; $env:E2E_EMAIL="..."; $env:E2E_PASSWORD="..."; npm run test:live
```

Then run Caddy-path authentication, CRUD, ingestion, scheduler proposal, GraphRAG,
Balance, and authorization-failure smoke tests on the deploy host.
