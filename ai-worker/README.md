# AI Worker

Private durable worker for Redis Streams ingestion jobs, graph construction,
vector embedding, retries, and status callbacks to the ASP.NET internal API.

This process is the only runtime that may execute Neo4j write and embedding
pipelines. It has no HTTP listener and no browser-facing route.
