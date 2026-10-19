# Safety service

Safety and pharmacovigilance scaffold. It holds the safety case, ICSR reporting evaluation, SAE reconciliation, narrative, signal detection, periodic report and device investigation model, and renders ICSR E2B(R3) XML from a Liquid template.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/safety/src/Cadence.Apps.Safety.csproj` (assembly `Cadence.Apps.Safety`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (safety cases, reporting evaluations, SAE reconciliation runs and discrepancies, narratives, signal detection, periodic reports, device investigations, AS2 transmissions, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `SafetyDbContext`, a design-time `SafetyDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Application/DsurCompilerEngine.cs`: DSUR compilation, draft update and approval (GxPSigner manifest, record lock).
- `Infrastructure/Persistence/Migrations/`: `20260924145528_InitialCreate`, `20261019200603_WidenDsurSignatureManifest`
  (`periodic_safety_reports.EsignatureManifestId` becomes `text`: it holds the whole signed manifest, which never fit
  in the former `varchar(255)`, so approval failed on PostgreSQL).
- `../Templates/e2b_r3_icsr.xml.liquid`: ICSR template embedded into the assembly.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `SafetyDbContext`.
- `GET /api/v1/safety/status` (anonymous status payload).
- `POST /api/v1/safety/icsr/render`: renders an `IcsrReport` through `e2b_r3_icsr.xml.liquid` and returns `application/xml`. The template reads the report as `icsr`; field values are XML-escaped ([templating](../../packages/templating/README.md)). Needs `safety_report:create` (sysadmin, sponsor medical monitor); other callers get `403` ([ADR-2511](../../docs/adr/2026-09-30-template-render-routes-need-a-permission.md)).
- DSUR periodic reports, under `/api/v1/safety/dsur` and the `/api/v1/safety/periodic-reports` aliases:
  - `POST .../compile` and `PUT .../{id}` need `safety_report:create`; `GET .../{id}` and the list need
    `safety_report:read`. The compiler (`CreatedBy`) and each audit entry are the verified caller; a request without one
    gets `401`, never a default user. An update needs a `reasonForChange` (`400` without one; no default reason is filled in) and is refused on a LOCKED report (`409`).
  - `POST /api/v1/safety/dsur/{id}/approve` (and `/periodic-reports/{id}/approve`) signs and locks the report. It needs
    `safety_report:approve`, which only the sponsor medical monitor holds (`PermissionMatrix.Safety.cs`, PRD-PVG-008),
    and a step-up signature token (`X-Sig-Token`, [ADR-2515](../../docs/adr/2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md))
    issued for action `/api/v1/safety/dsur/{id}/approve` and semantic action `safety.periodic_report.approve` on either
    route; without it the answer is `401 REAUTHENTICATION_REQUIRED`. The body is `{ "signingReason"?, "reasonForChange" }`:
    the reason for change is required (`400`), and the signer is always the verified caller. The report's compiler
    cannot approve it (`403`), and a LOCKED report cannot be signed again (`409`). The signature manifest, approver and
    the `DSUR_APPROVED` audit entry name the caller (#5462, [ADR-2518](../../docs/adr/2026-10-19-dsur-approval-is-its-own-capability-signed-by-the-verified-caller.md)). The former `/periodic-reports/{id}/sign` alias is removed.
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/safety/**` to this service. Compose publishes host port 8008 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<SafetyDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `safety` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Audit

`Program.cs` adds `GxPAuditInterceptor` to `SafetyDbContext` through `AddCadencePostgresDbContext(configureOptions: ...)`.
The DSUR operations record `DSUR_COMPILED`, `DSUR_UPDATED` and `DSUR_APPROVED` through `AuditActions`, and the
interceptor writes them as hash-chained `audit_log_entries` (chain `PeriodicSafetyReportRecord`) naming the verified
caller and the reason for change. The interceptor's actor is the request's verified caller; a save with audited work
outside a verified request fails. E-signature tokens are single use within one safety process (the process-local
`ReplayCache`), not across instances or restarts (ADR-2515 §4.2).

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Fluid.Core`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- `tests/Cadence.UnitTests/Safety/`: `DsurCompilerEngineTests` (engine and handlers) and `DsurApprovalAuthorityTests`
  (approval through the real host on an in-memory database: permission, step-up token, signer, compiler, reason, lock).
- `tests/Cadence.IntegrationTests/Safety/DsurApprovalPostgresTests.cs` runs compile and approval through the real host on
  PostgreSQL (`CADENCE_TEST_POSTGRES` or `CADENCE_TEST_DOCKER=1`) and checks the audit entries.
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/safety/src/Cadence.Apps.Safety.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8008, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
