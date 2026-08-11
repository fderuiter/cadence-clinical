# ADR-118: Chronological Date Validation and Visit Date Constraints in SDTM Models

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To prevent silent clinical data loss and maintain strict compliance with GxP and FDA standards, the clinical dataset transformation pipeline must robustly validate temporal relationships between dates. Specifically:
1. Subject Visit start dates (`SVSTDTC` in `SDTMRecordSV`) must be strictly required, and non-empty.
2. In domains with start and end dates (such as Adverse Events `AE`, Concomitant Medications `CM`, and Subject Visits `SV`), the end date must not chronologically precede the start date if both are provided.
3. Observations must be correctly partitioned between domains (e.g. Adverse Events `AE` must not be misclassified as Demographics `DM`).

This decision establishes how these chronological constraints are model-validated and enforced in our SDTM domain models.

This addresses clinical mapping and validation fidelity under PRD-CRF-006.

## 2. Decision Drivers & Constraints

- **Regulatory Compliance:** Under GxP and 21 CFR Part 11, missing visit start dates or chronological end dates before start dates represent severe validation failures.
- **Traceability:** Maintain correct mapping paths from verbatim CDASH to target SDTM datasets, in accordance with Trace-24 / PRD-CRF-006.
- **Data Integrity:** Prevent invalid dates from persisting in our relational database ledger.

## 3. Options Considered

### Option 1: Database-Level Constraints (Postgres CHECK constraints)

Add CHECK constraints directly to Postgres to enforce date ranges.

- **Pros:**
  - ✅ High reliability at the storage layer.
- **Cons:**
  - ❌ Difficult to support dynamic or flexible schema changes.
  - ❌ Validation fails late (at save time) rather than during API ingestion/mapping validation.

### Option 2: Pydantic Model-Level Validators (Selected)

Add model-level validators (`@model_validator(mode="after")`) to `SDTMRecordAE`, `SDTMRecordCM`, and `SDTMRecordSV`.

- **Pros:**
  - ✅ Fail-fast: Rejects invalid date pairs during the mapping and conversion pipeline before database interaction.
  - ✅ Flexible: Handles standard/partial ISO 8601 strings cleanly.
  - ✅ Reusable: Fully integrated with Pydantic's typing and validation system.
- **Cons:**
  - ❌ Adds slight overhead during parsing (mitigated by highly-optimized string digit comparison).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing Pydantic model-level validators ensures robust, early validation of SDTM chronological dates. It also aligns perfectly with our existing schema definitions in `apps/execution/domain/sdtm/sdtm_models.py`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Invalid chronological ranges (e.g., end dates before start dates) are cleanly caught and rejected with descriptive ValidationError messages. Visit start dates are strictly enforced.
- **Negative Impact / Technical Debt:** Requires keeping string validation clean and robust against various ISO 8601 partial representations.
- **Mitigation Strategy:** Extract digits to compare equivalent-precision strings cleanly without fully parsing incomplete dates as datetime objects.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/execution/domain/sdtm/sdtm_models.py` (Add model validators, enforce `SVSTDTC` required)
  - `apps/execution/services/sdtm_mapper.py` (Refactor mappings and domains)
- **Verification Plan:**
  - Enforce via automated validation scripts (`python3 scripts/validate_adrs.py`).
  - Unit and integration testing via `pytest apps/execution/tests/test_sdtm_mapper.py`.
