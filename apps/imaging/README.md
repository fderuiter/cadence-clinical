# Imaging service

Medical imaging scaffold. It holds the imaging study/series/instance envelope, de-identification certificate, annotation, tumour response and adjudication session model; no business endpoints are mapped yet.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/imaging/src/Cadence.Apps.Imaging.csproj` (assembly `Cadence.Apps.Imaging`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (imaging study, series and instance envelopes, de-identification certificates, annotations, tumour response assessments, adjudication sessions, audit log).
- `Infrastructure/Persistence/`: `ImagingDbContext`, a design-time `ImagingDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145016_InitialCreate`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `ImagingDbContext`.
- `GET /api/v1/imaging/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/imaging/**` to this service. It is not in `docker/docker-compose.yml`; the gateway's default upstream is `http://localhost:8014` (`IMAGING_SERVICE_URL`).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<ImagingDbContext>()` from [packages/database](../../packages/database/README.md) resolves the connection string from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`. Only the Development environment falls back to a local `cadence_edc` database on `localhost:5432`; other environments fail at startup. It has no service entry in `docker/docker-compose.yml`.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- No service-specific tests exist yet.
- Architecture rules (`tests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=<password>"
dotnet run --project apps/imaging/src/Cadence.Apps.Imaging.csproj
```

This service is not wired into the Aspire AppHost ([apps/apphost](../apphost/README.md)).
