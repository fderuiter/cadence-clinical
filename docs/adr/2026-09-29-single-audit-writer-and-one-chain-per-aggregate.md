# ADR-2446: Single Audit Writer and One Chain per Aggregate Type

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The execution service wrote two audit entries for most changes, on two unrelated chains (#4768):

- `EfAuditLogWriter` added a semantic entry (`ENROLL_SUBJECT`, `RECORD_CONSENT`, ...) chained by table name (`clinical_subjects`).
- `GxPAuditInterceptor` added a second entry for the same change, labelled only with the EF state (`Added`, `Modified`) and chained by CLR type name (`ClinicalSubject`).

A reviewer reading one chain saw either the meaning of a change or its field values, never both. Neither chain was complete, and nothing verified `AuditLogEntry` chains at all: `AuditChainVerifier` in `packages/compliance` verifies a different record type. `02_technical_design.md` §4.4 already called for one interceptor-written entry labelled with the caller's semantic action; this ADR records how that was done.

## 2. Decision Drivers & Constraints

- **One change, one entry (PRD-SYS-053):** an audit trail with duplicate, differently-labelled entries is harder to review and to defend in an inspection.
- **Tamper evidence:** a Merkle chain is only useful if something verifies it.
- **Same transaction:** the audit entry must commit in the same `SaveChangesAsync` as the change.
- **No history rewrite:** entries already written under table-name keys must stay readable and verifiable.
- **Scope:** today's audit path, `Cadence.Database.GxPAuditInterceptor` and the execution service's `IAuditLogWriter`. It does not change the separate `Cadence.Security.Audit.GxPAuditInterceptor`, and it is not the kernel audit model: the kernel `AuditEntry` with one chain per tenant and insert-only tables (#4779) is the target this path migrates onto.

## 3. Options Considered

1. **Keep both writers and link their entries.** Rejected: still two entries and two chains per change.
2. **Make `EfAuditLogWriter` the only writer and drop the interceptor's entries.** Rejected: changes made without an explicit `WriteAsync` call would go unaudited.
3. **Make the interceptor the only writer, and let application code label the next save with a semantic action.** Chosen.

## 4. Decision Outcome

1. **`GxPAuditInterceptor` is the only audit writer.** It writes one `AuditLogEntry` per changed `IGxPAuditable` entity per save, on one chain per `EntityName`, which is the aggregate's CLR type name (`ClinicalSubject`, `SubjectConsent`, `SiteComplianceCache`).
2. **Semantic actions label the entry.** `IAuditLogWriter.WriteAsync` no longer writes a row. `EfAuditLogWriter` records an `AuditAction` against the context through `Cadence.Database.AuditActions`. At save time the interceptor puts the action, actor and reason on the entry for the entity with the same name and id. A change with no recorded action is still audited, with the EF state as its action.
3. **Events with no row change are still audited.** An action with no matching changed entity (`BLOCKED_ENROLLMENT`, `BLOCKED_ACTIVATION`, `SITE_ACTIVATION`) becomes an action-only entry on the same chain, with version index 0 and the caller's details as `ChangesJson`.
4. **Chains are verifiable.** `AuditLogChainVerifier` (`packages/engine_core/src/Audit`) groups entries by `EntityName`, requires exactly one genesis entry, recomputes every block hash, and reports tampering, forks and unreachable entries.
5. **Synchronous saves of audited changes fail.** The interceptor's chain lookup is asynchronous, so `SaveChanges()` with anything to audit throws instead of committing without an audit entry.
6. **Timestamps are hashed as stored.** PostgreSQL keeps timestamps to the microsecond, so a hash over the full 100 ns .NET value never matched the read-back row: no chain written to PostgreSQL could be verified. `AuditLogEntry.NormalizeTimestamp` truncates to whole microseconds in UTC before hashing, and the verifier normalises the same way. Entries written before this fix with a sub-microsecond remainder will report a hash mismatch; there is no production data yet.
7. **Legacy chains stay as they are.** Entries written before this change under table-name keys (`clinical_subjects`, `subject_consents`, `site_compliance_caches`) are not rewritten. They verify as their own closed chains, and new entries start the CLR-name chains.

## 5. Consequences & Trade-offs

- Positive: each change has exactly one entry carrying both its meaning and its field values, and every chain can be verified end to end.
- Positive: the golden-master fixtures now assert CLR entity names, the same names the rest of the platform uses.
- Negative: two concurrent saves of the same aggregate type can both read the same chain tip and fork the chain. The verifier reports this rather than hiding it; serializing chain appends belongs with the kernel's per-tenant chain (spec §8.3.3).
- Negative: the save retry pipeline can replay a save after the interceptor has added its entries. That is part of the concurrency-policy defect (#4766), not changed here.
- Long term, the kernel `AuditEntry` with a per-tenant chain (#4779) supersedes this per-type chain. The single-writer rule and the microsecond timestamp normalisation carry over to it.
- The kernel target model is ADR-2449 (kernel command pipeline, per-tenant audit chain); this path migrates onto it.

## 6. Implementation & Verification

- `packages/database`: `AuditActions`/`AuditAction`, and `GxPAuditInterceptor` as the single writer with the synchronous-save guard.
- `packages/engine_core`: `AuditLogChainVerifier`.
- `apps/execution`: `EfAuditLogWriter` records actions only; callers pass `nameof(...)` entity names.
- Tests: `AuditChainPostgresRoundTripTests` (a chain written to PostgreSQL verifies when read back), `EfAuditLogWriterTests` (one entry per change and a verified chain, unlabelled changes, action-only entries, tampering, forks, legacy chains), `GxPAuditInterceptorMerkleChainTests` (synchronous save), and the golden masters with CLR entity names. PRD-SYS-053.
