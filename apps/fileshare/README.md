# FileShare service

File sharing scaffold. It holds the file record, share grant, guest link and playback/file audit model; no business endpoints or object-storage code exist yet.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/fileshare/src/Cadence.Apps.Fileshare.csproj` (assembly `Cadence.Apps.Fileshare`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (file records, share grants, guest links, playback and file audit logs).
- `Infrastructure/Persistence/`: `FileshareDbContext`, a design-time `FileshareDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924144653_InitialCreate`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `FileshareDbContext`.
- `GET /api/v1/fileshare/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/fileshare/**` to this service. Compose publishes host port 8013 (container port 8000). The gateway also forwards `/api/v1/files/**` here, but no such route is mapped.

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2250](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<FileshareDbContext>()` from [packages/database](../../packages/database/README.md) resolves the connection string from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`. Only the Development environment falls back to a local `cadence_edc` database on `localhost:5432`; other environments fail at startup. In `docker/docker-compose.yml` this service only receives a legacy `FILESHARE_DATABASE_URL` (SQLite URL from the Python era), which the .NET service does not read. The AppHost passes `STORAGE_ENDPOINT_URL` (MinIO), but no code reads it yet.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- `tests/Audit/GxPAuditTrailGenerationTests.cs` checks that `FileshareDbContext` mutations produce hash-chained audit entries: `dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Audit"` (covers the CTMS, eTMF, quality and FileShare contexts).
- Architecture rules (`tests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=<password>"
dotnet run --project apps/fileshare/src/Cadence.Apps.Fileshare.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service with a reference to its `cadence` PostgreSQL database.
