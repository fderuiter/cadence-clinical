# Organization directory service

Organization directory scaffold. It holds the personnel assignment, delegation of authority and training log model; no business endpoints are mapped yet.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/org/src/Cadence.Apps.Org.csproj` (assembly `Cadence.Apps.Org`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (personnel assignments, delegations of authority, training logs, audit log).
- `Infrastructure/Persistence/`: `OrgDbContext`, a design-time `OrgDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145321_InitialCreate`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `OrgDbContext`.
- `GET /api/v1/org/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/org/**` to this service. Compose publishes host port 8012 (container port 8000).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<OrgDbContext>()` from [packages/database](../../packages/database/README.md) resolves the connection string from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`. Only the Development environment falls back to a local `cadence_edc` database on `localhost:5432`; other environments fail at startup. `docker/docker-compose.yml` sets `ConnectionStrings__cadence` to the `cadence_edc` database on `postgres`.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Kernel.Domain`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- No service-specific tests exist yet.
- Architecture rules (`tests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=<password>"
dotnet run --project apps/org/src/Cadence.Apps.Org.csproj
```

This service is not wired into the Aspire AppHost ([apps/apphost](../apphost/README.md)).
