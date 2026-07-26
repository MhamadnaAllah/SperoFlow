# SperoFlow

SperoFlow is a personal productivity application with an ASP.NET Core application
API, PostgreSQL as the authority for user data, and an isolated knowledge
administration platform.

## Product Objective

The active product roadmap and approval-first AI invariants are tracked in
[PRODUCT_OBJECTIVE_STATUS.md](PRODUCT_OBJECTIVE_STATUS.md).
## Runtime Architecture

~~~mermaid
flowchart LR
    Browser --> Caddy
    Caddy --> Web["Next.js web / SSR"]
    Caddy --> Api["ASP.NET Core API"]
    Api --> AppDb[("PostgreSQL")]
    Api --> AppRedis[("Redis")]
    Api --> AiApi["Private AI API"]
    ApiWorker["ASP.NET worker"] --> AppRedis
    AiWorker["AI worker"] --> AppRedis

    Admin --> Caddy
    Caddy --> Portal["Knowledge Portal"]
    Caddy --> KnowledgeApi["Knowledge API"]
    Api -->|"short-lived grant request"| KnowledgeApi
    KnowledgeApi --> KnowledgeDb[("Knowledge PostgreSQL")]
    KnowledgeApi --> KnowledgeStore[("Private MinIO")]
    KnowledgeApi --> KnowledgeRedis[("Knowledge Redis")]
    KnowledgeWorker["Knowledge worker"] --> KnowledgeStore
    KnowledgeWorker --> KnowledgeRedis
    KnowledgeWorker --> KnowledgeGraph[("Knowledge Neo4j")]
    AiApi -->|"read-only bridge"| KnowledgeGraph
~~~

Only Caddy publishes ports 80 and 443. The browser calls only same-origin
main API routes. Administrative upload and publication work happens on the
separate knowledge subdomain. The main API receives no knowledge database,
object-store, or graph-writer credential.

## Repository Layout

- frontend/: Next.js application UI and SSR only.
- backend/: .NET 10 main API, worker, and application database migrations.
- ai-api/ and ai-core/: private retrieval and inference runtime.
- ai-worker/: ordinary application-document background work.
- knowledge-platform/: isolated knowledge portal, API, workers, storage,
  PostgreSQL, Redis, Neo4j, and its Compose stack.
- knowledge-base/: curated source assets that must enter the knowledge
  platform through a reviewed release.
- infrastructure/: Caddy, container files, deployment, and operations material.

## Deployment

1. Create the four shared narrow networks once with
   `./scripts/Initialize-KnowledgePlatformNetworks.ps1`.
2. Generate protected deployment secrets with scripts/bootstrap-secrets.ps1.
3. Create an untracked .env from .env.example, set both host names, and give
   Caddy an email address.
4. Start the knowledge platform, then the main stack:

~~~powershell
docker compose -f knowledge-platform/compose.yaml up -d --build
docker compose up -d --build
~~~

See [the knowledge-platform guide](knowledge-platform/README.md) and
[infrastructure/DEPLOYMENT.md](infrastructure/DEPLOYMENT.md) for the exact
cutover and verification steps.
