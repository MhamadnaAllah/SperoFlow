# SperoFlow Deployment

Deploy the two Compose stacks on one VM. Caddy is the only service that
publishes host ports. Never publish PostgreSQL, Redis, MinIO, Neo4j, AI API, or
worker ports.

## Pre-flight checklist

- [ ] CI green on the release commit (`.github/workflows/ci.yml`)
- [ ] `scripts/preflight.ps1` clean (or equivalent secret/compose/test gates)
- [ ] Secrets generated on the host only (no committed secrets; no plaintext summary)
- [ ] `.env` created from `.env.example` with real domains and Caddy email
- [ ] Backup + restore drill planned (`scripts/backup-volumes.ps1`)
- [ ] After up: `scripts/stack-status.ps1 -FailOnUnhealthy`, `scripts/smoke-release.ps1`, and `scripts/e2e-smoke.ps1 -RequireStack` pass
- [ ] Log shipping configured (CloudWatch or equivalent) for json container logs
- [ ] Optional: monitoring profile up; confirm `/metrics` is **404** on the public app host and scrapable only on the private network

## Before First Start

1. Install Docker Engine with Compose, point both DNS names to the VM, and
   allow inbound TCP 80 and 443 only.
2. Create the cross-stack networks once:

~~~powershell
docker network create speroflow_knowledge_edge
docker network create speroflow_knowledge_grant_bridge
docker network create speroflow_knowledge_read_bridge
docker network create speroflow_knowledge_storage_bridge
~~~

3. Secrets — prefer AWS Secrets Manager (see
   [`MANAGED_SECRETS.md`](MANAGED_SECRETS.md)):

~~~powershell
# First time (admin): bootstrap locally, push to SM, scrub workstation
powershell -ExecutionPolicy Bypass -File scripts/bootstrap-secrets.ps1
powershell -ExecutionPolicy Bypass -File scripts/aws-secrets-push.ps1 -Environment prod -Region us-east-1 -KmsKeyId alias/speroflow-secrets
powershell -ExecutionPolicy Bypass -File scripts/reset-secrets-before-git.ps1

# Every deploy host boot (EC2 instance role):
powershell -ExecutionPolicy Bypass -File scripts/aws-secrets-pull.ps1 -Environment prod -Region us-east-1
~~~

   Prefer deploying `infrastructure/aws/speroflow-secrets-stack.yaml` once per
   env (KMS + IAM). Fall back to local-only `bootstrap-secrets.ps1` only for
   air-gapped labs. Do not pass `-WritePlaintextSummary` on shared hosts.
   Prefer IAM for Bedrock over `bedrock_api_key`.

4. Create an untracked root .env from .env.example. Set APP_DOMAIN,
   KNOWLEDGE_DOMAIN, and CADDY_EMAIL. Prefer least-privilege VM or workload
   identity for Bedrock; never commit static AWS keys.
5. Confirm the knowledge Neo4j reader password is mounted only by main ai-api.
   The writer credential belongs only to knowledge-worker.
6. Use immutable image digests in the production deployment manifest after the
   approved image versions have been scanned and promoted.
7. Validate Compose before first up:

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts/validate-compose.ps1
~~~

## Start Order

Start knowledge services first so its migrations, bucket, role provisioning, and
health checks finish before main application requests any grants:

~~~powershell
docker compose -f knowledge-platform/compose.yaml up -d --build
docker compose -f knowledge-platform/compose.yaml ps
docker compose up -d --build
docker compose ps
~~~

The default inference path is Bedrock. A GPU host can enable the optional
llm-runtime profile:

~~~powershell
docker compose --profile gpu up -d --build
~~~

## First Access

The bootstrap account receives Admin. It may grant KnowledgeOwner or
KnowledgeAdmin centrally through:

~~~text
PUT /api/v1/admin/identity/users/{userId}/knowledge-role
{ "role": "KnowledgeOwner", "enabled": true }
~~~

A changed role takes effect on the person's next OIDC portal sign-in. Dataset
owner assignment in the portal must follow central role assignment.

## Verification

- Check both Compose projects are healthy.
- Visit the knowledge domain; unauthenticated access must redirect through main
  OIDC login with PKCE.
- Upload a small source, wait for a validated release, submit it for review,
  and publish it as an administrator.
- From the main app, list the dataset and run a query through /api/v1/ai/query.
- Verify Caddy is the only service with host ports and that the main API has no
  knowledge database or object-storage credentials.
- Take a pre-cutover backup and test restoration for both PostgreSQL stores,
  MinIO volumes, and the dedicated Neo4j graph:

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts/backup-volumes.ps1 -IncludeNeo4j -IncludeObjectStores
~~~

Knowledge access grants are deliberately short-lived (90 seconds by default).
Role, publication, or ownership changes stop new grants immediately; the
remaining exposure of an already-issued grant is bounded by that lifetime.

The old main knowledge tables are not dropped automatically. Keep them backed
up until source artifacts and metadata have been re-ingested and the rollback
window has closed.
