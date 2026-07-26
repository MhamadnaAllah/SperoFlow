# Knowledge Platform Cutover

This procedure moves knowledge administration out of the main application
without deleting a source record or changing a graph until the replacement has
been verified.

## 1. Prepare the isolated platform

1. Create the shared Docker bridges once:
   `./scripts/Initialize-KnowledgePlatformNetworks.ps1`.
2. Create the knowledge-specific secrets described in
   `infrastructure/secrets/README.md`; never reuse MinIO, PostgreSQL, Redis,
   graph, Data Protection, or worker-key material from the main application.
3. Start the main Compose stack and the knowledge Compose stack. Caddy is the
   only service with host ports. Knowledge health probes run container-to-
   container and are not served at the knowledge hostname.
4. Grant `KnowledgeOwner` or `KnowledgeAdmin` through the main identity admin
   API. The OIDC subject used by the portal is the only dataset owner key.

## 2. Re-ingest source material

1. Inventory each legacy dataset and its source-object SHA-256 values before
   Generate a deterministic source manifest with Generate-KnowledgeSourceManifest.ps1 before the migration and preserve it for the post-ingestion comparison.
   changing any records.
2. Create an equivalent private dataset in the Knowledge Portal for the same
   immutable owner subject.
3. Upload each source through the portal. The platform verifies the signed
   upload, MIME type, file signature, byte count, and SHA-256 before queuing it.
4. Wait for the release snapshot to validate. A release always reprocesses the
   new source together with every previously completed source, so an active
   published release stays intact until the replacement is approved.
5. For curated roadmap and CBT material, create owner-controlled datasets,
   submit them for review, and have a KnowledgeAdmin publish the validated
   release. Do not mount curated sources into an API container or rebuild a
   graph on startup.

## 3. Verify retrieval and rollback

1. Compare source counts, content-unit counts, citations, hashes, and sampled
   queries between the old and isolated graph.
2. Through Caddy, confirm a main-app query receives only published datasets;
   confirm a private dataset query works only for its owner. Test an expired or
   revoked grant and verify the AI API denies it.
3. Confirm `knowledge-worker` is the only graph writer and the main API cannot
4. Confirm an expired or stale worker callback cannot update a retried job, and verify the release validation report contains every source checksum and aggregate counter.
   reach knowledge PostgreSQL, Redis, object storage, or the worker.
4. Back up PostgreSQL, Neo4j, and MinIO from both sides and perform a restore
   drill before removing anything.

## 4. Retire the legacy path

`LegacyKnowledgeIngestion:Enabled` defaults to `false`. This prevents the main
outbox and worker from processing old dataset jobs while preserving legacy rows
for audit and rollback. After parity and a production rollback test:

1. archive the legacy dataset records and source objects;
2. apply a separately reviewed destructive migration that drops the legacy
   dataset tables and code; and
3. remove legacy graph credentials and rotate any credentials exposed by the
   former path.

Do not skip the parity and restore steps. They are the boundary between a
migration and an irreversible data-loss event.