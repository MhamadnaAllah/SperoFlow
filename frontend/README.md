# SperoFlow Web

`frontend/` is the Next.js UI and SSR runtime. It is not an application backend.
All browser requests use same-origin `/api/v1` URLs through Caddy and ASP.NET
Core.

## Security Contract

- Authentication uses the ASP.NET Identity cookie; the browser has no bearer
token or client-provided user identifier.
- Unsafe requests obtain and send `X-CSRF-TOKEN` through `src/lib/api/client.js`.
- Server-side route guards forward the request cookie to the internal ASP.NET
endpoint using `API_INTERNAL_BASE_URL`.
- Do not add Supabase, database, Neo4j, Bedrock, Redis, MinIO, or AI-service
credentials to this project.
- Do not add Next.js API route handlers or server actions for business data.

The shared API client, DTO mappers, and API tests are in `src/lib/api/`. UI
features consume those modules instead of calling private services directly.

## Environment

`API_INTERNAL_BASE_URL` is server-only. In Compose it is `http://api:8080`.
It is used only for SSR authentication checks; browser data calls remain
same-origin and require Caddy in front of the stack.

## Development

Install JavaScript dependencies and run the normal Next.js commands for UI
work. Use the container stack for an end-to-end session so cookie, CSRF, Caddy,
and ASP.NET behavior match production.

```powershell
npm.cmd run lint
npm.cmd run build
```