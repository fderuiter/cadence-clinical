# ADR-062: PI-only Atomic Batch Electronic Sign-Off in Execution

* **Status:** Accepted
* **Date:** 2026-08-08 (Updated: 2026-08-25)
* **Authors:** @fderuiter
* **Deciders:** @engineering-lead, @quality-compliance

---

## 1. Context & Problem Statement
In clinical trials, the Principal Investigator (PI) needs a deliberate, single re-authentication and signature action to approve eligible, completed form submissions in bulk. However, to comply with 21 CFR Part 11, each approved form submission must maintain its own independent, distinct signature manifest containing unique record binding and version hashes.

This decision implements requirements under **Trace-14**.

---

## 2. Decision Drivers & Constraints
* **GxP/21 CFR Part 11 Compliance:** Immutable per-record electronic signatures, user identity verification, and re-authentication token validation.
* **Database Consistency & Atomicity:** Guaranteeing that the entire batch sign-off succeeds or rolls back in its entirety (no partial approvals on error).
* **Data Locking & Freezing:** Respecting trial, site, visit, subject, and form-level locks during write/mutation actions.
* **Parent Issue Association:** Cross-references development work under **#122 (Batch PI Electronic Sign-Off)**, distinct from eTMF document certificate-signing work (which is handled separately under Trace-13).

---

## Core Architecture & Reconciliation

To address the audit findings and reconcile system behaviors, the batch PI electronic sign-off architecture enforces the following parameters:

### Role Names Consistency
Authorized roles for executing batch sign-offs are strictly restricted to the Principal Investigator (`pi` or `principal investigator`, mapped to `ROLE_PI` or `ROLE_INVESTIGATOR` downstream). Attempting to perform batch sign-off under coordinator, cra, data manager, or any auditor/read-only role is rejected with HTTP 403 Forbidden.

### Target Semantics
The endpoint accepts a flexible target query model defined by a `target_type`:
* **`FORM`**: Direct list of specific `FormSubmission` primary key UUIDs.
* **`VISIT`**: Target visit identifiers (e.g., `VISIT-001`), resolving to all eligible completed form submissions for those visits.
* **`SUBJECT`**: Subject pseudonyms, resolving to all completed form submissions associated with the specified subjects.

Only form submissions currently in the `COMPLETED` status are eligible for sign-off. Draft or already approved submissions are skipped, with appropriate skip markers returned in the REST JSON response payload.

### Token Action & Batch Binding
Re-authentication issues a single JWT `X-Sig-Token`. To strictly bind this token to the exact targets and reason intended by the signer:
1. The client computes a deterministic, colon-delimited binding string:
   `{study_id}:{target_type}:{sorted_target_ids_comma_separated}:{signing_reason}`
2. The SHA-256 hash of this binding string is embedded in the token as the `batch_id` claim.
3. The downstream Execution microservice recalculates this hash from the incoming payload and rejects the signature verification if there is any mismatch (preventing payload interception or target tampering attacks).

### Single-Use Replay Prevention
Every `X-Sig-Token` contains a unique `jti` claim. Upon verification, the token is recorded in an active, in-memory single-use replay cache (pruned automatically post-expiry), strictly preventing token replay attacks. Duplicate requests using the same token are rejected with HTTP 401.

### Lock Check and Atomicity Guarantees
Before committing any approvals, the execution session enforces pessimistic write locking on target tables. If any trial, site, visit, subject, or form lock is detected via the `TrialLockManager`, a `PermissionError` is immediately raised. The transaction executes within a single database transactional context (`session.begin_nested()`), ensuring that any lock breach or validation failure results in an atomic rollback of the entire batch (no partial writes or approvals).

### Manifestation Vocabulary and Record Binding
For every successfully approved submission, the system generates a distinct 21 CFR § 11.50 compliant signature manifestation:
* **Printed Signer Name**: E.g., `Test User` / `Dr. Robert` (retrieved from verified identity).
* **Precise Timestamp**: Precise UTC datetime representation ending with a `Z` suffix.
* **Controlled Reason Code**: Bound strictly to `PI_APPROVAL` ("I approve this clinical record and confirm medical responsibility.").
* **Record & Version Binding**: Specifically binds the unique `record_id` and the newly incremented `record_version` (e.g., transitioning `version` from 1 to 2).
* **Cryptographic Signature**: SHA-256 hash stored under `canonical_signature_hash`.

The generated manifest is written to the updated record's `signature_manifest` JSON column. The change reason and manifestation values are simultaneously written to the immutable `AuditLog` table.

---

## 3. Options Considered
### Option 1: Client-Side Iterative Approvals
* **Overview:** The client-side application loops and calls individual `/approve` endpoints sequentially.
* **Pros:** Reuses existing individual approval endpoints.
* **Cons:**
  * ❌ No batch transaction safety: if a failure or lock occurs halfway, some forms are approved while others remain completed, violating transactional atomicity.
  * ❌ Violates re-authentication requirements: the user would have to enter their password/signature token multiple times, or we would have to reuse the signature token, which violates single-use replay protections.

### Option 2: Server-Side Atomic Batch Sign-Off (Selected)
* **Overview:** A dedicated server-side `/batch-sign-off` endpoint that processes the entire collection in a single transaction block.
* **Pros:**
  * ✅ Absolute transactional safety and database consistency.
  * ✅ Compliant re-authentication: a single valid signature token authorizes the entire batch transaction exactly once.
  * ✅ Distinct, record-bound Part 11 manifests generated and recorded in the audit trail per submission.

---

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Server-side batch sign-off is the only option that satisfies the strict GxP transaction boundaries (atomicity) while fully complying with 21 CFR Part 11 signature re-authentication (single-use token verification) and individual manifest binding constraints.

---

## 5. Consequences & Trade-offs
* **Positive Impact:** Robust transactional sign-offs, zero risk of partial/corrupted batch approval states, clean Part 11 compliance.
* **Negative Impact / Technical Debt:** Requires careful tracking of skipped or ineligible form submissions/targets in the API response.

---

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution/main.py`, `apps/execution/database/models.py`.
* **Verification Plan:** Full automated integration tests written under `tests/test_batch_sign_off.py` verifying atomic rollback under site locks, token replay prevention, PI-only access, and target resolution logic.

---

## Hardened Validation & Execution Evidence (#319)

### Status of Preceding Pull Requests
* **PR #646 (Draft Batch Sign-Off Experimental)**: *Closed-Unmerged*. Contained initial experiments without strict lock rollback and v2 signature binding checks.
* **PR #666 (Alternative Certificate Signing)**: *Closed-Unmerged*. Proposed direct certificate generation at the gateway layer, which violated separation-of-duty boundaries and was rejected.

### Hardened Verification Evidence (Dated 2026-08-25)
The consolidated and fully-merged implementation of **#319** has been successfully verified via a hardened automated test suite under `tests/test_batch_sign_off.py`. 100% of cases are passing and have been qualified under the GxP baseline.

Executed test cases:
1. `test_batch_sign_off_happy_path_form`: Verifies successful batch approval, signature manifestation structure, and version index increments.
2. `test_batch_sign_off_visit_resolution`: Verifies that visit-level target types correctly resolve and sign off on nested forms.
3. `test_batch_sign_off_subject_resolution`: Verifies subject-level target types resolve and sign off.
4. `test_batch_sign_off_pi_only`: Rejects non-PI roles with HTTP 403.
5. `test_batch_sign_off_token_replay`: Rejects token reuse with HTTP 401.
6. `test_batch_sign_off_locks_and_atomic_rollback`: Confirms site/visit locks trigger atomic rollback with no partial approvals.
7. `test_batch_sign_off_mismatched_bindings_and_no_write`: Verifies token binding checks reject modified target lists or reasons, preventing unauthorized writes.
8. `test_batch_sign_off_all_locks`: Confirms trial, visit, subject, and form level locks trigger rollback.
9. `test_batch_sign_off_non_lock_rollback`: Verifies standard application failures roll back state.
10. `test_batch_sign_off_audit_manifestation_capture`: Asserts that `AuditLog` capture correctly binds signature manifestation properties.
