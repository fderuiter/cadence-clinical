# ADR-065: Medical Coding Engine and Query Subsystem Integration

* **Status:** Accepted
* **Date:** 2026-08-09
* **Authors:** @jules
* **Deciders:** @fderuiter, @lead-architect

---

## 1. Context & Problem Statement
When matching uncodable verbatim clinical terms (e.g. adverse events, concomitant medications, or medical history with confidence below 0.60), the Medical Coding Engine needs to close the loop by automatically generating traceable EDC queries. These queries must identify the affected clinical field without exposing unrelated patient/subject demographics or PII. Additionally:
1. Creating assignments and query generation must occur in a Part 11 compliant audit context.
2. Generating these system coding queries must be completely idempotent for a given unresolved assignment to prevent duplicate open queries on the same target coordinate.
3. Manual coding actions (like accept or override) must resolve and close the original system coding queries.
4. Conversely, resolving or cancelling a system query from the EDC side must return the coding assignment back to the coding review loop.

## 2. Decision Drivers & Constraints
* **GxP & 21 CFR Part 11 Compliance:** All mutations must be captured in the audit history.
* **Security & Privacy (HIPAA/GDPR):** Query content must identify the uncodable field and verbatim context without leaking subject demographics or PII.
* **Operational Efficiency:** Prevent duplicate open queries on the same field coordinate, and automatically synchronize manual resolutions and query cancellations.

## 3. Options Considered
### Option 1: Standalone Coding Query Handler
* **Overview:** Build a separate service to manage coding queries, with periodic polling to sync query state with coding assignments.
* **Pros:**
  * ✅ High isolation.
* **Cons:**
  * ❌ Complex synchronization and higher risk of state drift.
  * ❌ Lacks transactional safety across database updates and query changes.

### Option 2: Transactional Inline Integration in Clinical Execution Service (Selected)
* **Overview:** Integrate query and assignment tracking directly inside the transactional operations of the clinical execution service (`create_observation`, `process_coding_action`, and query state updates).
* **Pros:**
  * ✅ Atomic, single-transaction guarantees for assignment creation, query generation, and ledger updates.
  * ✅ Easy implementation of robust, database-level checks for duplicate open queries on coordinates.
  * ✅ Native use of the database shadow triggers for automatic, zero-overhead Part 11 auditing.
* **Cons:**
  * ❌ Adds logical coupling between the coding matcher and the query service within `apps/execution/main.py`.

---

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Chosen Option 2 because atomic database transactions are critical for GxP data integrity. This guarantees that an uncodable verbatim term *always* has its assignment set to `QUERY_PENDING` and its query raised in a single operation, with no risk of drift.

---

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Perfect transactional consistency and real-time synchronization.
  * Automatic coverage by database-trigger audit logs.
  * No risk of duplicate open system queries.
* **Negative Impact / Technical Debt:**
  * Increased logical complexity in `create_observation` and `process_coding_action`.

---

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  - `apps/execution/database/models.py` (added `form_id`, `field_id`, `query_type`, `action_required` to `ClinicalQuery`)
  - `apps/execution/main.py` (updated query response mapping, uncodable matching query creation, manual override auto-closure, and query cancel/close assignment status reversion)
* **Verification Plan:**
  - Validated by unit/integration tests in `tests/test_system_coding_queries.py`.
  - Executed tests using `pytest` ensuring 100% success rate.
