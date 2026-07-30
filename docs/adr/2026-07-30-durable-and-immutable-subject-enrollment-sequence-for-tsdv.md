# ADR-107: Durable and Immutable Subject Enrollment Sequence for TSDV

* **Status:** Accepted
* **Date:** 2026-07-30
* **Authors:** Jules
* **Deciders:** Jules

---

## 1. Context & Problem Statement

Under Targeted Source Data Verification (TSDV), selecting the "first N enrolled subjects" for 100% full SDV has historically relied on lexical ordering of the `subject_id` (alphabetical sort). However, lexical sort is unstable when new subjects with lexicographically earlier identifiers are subsequently added, which can alter historical first-N decisions and violate GxP traceability rules (PRD-QRY-007). We need a durable, immutable, study-scoped enrollment sequence assigned at subject creation to serve as the stable source of truth for first-N sampling.

## 2. Decision Drivers & Constraints

* **GxP 21 CFR Part 11 Compliance (PRD-QRY-007):** Historical decisions must be immutable. Later enrollments must not affect previous sampling decisions.
* **Deterministic Backfill:** Pre-existing subjects must be backfilled deterministically, leveraging the audit trail timestamp when available, with a stable alphabetical fallback if necessary.
* **API Invariants:** The `evaluate_tsdv_rule` endpoint must resolve subject enrollment index via the persisted index rather than lexical IDs, and explicitly reject conflicting caller-provided indices with HTTP 400.

## 3. Options Considered

1. **Option A (Selected): Dedicated Persisted column `enrollment_index` on `ClinicalSubject`**
   Add a nullable integer column `enrollment_index` to the `ClinicalSubject` table. The index is assigned dynamically during the creation transaction as `max(enrollment_index) + 1` for the study scope, making it permanent, immutable, and independent of alphabetical names or ID formats.
2. **Option B (Alternative): Dynamic query from audit logs on every evaluation**
   Dynamically fetch the subject's first insert event timestamp from `audit_logs` on every evaluation to compute the rank. This avoids schema changes but is highly non-performant and depends entirely on audit logs remaining present and untouched, which risks instability.

## 4. Decision Outcome

Chosen option: **Option A** because it ensures a high-performance, direct read of the immutable enrollment index, guarantees GxP stability for first-N sampling, and fully decouples subject names from the sampling logic to satisfy PRD-QRY-007.

## 5. Consequences & Trade-offs

* **Positive:**
  * ✅ High performance: No additional joins or aggregate queries on audit logs during evaluation.
  * ✅ Absolute stability: Once written, a subject's enrollment index never changes.
  * ✅ Clean validation: The API can easily check and reject mismatched caller-provided parameters.
* **Negative:**
  * ❌ Database Schema Migration: Requires modifying the `clinical_subjects` table and backfilling legacy entries.

## 6. Implementation & Verification

* **Target files:**
  * `apps/execution/database/models.py` (added `enrollment_index` column)
  * `apps/execution/database/migrate.py` (added schema migration & deterministic audit-timestamp-sorted backfill)
  * `apps/execution/main.py` (updated `create_subject` to compute sequence, and `evaluate_tsdv_rule` to validate and enforce the persisted index)
* **Verification tests:**
  * `tests/test_tsdv.py` (unit and integration coverage verifying non-lexical IDs, subsequent enrollments, and validation)
