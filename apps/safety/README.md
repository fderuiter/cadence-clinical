# Safety service

Safety and pharmacovigilance scaffold. It holds the safety case, ICSR reporting evaluation, SAE reconciliation, narrative, signal detection, periodic report and device investigation model, and renders ICSR E2B(R3) XML from a Liquid template.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/safety/src/Cadence.Apps.Safety.csproj` (assembly `Cadence.Apps.Safety`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (safety cases, reporting evaluations, SAE reconciliation runs and discrepancies, narratives, signal detection, periodic reports, device investigations, AS2 transmissions, audit log).
- `Infrastructure/Persistence/`: `SafetyDbContext`, a design-time `SafetyDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145528_InitialCreate`.
- `../Templates/e2b_r3_icsr.xml.liquid`: ICSR template embedded into the assembly.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `SafetyDbContext`.
- `GET /api/v1/safety/status` (anonymous status payload).
- `POST /api/v1/safety/icsr/render`: renders an `IcsrReport` through `e2b_r3_icsr.xml.liquid` and returns `application/xml`.
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/safety/**` to this service. Compose publishes host port 8008 (container port 8000).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2250](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<SafetyDbContext>()` from [packages/database](../../packages/database/README.md) resolves the connection string from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`. Only the Development environment falls back to a local `cadence_edc` database on `localhost:5432`; other environments fail at startup. In `docker/docker-compose.yml` this service only receives a legacy `SAFETY_DATABASE_URL` (SQLite URL from the Python era), which the .NET service does not read.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Fluid.Core`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- No service-specific tests exist yet.
- Architecture rules (`tests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=<password>"
dotnet run --project apps/safety/src/Cadence.Apps.Safety.csproj
```

This service is not wired into the Aspire AppHost ([apps/apphost](../apphost/README.md)).
