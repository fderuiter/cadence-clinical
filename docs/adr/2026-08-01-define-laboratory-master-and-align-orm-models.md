# ADR-142: Define Laboratory Master and Align ORM Models

- **Status:** Accepted
- **Date:** 2026-08-01
- **Authors:** Jules
- **Deciders:** Cadence Clinical Team

---

## 1. Context & Problem Statement

To ensure standard-compliant laboratory references and unit conversions under Phase 4 of the Cadence Clinical Platform, we must maintain strict database schema and quality model consistency for GxP/21 CFR Part 11 auditing.
The `LabReferenceRange` class had a duplicated block of GxP audit fields which caused structural redundancy and schema mismatch. Additionally, `LabUnitConversion` had its `version_index` field set as nullable, which did not align with the stricter pattern enforced on `LabTestMaster` and `SubjectConsent`.

## 2. Decision Drivers & Constraints

- **GxP & 21 CFR Part 11 Compliance:** The platform demands an immutable audit log and robust history tracking with consistent constraints on fields like `version_index`.
- **Database Schema Coherence:** Eliminating duplication in standard SQLAlchemy tables prevents redundant physical column mapping or session flush discrepancies.
- **Traceability (PRD-LAB-001):** Standardizing laboratory reference models and mapping them to their corresponding verification runs.

## 3. Options Considered

1. **Option A (Selected):** Remove the duplicate GxP audit block on `LabReferenceRange` keeping a single canonical declaration. Align `LabUnitConversion`'s `version_index` constraint to be non-nullable (`nullable=False`) with a default of 1.
2. **Option B (Legacy Fallback):** Retain loose constraints and nullable types across legacy helper classes, risking audit log index gaps.

## 4. Decision Outcome

Chosen option: **Option A** because it enforces GxP audit field consistency across all lab-related catalog tables and guarantees a uniform schema representation.

## 5. Consequences & Trade-offs

- **Positive:** Strict schema model validation prevents runtime database failures or loose constraints during record insertion/migration.
- **Positive:** Consistent audit triggers across all laboratory tables.
- **Negative:** Requires aligning all existing unit test seeds to provide proper `version_index` values when bypassed manually, though handled seamlessly by the model defaults.

## 6. Implementation & Verification

- Target files/packages modified: `apps/execution/database/models.py`.
- Verified by running relevant tests:
  - `tests/test_lab_master_persistence.py`
  - `tests/test_lab_master_migrations.py`
  - `tests/test_lab_ranges.py`
  - `tests/test_lab_reference_range_persistence.py`
