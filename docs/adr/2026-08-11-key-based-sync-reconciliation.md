# ADR-2167: Key-Based Sync Reconciliation

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In the Subject Portal offline synchronization workflow, mismatch vulnerabilities could occur if server synchronization responses were processed partially or out of order. Standard array index position matching is brittle under network failure scenarios or multi-device submission conditions. To guarantee absolute clinical history integrity under partial or out-of-order server synchronization responses and satisfy PRD-SYS-001, we need a robust reconciliation mechanism based on unique tracking keys.

## 2. Decision Drivers & Constraints

* **Clinical History Integrity (PRD-SYS-001):** High-reliability offline queues must reconcile state deterministically with the backend ledger.
* **Backward Compatibility:** Legacy backend responses without tracking keys must still match chronologically by array index position.
* **IndexedDB Transaction Life Cycle:** Avoid automatic IndexedDB transaction commits by de-coupling asynchronous cryptographic processes from database write transactions.

## 3. Options Considered

1. **Option 1: Unique Key-Based Reconciliation with Chronological Index Fallback (Selected)**
   - **Overview:** Use explicit tracking key parameters (`sequence_number` and `client_id`) passed back from backend endpoints as `offline_sync_markers` to reconcile local queued submissions precisely. If tracking keys are completely absent, fall back to array-index matching.
   - **Pros:** Deterministic state tracking under out-of-order responses, robust omission guardrail preserving unmatched items in a pending state, full backward compatibility.
   - **Cons:** Slightly increased payload size due to sync marker metadata.

2. **Option 2: Pure Chronological Array Index Position Matching**
   - **Overview:** Reconcile status updates assuming the order returned by the server perfectly mirrors the order sent.
   - **Pros:** Simple implementation.
   - **Cons:** Susceptible to out-of-order response matching and partial submission failures, violating clinical safety standards (PRD-SYS-001).

## 4. Decision Outcome

Chosen option: **Option 1** because it perfectly guarantees clinical history integrity, enforces the Omission Guardrail, resolves IndexedDB lifecycle limitations via bulk transaction blocks, and is fully backward compatible with legacy APIs.

## 5. Consequences & Trade-offs

* **Positive:** Perfect out-of-order resilience, batch atomic updates via `bulkUpdateSubmissionStatuses(updates)`, secure cryptographic separation.
* **Negative:** Requires sync marker tracking on both client and server schemas.

## 6. Implementation & Verification

* **Frontend Target Files:**
  - `apps/subject-portal/sync-queue.js` (bulk IndexedDB updates)
  - `apps/subject-portal/index.js` (synchronization reconciliation engine)
* **Backend Target Files:**
  - `apps/execution/domain/offline_models.py`
  - `apps/gateway/domain/acl/ecoa_dto.py`
  - `apps/interop/presentation/routers/interop.py`
* **Verification Tests:**
  - `apps/subject-portal/tests/key-based-reconciliation.test.js`
