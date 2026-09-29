# ADR-2448: Reject Concurrency Conflicts, Retry Only Transient Failures

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The execution service's save pipeline caught `DbUpdateConcurrencyException`, reloaded the row's database values into the entity's original values, and replayed the save up to three times (#4766). That "client wins" resolution meant that when two people edited the same subject, form, observation or query, the second save silently overwrote the first. The first change was lost, nobody was told, and the audit trail recorded the overwrite as an ordinary update. `02_technical_design.md` §4.2 documented this as intended.

## 2. Decision Drivers & Constraints

- **No silent overwrite (PRD-SUB-001, PRD-SYS-053):** a correction to clinical data must never disappear because another request saved later.
- **Stable client contract:** a caller that loses a race needs a distinct, machine-readable signal so it can reload and re-apply.
- **Resilience:** genuinely transient database failures (serialization failures, deadlocks, dropped connections) should still be retried.
- **Kernel direction:** K-FACT-1 (corrections supersede, never overwrite) and K-TX-4 (per-aggregate locking).

## 3. Options Considered

1. **Keep "client wins".** Rejected: it is the defect.
2. **"Database wins": reload and discard the caller's change.** Rejected: still silent, just in the other direction.
3. **Reject the conflicting save with 409, and retry only transient failures.** Chosen.
4. **Pessimistic row locks for every edit.** Rejected for now: a larger change that belongs with the kernel's per-aggregate locking.

## 4. Decision Outcome

1. **Conflicts are rejected.** `ExecutionRepositoryBase.SaveChangesAsync` rethrows `DbUpdateConcurrencyException` as `ConcurrencyConflictException`, naming the conflicting entity types. `ConcurrencyConflictExceptionHandler` returns 409 Conflict ProblemDetails with `code: CONCURRENCY_CONFLICT`, which `DomainErrorHttpMapping` also pins to 409.
2. **Only transient failures are retried.** `SaveChangesResiliencePipelineFactory` retries up to three times, with backoff, when the exception chain contains an `NpgsqlException` that Npgsql marks transient. A concurrency conflict is never retried.
3. **No retry inside an explicit transaction.** A failed statement aborts a PostgreSQL transaction, so a save inside one (enrollment's advisory-locked transaction) gets a single attempt.
4. **A retried save is audited once.** `GxPAuditInterceptor` detaches the audit entries a failed attempt added and restores its semantic actions, and computes `VersionIndex` from the loaded value, so a retry neither duplicates entries nor double-counts versions.

## 5. Consequences & Trade-offs

- Positive: a lost race is visible to the caller, the winning change is kept, and the audit trail records only changes that happened.
- Negative: callers must handle 409 by reloading. The eTMF webhook's sender must retry a conflicting delivery.
- Conflicts are detected only between a request's own read and save. Detecting edits made against a stale copy across user think time needs the client to send the version it read (for example `If-Match`), which is left to the kernel command pipeline.
- A transient failure during COMMIT is ambiguous. A retried insert that had in fact committed fails on its primary key rather than duplicating the row.

## 6. Implementation & Verification

- `apps/execution`: `ConcurrencyConflictException`, `ConcurrencyConflictExceptionHandler`, `ExecutionRepositoryBase`, `SaveChangesResiliencePipelineFactory`.
- `packages/database`: `GxPAuditInterceptor` failure rollback and idempotent version stamping; `AuditActions.Restore`.
- Tests: `ExecutionRepositoryBaseTests` (stale write rejected and the other writer's change kept; two concurrent form updates give one success, one conflict and one audited change; a transient failure is retried with each audit entry written once; the pipeline never retries a conflict) and `ConcurrencyConflictExceptionHandlerTests` (409 with the stable code). PRD-SUB-001, PRD-SYS-053.
