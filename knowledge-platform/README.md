# Isolated Knowledge Administration Platform

The knowledge platform is a separate deployment for administrative uploads,
wrangling, graph construction, release validation, and publication. It is not a
feature area inside the main SperoFlow application.

## Services

- knowledge-portal: operator UI at the knowledge subdomain.
- knowledge-api: OIDC-protected owner/admin API and grant issuer.
- knowledge-outbox-worker: PostgreSQL outbox to Redis Streams dispatcher. It is the only service with the worker callback signing key.
- knowledge-worker: the only knowledge graph writer; it profiles, OCRs,
  chunks, extracts, embeds, validates provenance, and reports durable results.
- knowledge-postgres, knowledge-redis, knowledge-object-storage, and
  knowledge-neo4j: isolated stores.

The portal has its own host-only secure cookie and separate Data Protection key
ring. It uses main SperoFlow only as the OIDC issuer. No browser cookie,
database, or general service credential is shared between applications.

## Access Model

- KnowledgeOwner creates and manages only datasets whose immutable OIDC sub is
  their owner value.
- KnowledgeAdmin reviews all datasets, assigns owners, publishes validated
  releases, and archives or restores datasets.
- Owners begin with private datasets. Shared content requires pending_review, a
  validated graph release, and explicit publication.
- Main users never call this platform. Main ASP.NET requests an asymmetric,
  short-lived grant; main ai-api validates it and reads only the exact
  dataset/release/owner tuples from Neo4j. Grants expire after 90 seconds by
  default, bounding access after role, ownership, or publication changes.

## Start

Run from the repository root after creating the shared networks and secrets:

~~~powershell
./scripts/Initialize-KnowledgePlatformNetworks.ps1
docker compose -f knowledge-platform/compose.yaml up -d --build
docker compose -f knowledge-platform/compose.yaml ps
~~~

Caddy in the main stack routes the knowledge hostname. Start or reload the main
Compose project after the knowledge platform is healthy.

## Upload and Publication Flow

1. An owner signs in through central OIDC Authorization Code plus PKCE.
2. The portal asks knowledge-api for a presigned, checksum-bound upload.
3. The portal sends bytes only to the private bucket through Caddy's signed
   proxy path; MinIO has no published host port.
4. knowledge-api verifies MIME type, signature, size, and SHA-256, then writes
   an outbox event in the same transaction.
5. knowledge-outbox-worker publishes the job to the isolated Redis stream.
   The dispatcher persists the processing attempt before delivery; each worker JWT is bound to that job and attempt, so stale callbacks cannot change a later retry.
6. knowledge-worker performs bounded ingestion and is the sole graph writer.
   Scanned PDFs enter durable delayed OCR polling rather than startup rebuilds.
7. The API validates a completed release. An administrator may publish it.
   Validation aggregates every source report into a deterministic checksum manifest and refuses mismatched release keys, source checksums, or counters.

Curated roadmap and CBT source material must be introduced through this same
owner/review/release path. A release re-ingests every completed source as one
immutable graph snapshot, so readers never observe a partial rebuild. Do not
mount curated content into a runtime API or rebuild a graph at startup.

## Cutover

Before dropping the old main-app tables, export and checksum legacy source
artifacts, create equivalent platform datasets/releases, verify graph counts and
citations, exercise grants from the main app, back up both sides, and test
rollback. Only then create and apply the destructive legacy-table cleanup
migration.

For the production migration and retirement sequence, see [CUTOVER.md](CUTOVER.md).

Use tools/Generate-KnowledgeSourceManifest.ps1 before and after a curated-source migration to compare the file count, byte count, per-file SHA-256 values, and deterministic manifest hash.
