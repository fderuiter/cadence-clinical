# ADR-061: Reference Range Data Model & Persistence

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Clinical trials require a rigorous and audited persistence structure for reference ranges and clinical observation outcomes to ensure safety, traceability, and regulatory compliance. We need to formalize the database models and the schema evolution process for `LabReferenceRange` and `ClinicalObservation` to support multi-dimensional specificity-based matching.

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **Driver 1 (Traceability & Auditing):** Full GxP and 21 CFR Part 11 compliance requires all mutations to have detailed, immutable audit trails.
* **Driver 2 (Backward Compatibility):** Existing clinical systems and APIs use legacy column names (e.g., `source`, `sex_applicability`), which must remain supported.
* **Driver 3 (Database Evolution):** Schema updates must be performed cleanly pre-boot using dynamic inspection and alter commands without data-loss.

## 3. Options Considered
### Option 1: Dual Column Sets with Synonyms (Selected)
Define actual database columns mapping the new structured schema requirements (`lab_source`, `sex`, `range_low`, `range_high`) while defining standard SQLAlchemy synonyms (`source`, `sex_applicability`, `low_bound`, `high_bound`) pointing to them. Add additive nullable evaluation outcome columns (`range_indicator`, `is_out_of_range`, `reference_range_low`, `reference_range_high`) to `ClinicalObservation`.
* **Pros:**
  * ✅ High compliance with the new target data model layout.
  * ✅ 100% backward compatibility with all existing app code, business logic, and test suites.
  * ✅ Highly performant, direct schema representation.
* **Cons:**
  * ❌ Requires careful handling of serialization in audit trails (auditing physical columns).

### Option 2: Full schema reconstruction and manual data mapping
Recreate the tables from scratch with the new names and discard or manually map existing tables during deployment.
* **Pros:**
  * ✅ Simpler mapping in Python code.
* **Cons:**
  * ❌ Risk of data loss and significant downtime during migration in live clinical environments.

## 4. Decision Outcome
* **Chosen Option:** Option 1
* **Justification:** Option 1 delivers full compatibility with regulatory expectations and existing code while ensuring a robust, zero-downtime, and completely safe upgrade path.

## 5. Consequences & Trade-offs
* **Positive Impact:** All historical and newly ingested reference range limits and evaluation results are physically and cleanly persisted, and fully auditable, while keeping existing code perfectly functional.
* **Negative Impact / Technical Debt:** Audit log entries serialize the physical column names (`range_low` instead of `low_bound`), requiring updates in downstream log-assertion tests.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution/database/models.py`, `apps/execution/database/migrate.py`
* **Verification Plan:** Verified using the comprehensive suite of tests in `tests/test_lab_reference_range_persistence.py` and `tests/test_lab_ranges.py` covering CRUD, precision, schema evolution, and auditable mutations.
