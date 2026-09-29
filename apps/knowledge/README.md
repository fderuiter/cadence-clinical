# Knowledge service

Knowledge base scaffold. It holds the article, article version, category, contextual help mapping and protocol knowledge chunk model; no business endpoints are mapped yet.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/knowledge/src/Cadence.Apps.Knowledge.csproj` (assembly `Cadence.Apps.Knowledge`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (knowledge categories, articles and versions, contextual help mappings, protocol knowledge chunks, audit log).
- `Infrastructure/Persistence/`: `KnowledgeDbContext`, a design-time `KnowledgeDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145203_InitialCreate`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `KnowledgeDbContext`.
- `GET /api/v1/knowledge/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/knowledge/**` to this service. Compose publishes host port 8015 (container port 8080); the gateway's default upstream is `http://localhost:8015` (`KNOWLEDGE_SERVICE_URL`).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<KnowledgeDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `knowledge` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- No service-specific tests exist yet.
- Architecture rules (`tests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/knowledge/src/Cadence.Apps.Knowledge.csproj
```

This service is not wired into the Aspire AppHost ([apps/apphost](../apphost/README.md)).
