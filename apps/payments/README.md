# Payments service

Site and participant payments scaffold. It holds the budget contract, fee schedule, payee split, payable, holdback, invoice, remittance and participant wallet model, and maps the participant wallet, stipend, expense and self-billing invoice routes under `/api/v1/payments` (`Endpoints/PaymentEndpoints.cs`).

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/payments/src/Cadence.Apps.Payments.csproj` (assembly `Cadence.Apps.Payments`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (budget contracts, fee schedules, split payees, payables and allocations, holdback reserves, invoices, remittance advice, participant wallets, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Domain/Ledger/`: the grant payables ledger, ported from `packages/engine_core/financial/` ([ADR-2521](../../docs/adr/2026-09-30-grant-payables-ledger-in-decimal-with-kernel-ids.md)), in `decimal` throughout.
  - `ContractCalculations`: tiered overhead, payee splits (the primary payee absorbs rounding) and SHA-256 line-item and statement digests.
  - `PayableLedger`: turns `PaymentEvent`s into `PayableLineItem`s under a `GrantContract`, with per-participant fee caps, withholding and a capped holdback reserve, and releases the reserve. It takes an `IClock` and an ID source.
  - `GrantContractValidator`: the contract limits (rates, holdback of at most 50%, splits that total 100%).
  - No endpoint or persistence uses the ledger yet. The EF Core entities are still the stored shape.
- `Infrastructure/Persistence/`: `PaymentsDbContext`, a design-time `PaymentsDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145423_InitialCreate`, `20260925183437_ConvertPaymentPropertiesToDecimal`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `PaymentsDbContext`.
- `GET /api/v1/payments/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/payments/**` to this service. Compose publishes host port 8016 (container port 8080); the gateway's default upstream is `http://localhost:8016` (`PAYMENTS_SERVICE_URL`).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

### Who may call what

Routes are denied unless they declare a permission ([ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md)).
Every `/api/v1/payments` route calls `RequirePermission("resource:action")`, and
[`PermissionMatrix.Payments.cs`](../../packages/security/src/Rbac/PermissionMatrix.Payments.cs) grants each permission to
roles (#5361). `finance_officer` is the finance role; the local realm's "Grants Manager" and "finance officer" map to it
(`RoleNormalization`). The principal investigator roles inherit the investigator's grants.

| Permission                                 | Routes                                                                                                                                                                                                     | Roles                                                             |
| ------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| `payments_stipend:disburse`                | `POST /disburse`, `POST /participant/wallets/disburse`                                                                                                                                                     | finance_officer                                                   |
| `payments_wallet:disburse`                 | `POST /participant/wallets/{walletId}/disburse`                                                                                                                                                            | finance_officer                                                   |
| `payments_wallet:issue_card`               | `POST /participant/wallets/{walletId}/issue-card`                                                                                                                                                          | finance_officer, crc, investigator                                |
| `payments_wallet:read`                     | `GET /participant/wallets`, `/participant/wallets/subject/{subjectId}`, `/participant/wallets/{walletId}`, `.../tax-1099-status`, `.../balance`, `/participant/transactions`, `/participant/disbursements` | finance_officer, crc, investigator, sponsor_dm, auditor           |
| `payments_w9:submit`, `payments_w9:verify` | `POST /participant/wallets/{walletId}/submit-w9`, `.../verify-w9`                                                                                                                                          | crc, investigator                                                 |
| `payments_stipend_rule:configure`          | `POST /participant/stipend-rules`                                                                                                                                                                          | finance_officer                                                   |
| `payments_stipend_rule:read`               | `GET /participant/stipend-rules`                                                                                                                                                                           | finance_officer, crc, investigator, sponsor_dm, auditor, sysadmin |
| `payments_visit_event:record`              | `POST /participant/events/visit-verified`                                                                                                                                                                  | crc, investigator                                                 |
| `payments_expense:submit`                  | `POST /participant/expenses`, `POST /participant/expenses/ocr-extract`                                                                                                                                     | crc, investigator                                                 |
| `payments_expense:review`                  | `POST /participant/expenses/{claimId}/review`                                                                                                                                                              | crc, investigator                                                 |
| `payments_expense:read`                    | `GET /participant/expenses`                                                                                                                                                                                | finance_officer, crc, investigator, sponsor_dm, auditor           |
| `payments_audit_log:read`                  | `GET /audit-logs`                                                                                                                                                                                          | finance_officer, auditor, sysadmin                                |
| `payments_invoice:read`                    | `GET /invoices`, `GET /invoices/{id}`, `GET /invoices/{id}/remittance-advice/pdf`                                                                                                                          | finance_officer, sponsor_dm, auditor, sysadmin                    |
| `payments_invoice:compile`                 | `POST /invoices/compile`, `POST /invoices/batch-compile`                                                                                                                                                   | finance_officer                                                   |
| `payments_invoice:approve`                 | `POST /invoices/{id}/approve`                                                                                                                                                                              | finance_officer                                                   |
| `payments_invoice:sign_remittance`         | `POST /invoices/{id}/remittance-advice/sign`                                                                                                                                                               | finance_officer                                                   |
| `payments_invoice:reconcile`               | `POST /invoices/{id}/reconcile`                                                                                                                                                                            | finance_officer                                                   |
| `payments_invoice:export_erp`              | `POST /invoices/{id}/erp-export`                                                                                                                                                                           | finance_officer                                                   |

- **Service callers.** `POST /participant/events/visit-verified` is called by the site coordinator who verified the visit
  (or an EDC integration account holding the `crc` role). `POST /invoices/{id}/erp-export` is called by a finance officer
  or an ERP integration account holding `finance_officer`. Both record that account as the actor.
- **Separation of duties.** Approval and remittance signing share the finance role, so the handler refuses (`409`) a
  remittance signature from the user who approved the invoice. Compiling with `auto_approve` also needs
  `payments_invoice:approve`.
- **Identity in records.** Handlers take the actor from the signed principal (`PaymentsCaller`): the `sub` claim as the
  user id and the signed role claims (canonical names, so "Grants Manager" is recorded as `finance_officer`) as
  `UserRoles`. Audit rows, `CreatedBy`, `ApprovedBy`, `ReviewedBy`,
  `ReconciledBy`, `SignedBy` and `SignerRole` all come from it. Request bodies carry no actor: the remittance signature
  takes only `signature_reason`, and the visit-verified event has no `verified_by`. Two system identities remain:
  `svc:payments` on the demonstration stipend rules the service seeds on first read, and `svc:system_auto_approval` on
  the rows the expense auto-approval policy writes (its details name the submitting caller). `SubjectId` on a wallet,
  claim or transaction is the participant the row belongs to, not the actor.
- **Participant self-service is not open.** A participant's sign-in is not bound to a wallet yet, so the `subject` role
  holds no Payments permission; site staff submit W-9s and expense claims for the participant. Self-service needs that
  binding first.
- `GET /participant/wallets/subject/{subjectId}` no longer opens a wallet when none exists; it answers `404`.

## Persistence

`AddCadencePostgresDbContext<PaymentsDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `payments` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Configuration and secrets at rest

| Setting                | Purpose                                                                                                                                                                         |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
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

- `tests/Cadence.UnitTests/Payments/ContractCalculationsTests.cs` and `PayableLedgerTests.cs`: the ledger, pinned to the Python reference's amounts and digests.
- `tests/Cadence.UnitTests/Payments/PaymentsCallerAuthorizationTests.cs`: the host over SQLite; a caller without a write route's role gets `403` and no row changes, and audit rows record the signed caller.
- `tests/Cadence.UnitTests/Payments/PaymentDecimalCalculationTests.cs`: `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Payments"`.
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/payments/src/Cadence.Apps.Payments.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8016, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
