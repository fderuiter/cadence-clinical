# ADR-2646: Standalone Optimistic Concurrency, Participant Wallet Row Locks, and Bitemporal or Insert-Only Revisions

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-CRF-013, PRD-PAY-003, PRD-TCK-001

---

## 1. Context & Problem Statement

Fifteen standalone service `DbContext` schemas previously persisted 134 tenant-scoped domain tables where 129 tables omitted EF Core `.IsConcurrencyToken()` configuration, allowing concurrent requests to overwrite regulated fields in place and compound manually incremented `VersionIndex` values across retries. In `apps/payments`, staff and participant self-service wallet mutations read and updated unlocked `ParticipantWallet` rows and checked non-unique `ReferenceId` values in `CreditSelfDiaryStipend`, leaving concurrent payout or stipend calls with distinct or omitted `Idempotency-Key` headers exposed to lost updates or duplicate credits. In addition, effective-dated delegations and site activation states, regulated documents, DSUR safety reports and narratives, and operational tickets lacked database-enforced bitemporal `tstzrange` intervals or insert-only historical revision tables while remaining constrained by `K-XS-2` not to reference `Cadence.Kernel.Persistence`.

## 2. Decision Drivers & Constraints

- **Participant Financial Serialization (`PRD-PAY-003`, ADR-2817, ADR-2526):** Concurrent staff and self-service balance checks, credits, debits, and initial wallet creation must serialize per participant without overdrafts or duplicate diary stipends across distinct or omitted `Idempotency-Key` headers.
- **Universal Optimistic Concurrency & Conflict Mapping (`PRD-SYS-001`, ADR-2620):** Every standalone domain table must enforce `.IsConcurrencyToken()` on `VersionIndex` or shadow `_version`, advance tokens as `OriginalValue + 1` without compounding on retries, and map `DbUpdateConcurrencyException` to HTTP `409` ProblemDetails (`code: "CONCURRENCY_CONFLICT"`).
- **Point-in-Time Regulatory Reconstruction (`K-TIME-1`, `K-TIME-2`, ADR-2246, ADR-2621):** Contestable effective-dated state requires PostgreSQL `tstzrange` (`valid_period`, `recorded_period`) with `btree_gist` exclusion constraints and `KT001` append-only triggers, while document, safety report/narrative, and ticket corrections require numbered immutable revision rows guarded by `KA001` insert-only triggers.
- **Strict Kernel Boundary & Tenant Isolation (`K-XS-2`, ADR-2548):** Standalone helpers and triggers must live in `packages/database` without referencing `Cadence.Kernel.Persistence`, while preserving all 17 `ServiceTopology` routes and `FORCE ROW LEVEL SECURITY`.

## 3. Options Considered

### Option 1: Handler-by-Handler Optimistic Checks and Application-Only Audit Logs

- **Overview:** Add ad-hoc `VersionIndex` checks in selected endpoint handlers and rely on JSON audit payloads to inspect historical values without PostgreSQL row locks, `tstzrange` exclusion constraints, or `KT001`/`KA001` triggers.
- **Pros:**
  - Avoids coordinated EF Core migrations across the 15 standalone schemas.
- **Cons:**
  - Leaves uninstrumented tables exposed to silent lost updates, permits concurrent wallet payouts with distinct `Idempotency-Key` headers to overdraft balances before commit, and cannot enforce single-truth `as_of(valid_at, known_at)` intervals or block direct DML tampering at the database boundary.

### Option 2: Shared `packages/database` Concurrency & Temporal/Insert-Only Guards Plus `SELECT ... FOR UPDATE` Wallet Serialization (Selected)

- **Overview:** Configure `.IsConcurrencyToken()` (`VersionIndex` or shadow `_version`) across all standalone tenant-scoped tables in `StandaloneTenantModelExtensions`, compute `OriginalValue + 1` for `int` and `long` tokens in `StandaloneConcurrencyTokenInterceptor` and `GxPAuditInterceptor`, register `StandaloneConcurrencyExceptionHandler` for HTTP `409` ProblemDetails, serialize `ParticipantWallet` mutations under `ExecuteInTransactionAsync` with `SELECT ... FOR UPDATE` and unique `(tenant_id, SubjectId, StudyId)`, `(WalletId, TransactionType, ReferenceId)`, and `(WalletId, IdempotencyKey)` indexes, and enforce `StandaloneTemporalSql` (`btree_gist` + `KT001`) and `StandaloneInsertOnlySql` (`KA001`) across contestable period, revision, ledger, and audit tables.
- **Pros:**
  - Eliminates concurrent wallet overdrafts and duplicate diary stipends across distinct or omitted `Idempotency-Key` values while preserving paired `REIMBURSEMENT_CREDIT` and `DISBURSEMENT_DEBIT` expense rows.
  - Enforces optimistic concurrency and HTTP `409` conflict responses uniformly across all 15 standalone schemas without violating `K-XS-2`.
  - Guarantees database-level `KT001` and `KA001` immutability and deterministic `as_of(valid_at, known_at)` reconstruction under `cadence_app`.
- **Cons:**
  - Adds one coordinated migration per standalone `DbContext` schema and requires clients presenting stale version tokens to reload before retrying.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing concurrency token configuration, `OriginalValue + 1` interception, HTTP `409` exception mapping, and `KT001`/`KA001` DDL helpers in `packages/database` while locking `ParticipantWallet` rows with `SELECT ... FOR UPDATE` satisfies `PRD-SYS-001`, `PRD-CRF-013`, `PRD-PAY-003`, `PRD-TCK-001`, `ADR-2246`, `ADR-2620`, and `ADR-2621` within `K-XS-2` and `ADR-2548` guardrails.

## 5. Consequences & Trade-offs

- **Positive Impact:** Concurrent staff and self-service payouts serialize cleanly without overdrafts; racing `CreditSelfDiaryStipend` requests deduplicate via unique composite indexes and return `ALREADY_PROCESSED`; stale edits across all 15 standalone services fail closed with HTTP `409`; and auditors can query `as_of(valid_at, known_at)` periods and numbered revisions protected by `KT001` and `KA001`.
- **Negative Impact / Technical Debt:** Standalone schemas carry dedicated `*Period` and `*Revision` tables alongside current-state projection tables until standalone modules migrate into the Kernel host.
- **Mitigation Strategy:** `ArchitectureSentinelTests.StandaloneDomainTablesMustEnforceConcurrencyTokensAndContestableHistory` and `ServiceSchemaPostgresTests.AllStandaloneSchemas_InstallTemporalAndInsertOnlyTriggers` ratchet every standalone `DbContext` model and PostgreSQL schema against regressions.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/database`, `ctms`, `econsent`, `eisf`, `etmf`, `execution`, `fileshare`, `imaging`, `interop`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, and `tickets`.
- **Verification Plan:** Verified via `ArchitectureSentinelTests`, `KernelBoundaryTests`, `MigrationUnitTests`, `StandaloneConcurrencyAndWalletLocksPostgresTests`, `StandaloneTenantIsolationPostgresTests`, and `ServiceSchemaPostgresTests`.
