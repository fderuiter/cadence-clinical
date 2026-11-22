# Interoperability service

Interoperability service. It holds the participant ePRO / eCOA endpoints, instrument and subject assignment model, EHR endpoint and reconciliation, statistical computing environment, and device/sensor telemetry model. It maps participant ePRO routes under `/api/v1/interop`, eSource (EHR) routes under `/api/v1/interop/esource`, and the statistical computing environment under `/api/v1/interop/sce`.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/interop/src/Cadence.Apps.Interop.csproj` (assembly `Cadence.Apps.Interop`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (ePRO submissions and quarantine, instruments, subject assignments, EHR endpoints, device registrations, sensor telemetry, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `InteropDbContext`, a design-time `InteropDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145123_InitialCreate`, `20260929203013_HashDeviceTokensAtRest`,
  `20261006202718_AddSceEntities`, `20261019174341_AddEhrEndpointSigningKeyRef` (#5436: an EHR endpoint names a
  server-side signing key reference instead of holding a private key).

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `InteropDbContext`.
- `GET /api/v1/interop/status` (anonymous status payload).
- Participant ePRO & eCOA (`Endpoints/EproEndpoints.cs`, `/api/v1/interop`): assignments (`/assignments/subject/{subjectId}`), assigned instruments (`/subjects/{subjectId}/instruments`), instrument detail (`/instruments/{id}`), compliance (`/subjects/{subjectId}/compliance`), notifications (`/subjects/{subjectId}/notifications`), notification acknowledgement (`POST /notifications/{notificationId}/acknowledge`), single diary submission (`POST /epro/submit`) and offline queue sync (`POST /epro/sync`) with sequence idempotency, conflict archival (`EproSubmissionDefeated`), stale-version quarantine (`EproSubmissionQuarantine`), fail-closed `Subject` caller ownership enforcement (`X-Subject-Code`) and canonical EDC/Kernel observation forwarding (`CanonicalObservationForwarder`, `K-TRUTH-1`).
- eSource (`Endpoints/EsourceEndpoints.cs`, `/api/v1/interop/esource`): EHR endpoint list and registration
  (`/endpoints`), SMART client assertion signing and verification (`/smart/assertion`, `/smart/verify`), polling a
  hospital EHR and ingesting a FHIR bundle (`/poll`, `/ingest`), reconciliation candidates (`/candidates`,
  `/candidates/{candidate_id}`), accepting, rejecting and querying them (`/reconcile/accept`, `/reconcile/batch-accept`,
  `/reconcile/reject`, `/reconcile/batch-reject`, `/reconcile/query`) and the provenance audit
  (`/provenance/{candidate_id}`).
- Statistical computing environment (`Endpoints/SceEndpoints.cs`, `/api/v1/interop/sce`): programs, lockfile
  validation, database-lock tokens (issue, list, revoke), program execution, runs, run verification and run artifacts;
  the permissions are under [Security](#security).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/interop/**` to this service. Compose publishes host port 8004 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

The statistical computing environment (`/api/v1/interop/sce/**`) needs an `sce_*` permission from the permission
matrix on every route, and takes identity and roles only from the verified gateway principal. Only `sponsor_dm` (and
`admin`) issues and revokes database-lock tokens; a token authorizes the roles the matrix lets run unblinded analyses,
never roles from the request. An unblinded run needs `sce_run:execute_unblinded` and a token issued to the caller and
signed with `CADENCE_SCE_SIGNING_KEY`. Unblinded runs and their artifacts are hidden from callers without
`sce_run:read_unblinded` (#5382,
[ADR-2515](../../docs/adr/2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md)). Every SCE route
that names a study also requires the caller to be assigned to it in the signed `study_ids` claim: lists outside the
caller's studies are empty, a program, token or run outside them is not found, and creating a program, issuing or
revoking a token, or executing outside them returns `403` and writes nothing. A run's program must belong to the run's
study (#5402,
[ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md)). `POST /sce/execute`
enforces a server-side sandbox policy: `network_isolated: false`, `filesystem_readonly_root: false` and unapproved
container images are rejected (`400`), `max_memory_mb`, `timeout_seconds` and `max_output_bytes` are clamped to server
bounds, `SceDockerEngineDriver` always runs approved R/Python images with `--network=none`, `--read-only`,
`--cap-drop=ALL`, `--security-opt=no-new-privileges` and `--` before the image name, and artifact collection skips
symbolic links/reparse points and enforces canonical workspace containment and `max_output_bytes` before reading files.

The eSource routes (`/api/v1/interop/esource/**`) need `esource_endpoint:read`/`configure` or `esource_candidate:read`/`ingest`/`reconcile`/`query` (`PermissionMatrix.Interop.cs`). Site staff ingest and reconcile; monitors and data management raise queries. Polling, ingesting and reconciling refuse a study the caller is not assigned to, lists show only the caller's studies, and signatures and audit entries name the signed caller ([ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md)).

Reconciliation candidates come only from source data (#5432):

- `POST /esource/ingest` parses the posted FHIR `Bundle` (`RawFhirBundleJson`; `SiteId` and `EhrSystemId` are required). `FhirObservationMapper` turns each Observation with an id, an `effectiveDateTime`, a numeric `valueQuantity` and a supported LOINC code (systolic and diastolic blood pressure, pulse, temperature, weight, height, glucose) into one candidate. The candidate carries the bundle's value, unit, time and raw resource. Unsupported Observations are skipped and named in the response, and a resource already filed for the same variable is not filed twice.
- Candidates are not compared with the eCRF here, so their discrepancy type is `NOT_COMPARED` and they carry no eCRF value.
- A payload that is not a bundle, or a bundle with nothing to map, returns `400` and writes nothing.
- `POST /esource/poll` returns `501` until a FHIR client for registered endpoints exists. It never fabricates rows.
- `GET /esource/candidates` only reads: an empty study returns an empty page.

SMART Backend Services client assertions are signed only with server-side keys (#5436). An EHR endpoint is registered
with a `SigningKeyRef` and the `SigningKeyId` (`kid`) the EHR knows it by; the PEM private key itself lives in
configuration `SmartSigningKeys:<ref>` (environment variable `SmartSigningKeys__<ref>`, from a secret store, see
[ENVIRONMENT_VARIABLES.md](../../docs/ENVIRONMENT_VARIABLES.md)). `POST /esource/smart/assertion` takes the endpoint id
and a lifetime of at most 300 seconds and signs an RS384 assertion whose issuer and subject are the endpoint's client id
and whose audience is its token URL. `POST /esource/smart/verify` checks an assertion's signature, issuer, audience and
lifetime against the same endpoint. An unknown endpoint is `404`; no reference, an unconfigured key, a malformed PEM or a
public key alone is `422` and signs nothing. Neither route, nor polling, accepts a key in the request body
([ADR-2517](../../docs/adr/2026-10-19-smart-client-assertions-are-signed-only-with-server-side-endpoint-keys.md)).

## Persistence

`AddCadencePostgresDbContext<InteropDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `interop` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Configuration and secrets at rest

| Setting                   | Purpose                                                                                                                                                                                                                                                           |
| ------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `FIELD_LOOKUP_HASH_KEY`   | **Required.** Base64 HMAC-SHA256 key (`openssl rand -base64 32`) for device-token lookup hashes. The service refuses to start without a valid key; there is no default.                                                                                           |
| `CADENCE_SCE_SIGNING_KEY` | **Required.** Base64 HMAC-SHA256 key (`openssl rand -base64 32`) that signs SCE database-lock tokens and run signature blocks. The service refuses to start without a valid key; there is no default.                                                             |
| `EXECUTION_SERVICE_URL`   | Optional base URL of the Execution service (`http://localhost:8002` locally, `execution:8080` in compose). When set, `ActivationBindingResolver` resolves redeemed BYOD participant activation bindings via `GET /api/v1/execution/subjects/activation-codes/me`. |
| `KERNEL_SERVICE_URL`      | Optional base URL of the Kernel API host (`http://localhost:8018` locally, `kernel:8080` in compose). When set, `CanonicalObservationForwarder` posts resolved ePRO diary items to `POST /api/v1/kernel/observations`.                                            |

Wearable/sensor device tokens are never stored (#4770). `device_registrations`, `sensor_telemetry_streams`,
`sensor_daily_summaries` and `sensor_safety_alerts` each keep an indexed `DeviceTokenHash`: the keyed HMAC-SHA256
computed by `DeviceTokenHasher` (registered in DI). To find a device's rows, hash the presented token and compare
hashes. Migration `HashDeviceTokensAtRest` cleared the raw tokens of existing rows (their hash is empty until the
device is re-registered); run `VACUUM FULL` on those four tables afterwards. See
[ADR-2494](../../docs/adr/2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md).

## Tests

- `tests/Cadence.IntegrationTests/Interop/SceUnblindingAuthorizationTests.cs`: SCE lock-token and unblinded-run
  authorization through the real host on PostgreSQL (`CADENCE_TEST_POSTGRES` or `CADENCE_TEST_DOCKER=1`).
- `tests/Cadence.IntegrationTests/Interop/SceStudyScopeTests.cs`: a caller assigned to one study cannot list, read,
  create, mint, revoke or execute for another, and the host connects as a `NOSUPERUSER NOBYPASSRLS` role.
- `tests/Cadence.UnitTests/Interop/StatisticalComputingEnvironmentTests.cs`: lockfile validation and sandbox driver.
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/interop/src/Cadence.Apps.Interop.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8004, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
