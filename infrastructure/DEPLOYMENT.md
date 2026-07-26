# SperoFlow Deployment

Deploy the two Compose stacks on one VM. Caddy is the only service that
publishes host ports. Never publish PostgreSQL, Redis, MinIO, Neo4j, AI API, or
worker ports.

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

3. Generate secrets on the deployment host:

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts/bootstrap-secrets.ps1
~~~

4. Create an untracked root .env from .env.example. Set APP_DOMAIN,
   KNOWLEDGE_DOMAIN, and CADDY_EMAIL. Provide Bedrock access through a
   least-privilege VM or workload identity, never static AWS keys.
5. Confirm the knowledge Neo4j reader password is mounted only by main ai-api.
   The writer credential belongs only to knowledge-worker.
6. Use immutable image digests in the production deployment manifest after the
   approved image versions have been scanned and promoted.

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
- Test backup restoration for both PostgreSQL stores, MinIO volumes, and the
  dedicated Neo4j graph before production cutover.

Knowledge access grants are deliberately short-lived (90 seconds by default).
Role, publication, or ownership changes stop new grants immediately; the
remaining exposure of an already-issued grant is bounded by that lifetime.

The old main knowledge tables are not dropped automatically. Keep them backed
up until source artifacts and metadata have been re-ingested and the rollback
window has closed.
