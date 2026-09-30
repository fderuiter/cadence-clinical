# Designer service

Study design service. Today it exposes a CDISC USDM compile endpoint backed by `UsdmCompiler` from [packages/ddf](../../packages/ddf/README.md) and a placeholder study list; it has no datastore.

**Status:** a standalone ASP.NET Core 10 service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) study design becomes an application module over the kernel's ProtocolVersion model; the disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/designer/src/Cadence.Apps.Designer.csproj` (assembly `Cadence.Apps.Designer`).
- `Program.cs` is the only source file: service registration, route mapping and the `CompileProtocolRequest` record.

## HTTP surface

- `GET /healthz` (anonymous, mapped in `Program.cs`); `GET /healthz/live` and `GET /healthz/ready` (anonymous, mapped by `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)).
- `GET /api/v1/studies/`: returns an empty array.
- `POST /api/v1/protocols/compile`: body `{ usdmJson, studyId?, versionTag? }`; returns the DDF compilation result with its accessibility report. A form that fails the compiler's accessibility check is not returned: the route answers `422` problem details with `violations` and `layoutErrors` ([ADR-2501](../../docs/adr/2026-09-30-the-ddf-compiler-refuses-forms-that-fail-the-accessibility-check.md)).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/studies/**` and `/api/v1/protocols/**` here. Compose publishes host port 8001 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route except the health probes requires the gateway-signed caller identity. `POST /api/v1/protocols/compile` also requires `study_design:create` from the permission matrix (sponsor designers and system administrators) and answers `403` to anyone else (`.RequirePermission`, [ADR-2510](../../docs/adr/2026-09-30-endpoint-permissions-from-the-permission-matrix.md), #4958). `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

None. Neo4j is retired ([ADR-2245](../../docs/adr/2026-09-29-native-postgresql-protocolversion-snapshots-and-neo4j-retirement.md)): the service no longer references a Neo4j driver, and protocol versions are to be stored as PostgreSQL snapshots in the kernel.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Kernel.Domain`, `Cadence.Ddf`, `Cadence.Security`, `Cadence.Telemetry`.
- Packages: OpenTelemetry, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`.

## Tests

- `tests/Cadence.UnitTests/Designer/DesignerCompileAuthorityTests.cs` checks through the real host that only study designers may compile and that a form failing the accessibility check gets `422`. The compiler it calls is covered by `tests/Cadence.UnitTests/Packages/DdfCompilerTests.cs`: `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DdfCompilerTests"`.
- Architecture rules: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
dotnet run --project apps/designer/src/Cadence.Apps.Designer.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8001, as docker compose does (no datastore reference).
