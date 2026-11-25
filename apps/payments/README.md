# Payments service

Site and participant payments scaffold. It holds the budget contract, fee schedule, payee split, payable, holdback, invoice, remittance and participant wallet model, and maps the participant wallet, stipend, expense and self-billing invoice routes under `/api/v1/payments` (`Endpoints/PaymentEndpoints.cs`).

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/payments/src/Cadence.Apps.Payments.csproj` (assembly `Cadence.Apps.Payments`).
- `Program.cs`: composition root and route mapping.
- `Application/`: `ReceiptOcrEngine` (deterministic raster/vector receipt OCR extraction for PNG, BMP, PNM, JPEG, SVG, and PDF inputs with 10 MiB payload limits, bounded PNG chunk/ZLib decompression and ratio quotas, sharpness/completeness confidence scoring, and zero fallback fabrication on unreadable images).
- `Domain/`: EF Core entity classes (budget contracts, fee schedules, split payees, payables and allocations, holdback reserves, invoices, remittance advice, participant wallets, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Domain/Ledger/`: the grant payables ledger, ported from `packages/engine_core/financial/` ([ADR-2521](../../docs/adr/2026-09-30-grant-payables-ledger-in-decimal-with-kernel-ids.md)), in `decimal` throughout.
  - `ContractCalculations`: tiered overhead, payee splits (the primary payee absorbs rounding) and SHA-256 line-item and statement digests.
  - `PayableLedger`: turns `PaymentEvent`s into `PayableLineItem`s under a `GrantContract`, with per-participant fee caps, withholding and a capped holdback reserve, and releases the reserve. It takes an `IClock` and an ID source.
  - `GrantContractValidator`: the contract limits (rates, holdback of at most 50%, splits that total 100%).
  - `PayableLedger` is integrated with `ClinicalVisitCompletedConsumer` to calculate investigator fee line items, split payee allocations, and holdback retention reserves upon signed visit milestone completion via the transactional outbox.
- `Infrastructure/Consumers/`: `ClinicalVisitCompletedConsumer` listens for `ClinicalVisitCompleted` domain events and writes `Payable`, `PayableAllocation`, `HoldbackReserve`, and `PaymentAuditLog` entities.
- `Infrastructure/Persistence/`: `PaymentsDbContext`, a design-time `PaymentsDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145423_InitialCreate`, `20260925183437_ConvertPaymentPropertiesToDecimal`, `20260929203006_StoreProcessorReferencesInsteadOfPaymentSecrets`, `20261001161305_AddParticipantCompensationTables`, `20261006193258_AddDisbursementReferenceToExpenseClaims`, `20261121012903_AddPaymentStatusCheckConstraints`, `20261121053614_EnforceTenantRowLevelSecurity`, `20261121150826_AddSelfServiceIdempotencyKeys`.

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
| `payments_self_wallet:read`                | `GET /participant/me/wallet`                                                                                                                                                                               | subject                                                           |
| `payments_self_wallet:disburse`            | `POST /participant/me/disburse`                                                                                                                                                                            | subject                                                           |
| `payments_self_expense:ocr_scan`           | `POST /participant/me/expenses/ocr-extract`                                                                                                                                                                | subject                                                           |
| `payments_self_expense:submit`             | `POST /participant/me/expenses`                                                                                                                                                                            | subject                                                           |
| `payments_self_stipend:credit`             | `POST /participant/me/diary-stipend`                                                                                                                                                                       | subject                                                           |
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
  remittance signature from the user who approved the invoice. Expense submission and expense review likewise share site
  clinical roles (`crc`, `investigator`), so `POST /participant/expenses/{claimId}/review` and `ParticipantExpenseClaim.Approve`
  refuse (`409`) an approval from the user who submitted the claim (`CreatedBy == userId`), bound `AdjustedAmount`
  between `$0.01` and `claim.GrossAmount`, and recompute `TaxDeduction` and `NetAmount` on the server. Form W-9
  submission and verification also share site clinical roles (`crc`, `investigator`): `POST /participant/wallets/{walletId}/submit-w9`
  requires a non-empty `FullLegalName` and a four-digit `TaxIdLastFour` (`422`), and `POST /participant/wallets/{walletId}/verify-w9`
  requires `W9Status == SUBMITTED` with a four-digit `TaxIdLastFour` and refuses (`409`) verification by the user who
  recorded the latest `W9_FORM_SUBMITTED` entry. Direct stipend disbursement (`POST /disburse`, `POST /participant/wallets/disburse`)
  enforces positive amounts (`400`), IRB `AntiCoercionCap` (`422`), `AutoDisbursementThreshold` (`409`), and IRS `$600.00`
  `TaxableYtd` verified W-9 holds (`409`), deduplicates per wallet on `ParticipantTransaction.IdempotencyKey`, and records
  `ParticipantTransaction` stipend credits and `TaxableYtd` without inflating `LifetimeDisbursements`. Compiling with
  `auto_approve` also needs `payments_invoice:approve`.
- **Identity in records.** Handlers take the actor from the signed principal (`CallerScope`): the `sub` claim as the
  user id and the signed role claims (canonical names, so "Grants Manager" is recorded as `finance_officer`, truncated to
  255 characters via `CallerScope.Roles(httpContext, 255)`) as `UserRoles`. Audit rows, `CreatedBy`, `ApprovedBy`, `ReviewedBy`,
  `ReconciledBy`, `SignedBy` and `SignerRole` all come from it. Request bodies carry no actor: the remittance signature
  takes only `signature_reason`, and the visit-verified event has no `verified_by`. Two system identities remain:
  `svc:payments` on the demonstration stipend rules the service seeds on first read, and `svc:system_auto_approval` on
  the rows the expense auto-approval policy writes (its details name the submitting caller). `SubjectId` on a wallet,
  claim or transaction is the participant the row belongs to, not the actor.
- **Caller-bound participant self-service ([ADR-2817](../../docs/adr/2026-11-19-caller-bound-participant-self-service-wallet-and-receipt-ocr-capabilities.md), [ADR-2526](../../docs/adr/2026-11-20-idempotent-self-service-participant-expense-claims-and-wallet-disbursements.md)).**
  The `subject` role holds only the five `payments_self_*` capabilities mapped to `/api/v1/payments/participant/me/*`
  and never reaches staff directory, card-issuance, W-9 verification, claim-review, or invoice routes (`403` with zero
  database mutations). Every `/participant/me/*` handler resolves the wallet through `ResolveOwnWalletAsync` bound to
  `CallerScope.VerifiedUserId`, refuses (`403`) any cross-participant `subjectId` or `walletId`, masks card/routing/account
  identifiers before serialization, enforces the server-computed `<= $100.00` (`<= $75.00` server meal per-diem cap) /
  `>= 0.90` server-bound OCR confidence (from verified image/document bytes or a single-use HMAC token minted exclusively from readable image/document bytes when `rawText` is absent, with plain-text uploads,
  manual field edits, and duplicate receipt SHA-256 digests held in `PENDING_REVIEW`) / IRS `$600.00` verified W-9
  auto-approval policy, persists `(WalletId, IdempotencyKey)` deduplication keys across `/participant/me/expenses` and
  `/participant/me/disburse` so retries never double-credit or double-debit balances, and computes diary synchronization
  stipends strictly from eligible (`RequiresVerification = false`, `IsMilestone = false`) server-side
  `ParticipantStipendRule` rows gated on a single-use `GATEWAY_SECRET`-derived HMAC `SyncVerificationToken` bound to
  `(caller.UserId, StudyId, DiaryId, AssignmentId, SequenceNumber, ExpiresAtUnix)`, `AntiCoercionCap`,
  `AutoDisbursementThreshold`, and IRS `$600.00` W-9 compliance (`400` if a request supplies an `amount`;
  `FLAGGED_FOR_REVIEW` with `$0.00` credited on missing, invalid, or replayed sync tokens or exceeded thresholds). Staff
  `POST /participant/expenses` applies the same server-computed OCR confidence, receipt deduplication, and `$75.00` meal
  per-diem cap rules without trusting client `OcrConfidence`, `ReceiptOcrData`, or `PerDiemCap`.
- `GET /participant/wallets/subject/{subjectId}` no longer opens a wallet when none exists; it answers `404`.
- **Study and site scope (#5401, [ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md), [ADR-2661](../../docs/adr/2026-11-22-consolidate-standalone-caller-and-clinical-scope-guards.md)).**
  Every handler reaches only the studies in the caller's signed `study_ids` and, for a site-scoped role (CRC,
  investigators, monitors) or a caller with site assignments, only their signed sites (`ClinicalScopeGuard.CanAccessStudy`,
  `ClinicalScopeGuard.CanAccessSite`, `ClinicalScopeGuard.NarrowToScope`, and `ClinicalScopeGuard.NarrowToStudyScope`,
  backed by `StudyAccessEvaluator` and `SiteAccessEvaluator`). Scope fails closed: a caller with no
  study, or a site-scoped role with no site, reaches nothing.
  - Lists (wallets, claims, invoices, stipend rules) are narrowed in the query. Transactions and disbursements carry no
    site, so they are narrowed through their wallet. The audit log shows a restricted caller only the rows about the
    wallets, claims, stipend rules, invoices and remittances they reach, and no batch-level rows.
  - A wallet, claim, invoice or remittance outside scope answers `404` on every read and write by id, the same answer
    as a missing row.
  - A write that names a study or site (visit-verified event, expense claim, compile, stipend rule, direct
    disbursement) answers `403` outside scope and writes nothing. An existing wallet keeps its own site: naming the
    caller's site in the body does not reach another site's participant. Batch compile refuses a named site outside
    scope and narrows discovered sites to the caller's.

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
- `tests/Cadence.UnitTests/Payments/ParticipantSelfServiceAndOcrTests.cs`: caller-bound `/participant/me/*` self-service wallet balances and masks, cross-participant and staff-route `403` isolation, `ReceiptOcrEngine` raster/SVG extraction and zero-fabrication guardrails, `$100` / `$75` meal / `0.90` confidence / `$600` W-9 claim policies, on-demand balance payouts, and server-rule diary stipends.
- `tests/Cadence.IntegrationTests/Payments/PaymentsStudySiteScopeTests.cs`: the host on PostgreSQL as the application role; a CRC at one site gets `404` or `403` on another site's participants and never sees them in lists, a caller outside the study sees none of its rows, nothing is written, and in-scope flows still complete. Needs `CADENCE_TEST_POSTGRES` or `CADENCE_TEST_DOCKER=1`.
- `tests/Cadence.UnitTests/Payments/PaymentDecimalCalculationTests.cs`: `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Payments"`.
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/payments/src/Cadence.Apps.Payments.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8016, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
