# ADR-119: SDV Sign-Off Workflow & Automatic Verification Drop

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @jules
* **Deciders:** @engineering-lead, @quality-officer

---

## 1. Context & Problem Statement
To satisfy Phase 12 of the Source Data Verification (SDV) workflow, the execution service requires robust, CRA-gated sign-off endpoints and an automatic verification drop mechanism when verified data is modified. Specifically, editing `ClinicalObservation` value fields must trigger automatic drops of the verification state and alert clinical research associates (CRAs) through targeted dashboard notifications.

## 2. Decision Drivers & Constraints
* **Driver 1:** 21 CFR Part 11 and GxP regulatory compliance (specifically `PRD-QRY-006` or `Trace-12` data preservation/integrity).
* **Driver 2:** Automated auditing of clinical data changes and immediate alerts to previous verifiers/CRA roles.
* **Driver 3:** Clean Separation of Concerns inside SQLAlchemy session flushes and API endpoints.

## 3. Options Considered
### Option 1: Manual checking in route handlers
* **Overview:** Check each modify route handler for verification drop and GxP reason validation.
* **Pros:**
  * ✅ Direct and straightforward.
* **Cons:**
  * ❌ Easy to bypass via raw SQL queries, bulk updates, or other async tasks.

### Option 2: Database and Session-level Interceptor (Selected)
* **Overview:** Intercept modifications using a SQLAlchemy `before_flush` session event listener (in `apps/execution/database/audit.py`).
* **Pros:**
  * ✅ Guaranteed enforcement—cannot be bypassed by modifying clinical records in any route handler.
  * ✅ Leverages existing robust audit trail system.
* **Cons:**
  * ❌ Increases the complexity of the global session flush listener.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing the check in `receive_before_flush` guarantees GxP compliance on all clinical modifications and ensures data drops and auditing are fully captured atomically.

## 5. Consequences & Trade-offs
* **Positive Impact:** Secure, tamper-proof, and regulatory-compliant automatic drops and notifications.
* **Negative Impact / Technical Debt:** Marginal performance overhead during transaction flush operations.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution`
* **Verification Plan:** Fully verified using unit tests in `tests/test_sdv.py` and `tests/test_sdv_tsdv_persistence.py`.
