# ADR-KERNEL-20261128-synchronous-query-projection-milestone-lock-readiness-and-asymmetric-database-lock-certificates: Synchronous Query Projection, Milestone Lock Readiness, and Asymmetric Database Lock Certificates

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Database freeze and final database lock are GxP-critical clinical milestones (21 CFR Part 11, ICH E6(R3), PRD-SYS-007, PRD-SYS-008, PRD-SYS-011). Prior to this decision, five architectural gaps weakened fail-closed lock guarantees across the Clinical Kernel and downstream services:

1. **Asynchronous query projection lag:** `execution` form query commands (`RaiseFormQuery`, `AnswerFormQuery`, `RecordSubmittedObservation` auto-answer, `ReopenFormQuery`, `CloseFormQuery`, `CancelFormQuery`) projected `AssertionKind.Queried` and `AssertionKind.QueryAnswered` into `kernel.assertions` only asynchronously via `MirrorFormQueryToKernel`, leaving a window where `LockReadinessEvaluator` could observe zero open queries immediately after a query was raised.
2. **Tenant-wide outbox head-of-line blocking:** `KernelOutboxRelay` ordered and blocked all outbox messages per `tenant_id`, so one malformed or failing non-bootstrap event stalled unrelated aggregate streams across the tenant.
3. **Single readiness profile and missing Source Data Review (`Reviewed`):** `LockReadinessEvaluator` evaluated one readiness gate set (`InterimCut`) without distinguishing final study closeout (`FinalDatabaseLock`) or verifying `AssertionKind.Reviewed` coverage over recorded observations.
4. **Symmetric lock governance and unsigned cross-service unblinding:** Trial onboarding granted both `lock.apply` and `lock.release` to the same bootstrap role without dual-signer or covering-scope enforcement, and `apps/interop` SCE unblinded execution could not verify an asymmetric kernel-issued `DatabaseLockCertificate` offline.
5. **Unreplicated scope locks in `apps/econsent`:** Standalone `apps/econsent` did not replicate kernel scope locks, allowing consent capture (and single-use signature/liveness token consumption) or template publication to proceed against locked scopes.

## 2. Decision Drivers & Constraints

- **Zero-lag query readiness (PRD-SYS-007):** Raising, answering, auto-answering, reopening, closing, or cancelling a form query must update `kernel.assertions` before returning HTTP `200`/`201`, while keeping outbox replay idempotent.
- **Stream-isolated outbox progress:** Regular domain events must preserve ordering per `(tenant_id, stream_key)` while quarantining terminal poison messages without stalling sibling aggregates, and `TenantProvisioned` must remain a tenant-bootstrap barrier.
- **Milestone-specific closeout & SDR (PRD-SYS-008):** `FinalDatabaseLock` must enforce all `InterimCut` gates plus zero `Screening` enrollments, zero `NotYetOpen` visits on `Enrolled` participants, zero unsigned `NotAsked` forms, and 100% `AssertionKind.Reviewed` coverage on current observations.
- **Segregated dual-signer governance & offline asymmetric verification (PRD-SYS-011):** `lock.apply` (`TRIAL_DATA_MANAGER`) and `lock.release` (`SPONSOR_ADMIN`) must be segregated, self-release (`K.Assertion.SelfReleaseDenied`) and descendant release under an active ancestor lock (`K.Assertion.CoveredByParentLock`) must fail closed, and study-level `Locked` assertions must emit an Ed25519/ECDSA-signed `DatabaseLockCertificate` verified offline in `apps/interop` SCE using only public verification keys.
- **Fail-closed standalone replication:** `apps/econsent` must maintain an RLS-enforced `replicated_scope_locks` projection and reject consent capture (`423`) before consuming single-use step-up or biometric liveness tokens.

## 3. Options Considered

### Option 1: Synchronous Kernel Command Projection, Per-Stream Outbox Quarantine, Asymmetric Lock Certificates, and Replicated Scope Locks (Selected)

- **Overview:** Project form query lifecycle transitions synchronously within the `execution` command unit of work via `IFormQueryKernelProjector`, partition `KernelOutboxRelay` ordering by `(tenant_id, stream_key)` with `kernel.outbox_quarantine_messages`, add `ReviewObservation` (`observation.review`) and `LockMilestone` (`InterimCut` vs `FinalDatabaseLock`), enforce covering-scope hierarchy and segregated signer roles, sign study-level `DatabaseLockCertificate` payloads via `Cadence.Security.Crypto.AsymmetricSigner` with atomic revocation on `ReleaseLock`, and replicate `KernelScopeLockApplied`/`KernelScopeLockReleased` into `econsent.replicated_scope_locks`.
- **Pros:**
  - Eliminates read-after-write lag between form query mutations and `LockReadinessEvaluator`.
  - Isolates poison outbox messages per aggregate stream while preserving `TenantProvisioned` bootstrap ordering and `FORCE ROW LEVEL SECURITY` on quarantine rows.
  - Enforces 21 CFR Part 11 role binding, dual-signer release, and private-key isolation (`CADENCE_DB_LOCK_CERT_PRIVATE_KEY_PEM` stays in `kernel-api`; `apps/interop` holds only `CADENCE_DB_LOCK_CERT_PUBLIC_KEYS_PEM`).
  - Protects single-use signature and biometric liveness tokens in `apps/econsent` when a study, site, or participant scope is locked.
- **Cons:**
  - Adds schema migrations across `kernel` (`outbox_quarantine_messages`, `database_lock_certificates`), `interop` (`replicated_study_locks`, certificate columns on `sce_database_lock_tokens`), and `econsent` (`replicated_scope_locks`).

### Option 2: Polling Cross-Service HTTP Lock Checks and Eventual Outbox Projection

- **Overview:** Keep query assertion projection asynchronous in the outbox consumer and have `apps/interop` and `apps/econsent` call `kernel-api` synchronously over HTTP during SCE execution and consent capture.
- **Pros:**
  - Fewer replicated tables in standalone services.
- **Cons:**
  - Fails closed poorly under transient network partitions, leaves asynchronous race windows between query creation and lock application, and couples standalone service latency and availability to `kernel-api`.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 satisfies PRD-SYS-007, PRD-SYS-008, and PRD-SYS-011 with deterministic transaction boundaries, asymmetric cryptographic proof of study lock for unblinded SCE analyses, and tenant-isolated local lock enforcement in standalone services.

## 5. Consequences & Trade-offs

- **Positive Impact:** Study lock readiness reflects query and source-data-review state immediately; poison outbox payloads no longer stall unrelated study or participant streams; unblinded SCE tokens and runs require cryptographic proof of an unrevoked study `DatabaseLockCertificate`; locked scopes reject eConsent mutations with HTTP `423` prior to token consumption.
- **Negative Impact / Technical Debt:** Replicated lock tables in `interop` and `econsent` remain necessary until those standalone scaffold services migrate into kernel modules (#4787).
- **Mitigation Strategy:** Shared `KernelScopeLockApplied` and `KernelScopeLockReleased` contracts in `Cadence.Contracts`, idempotent upserts/releases keyed by `(tenant_id, lock_assertion_id)`, and PostgreSQL integration coverage across `Kernel`, `Execution`, `Interop`, `Database`, and `Econsent`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`, `packages/contracts`, `packages/ui`, `apps/kernel-api`, `apps/execution`, `apps/interop`, `apps/econsent`, `apps/web`, `docs/ENVIRONMENT_VARIABLES.md`, `.env.example`.
- **Verification Plan:** Verified via `Cadence.ArchitectureTests`, `Cadence.Kernel.ConsumerTests`, `Cadence.UnitTests` (`Kernel`, `Execution`, `Interop`, `Econsent`, `Security`), `Cadence.IntegrationTests` on PostgreSQL 18 (`FORCE ROW LEVEL SECURITY` and cross-tenant isolation), Vitest (`packages/ui`, `apps/web`), and Playwright WCAG 2.2 AA accessibility suites.
