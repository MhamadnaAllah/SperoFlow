# AI API

Private FastAPI runtime for GraphRAG retrieval, inference orchestration, CBT
resources, scheduling evaluation, and Balance evaluation. It is called only by
the ASP.NET API using short-lived service JWTs; Caddy does not expose it.

This runtime must not consume ingestion streams or register write routers.
