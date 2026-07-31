# ADR-[NUMBER]: Reference Range Data Model and Persistence Updates

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical trials necessitate robust data modeling and database persistence schemas for laboratory reference ranges and range-evaluation outcomes. To satisfy backward compatibility while ensuring proper schema design, physical column layouts on `LabReferenceRange` must use explicit field names like `lab_source`, `sex`, `range_low`, `range_high` while exposing synonym properties (`source`, `sex_applicability`, `low_bound`, `high_bound`). Additionally, `ClinicalObservation` must be extended incrementally with nullable outcome fields (`range_indicator`, `is_out_of_range`, `reference_range_low`, `reference_range_high`) via database migrations.

This decision maps to compliance requirements under `PRD-QRY-005`.

## 2. Decision Drivers & Constraints

* **Driver 1 (Backward Compatibility):** Ensure older API clients can read and write to `LabReferenceRange` using the legacy properties `source`, `sex_applicability`, `low_bound`, `high_bound` while utilizing standard SQL columns.
* **Driver 2 (Incremental Evolution):** Enable seamless, zero-downtime database upgrades on existing production/testing SQLite and PostgreSQL databases without dropping data.

## 3. Options Considered

### Option 1: Map Physical Columns with SQLAlchemy Synonyms (Selected)
Directly map the physical database columns (`lab_source`, `sex`, `range_low`, `range_high`) on `LabReferenceRange`, while using `sqlalchemy.orm.synonym` to define backwards-compatible properties. Upgrade `ClinicalObservation` with additive columns via `migrate.py`.
* **Pros:**
  * ✅ Full backward compatibility for existing application code and tests.
  * ✅ Clean physical database schema aligned with standards.
  * ✅ Automatically picked up by the pre-boot database migration engine.
* **Cons:**
  * ❌ Requires managing synonyms explicitly in the SQLAlchemy model layer.

### Option 2: Retain Old Column Names and Use Views/Adapters
Keep physical column names legacy and write mapping adaptors in the API layer.
* **Pros:**
  * ✅ No changes needed in the relational schema.
* **Cons:**
  * ❌ Schema remains unclean and diverges from expected platform-wide naming conventions.

## 4. Decision Outcome

* **Chosen Option:** Option 1
* **Justification:** Mapping physical columns with synonyms delivers a highly compliant, clean, and perfectly backwards-compatible solution that runs on top of standard pre-boot migration infrastructure.

## 5. Consequences & Trade-offs

* **Positive Impact:** Cleaner physical schemas with seamless backward compatibility.
* **Negative Impact / Technical Debt:** Added synonyms require keeping the mapping declarations intact.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `apps/execution/database/models.py`, `apps/execution/database/migrate.py`, `tests/test_lab_reference_range_persistence.py`
* **Verification Plan:** Verified using the focused persistence test suite in `tests/test_lab_reference_range_persistence.py` to assert correct insertion, querying, version-index increments, and GxP compliant audit trail generation.
