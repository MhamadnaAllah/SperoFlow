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

## Data Protection

- Back up PostgreSQL, Neo4j, MinIO, and Caddy data volumes to encrypted
  off-site storage.
- Test a full restore on an isolated host before each production release and at
  a regular operational cadence.
- Rotate deployment secrets after any suspected exposure. The retired legacy
  environment file must not be restored or inspected.
- Use `docker compose exec` for emergency private-service operations; do not
  add host port mappings to convenience tools.

## AI Boundaries

The API signs short-lived service JWTs for the private AI API. AI services have
no PostgreSQL credentials and never accept browser requests. The scheduler
returns a proposal only; ASP.NET validates ownership and persists a schedule
change only after explicit user acceptance.

## Release Checks

Run .NET tests, AI-core tests, frontend lint/build, the knowledge manifest
verification, and Compose configuration validation before deployment. Follow
with Caddy-path authentication, CRUD, ingestion, scheduler proposal, GraphRAG,
Balance, and authorization-failure smoke tests.