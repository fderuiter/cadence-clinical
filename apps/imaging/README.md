# Imaging service

Medical imaging service. It de-identifies DICOM uploads (single instances and batch folders or ZIP archives), stores the imaging study/series/instance envelopes and signed de-identification certificates, and runs imaging adjudication.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/imaging/src/Cadence.Apps.Imaging.csproj` (assembly `Cadence.Apps.Imaging`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (imaging study, series and instance envelopes, de-identification certificates, annotations, tumour response assessments, adjudication sessions, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `ImagingDbContext`, a design-time `ImagingDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145016_InitialCreate`.
- `Infrastructure/Security/DeidentificationKeys.cs`: `AddImagingDeidentification`, which registers the one `DicomDeidentifier` built from the configured keys.
- `Services/DicomIngestionService.cs` and `Services/BatchStreamHelpers.cs`: DICOM single-instance and streaming batch ingestion through `DicomDeidentifier` ([packages/deid](../../packages/deid/README.md)) and `IScratchWorkspaceProvider` ([packages/storage](../../packages/storage/README.md)). Batch ingestion requires `studyId` and `siteId` before any file section (`ClinicalScopeGuard.RequireStudyAndSite` runs prior to spooling), streams multipart sections and raw uploads, spools multipart files and non-seekable ZIP archives exclusively inside `Scratch:RootPath` (deleted on lease disposal in `finally` blocks), lazily enumerates archive entries through a bounded channel under explicit archive inflation quotas (`4,096` max entries, `128 MiB` per-entry uncompressed cap, `512 MiB` cumulative uncompressed cap, `100:1` max expansion ratio), validates DICOM `Rows`/`Columns` in 64-bit arithmetic against a `64 MiB` frame cap, and de-identifies slices over pooled sub-LOH blocks without retaining raw or de-identified slice byte arrays at `SaveChangesAsync`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `ImagingDbContext`.
- `GET /api/v1/imaging/status` (anonymous status payload).
- `POST /api/v1/imaging/studies` and `POST /api/v1/imaging/studies/batch` (`Endpoints/DicomWebEndpoints.cs`): single-instance STOW-RS (`128 MiB` cap) and multi-slice folder/ZIP batch uploads (`imaging_study:upload`). Uploads accept scope from query parameters, headers (`X-Upload-Study-Id`/`X-Imaging-Study-Id`/`X-Study-Id`, `X-Upload-Site-Id`/`X-Imaging-Site-Id`, `X-Upload-Subject-Id`/`X-Imaging-Subject-Id`/`X-Subject-Id`), or leading form fields, require non-empty `studyId` and `siteId` and pass `ClinicalScopeGuard.RequireStudyAndSite` (`403` when out of scope) before spooling any file bytes, require non-empty `subjectId` (`400 MISSING_SCOPE` after `EMPTY_PAYLOAD` checks when blank), and verify `(StudyId, SiteId, SubjectId)` ownership on stored `ImagingStudyEnvelopes`. `/studies/batch` streams `multipart/form-data` sections and raw uploads under a `2 GB` (`2,147,483,648` bytes) raw byte budget and chunked ZIP inflation quotas (`413 Payload Too Large` when raw size, entry count, uncompressed bytes, or expansion ratio exceed limits with or without `Content-Length`, `400 EMPTY_PAYLOAD` when empty), rejects non-positive or `> 64 MiB` DICOM frame dimensions, and aborts persistence if burned-in pixel masking fails (`PRD-IMG-002`).
- `GET /api/v1/imaging/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameList}` (`Endpoints/DicomWebEndpoints.cs`): WADO-RS frame retrieval (`imaging_study:read`). Verifies route `studyUid` and `seriesUid` against the instance's parent envelopes, enforces study and site scope (`404 Not Found` otherwise), and validates stored `Rows` and `Columns` in 64-bit arithmetic before allocation (`422 Unprocessable Entity` when dimensions are non-positive or exceed the `64 MiB` frame cap).
- `POST /api/v1/imaging/studies/{studyUid}/series/{seriesUid}/segment` (`Endpoints/ImagingSegmentationEndpoints.cs`, `Services/ImagingSegmentationService.cs`). No segmentation model reads the image, so the route measures nothing and writes nothing: an in-scope request answers 501 and the reader measures with the calipers (#5426). It used to store a fixed shape drawn around the seed, with constant Hounsfield statistics and an invented model version, as an AI-assisted RECIST measurement under the reader's name. The study comes from the stored scan, the series must belong to it, and the caller's signed study scope must include that study (403 otherwise, 404 for an unknown study or series); the route needs `imaging_adjudication:submit` (#5441). A real model will run in the Intelligence module and propose measurements a reader approves (ADR-2630). The viewer has no AI Auto-Segment tool until then.
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/imaging/**` to this service. Compose publishes host port 8014 (container port 8080); the gateway's default upstream is `http://localhost:8014` (`IMAGING_SERVICE_URL`).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

Uploads need `imaging_study:upload` and a `studyId` the caller is assigned to; reads need `imaging_study:read` and answer `404` outside the caller's studies. Adjudication needs `imaging_adjudication:submit`, `read` or `adjudicate` (the medical monitor chairs). The certificate operator and the chair's signature are the signed caller; the `X-Operator-Identity` header and a body `SignerId` are no longer accepted (`PermissionMatrix.Imaging.cs`) ([ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md)).

## De-identification keys

Imaging needs two keys, each base64 of 32 random bytes (`openssl rand -base64 32`), with no default in any environment (#5399, [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md)):

| Setting                                     | Purpose                                                                                                                                                                                                                    |
| ------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CADENCE_IMAGING_DEID_SIGNING_KEY`          | Signs each de-identification certificate and batch manifest (HMAC-SHA256 over canonical JSON). `GET /api/v1/imaging/certificates/{id}` verifies the stored certificate with it and reports `is_signature_valid`.           |
| `CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY` | Keys the anonymized Study, Series, SOP and Frame of Reference UIDs and the original-UID hashes on the certificate (HMAC-SHA256), so a holder of an original UID cannot link it to the de-identified study without the key. |

The service refuses to start when either key is missing, is not 32 bytes, or both are the same key. Rotating the signing key makes stored certificates report an invalid signature; rotating the pseudonymization key gives the same source study new anonymized UIDs.

## Persistence

`AddCadencePostgresDbContext<ImagingDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `imaging` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; there is no fallback in any environment, and startup fails when neither key is set ([ADR-2260](../../docs/adr/2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md)). `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Deid`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Storage`, `Cadence.Telemetry`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- `tests/Cadence.UnitTests/Imaging/`: ingestion, endpoint and de-identification key tests (`DicomIngestionServiceTests`, `DicomWebEndpointsTests`, `ImagingDeidentificationKeyTests`).
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
export CADENCE_IMAGING_DEID_SIGNING_KEY="$(openssl rand -base64 32)"
export CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY="$(openssl rand -base64 32)"
export Scratch__RootPath="/var/lib/cadence/imaging-scratch"
dotnet run --project apps/imaging/src/Cadence.Apps.Imaging.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8014, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
