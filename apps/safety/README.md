# Safety service

Safety and pharmacovigilance scaffold. It holds the safety case, ICSR reporting evaluation, SAE reconciliation, narrative, signal detection, periodic report and device investigation model, and renders ICH E2B(R3) ICSR messages from a Liquid template, validated against the vendored ICH schema.

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
  in the former `varchar(255)`, so approval failed on PostgreSQL), `20261019230618_AddConsumedSignatureTokens` (the
  `consumed_signature_tokens` table that makes step-up tokens single use, #5462), `20261020101549_WidenSafetySignatureManifests` (the
  same column on reporting evaluations, narratives, signal alerts and device investigations becomes `text`, so every
  record that can carry a signature stores the whole manifest; [ADR-2519](../../docs/adr/2026-10-20-safety-signature-manifests-are-stored-as-text.md), #5482),
  `20261020103234_SplitDsurExposureTotals`
  (`periodic_safety_reports.TotalSubjectExposure`, an integer that held a patient-year sum, becomes
  `TotalPatientYearsExposure` `double precision`, #5573).
- `Domain/IcsrReport.cs`, `Domain/IcsrConformance.cs`, `Domain/Hl7Timestamp.cs`: the ICSR request, its ICH E2B(R3)
  business-rule check and the HL7 TS date parser.
- `Infrastructure/Standards/IcsrSchema.cs`: validation against the ICH E2B(R3) schema, embedded from
  [`../Schemas/ich-e2b-r3`](Schemas/ich-e2b-r3/README.md) (unmodified ICH files; source, checksum and licences there).
- `../Templates/e2b_r3_icsr.xml.liquid`: ICSR template embedded into the assembly.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `SafetyDbContext`.
- `GET /api/v1/safety/status` (anonymous status payload).
- `POST /api/v1/safety/icsr/render`: renders an `IcsrReport` through `e2b_r3_icsr.xml.liquid` and returns an ICH
  E2B(R3) ICSR (`application/xml`): an HL7 v3 `MCCI_IN200100UV01` batch holding one `PORR_IN049016UV` message, with the
  ICH OIDs and code lists of the ICH ICSR Implementation Guide. Field values are XML-escaped
  ([templating](../../packages/templating/README.md)). Needs `safety_report:create` (sysadmin, sponsor medical
  monitor); other callers get `403` ([ADR-2511](../../docs/adr/2026-09-30-template-render-routes-need-a-permission.md)).
  The request carries the batch and message header (N.1, N.2), the case (C.1: sender's report id, worldwide unique case
  id, creation and receipt dates, report type, expedited flag, documents held, other case ids), the primary sources
  (C.2.r qualification and country, exactly one primary for regulatory purposes), the sender type and organisation
  (C.3), the study (C.5), the patient id, sex, age and birth date (D), the reactions (E.i: MedDRA version and LLT code,
  reported term and language, dates, the six seriousness criteria, outcome), the drugs (G.k: role, product and
  substance names, dosage text, route, action taken) and the narrative (H.1). The patient name is always sent as
  `nullFlavor="MSK"`.
  - Before rendering, `Domain/IcsrConformance.cs` applies the ICH business rules: mandatory and conditional elements,
    the ICH code lists, lengths, HL7 date/time form and precision (seconds for N.1.5 and C.1.2, days for C.1.4, C.1.5
    and D.2.1), no future date, one MedDRA version per report, at least one suspect, interacting or not-administered
    drug. A report that breaks one is refused with a `400` validation problem whose `errors` are keyed by the request
    path (for example `reactions[0].meddraCoding.lltCode`).
  - After rendering, the message is validated against the ICH E2B(R3) schema; a failure (a template defect) is a `500`
    problem listing `schemaErrors`, never an invalid message.
  - MedDRA codes are checked for their form only: the MedDRA dictionary is licensed and not in the repository. Optional
    E2B(R3) sections the request has no field for (reporter name and address, literature references, medical history,
    lab tests, dose details beyond the text, drug-reaction relatedness assessments) are not emitted
    ([ADR-2464](../../docs/adr/2026-10-20-icsr-render-emits-ich-e2br3-validated-against-the-ich-schema.md), #5573).
- DSUR periodic reports, under `/api/v1/safety/dsur` and the `/api/v1/safety/periodic-reports` aliases:
  - Compilation counts as SAEs the serious cases only, groups placebo arms, then comparator arms, then everything else
    as investigational for both the exposure and the cross-tabulation, and stores the unrounded total patient-years
    (`TotalPatientYearsExposure`); the payload's `TotalSubjectsEnrolled` is the subject count (#5573).
  - `POST .../compile` and `PUT .../{id}` need `safety_report:create`; `GET .../{id}` and the list need
    `safety_report:read`. The compiler (`CreatedBy`) and each audit entry are the verified caller; a request without one
    gets `401`, never a default user. The period bounds are stored as UTC whatever offset the request gives them in
    (PostgreSQL `timestamptz` accepts only UTC, so a `+02:00` bound used to fail with `500`, #5464). An update needs a `reasonForChange` (`400` without one; no default reason is filled in) and is refused on a LOCKED report (`409`).
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

`AddCadencePostgresDbContext<SafetyDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `safety` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; there is no fallback in any environment, and startup fails when neither key is set ([ADR-2260](../../docs/adr/2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md)). `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Audit

`Program.cs` adds `GxPAuditInterceptor` to `SafetyDbContext` through `AddCadencePostgresDbContext(configureOptions: ...)`.
The DSUR operations record `DSUR_COMPILED`, `DSUR_UPDATED` and `DSUR_APPROVED` through `AuditActions`, and the
interceptor writes them as hash-chained `audit_log_entries` (chain `PeriodicSafetyReportRecord`) naming the verified
caller and the reason for change. `PeriodicSafetyReportRecord` is `IGxPAuditable`, so each action labels the record's
own entry, which carries the record's `VersionIndex` (1 on compile, then one more per update and on approval) and the
changed properties, instead of a version-0 action-only entry (#5573). The interceptor's actor is the request's verified caller; a save with audited work
outside a verified request fails. E-signature tokens are single use across instances and restarts: used token ids are stored in
`consumed_signature_tokens` (`AddCadenceDurableSigTokenStore<SafetyDbContext>()`, ADR-2515, #5434).

## Key dependencies

- Project references: `Cadence.Compliance`, `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Database`, `Cadence.Templating`.
- Packages: `Fluid.Core`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- `tests/Cadence.UnitTests/Safety/`: `DsurCompilerEngineTests` (engine and handlers) and `DsurApprovalAuthorityTests`
  (approval through the real host on an in-memory database: permission, step-up token, signer, compiler, reason, lock).
- `tests/Cadence.UnitTests/Safety/SafetyRouteAuthorityTests.cs` pins the permission of every safety route (the table
  fails when a route is added or changes permission) and checks `401` unsigned and `403` without the permission, with
  nothing written. `DsurCompilationTests` covers the missing compound (`400`), exposure and the MedDRA cross-tabulation
  against a hand-computed fixture, and the lock (update after approval and a second approval are `409`).
  `Compile_CountsSeriousCasesAsSaesAndGroupsComparatorArmsOnce` and
  `CompileUpdateApprove_AuditEntriesCarryTheRecordVersion` cover the SAE count, the arm grouping and the audit version
  (#5573). `IcsrRenderTests` compares the render with `Safety/Golden/e2b_r3_icsr.golden.xml`, validates the complete and
  the minimal report against the ICH schema (loaded from disk, independently of the service), and checks each missing
  or invalid element (`IcsrFixture.cs` holds the synthetic request).
- `tests/Cadence.IntegrationTests/Safety/DsurApprovalPostgresTests.cs` runs compile and approval through the real host on
  PostgreSQL (`CADENCE_TEST_POSTGRES` or `CADENCE_TEST_DOCKER=1`) and checks the audit entries.
  `SafetyPersistencePostgresTests.cs` checks that the migrations apply and match the model, and that compile, update
  and approve store UTC `timestamptz` instants and the verified caller, reason and version of each write.
  `SafetySignatureManifestPostgresTests.cs` checks that every `EsignatureManifestId` column is `text` and that a whole
  signed manifest round-trips through a host DSUR approval and through each of the other four records (#5482).
  The same file checks that compile stores the unrounded patient-years in a `double precision` column and counts only
  serious cases.
- SAE reconciliation has entities and tables only (`SaeReconciliationRun`, `SaeDiscrepancy`, `SaeReconciliationJob`)
  and no code path, so it has no tests.
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/safety/src/Cadence.Apps.Safety.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8008, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
