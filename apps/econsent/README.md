# eConsent service

Electronic informed consent and protocol amendment re-consent service. It owns the consent persistence model (documents, templates, clauses, translations, subject consents, Part 11 signatures, comprehension checks, withdrawals, and re-consent requirements), serves staff template publishing and staff/participant consent capture and amendment re-consent endpoints, and synchronizes signed consents to `apps/execution` over gateway-signed HTTP ([ADR-2658](../../docs/adr/2026-11-23-unify-participant-econsent-apis-and-shared-signing-modals.md)).

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/econsent/src/Cadence.Apps.Econsent.csproj` (assembly `Cadence.Apps.Econsent`).
- `Program.cs`: composition root and route mapping.
- `Endpoints/EconsentEndpoints.cs`: Minimal API endpoints for staff template publishing, staff consent capture, and caller-bound participant self-service consent review, comprehension grading, initial signing, pending re-consent lookup, and amendment re-consent completion.
- `Services/EconsentExecutionWebhookPublisher.cs`: `HttpEconsentExecutionWebhookPublisher` posting gateway-signed `svc:econsent` (`econsent_service`) consent sync events to `EXECUTION_SERVICE_URL` (`POST /api/v1/execution/webhooks/econsent`).
- `Domain/`: EF Core entity classes (consent documents and templates, subject consents, signatures, comprehension checks, withdrawals, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `EconsentDbContext`, a design-time `EconsentDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924144810_InitialCreate`, `20260928223000_ConvertEconsentScoreToDecimal`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `EconsentDbContext`.
- `GET /api/v1/econsent/status` (anonymous status payload).
- `POST /api/v1/econsent/templates/{template_id}/versions/{version_index}/publish` and `POST /api/v1/econsent/templates/{template_id}/publish`: staff template publishing (`econsent_template:publish`).
- `GET /api/v1/econsent/templates/{template_id}/approved-content` and `GET /api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks`: participant and site-assisted consent content and comprehension check lookup (`econsent_self_consent:read`).
- `POST /api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers`: comprehension check grading (`econsent_self_consent:verify`).
- `POST /api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent` (`econsent_self_consent:sign`) and `POST /api/v1/econsent/templates/{template_id}/capture-consent` (`econsent_consent:capture`): initial consent signature capture (`RequireElectronicSignature("econsent.consent.capture")`).
- `GET /api/v1/econsent/reconsent/pending/{study_id}` (`econsent_self_reconsent:read`) and `POST /api/v1/econsent/reconsent/complete/{requirement_id}` (`econsent_self_reconsent:sign`, `RequireElectronicSignature("econsent.reconsent.sign")`): caller-bound pending protocol amendment re-consent lookup and single-token re-consent signing.
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/econsent/**` to this service. Compose publishes host port 8011 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

Publishing a template needs `econsent_template:publish` (sponsor data management and study build) and staff consent capture needs `econsent_consent:capture` (site coordinators and investigators). `ROLE_SUBJECT` holds strictly self-service grants (`econsent_self_consent:read`, `econsent_self_consent:verify`, `econsent_self_consent:sign`, `econsent_self_reconsent:read`, `econsent_self_reconsent:sign`) and zero staff permissions (`PermissionMatrix.Econsent.cs`, [ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md), [ADR-2658](../../docs/adr/2026-11-23-unify-participant-econsent-apis-and-shared-signing-modals.md)). Participant-facing endpoints enforce caller-bound `subject_pseudonym == Caller(httpContext)` isolation (failing closed with HTTP `403` on cross-participant access) and require a passed `ComprehensionCheck` before signing when a template defines comprehension questions. Initial consent capture and amendment re-consent completion require a single-use e-signature step-up token (`econsent.consent.capture` or `econsent.reconsent.sign`) recorded in `econsent.consumed_signature_tokens` (`AddCadenceDurableSigTokenStore`, #5434, [ADR-2515](../../docs/adr/2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md)).

## Persistence

`AddCadencePostgresDbContext<EconsentDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `econsent` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Configuration and secrets at rest

| Setting                 | Purpose                                                                                                                                                                     |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `FIELD_ENCRYPTION_KEY`  | **Required.** Base64 AES-256 key (`openssl rand -base64 32`) that encrypts signer e-mail addresses. The service refuses to start without a valid key; there is no default.  |
| `EXECUTION_SERVICE_URL` | Optional upstream URL for `apps/execution` so `HttpEconsentExecutionWebhookPublisher` delivers signed `POST /api/v1/execution/webhooks/econsent` synchronization callbacks. |

Consent signatures (`consent_signatures`, #4770):

- `SignerEmail` is encrypted at rest (AES-256-GCM, `enc:v1:` format) by an EF Core value converter; it cannot be
  queried by equality. It moves to the Identity Vault (ADR-2253) once that exists in code.
- The raw signature capture is not stored. `SignatureBindingSha256` binds it to the signed document digest:
  `ConsentSignatureBinding.Compute(DigestSha256, signatureData)` computes it and `ConsentSignatureBinding.Matches`
  verifies a presented capture.
- Migration `ProtectSignerEmailAndBindSignatureData` computed the binding for existing rows in SQL, then cleared the
  raw captures and plaintext e-mail addresses. Run `VACUUM FULL consent_signatures` afterwards. See
  [ADR-2494](../../docs/adr/2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md).

## Tests

- `tests/Cadence.UnitTests/Econsent/EconsentScoreDecimalPrecisionTests.cs` and `tests/Cadence.UnitTests/Econsent/ParticipantEconsentAndWebhookTests.cs`: `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Econsent"`.
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/econsent/src/Cadence.Apps.Econsent.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8011, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
