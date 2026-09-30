# Execution service (EDC)

Electronic data capture runtime. It manages subjects (enrolment state, consent, demographics), site compliance status, clinical observations with source data verification, form submissions and clinical queries, and runs edit checks and lab-range normalisation.

**Status:** a standalone ASP.NET Core 10 service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap phase 1, "EDC on the kernel") it is rebuilt on kernel concepts. The type-by-type plan is in [EXECUTION_MIGRATION_INVENTORY.md](../../docs/kernel/EXECUTION_MIGRATION_INVENTORY.md).

## Project

- Project: `apps/execution/src/Cadence.Apps.Execution.csproj` (assembly `Cadence.Apps.Execution`).
- `Program.cs`: composition root (DI registrations, ProblemDetails mapping, MassTransit, route groups).
- `Domain/`: subjects, observations, forms, queries and compliance models, domain errors, and repository ports (`Domain/Ports/`).
- `Application/`: application services per slice, audit-context abstractions, validators, and snake_case JSON settings (`Serialization/`).
- `Infrastructure/Persistence/`: `ExecutionDbContext`, design-time factory, Fluent API `Configurations/`, `Repositories/`, `EfAuditLogWriter`, concurrency interceptor, seeder, and `Migrations/`.
- `Infrastructure/Consumers/`: `EConsentCompletedConsumer` (MassTransit).
- `Infrastructure/EditChecks/`, `Infrastructure/Normalization/`, `Infrastructure/Security/`: AST edit-check engine, UCUM/lab-range/outlier services, AES-GCM demographics encryption.
- `Endpoints/`: Minimal API route groups and `Filters/` (`ValidationFilter<T>`, `ChangeReasonHeaderFilter`).

## HTTP surface

- `GET /healthz` (anonymous, mapped in `Program.cs`); `GET /healthz/live` and `GET /healthz/ready` (anonymous, mapped by `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `ExecutionDbContext`.
- `/api/v1/execution/subjects`, `/api/v1/execution/observations`, `/api/v1/execution/forms`, `/api/v1/execution/queries` (route groups).
- `/api/v1/execution/sites/{siteId}/activate`, `/api/v1/execution/sites/{siteId}/compliance-status`, `/api/v1/execution/webhooks/etmf`.
- Legacy aliases: `/api/v1/observations`, `/api/v1/forms`, `/api/v1/queries`.
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/execution/**`, `/api/v1/observations/**`, `/api/v1/forms/**` and `/api/v1/queries/**` here. Compose publishes port 8002.

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route except the health probes requires the gateway-signed caller identity; selected mutating routes (for example form unlock, query reopen/cancel, subject state change) also require an `X-Change-Reason` header via `ChangeReasonHeaderFilter`. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md). The demographics key is bound from the `DemographicsEncryption` configuration section (`DemographicsEncryption__KeyBase64`). It must be Base64 of a 16, 24 or 32 byte AES key, and the service refuses to start without one, so a missing key fails at boot rather than on the first subject registration (`tests/Execution/Security/DemographicsKeyStartupValidationTests.cs`).

## Persistence

`ExecutionDbContext` on PostgreSQL. The connection string is resolved by `CadenceDatabaseExtensions.ResolvePostgresConnectionString` in [packages/database](../../packages/database/README.md): `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only Development falls back to `cadence` on `localhost:5432`. The context uses the `edc` schema in the shared `cadence` database (ADR-2447). The context registers the `GxPAuditInterceptor` and a concurrency-token interceptor. MassTransit uses the EF Core outbox on the same database with the in-memory transport. Migrations live in `Infrastructure/Persistence/Migrations/`; `dotnet run ... -- --seed` seeds sample data and exits.

## Errors and concurrency

- Domain errors come back as RFC 7807 ProblemDetails with a `code` extension. `DomainErrorHttpMapping` sets the status.
- Writes use optimistic concurrency: each aggregate row carries a `_version` token. When another writer changed or
  deleted the row after it was read, the save is rejected rather than retried over their change.
  `ConcurrencyConflictExceptionHandler` returns **409 Conflict** with `code: CONCURRENCY_CONFLICT`, and nothing
  from the rejected save is written or audited. Clients re-read and re-apply (ADR-2450).
- Only transient database failures (serialization failures, deadlocks, dropped connections) are retried, by the
  Polly pipeline in `SaveChangesResiliencePipelineFactory`, and never inside an explicit transaction.
- Every change is audited by `GxPAuditInterceptor` in the same save (ADR-2446), attributed to the caller or a
  named `svc:` service account (ADR-2257).

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Database`, `Cadence.Telemetry`.
- Packages: `Npgsql.EntityFrameworkCore.PostgreSQL`, `FluentValidation` (+ `DependencyInjectionExtensions`), `Polly`, `MassTransit`, `MassTransit.EntityFrameworkCore`, OpenTelemetry, `Scalar.AspNetCore`; `Microsoft.EntityFrameworkCore.Design` at design time only.

## Tests

- Unit and endpoint tests in `tests/Execution/`: `dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Execution"`. Tests under `tests/Execution/Integration/` use Testcontainers PostgreSQL and need Docker.
- End-to-end tests in `tests/E2E/`: `dotnet test tests/Cadence.E2ETests.csproj --filter "FullyQualifiedName~Cadence.E2ETests.E2E"`.
- Architecture rules: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/execution/src/Cadence.Apps.Execution.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service with a reference to its `cadence` PostgreSQL database.
