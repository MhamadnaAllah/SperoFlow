# Frontend API Integration

The frontend now uses the same-origin ASP.NET API only. Caddy must route
`/api/v1/*` to the `api` container and normal UI routes to Next.js.

## Browser Contract

- All browser requests use relative `/api/v1` URLs with `credentials: "same-origin"`.
- Unsafe requests obtain `/api/v1/auth/csrf` and send `X-CSRF-TOKEN`.
- Authentication uses the ASP.NET Identity cookie. The browser never stores or sends a bearer token.
- The only server-side frontend setting is `API_INTERNAL_BASE_URL=http://api:8080`, used to forward the browser cookie to `/api/v1/auth/me` from the dashboard layout.
- Do not add Supabase, Neo4j, Lightning, Bedrock, AWS, MinIO, Redis, or PostgreSQL settings to the web container.

## Active Endpoints

- Auth: `/auth/csrf`, `/auth/register`, `/auth/login`, `/auth/logout`, `/auth/me`
- Projects: `/projects`, `/projects/{id}`, `/projects/{id}/archive`, `/projects/{id}/restore`, `/projects/{id}/tasks/reorder`
- Workspace data: `/tasks`, `/calendar-events`, `/habits`, `/habits/check-ins`, `/journal`, `/documents`, `/jobs/{id}`
- Supported AI: `/ai/query`, `/ai/roadmaps`, `/ai/tasks/{id}/classify`, `/ai/tasks/{id}/schedule`, `/ai/balance`

## Product Rules

- Projects are personal in v1. A task belongs to zero or one project and remains the same record in Tasks, Projects, Matrix, and Calendar.
- Handle `409 Conflict` by refreshing the affected record and asking the user to retry.
- Treat AI results as optional UI assistance. The API is the only caller of private AI services.
- Document ingestion submits to `/documents` and polls `/jobs/{id}`. The browser does not upload graph data or call workers directly.

## Removed Surface

- Supabase clients, browser user IDs, browser Neo4j access, Lightning URLs, legacy Next API route handlers, and business server actions are removed.
- The frontend has no direct database, graph, or AI-service credentials.