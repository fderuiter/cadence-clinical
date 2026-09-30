# Payments service

Site and participant payments scaffold. It holds the budget contract, fee schedule, payee split, payable, holdback, invoice, remittance and participant wallet model; no business endpoints are mapped yet.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/payments/src/Cadence.Apps.Payments.csproj` (assembly `Cadence.Apps.Payments`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (budget contracts, fee schedules, split payees, payables and allocations, holdback reserves, invoices, remittance advice, participant wallets, audit log).
- `Infrastructure/Persistence/`: `PaymentsDbContext`, a design-time `PaymentsDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145423_InitialCreate`, `20260925183437_ConvertPaymentPropertiesToDecimal`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `PaymentsDbContext`.
- `GET /api/v1/payments/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/payments/**` to this service. Compose publishes host port 8016 (container port 8080); the gateway's default upstream is `http://localhost:8016` (`PAYMENTS_SERVICE_URL`).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<PaymentsDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `payments` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Configuration and secrets at rest

| Setting | Purpose |
| --- | --- |
| `FIELD_ENCRYPTION_KEY` | **Required.** Base64 AES-256 key (`openssl rand -base64 32`) that encrypts payment processor references. The service refuses to start without a valid key; there is no default. |

Participant wallets (`payments_participant_wallets`, #4770) never store card numbers, raw card tokens or bank
routing/account numbers:

- `VirtualCardProcessorReference` and `AchProcessorReference` hold the payment processor's opaque references and are
  encrypted at rest (AES-256-GCM, `enc:v1:` format) by an EF Core value converter. They cannot be queried by equality.
- `VirtualCardMask`, `AchRoutingMask` and `AchAccountMask` hold display masks only.
- Migration `StoreProcessorReferencesInsteadOfPaymentSecrets` turned existing routing numbers into masks and cleared
  the old `VirtualCardToken` / `AchRoutingNumber` columns; re-link references from the processor. Run
  `VACUUM FULL payments_participant_wallets` afterwards. See [ADR-2494](../../docs/adr/2026-09-30-field-level-encryption-and-keyed-lookup-hashes-for-secrets-at-rest.md).

The design-time factory (`dotnet ef`) builds the model with `DesignTimeFieldEncryptor`, which needs no key and
refuses to encrypt or decrypt.

## Tests

- `tests/Payments/PaymentDecimalCalculationTests.cs`: `dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Payments"`.
- Architecture rules (`tests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/payments/src/Cadence.Apps.Payments.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8016, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
