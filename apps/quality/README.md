# Quality service

Quality management scaffold. It holds the deviation, root cause analysis and CAPA model; no business endpoints are mapped yet.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/quality/src/Cadence.Apps.Quality.csproj` (assembly `Cadence.Apps.Quality`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (deviations, root cause analyses, CAPA records, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `QualityDbContext`, a design-time `QualityDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924144718_InitialCreate`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `QualityDbContext`.
- `GET /api/v1/quality/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/quality/**` to this service. Compose publishes host port 8005 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<QualityDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `quality` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

Regulated history is kept: `QualityRootCauseAnalysis` and `QualityCapaRecord` rows reference their `QualityDeviation` with `ON DELETE RESTRICT`, so deleting a parent that still has history fails instead of erasing it. Retire or soft-delete regulated records; never hard-delete them ([ADR-2258](../../docs/adr/2026-09-29-restrict-cascade-deletes-onto-audit-trails-and-regulated-children.md)).

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- `tests/Cadence.UnitTests/Audit/GxPAuditTrailGenerationTests.cs` checks that `QualityDbContext` mutations produce hash-chained audit entries: `dotnet test --project tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Audit"` (covers the CTMS, eTMF, quality and FileShare contexts).
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test --project tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/quality/src/Cadence.Apps.Quality.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8005, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
