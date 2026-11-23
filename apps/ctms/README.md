# CTMS service

Clinical trial management scaffold. It holds the CTMS persistence model (monitoring visits and findings, site milestones, CRA allocations, delegations, grants, budgets and payables) and renders site-visit letters from Liquid templates.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/ctms/src/Cadence.Apps.Ctms.csproj` (assembly `Cadence.Apps.Ctms`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (monitoring visits, site milestones, delegation records, investigator grants, budget line items, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `CtmsDbContext`, a design-time `CtmsDbContextFactory`, and `Configurations/` (Fluent API mappings, including `ConsumedSignatureTokenConfiguration`).
- `Infrastructure/Persistence/Migrations/`: schema, audit cascade restrictions, visit letter PDF/dispatch fields, greenlight gates, transactional outbox, tenant RLS, persisted `StudyId`/`SiteId` scope columns (`20261122171757_AddCtmsStudyAndSiteScopeColumns`), and unified delegation/letter/signature token contracts (`UnifyCtmsDelegationAndLetterContracts`).
- `Application/CtmsDbSeeder.cs`: idempotent baseline seeder for `CADENCE-101` site greenlight gates (also invoked via `--seed`).
- `../Templates/`: Liquid templates (`confirmation_letter.liquid`, `follow_up_letter.liquid`) embedded into the assembly and rendered via `Cadence.Kernel.Templating`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `CtmsDbContext`.
- `GET /api/v1/ctms/status` (anonymous status payload).
- `GET /api/v1/ctms/startup/matrix`, `GET /api/v1/ctms/startup/sites/{siteId}/greenlight`, `POST /api/v1/ctms/startup/sites/{siteId}/greenlight`, `PUT /api/v1/ctms/startup/sites/{siteId}/greenlight`, `POST /api/v1/ctms/startup/sites/{siteId}/greenlight/certify`, and `POST /api/v1/ctms/startup/matrix/batch-certify`: site start-up greenlight readiness initialization, facet updates, and certification.
- `POST /api/v1/ctms/doa/delegate`, `POST /api/v1/ctms/doa/sign-off`, `POST /api/v1/ctms/doa/revoke`, `GET /api/v1/ctms/doa/sites/{site_id}/log`, and `GET /api/v1/ctms/doa/sites/{site_id}/export-pdf`: Delegation of Authority (DoA) log management, 21 CFR Part 11 PI electronic sign-off (`X-Sig-Token`), and PDF export with fail-closed study and site isolation across single-study and shared clinical sites via persisted `StudyId` and `SiteId` columns on `CtmsDelegation` and `CtmsAuditLog`.
- `POST /api/v1/ctms/monitoring-visits`, `GET /api/v1/ctms/monitoring-visits`, `POST /api/v1/ctms/monitoring-visits/{visit_id}/complete`, `POST /api/v1/ctms/monitoring-visits/{visit_id}/sign-off`, and `GET /api/v1/ctms/monitoring-visits/{visit_id}/letters`: monitoring visit scheduling, finding persistence, report sign-off, and generated letter inspection.
- `POST /api/v1/ctms/letters/render?templateName=...`: renders a `SiteVisitLetterModel` through an embedded Liquid template and returns plain text. Needs `ctms_monitoring_visit:create` (sysadmin, sponsor data manager, CRA) plus study and site scope (`RequireCallerStudyAndSiteScope` and `ClinicalScopeGuard.RequireStudyAndOptionalSite`); other callers or site-scoped roles without site assignments get `403` ([ADR-2511](../../docs/adr/2026-09-30-template-render-routes-need-a-permission.md)).
- `POST /api/v1/ctms/letters/compile?templateName=...` and `GET /api/v1/ctms/letters/{letterId}/pdf`: compile and stream GxP visit letter PDFs, validating explicit `VisitId` references against `CtmsMonitoringVisits`, enforcing `CtmsScopeGuard.RequireStudyAndSite` even when `PdfObjectKey` is `null`, verifying `X-Sig-Token` on finalization, and publishing MassTransit `EtmfDocumentFilingRequested` into eTMF Zone 5 (`05.02.06`) when finalized.
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/ctms/**` to this service. Compose publishes host port 8007 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

Site start-up greenlight routes need `ctms_greenlight:read`, `ctms_greenlight:write`, or `ctms_greenlight:certify`; the site routes also apply site scope, all state mutations enforce `X-Change-Reason`, and every route refuses a study the caller is not assigned to. The certifier is the signed caller, and tokens are sealed with `GATEWAY_SECRET` with no fallback (`PermissionMatrix.Ctms.cs`) ([ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md)). Part 11 sign-offs (`POST /api/v1/ctms/doa/sign-off` and finalized `POST /api/v1/ctms/letters/compile`) verify single-use `X-Sig-Token` headers against `ctms.consumed_signature_tokens` (`AddCadenceDurableSigTokenStore<CtmsDbContext>`).

Greenlight gates are activation evidence, so GET routes never invent them (#5433). Every route needs a `study_id`;
there is no default study. `GET /startup/matrix` only reads, and a study and country with no recorded gates is an empty
matrix. Reading or certifying a single site with no recorded gate returns `404` and writes nothing. Batch certify
refuses the whole request with `400` when any named site has no gate in that study and country, and certifies nothing.
Authorized callers initialize and update facet evidence explicitly via `POST` and `PUT /api/v1/ctms/startup/sites/{siteId}/greenlight`, and `CtmsDbSeeder` (`--seed`) seeds baseline gates for `CADENCE-101`.

## Persistence

`AddCadencePostgresDbContext<CtmsDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `ctms` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

Regulated history is kept: `CtmsBudgetLineItem`, `CtmsPaymentMilestone` and `CtmsInvestigatorPayable` rows reference their `CtmsInvestigatorGrant` with `ON DELETE RESTRICT`, so deleting a parent that still has history fails instead of erasing it. Retire or soft-delete regulated records; never hard-delete them ([ADR-2258](../../docs/adr/2026-09-29-restrict-cascade-deletes-onto-audit-trails-and-regulated-children.md)).

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Fluid.Core`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- `tests/Cadence.UnitTests/Audit/GxPAuditTrailGenerationTests.cs` checks that `CtmsDbContext` mutations produce hash-chained audit entries: `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Audit"` (covers the CTMS, eTMF, quality and FileShare contexts).
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/ctms/src/Cadence.Apps.Ctms.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8007, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
