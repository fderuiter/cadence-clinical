# Interoperability service

Interoperability scaffold. It holds the ePRO submission, instrument, subject assignment, EHR endpoint and reconciliation, and device/sensor telemetry model; no business endpoints are mapped yet, so the subject portal's `/api/v1/interop/...` calls have no backend.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/interop/src/Cadence.Apps.Interop.csproj` (assembly `Cadence.Apps.Interop`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (ePRO submissions and quarantine, instruments, subject assignments, EHR endpoints, device registrations, sensor telemetry, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `InteropDbContext`, a design-time `InteropDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145123_InitialCreate`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `InteropDbContext`.
- `GET /api/v1/interop/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/interop/**` to this service. Compose publishes host port 8004 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<InteropDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `interop` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Configuration and secrets at rest

| Setting | Purpose |
| --- | --- |
| `FIELD_LOOKUP_HASH_KEY` | **Required.** Base64 HMAC-SHA256 key (`openssl rand -base64 32`) for device-token lookup hashes. The service refuses to start without a valid key; there is no default. |

Wearable/sensor device tokens are never stored (#4770). `device_registrations`, `sensor_telemetry_streams`,
`sensor_daily_summaries` and `sensor_safety_alerts` each keep an indexed `DeviceTokenHash`: the keyed HMAC-SHA256
computed by `DeviceTokenHasher` (registered in DI). To find a device's rows, hash the presented token and compare
hashes. Migration `HashDeviceTokensAtRest` cleared the raw tokens of existing rows (their hash is empty until the
device is re-registered); run `VACUUM FULL` on those four tables afterwards. See
[ADR-2494](../../docs/adr/2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md).

## Tests

- No service-specific tests exist yet.
- Architecture rules (`tests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/interop/src/Cadence.Apps.Interop.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8004, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
