# ADR-2831: Stage Encrypted Randomization Slots Before Acquiring the Study Lock

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SUB-001, PRD-SUB-003, PRD-SYS-001, ICH E6(R3) 4.2, 21 CFR 11.10(e)
- **Extends:** [ADR-2523](2026-10-19-explicit-transactions-run-inside-the-execution-strategy.md), [ADR-2548](2026-11-20-standalone-postgres-rls-and-shared-scope-guards.md), [ADR-2640](2026-10-20-randomisation-allocates-from-approved-encrypted-scheme-versions.md)

---

## 1. Context & Problem Statement

Under [ADR-2640](2026-10-20-randomisation-allocates-from-approved-encrypted-scheme-versions.md), approving a permuted-block `RandomizationScheme` (`MaxListSize = 100_000`) acquired the per-study advisory lock (`AdvisoryLockSql.ForRandomization(studyCode)`) first, then generated and AES-256-GCM-encrypted up to 100,000 `RandomizationListSlot` rows one cipher context at a time (`new AesGcm(_key, TagSize)` per slot) and tracked every slot through EF Core `AddRangeAsync` before saving.

At 100,000 slots, holding the study lock across slot encryption and EF Core change tracking stalled concurrent same-study `RandomizeAsync` requests at clinical sites for over `2,000 ms` and allocated `422.14 MiB` of managed memory per approval.

## 2. Decision Drivers & Constraints

- **Site responsiveness (PRD-SUB-003):** approving an amended scheme version must keep study-lock hold time and contended same-study `RandomizeAsync` median latency under `15 ms` even at `MaxListSize = 100_000`.
- **Cryptographic concealment & blinding (ADR-2640):** `IFieldEncryptor` / `IAllocationProtector` must preserve the exact `enc:v1:<keyId>:<base64(nonce ‖ ciphertext ‖ tag)>` wire format, draw a unique 96-bit cryptographic nonce per slot, authenticate `enc:v1:<keyId>:<purpose>`, and remain thread-safe as a singleton.
- **Retryable atomicity & RLS (ADR-2523, ADR-2548):** explicit transactions must execute inside `CadenceTransactions.ExecuteInTransactionAsync` with immutable pre-generated slot instances, enforce `FORCE ROW LEVEL SECURITY` (`tenant_id = kernel.current_tenant_id()`), serialize concurrent sign-off on the same draft scheme, and preserve GxP Merkle audit chains (`PRD-SYS-001`).
- **Synchronous HTTP contract:** `POST /api/v1/execution/randomization/schemes/{schemeId:guid}/approve` continues to return synchronous `200 OK` (`409 Conflict` on duplicate approval).

## 3. Options Considered

### Option 1: Pre-generate batch-encrypted immutable slots, serialize per draft scheme, and bulk-stage untracked rows before locking the study (Selected)

- **Overview:**
  1. Verify draft existence and `RandomizationScheme.CanApprove` before generating slots or opening a transaction.
  2. Add `IFieldEncryptor.EncryptBatch` / `IAllocationProtector.ProtectBatch`, reusing one `AesGcm` cipher context per batch call with stack/pooled buffers while drawing a fresh 96-bit `RandomNumberGenerator` nonce per slot.
  3. Inside `ExecuteInTransactionAsync`, acquire a per-scheme advisory lock (`AdvisoryLockSql.ForRandomizationScheme`), reload `RandomizationScheme`, and re-verify `CanApprove` so concurrent sign-offs on the same draft serialize cleanly (`409 Conflict` for the loser).
  4. Bulk-stage `randomization_list_slots` via chunked PostgreSQL `unnest` (`INSERT INTO randomization_list_slots ... SELECT ... FROM unnest(...)`) with the verified `tenant_id` without adding slot entities to EF Core's `ChangeTracker`.
  5. Acquire `LockStudyAsync` only after all slots are staged, retire any active scheme, approve the target scheme, write `AuditLogEntry` records, and commit.
- **Pros:**
  - ✅ Cuts study-lock hold time from `2,005.81 ms` to `< 10 ms` and contended `RandomizeAsync` median latency to `< 12 ms` at 100,000 slots.
  - ✅ Cuts managed allocations by ~88% (`422.14 MiB` to `~50.24 MiB`) and keeps `GxPAuditInterceptor` and concurrency interceptors scanning only `RandomizationScheme` rows.
  - ✅ Preserves synchronous atomic commit/rollback semantics and strict lock ordering (`LockSchemeAsync` -> `LockStudyAsync`).
- **Cons:**
  - ❌ `RandomizationRepository.AddListSlotsAsync` maintains provider-specific untracked insert paths for Npgsql, relational SQLite unit tests, and EF Core InMemory tests.

### Option 2: Offload list generation to an asynchronous background job (`202 Accepted`)

- **Overview:** Accept scheme approval asynchronously and populate slots in a background worker before flipping scheme status.
- **Pros:**
  - ✅ Short HTTP response latency for the approving statistician.
- **Cons:**
  - ❌ Breaks the synchronous `200 OK` OpenAPI contract on `RandomizationEndpoints` and introduces an intermediate "approving" lifecycle state.

### Option 3: Keep `AddRangeAsync` and `LockStudyAsync` at the start of `ApproveSchemeAsync`

- **Overview:** Retain existing lock scope and EF Core entity tracking.
- **Pros:**
  - ✅ Zero repository SQL changes.
- **Cons:**
  - ❌ Continues to block live participant randomizations for >2 seconds during 100,000-slot scheme approvals.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** Option 1 eliminates multi-second study-lock contention and >85% of managed allocations while preserving synchronous HTTP contracts, AES-256-GCM wire compatibility, PostgreSQL forced RLS, retryable transactions, and GxP audit chain integrity.

## 5. Consequences & Trade-offs

- **Positive Impact:** Participant randomization remains responsive (`< 15 ms` median latency) during large protocol-amendment scheme approvals; concurrent duplicate approvals on a draft scheme deterministically return one `200 OK` and one `409 Conflict` without staging duplicate slots.
- **Negative Impact / Technical Debt:** Untracked SQL slot insertion must stay aligned with the `randomization_list_slots` schema (`Id`, `SchemeId`, `StratumKey`, `Sequence`, `ArmCiphertext`, `AllocationId`, `tenant_id`, `_version`).
- **Mitigation Strategy:** Covered by SQLite retry unit tests, PostgreSQL 18 forced-RLS integration tests (`cadence_app`), and `migrations verify`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security` (`IFieldEncryptor`, `AesGcmFieldEncryptor`, `DesignTimeFieldEncryptor`), `apps/execution` (`IAllocationProtector`, `AesGcmAllocationProtector`, `RandomizationApplicationService`, `IRandomizationRepository`, `RandomizationRepository`, `AdvisoryLockSql`).
- **Verification Plan:**
  - `FieldProtectionTests` verifies `EncryptBatch` round-trip decryption, unique 96-bit nonces per item, associated-data binding, and concurrent singleton thread safety.
  - `RandomizationSchemeTests` verifies pre-lock `CanApprove` short-circuiting, post-`LockSchemeAsync` conflict handling, relational retry idempotency, and zero tracked `RandomizationListSlot` entries during `SaveChangesAsync`.
  - `RandomizationPostgresTests` verifies atomic rollback on post-staging failure, active-scheme retirement, Merkle audit chain verification, and `409 Conflict` under concurrent duplicate approvals.
