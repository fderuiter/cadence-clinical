# CTMS service

Clinical trial management scaffold. It holds the CTMS persistence model (monitoring visits and findings, site milestones, CRA allocations, delegations, grants, budgets and payables) and renders site-visit letters from Liquid templates.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/ctms/src/Cadence.Apps.Ctms.csproj` (assembly `Cadence.Apps.Ctms`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (monitoring visits, site milestones, delegation records, investigator grants, budget line items, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `CtmsDbContext`, a design-time `CtmsDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924144609_InitialCreate`.
- `../Templates/`: Liquid templates (`confirmation_letter.liquid`, `follow_up_letter.liquid`) embedded into the assembly and rendered via `Cadence.Kernel.Templating`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `CtmsDbContext`.
- `GET /api/v1/ctms/status` (anonymous status payload).
- `POST /api/v1/ctms/letters/render?templateName=...`: renders a `SiteVisitLetterModel` through an embedded Liquid template and returns plain text. Needs `ctms_monitoring_visit:create` (sysadmin, sponsor data manager, CRA); other callers get `403` ([ADR-2511](../../docs/adr/2026-09-30-template-render-routes-need-a-permission.md)).
- `POST /api/v1/ctms/doa/delegate` and `POST /api/v1/ctms/doa/revoke`: assign and revoke site Delegation of Authority (DoA) duties. When `staff_email` is supplied and `KERNEL_SERVICE_URL` (or `KernelApi:BaseUrl`) is configured, `KernelDelegationBridge` forwards signed caller headers to `Cadence.Modules.Org` (`POST /api/v1/org/delegations` and `POST /api/v1/org/grants/{id}/revoke`, [ADR-2611](../../docs/adr/2026-11-20-org-module-oidc-provisioning-and-grant-invitations.md)) to provision OIDC accounts, seal direct identifiers in `IdentityVault`, store only the pseudonymous `PersonId` and linked `KernelGrantIds` in `CtmsDelegation`, and propagate structured RFC 9457 `explanation` payloads verbatim on kernel refusal.
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/ctms/**` to this service. Compose publishes host port 8007 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

Site start-up greenlight routes need `ctms_greenlight:read` or `ctms_greenlight:certify`; the site routes also apply site scope, and every route refuses a study the caller is not assigned to. The certifier is the signed caller, and tokens are sealed with `GATEWAY_SECRET` with no fallback (`PermissionMatrix.Ctms.cs`) ([ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md)).

Greenlight gates are activation evidence, so the service never invents them (#5433). Every route needs a `study_id`;
there is no default study. `GET /startup/matrix` only reads, and a study and country with no recorded gates is an empty
matrix. Reading or certifying a single site with no recorded gate returns `404` and writes nothing. Batch certify
refuses the whole request with `400` when any named site has no gate in that study and country, and certifies nothing.
No route records facet evidence yet (#5445), so gates come only from rows already in the database; the CLI has no seed command.

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
