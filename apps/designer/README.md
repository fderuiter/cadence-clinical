# Designer service

Study design service. Today it exposes a CDISC USDM compile endpoint backed by `UsdmCompiler` from [packages/ddf](../../packages/ddf/README.md) and a placeholder study list; it has no datastore.

**Status:** a standalone ASP.NET Core 10 service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) study design becomes an application module over the kernel's ProtocolVersion model; the disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/designer/src/Cadence.Apps.Designer.csproj` (assembly `Cadence.Apps.Designer`).
- `Program.cs` is the only source file: service registration, route mapping and the `CompileProtocolRequest` record.

## HTTP surface

- `GET /healthz` (anonymous, mapped in `Program.cs`); `GET /healthz/live` and `GET /healthz/ready` (anonymous, mapped by `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)).
- `GET /api/v1/studies/`: returns an empty array.
- `POST /api/v1/protocols/compile`: body `{ usdmJson, studyId?, versionTag? }`; returns the DDF compilation result.
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/studies/**` and `/api/v1/protocols/**` here. Compose publishes host port 8001 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route except the health probes requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

None. Neo4j is retired (ADR-2245), and its unused `Neo4j.Driver` reference was removed in #4790.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Kernel.Domain`, `Cadence.Ddf`, `Cadence.Security`, `Cadence.Telemetry`.
- Packages: OpenTelemetry, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`.

## Tests

- No service-specific tests. The compiler it calls is covered by `tests/Packages/DdfCompilerTests.cs`: `dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DdfCompilerTests"`.
- Architecture rules: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
dotnet run --project apps/designer/src/Cadence.Apps.Designer.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service (no datastore reference).
