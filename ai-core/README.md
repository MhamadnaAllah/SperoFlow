# SperoFlow AI Core

This package contains shared, framework-independent AI code used by the two
separate runtimes:

- `ai-api`: private FastAPI GraphRAG, resource retrieval, and inference.
- `ai-worker`: durable ingestion, embeddings, and Neo4j graph writes.

It is a shared library, not a deployable service. The service-specific entry
points and Docker images live in sibling `ai-api` and `ai-worker` directories.
