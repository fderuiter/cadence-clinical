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
- `POST /api/v1/safety/icsr/render`: renders an `IcsrReport` through `e2b_r3_icsr.xml.liquid` and returns `application/xml`. The template reads the report as `icsr`; field values are XML-escaped ([templating](../../packages/templating/README.md)). Needs `safety_report:create` (sysadmin, sponsor medical monitor); other callers get `403` ([ADR-2511](../../docs/adr/2026-09-30-template-render-routes-need-a-permission.md)).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/safety/**` to this service. Compose publishes host port 8008 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<SafetyDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `safety` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Fluid.Core`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- No service-specific tests exist yet.
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/safety/src/Cadence.Apps.Safety.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8008, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
