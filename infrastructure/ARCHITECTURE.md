# Container Boundaries

Caddy is the sole public container and publishes ports 80 and 443. It serves
the main application at APP_DOMAIN and the private knowledge portal at
KNOWLEDGE_DOMAIN.

The main edge network contains Caddy, web, and API. The main private network
contains ordinary app services only. The knowledge platform has its own private
network, PostgreSQL, Redis, MinIO, Neo4j, API, portal, and workers.

Four deliberately narrow bridges are the only cross-stack links:

- speroflow_knowledge_edge: Caddy to the knowledge portal/API only.
- speroflow_knowledge_storage_bridge: Caddy to the private object store for
  signed upload requests only; Caddy has no object-store credentials.
- speroflow_knowledge_grant_bridge: main ASP.NET API to knowledge API for
  catalog and short-lived access grants only.
- speroflow_knowledge_read_bridge: main AI API to dedicated knowledge Neo4j
  with a read-only account only.


The knowledge worker is the sole application graph writer. The main AI API
validates an asymmetric, short-lived grant and executes fixed, bounded,
parameterized retrieval queries. It has no access to knowledge PostgreSQL,
Redis, MinIO, worker credentials, or graph-writer credentials.

PostgreSQL remains authoritative for main users and productivity records.
Knowledge PostgreSQL is authoritative for dataset metadata, releases, uploads,
jobs, outbox records, and audits. Both Neo4j databases are derived stores.
