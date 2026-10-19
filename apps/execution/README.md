# Execution service (EDC)

Electronic data capture runtime. It manages subjects (enrolment state, consent, demographics), site compliance status, clinical observations with source data verification, form submissions and clinical queries, and runs edit checks and lab-range normalisation.

**Status:** a standalone ASP.NET Core 10 service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap phase 1, "EDC on the kernel") it is rebuilt on kernel concepts. The type-by-type plan is in [EXECUTION_MIGRATION_INVENTORY.md](../../docs/kernel/EXECUTION_MIGRATION_INVENTORY.md).

## Project

- Project: `apps/execution/src/Cadence.Apps.Execution.csproj` (assembly `Cadence.Apps.Execution`).
- `Program.cs`: composition root (DI registrations, ProblemDetails mapping, MassTransit, route groups).
- `Domain/`: subjects, observations, forms, queries and compliance models, domain errors, and repository ports (`Domain/Ports/`).
  Times the service stamps (SDV verification, query resolution and escalation) are `DateTimeOffset` values taken from `Cadence.Kernel.Time.IClock`; an observation time sent without an offset is stored as UTC. The project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784).
- `Application/`: application services per slice, audit-context abstractions, validators, and snake_case JSON settings (`Serialization/`).
- `Infrastructure/Persistence/`: `ExecutionDbContext`, design-time factory, Fluent API `Configurations/`, `Repositories/`, `EfAuditLogWriter`, concurrency interceptor, seeder, and `Migrations/`.
- `Infrastructure/Consumers/`: `EConsentCompletedConsumer` (MassTransit).
- `Infrastructure/EditChecks/`, `Infrastructure/Normalization/`, `Infrastructure/Security/`: AST edit-check engine, UCUM/lab-range/outlier services, AES-GCM demographics encryption.
- `Endpoints/`: Minimal API route groups and `Filters/` (`ValidationFilter<T>`, `ChangeReasonHeaderFilter`).

## HTTP surface

- `GET /healthz` (anonymous, mapped in `Program.cs`); `GET /healthz/live` and `GET /healthz/ready` (anonymous, mapped by `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `ExecutionDbContext`.
- `/api/v1/execution/subjects`, `/api/v1/execution/observations`, `/api/v1/execution/forms`, `/api/v1/execution/queries` (route groups).
- `GET /api/v1/execution/subjects?study_id=` lists a study's subjects in enrollment order. `site_id` and `status` narrow it; `limit` (default 50, at most 200) and `offset` page it, and `total` counts every match. The list carries no treatment group or demographics, so a blinded caller can see it; the detail is `GET /api/v1/execution/subjects/{subjectId}`.
- `/api/v1/execution/sites/{siteId}/activate`, `/api/v1/execution/sites/{siteId}/compliance-status`, `/api/v1/execution/webhooks/etmf`.
- Legacy aliases: `/api/v1/observations`, `/api/v1/forms`, `/api/v1/queries`. The gateway proxies them, so each alias carries the same filters as its documented route: a form update or unlock, or a query reopen or cancel, without `X-Change-Reason` gets `400` on either path (#5060, `LegacyAliasChangeReasonTests`).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/execution/**`, `/api/v1/observations/**`, `/api/v1/forms/**` and `/api/v1/queries/**` here. Compose publishes port 8002.

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route except the health probes requires the gateway-signed caller identity and declares the permission it needs from `PermissionMatrix` (Execution's grants are in `packages/security/src/Rbac/PermissionMatrix.Execution.cs`, [ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md)); a caller whose roles lack it gets `403` before the handler runs, and nothing is read or written. Routes that name a study or site in the path also require the caller's study assignment (`RequireStudyScope`) or site access (`RequireSiteScope`). The permissions keep separation of duties: site staff enter and sign data, CRAs and monitors verify and query it, only a data manager unlocks a signed form or commits a bulk lab-range import, only the pharmacist reaches IP kit reconciliation, releasing a device quarantine is a sponsor safety decision, the eTMF completeness webhook is called by the eTMF service account (role `etmf_service`) and only the CEC chair (role `cec_chair`) finalizes an adjudication. The in-service role checks below still apply. Selected mutating routes (for example form unlock, query reopen/cancel, SDV verify/flag, subject state change) also require an `X-Change-Reason` header via `ChangeReasonHeaderFilter`. Who acted always comes from the verified caller, never the request body: a query's responder and resolver, an SDV verifier and a PI signer. SDV verify and flag answer `403` unless the caller is a CRA, monitor or data manager (`ObservationApplicationService.CanVerifySourceData`, #4920), and query close, reopen and cancel need a CRA or data manager. Adding a participant, recording consent, changing state and editing or clearing demographics answer `403` unless the caller is an investigator (site, principal, sub or lead) or a clinical research coordinator; a data manager reviews and queries but does not modify source subject entries (PRD §3.2, #4981). A state change the transition matrix forbids, or to an unknown state name, answers `422` `INVALID_STATE_TRANSITION` and leaves the state unchanged (PRD-SUB-001; the Python reference answered `400`). A state change to `UNBLINDED` needs the principal investigator or an authorized ER physician instead (`SubjectAuthority`, #4943, PRD-SUB-005, #4982), and a reason for change of at least 50 characters explaining the clinical necessity, or it answers `400` `UNBLINDING_REASON_TOO_SHORT`. The OIDC password re-entry, reason codes and alerts PRD-SUB-005 and PRD-SUB-006 also ask for are not built yet. An unblinding records the caller, the reason and the time on the subject (`unblinded_by`, `unblinded_reason`, `unblinded_at`, #4951) as well as in the audit entry. Subject detail, and the subject returned by a state change or demographics edit, carry `treatment_group: null` unless the caller's signed identity has the `unblinded_access` claim (the gateway's `X-Unblinded-Access`). These role checks become grants from the Trust engine (#4781). The header is decoded with `ChangeReasonHeader` from `packages/security` (plain ASCII, or RFC 8187 `UTF-8''` percent-encoded UTF-8, [ADR-2495](../../docs/adr/2026-09-30-reason-for-change-header-encoding.md)); never read it from `Request.Headers` directly. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md). The demographics key is bound from the `DemographicsEncryption` configuration section (`DemographicsEncryption__KeyBase64`). It must be Base64 of a 16, 24 or 32 byte AES key, and the service refuses to start without one, so a missing key fails at boot rather than on the first subject registration (`tests/Cadence.UnitTests/Execution/Security/DemographicsKeyStartupValidationTests.cs`).

A PI approves a completed form with `POST /api/v1/execution/forms/{id}/approve`, the only signing route for one form (the legacy `/api/v1/forms` alias does not sign). The request carries `X-Sig-Token`, a single-use step-up token (ADR-2515) issued to the caller with action `/api/v1/execution/forms/{id}/approve`, semantic action `execution.form.approve` and `batch_id` set to the payload hash, so it signs one form and one version of its content. The body is `signing_reason` and `signature_manifest: { payload_hash, signer_id?, signer_name? }`; it carries no signature. The service takes the signer and role from the verified caller, the time from its clock, and seals the stored manifest with its own key (#5403). The `payload_hash` is the lower-case hex SHA-256 of the RFC 8785 (JCS) canonical form of the form payload, computed by the client from the payload it submitted: in the web client, `sha256(canonicalSerialize(JSON.parse(payload)))` with `packages/ui/signing.js`, and in .NET `Cadence.Kernel.Trust.JsonCanonicalization.Sha256Hex(payload)`. The service canonicalizes the stored payload the same way, so PostgreSQL's `jsonb` rewriting of key order, spacing and number spelling does not break the check; a hash of other content answers `400` `INVALID_PAYLOAD_HASH` ([ADR-2513](../../docs/adr/2026-09-30-form-approval-hashes-the-rfc-8785-canonical-payload.md), #5023).

## Persistence

`ExecutionDbContext` on PostgreSQL. The connection string is resolved by `CadenceDatabaseExtensions.ResolvePostgresConnectionString` in [packages/database](../../packages/database/README.md): `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only Development falls back to `cadence` on `localhost:5432`. The context uses the `edc` schema in the shared `cadence` database (ADR-2447). The context registers the `GxPAuditInterceptor` and a concurrency-token interceptor. MassTransit uses the EF Core outbox on the same database with the in-memory transport. Migrations live in `Infrastructure/Persistence/Migrations/`; `dotnet run ... -- --seed` seeds sample data and exits.

## Lab reference ranges

An observation's out-of-range flag comes from `LabRangeService` (#5435). It first looks for an active custom range
imported for the observation's own study and test code; a range never applies to another study, and a range with no
study never applies. Among that study's ranges it prefers, in order, the exact local site (for a `LOCAL` observation,
then a generic local range, then central), a matching sex over `ALL`, and the narrowest matching age band. When the
study has no custom range for the test it uses the built-in standard catalog. Evaluation requires the study.

The custom ranges are an in-process snapshot. Before the first evaluation after a start, `EnsureLoadedAsync` loads
the persisted active ranges; if it cannot, the observation is refused rather than flagged against the wrong ranges.
Loading is lazy so hosts that never evaluate (the OpenAPI export, composition tests) start without a database. Each import, update or delete
rebuilds the snapshot and swaps it in one step, so an evaluation never sees an empty or partial set. A second instance
sees another instance's import only after it restarts (durable shared state is #5364).

## Errors and concurrency

- Domain errors come back as RFC 7807 ProblemDetails with a `code` extension. `DomainErrorHttpMapping` sets the status.
- Writes use optimistic concurrency: each aggregate row carries a `_version` token. When another writer changed or
  deleted the row after it was read, the save is rejected rather than retried over their change.
  `ConcurrencyConflictExceptionHandler` returns **409 Conflict** with `code: CONCURRENCY_CONFLICT`, and nothing
  from the rejected save is written or audited. Clients re-read and re-apply (ADR-2450).
- Only transient database failures (serialization failures, deadlocks, dropped connections) are retried, by EF
  Core's retrying execution strategy (`EnableRetryOnFailure`). It retries a whole save, or a whole
  `ExecuteInTransactionAsync` unit of work such as an enrollment with its per-study advisory lock (ADR-2523).
- Every change is audited by `GxPAuditInterceptor` in the same save (ADR-2446), attributed to the caller or a
  named `svc:` service account (ADR-2257).

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Database`, `Cadence.Telemetry`.
- Packages: `Npgsql.EntityFrameworkCore.PostgreSQL`, `FluentValidation` (+ `DependencyInjectionExtensions`), `MassTransit`, `MassTransit.EntityFrameworkCore`, OpenTelemetry, `Scalar.AspNetCore`; `Microsoft.EntityFrameworkCore.Design` at design time only.

## Tests

- Unit and endpoint tests in `tests/Cadence.UnitTests/Execution/`: `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Execution"`. The PostgreSQL tests are in `tests/Cadence.IntegrationTests/Execution/` and need `CADENCE_TEST_POSTGRES` or Docker (`dotnet cadence test --integration`).
- End-to-end tests in `tests/Cadence.E2ETests/E2E/`: `dotnet test tests/Cadence.E2ETests/Cadence.E2ETests.csproj --filter "FullyQualifiedName~Cadence.E2ETests.E2E"`.
- Architecture rules: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/execution/src/Cadence.Apps.Execution.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8002, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
