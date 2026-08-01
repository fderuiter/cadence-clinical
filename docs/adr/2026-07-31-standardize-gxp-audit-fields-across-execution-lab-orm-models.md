# ADR-143: Standardize GxP Audit Fields Across Execution Lab ORM Models

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In `apps/execution/database/models.py`, `LabReferenceRange` inherited redundant audit field definitions that conflicted with the base `AuditedModel` class, while `LabUnitConversion.version_index` was defined as nullable (`Optional[int]`). 21 CFR Part 11 compliance requires strict version indexing and audit column inheritance across all laboratory master tables.

## 2. Decision Drivers & Constraints

* Align `LabReferenceRange` and `LabUnitConversion` ORM mappings with `AuditedModel`.
* Ensure `version_index` is non-nullable (`Mapped[int] = mapped_column(Integer, default=1, nullable=False)`).
* System requirement compliance: PRD-SYS-001.

## 3. Options Considered

1. **Inheritance Cleanup & Non-Nullable Version Indexing (Selected)**: Rely on `AuditedModel` base inheritance for `LabReferenceRange` and enforce `nullable=False` for `LabUnitConversion.version_index`.
2. Keep duplicated nullable audit fields on individual model classes.

## 4. Decision Outcome

Chosen option 1 to guarantee 21 CFR Part 11 audit field consistency and prevent schema drift.

## 5. Consequences & Trade-offs

* **Positive**: Unified audit field definitions across all laboratory execution models.
* **Positive**: Passed unit and integration tests under `tests/test_lab_master_persistence.py`.
* **Negative**: Requires strict migrations for existing database schemas.

## 6. Implementation & Verification

* Updated `apps/execution/database/models.py`.
* Verified using `uv run pytest tests/test_lab_master_persistence.py` and `python3 scripts/validate_adrs.py`.
