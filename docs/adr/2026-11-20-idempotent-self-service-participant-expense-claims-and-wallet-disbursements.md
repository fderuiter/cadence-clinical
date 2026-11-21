# ADR-2526: Idempotent Self-Service Participant Expense Claims and Wallet Disbursements

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-PAT-002, PRD-PAY-006, PRD-SYS-182

---

## 1. Context & Problem Statement

Participants submitting travel or meal reimbursement receipts (`POST /api/v1/payments/participant/me/expenses`) or requesting wallet balance payouts (`POST /api/v1/payments/participant/me/disburse`) from mobile networks frequently encounter transient packet loss, slow cellular connections, or repeated button taps (`PRD-PAT-002`, `PRD-PAY-006`, `PRD-SYS-182`). Without persisted server-side idempotency keys scoped to each participant wallet, retrying an in-flight reimbursement claim or balance disbursement could create duplicate `ParticipantExpenseClaim` or `ParticipantDisbursement` ledger records, double-credit auto-approved reimbursements, or double-debit available wallet balances.

## 2. Decision Drivers & Constraints

- **Financial Ledger Integrity:** Replaying a participant expense claim or payout request with the same `Idempotency-Key` must return the original evaluation or disbursement outcome without mutating `ParticipantWallet` balances twice or dispatching duplicate ACH/virtual-card transfers.
- **Per-Wallet Tenant & Caller Isolation ([ADR-2817](2026-11-19-caller-bound-participant-self-service-wallet-and-receipt-ocr-capabilities.md)):** Idempotency keys must be scoped to `(WalletId, IdempotencyKey)` so keys remain isolated per caller-bound participant wallet without cross-participant correlation.
- **Concurrency Safety:** Simultaneous duplicate submissions racing across connections must be guarded by database-level unique constraints and deterministic conflict recovery rather than best-effort in-memory caches.
- **Clinical Kernel Boundary Compliance (`K-XS-1`, `K-TIME-3`):** Public DTOs in `Cadence.Apps.Payments` must avoid raw `StudyId`/`SiteId` value-type drift and derive timestamps exclusively via `IClock`.

## 3. Options Considered

### Option 1: Persisted `IdempotencyKey` Columns with Unique `(WalletId, IdempotencyKey)` Indexes (Selected)

- **Overview:** Persist nullable `IdempotencyKey` (`varchar(255)`) on `ParticipantExpenseClaim` and `ParticipantDisbursement` with unique composite indexes on `(WalletId, IdempotencyKey)`, accept `Idempotency-Key` via HTTP header or request payload, short-circuit matching replays prior to mutation, and recover cleanly from concurrent `DbUpdateException` unique-index races. Pair with client-side per-draft `Idempotency-Key` persistence and post-success form reset in `apps/subject-portal`.
- **Pros:**
  - Guarantees exactly-once ledger mutation across restarts, horizontal replicas, and concurrent retries.
  - Preserves full 21 CFR Part 11 audit trail fidelity without emitting duplicate disbursement events on replay.
  - Backward-compatible for callers omitting `Idempotency-Key` (`NULL` keys are not constrained against each other in PostgreSQL unique indexes).
- **Cons:**
  - Requires an EF Core schema migration (`AddSelfServiceIdempotencyKeys`) on `payments_participant_expense_claims` and `payments_participant_disbursements`.

### Option 2: Ephemeral In-Memory / Distributed Cache Deduplication Window

- **Overview:** Store recent `Idempotency-Key` responses in process memory or short-TTL cache without persisting the key on the financial ledger entities.
- **Pros:**
  - Avoids an EF Core schema migration.
- **Cons:**
  - Loses deduplication guarantees across process restarts or cache evictions, risking duplicate regulated payouts.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Persisting `IdempotencyKey` directly on `ParticipantExpenseClaim` and `ParticipantDisbursement` with database-enforced `(WalletId, IdempotencyKey)` uniqueness provides durable, restart-safe financial idempotency aligned with [ADR-2817](2026-11-19-caller-bound-participant-self-service-wallet-and-receipt-ocr-capabilities.md) caller isolation.

## 5. Consequences & Trade-offs

- **Positive Impact:** Mobile participants can safely retry receipt claims and confirmed `DIRECT_ACH` or `VIRTUAL_CARD` payouts over unreliable connections with zero duplicate ledger mutations.
- **Negative Impact / Technical Debt:** Adds two indexed `varchar(255)` columns to the Payments persistence schema.
- **Mitigation Strategy:** Synchronized across `docs/schema/payments.md` and `docs/openapi/payments_openapi.json` and verified by sentinel drift gates.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/payments` (`PaymentEntities.cs`, `PaymentConfigurations.cs`, `PaymentEndpoints.cs`, migration `20261121050828_AddSelfServiceIdempotencyKeys`), `apps/subject-portal` (`features/wallet.js`, `views/WalletView.vue`), `docs/openapi/payments_openapi.json`, and `docs/schema/payments.md`.
- **Verification Plan:** Automated xUnit idempotency replay and wallet isolation tests in `tests/Cadence.UnitTests/Payments/ParticipantSelfServiceAndOcrTests.cs`, Vitest unit tests in `apps/subject-portal/tests/expense-ocr-autodisburse.test.js`, and Playwright E2E verification in `apps/subject-portal/tests/e2e/wallet-recovery-and-touch-targets.spec.js`.
