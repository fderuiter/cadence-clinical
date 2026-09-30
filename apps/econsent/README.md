# eConsent service

Electronic consent scaffold. It holds the consent persistence model (documents, templates, clauses, translations, subject consents, signatures, comprehension checks, withdrawals and re-consent requirements); no business endpoints are mapped yet.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/econsent/src/Cadence.Apps.Econsent.csproj` (assembly `Cadence.Apps.Econsent`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (consent documents and templates, subject consents, signatures, comprehension checks, withdrawals, audit log).
- `Infrastructure/Persistence/`: `EconsentDbContext`, a design-time `EconsentDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924144810_InitialCreate`, `20260928223000_ConvertEconsentScoreToDecimal`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `EconsentDbContext`.
- `GET /api/v1/econsent/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/econsent/**` to this service. Compose publishes host port 8011 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<EconsentDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `econsent` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Configuration and secrets at rest

| Setting | Purpose |
| --- | --- |
| `FIELD_ENCRYPTION_KEY` | **Required.** Base64 AES-256 key (`openssl rand -base64 32`) that encrypts signer e-mail addresses. The service refuses to start without a valid key; there is no default. |

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

- `tests/Econsent/EconsentScoreDecimalPrecisionTests.cs`: `dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Econsent"`.
- Architecture rules (`tests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/econsent/src/Cadence.Apps.Econsent.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8011, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
