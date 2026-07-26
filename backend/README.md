# SperoFlow ASP.NET Backend

The backend is a .NET 10 solution with six runtime boundaries:

- SperoFlow.Domain: entities, state transitions, and business invariants.
- SperoFlow.Contracts: browser and internal service DTOs.
- SperoFlow.Application: infrastructure-independent interfaces.
- SperoFlow.Infrastructure: PostgreSQL, Identity, MinIO, Redis, JWT, and AI
  gateway implementations.
- SperoFlow.Api: same-origin public API at /api/v1 and private job callbacks.
- SperoFlow.Worker: Quartz schedules, reminders, and outbox dispatch.

Use scripts/create-initial-migration.ps1 after installing .NET 10, then run:

    dotnet test backend/SperoFlow.sln

The API owns all primary data. FastAPI receives only signed short-lived
service requests and has no PostgreSQL credential.
