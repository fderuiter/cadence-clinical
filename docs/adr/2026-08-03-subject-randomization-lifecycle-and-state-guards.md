# ADR-054: Subject Randomization Lifecycle and State Guards

* **Status:** Accepted
* **Date:** 2026-08-03
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
In clinical trials, once a subject is randomized, their baseline characteristics (stratification factors) must remain completely static to ensure statistical validity and prevent post-hoc bias. Furthermore, state transitions—such as moving from screening to emergency unblinding or withdrawal—must follow a highly regulated, audited path.
To satisfy these requirements, the platform needs a secure, randomization-aware state machine and immutable stratification factor locking in the clinical execution subject model (`ClinicalSubject`).

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance & GxP Standards):** Maintain CDISC USDM, CDISC ODM, and 21 CFR Part 11 compliant audit fields and immutability guards.
* **Driver 2 (Statistical Validity):** Block post-randomization stratification factor mutation.
* **Driver 3 (Traceability):** Standardized, audited transition flow with explicit reasons for critical transitions.

## 3. Options Considered
### Option 1: Client-side Validation Only
* **Overview:** Enforce state transitions and stratification factor locking in UI code or api views.
* **Pros:**
  * ✅ Simplifies database schema and model logic.
* **Cons:**
  * ❌ Susceptible to direct API abuse or scripting database updates bypass.
  * ❌ Does not guarantee database-level GxP compliance.

### Option 2: Database-level Immutability Guards & Central State Machine (Selected)
* **Overview:** Build pure-Python transition guards on the SQLAlchemy model level with custom validators. Enforce a strict state machine (`SubjectState`) and automated `@validates` hooks in the `ClinicalSubject` model.
* **Pros:**
  * ✅ Guaranteed database-level compliance and immutability.
  * ✅ Automatic triggers and model validation catch direct mutations securely.
* **Cons:**
  * ❌ Slightly increases SQL model logic complexity.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Database/model level immutability and centralized validation of state transitions is the only option that strictly satisfies 21 CFR Part 11 and FDA data integrity guidelines. It prevents any accidental or malicious change of baseline stratification factors post-randomization.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Strict transition verification across all clients.
  * Deterministic and un-bypassable locking of critical stratification factors.
* **Negative Impact / Technical Debt:**
  * Explicit state machine transitions are required for all lifecycle updates, adding minor code path overhead.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution/database/models.py`, `apps/execution/subject_lifecycle.py`
* **Verification Plan:**
  * Run unit and integration tests under `tests/test_subject_randomization_lifecycle.py`.
  * Validate with `scripts/validate_adrs.py` to ensure compliant structure.
