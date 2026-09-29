# CTMS service

Clinical trial management scaffold. It holds the CTMS persistence model (monitoring visits and findings, site milestones, CRA allocations, delegations, grants, budgets and payables) and renders site-visit letters from Liquid templates.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/ctms/src/Cadence.Apps.Ctms.csproj` (assembly `Cadence.Apps.Ctms`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (monitoring visits, site milestones, delegation records, investigator grants, budget line items, audit log).
- `Infrastructure/Persistence/`: `CtmsDbContext`, a design-time `CtmsDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924144609_InitialCreate`.
- `../Templates/`: Liquid templates (`confirmation_letter.liquid`, `follow_up_letter.liquid`) embedded into the assembly and rendered via `Cadence.Kernel.Templating`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `CtmsDbContext`.
- `GET /api/v1/ctms/status` (anonymous status payload).
- `POST /api/v1/ctms/letters/render?templateName=...`: renders a `SiteVisitLetterModel` through an embedded Liquid template and returns plain text. The gateway also forwards `/api/v1/sites/**` here, but no such route is mapped.
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/ctms/**` to this service. Compose publishes host port 8007 (container port 8000).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2250](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<CtmsDbContext>()` from [packages/database](../../packages/database/README.md) resolves the connection string from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`. Only the Development environment falls back to a local `cadence_edc` database on `localhost:5432`; other environments fail at startup. `docker/docker-compose.yml` sets `ConnectionStrings__cadence` to the `cadence_ctms` database on `postgres-ctms`.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Fluid.Core`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- `tests/Audit/GxPAuditTrailGenerationTests.cs` checks that `CtmsDbContext` mutations produce hash-chained audit entries: `dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Audit"` (covers the CTMS, eTMF, quality and FileShare contexts).
- Architecture rules (`tests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=<password>"
dotnet run --project apps/ctms/src/Cadence.Apps.Ctms.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service with a reference to its `cadence` PostgreSQL database.
