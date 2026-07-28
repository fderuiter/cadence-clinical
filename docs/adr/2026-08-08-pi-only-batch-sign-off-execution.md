# ADR-062: PI-only Atomic Batch Electronic Sign-Off in Execution

* **Status:** Accepted
* **Date:** 2026-08-08
* **Authors:** @jules
* **Deciders:** @engineering-lead, @quality-compliance

---

## 1. Context & Problem Statement
In clinical trials, the Principal Investigator (PI) needs a deliberate, single re-authentication and signature action to approve eligible, completed form submissions in bulk. However, to comply with 21 CFR Part 11, each approved form submission must maintain its own independent, distinct signature manifest containing unique record binding and version hashes.

This decision implements requirements under Trace-7.

## 2. Decision Drivers & Constraints
* **GxP/21 CFR Part 11 Compliance:** Immutable per-record electronic signatures, user identity verification, and re-authentication token validation.
* **Database Consistency & Atomicity:** Guaranteeing that the entire batch sign-off succeeds or rolls back in its entirety (no partial approvals on error).
* **Data Locking & Freezing:** Respecting trial, site, visit, subject, and form-level locks during write/mutation actions.

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
* **Cons:**
  * ❌ Slightly more complex backend target resolution and query processing.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Server-side batch sign-off is the only option that satisfies the strict GxP transaction boundaries (atomicity) while fully complying with 21 CFR Part 11 signature re-authentication (single-use token verification) and individual manifest binding constraints.

## 5. Consequences & Trade-offs
* **Positive Impact:** Robust transactional sign-offs, zero risk of partial/corrupted batch approval states, clean Part 11 compliance.
* **Negative Impact / Technical Debt:** Requires careful tracking of skipped or ineligible form submissions/targets in the API response.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution/main.py`, `apps/execution/database/models.py`.
* **Verification Plan:** Full automated integration tests written under `tests/test_batch_sign_off.py` verifying atomic rollback under site locks, token replay prevention, PI-only access, and target resolution logic.
