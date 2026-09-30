# AI gateway service

Placeholder for the medical coding and AI proxy service. It currently maps only health and status endpoints; no AI, coding or de-identification logic exists in this project.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel, is merged, or is retired. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/ai_gateway/src/Cadence.Apps.AiGateway.csproj` (assembly `Cadence.Apps.AiGateway`).
- `Program.cs` is the only source file.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)).
- `GET /api/v1/ai/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/ai/**` here (default upstream `http://localhost:8017`, `AI_GATEWAY_SERVICE_URL`). Compose publishes host port 8017 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

None. The project references `Cadence.Database` and the EF Core/Npgsql packages but registers no `DbContext`.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation`.

## Tests

- No service-specific tests exist yet.
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
dotnet run --project apps/ai_gateway/src/Cadence.Apps.AiGateway.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8017, as docker compose does (no datastore reference).
