# eTMF service

Electronic trial master file service. It hosts TMF document ingestion, intelligence classification, CRA/Sponsor QC staging and review, study binder export jobs, taxonomy lookup, and signed `SITE_ACTIVATION` completeness webhook notifications to `apps/execution`.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/etmf/src/Cadence.Apps.Etmf.csproj` (assembly `Cadence.Apps.Etmf`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (TMF documents, expected documents, QC transitions, expiration alert state, audit ledger seals, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `EtmfDbContext`, a design-time `EtmfDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924144631_InitialCreate`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `EtmfDbContext`.
- `GET /api/v1/etmf/status` (anonymous status payload).
- `POST /api/v1/etmf/intelligence/classify`, `POST /api/v1/etmf/intelligence/stage-qc`, `GET /api/v1/etmf/intelligence/qc-queue`, `POST /api/v1/etmf/intelligence/qc-review/{doc_id}`: document intelligence classification, QC staging, and 21 CFR Part 11 clinical QC transitions. When site startup artifacts are staged or reviewed, `HttpEtmfCompletenessWebhookPublisher` evaluates `SITE_ACTIVATION` completeness and posts a gateway-signed `svc:etmf` (`etmf_service`) webhook to `EXECUTION_SERVICE_URL` (`POST /api/v1/execution/webhooks/etmf`).
- `GET /api/v1/etmf/documents`, `GET /api/v1/etmf/documents/{document_id}`, `POST /api/v1/etmf/documents/export`, `GET /api/v1/etmf/export-jobs`, `GET /api/v1/etmf/export-jobs/{job_id}`, `GET /api/v1/etmf/export-jobs/{job_id}/download`, `POST /api/v1/etmf/ingest`, `GET /api/v1/etmf/taxonomy`.
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/etmf/**` to this service. Compose publishes host port 8003 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

eTMF routes need an `etmf_document` or `etmf_taxonomy` permission (`PermissionMatrix.Etmf.cs`). Exporting a binder needs `etmf_document:export` (sponsor data management, CRAs, monitors, auditors) and a `study_id` the caller is assigned to; document and export-job lists are narrowed to the caller's studies, and a document or job outside them answers `404`. Audit entries record the signed caller and roles ([ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md)).

## Persistence

`AddCadencePostgresDbContext<EtmfDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `etmf` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

Regulated history is kept: `TmfDocumentQcTransition` and `TmfDocumentExpirationAlertState` rows reference their `TmfDocument` with `ON DELETE RESTRICT`, so deleting a parent that still has history fails instead of erasing it. Retire or soft-delete regulated records; never hard-delete them ([ADR-2258](../../docs/adr/2026-09-29-restrict-cascade-deletes-onto-audit-trails-and-regulated-children.md)).

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- `tests/Cadence.UnitTests/Audit/GxPAuditTrailGenerationTests.cs` checks that `EtmfDbContext` mutations produce hash-chained audit entries: `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Audit"` (covers the CTMS, eTMF, quality and FileShare contexts).
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/etmf/src/Cadence.Apps.Etmf.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8003, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
