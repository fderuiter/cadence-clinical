# ADR-2185: Dynamic Subject Enrollment and eCRF Visit Execution with Real Time Edit Checks

* **Status:** Accepted
* **Date:** 2026-08-19
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

# ADR-2185: Dynamic Subject Enrollment and eCRF Visit Execution with Real Time Edit Checks

* **Status:** Accepted
* **Date:** 2026-08-19
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In clinical electronic data capture (EDC) systems under 21 CFR Part 11 and GxP standards, subject enrollment, eCRF form visit data entry, and edit check rule evaluations must be performed dynamically across evolving protocol versions. When a new subject is enrolled (assigning subject ID, site ID, consent date, and study arm), clinical visit forms must dynamically project the fields appropriate for their active protocol version (e.g. v1.0.0 baseline vs. v2.0.0 amended biomarkers and laboratory panels). In addition, clinical boundary rules must be evaluated in real time (e.g. Diastolic BP >= Systolic BP, Systolic BP > 180 mmHg), displaying inline error banners and severity badges (`Warning` vs. `Discrepancy`), while persisting observations to PostgreSQL with complete 21 CFR Part 11 audit fields (`created_at`, `created_by`, `reason_for_change`, `version_index`). When a protocol amendment requires re-consent, data entry must be explicitly blocked until re-consent is completed. This addresses requirements PRD-SYS-001, PRD-SUB-007, and PRD-EDC-005.

## 2. Decision Drivers & Constraints

* **GxP and 21 CFR Part 11 Compliance:** Complete audit trail tracking on all observation and form submission writes (`created_at`, `created_by`, `reason_for_change`, `version_index`).
* **Dynamic Protocol Version Projection:** Dynamic schema projection of eCRF fields based on the subject's active protocol version tag and version index.
* **Real-Time Edit Check Rule Evaluation:** Real-time evaluation of clinical boundary rules and cross-field constraints with distinct severity badges (`Warning` vs. `Discrepancy`) and direct query creation.
* **Re-Consent Gating Enforcement:** Explicit blocking modal preventing data capture for subjects requiring amendment re-consent.

## 3. Options Considered

1. **Option 1: Dynamic Client & Server Schema Projection with Write-Time Observation Persistence (Selected)**
   - Maintain protocol-version-aware dynamic projection in `CrcFormRenderer.vue` and `useClinicalStore`.
   - Implement real-time edit check evaluation engine with Warning and Discrepancy badges.
   - Automatically persist individual `ClinicalObservation` records with 21 CFR Part 11 audit fields during `FormSubmission` creation in `apps/execution/main.py`.
   - Enforce explicit Re-Consent Gate blocking modal on the web client alongside the database flush listener.
2. **Option 2: Static Form Schemas with Separate Observation Ingestion Endpoints**
   - Require separate API calls for form submissions and individual observations without client-side dynamic projection.
   - Disadvantage: Disconnected audit histories, slower CRC workflows, and risk of inconsistency between eCRF form submissions and clinical observations.

## 4. Decision Outcome

**Chosen Option:** Option 1. We implemented dynamic subject enrollment, protocol-version-aware form rendering, real-time edit check validation with Warning and Discrepancy badges, 21 CFR Part 11 compliant observation persistence, and explicit re-consent gating.

## 5. Consequences & Trade-offs

* **Positive:** Real-time feedback for site CRCs, robust GxP compliance, seamless protocol version migration, and immutable audit logs.
* **Negative:** Requires ongoing synchronisation of clinical rules between client and server edit check engines.

## 6. Implementation & Verification

* **Affected Repositories / Files:**
  - `apps/execution/main.py`: Subject creation schemas and form submission observation persistence with audit logging.
  - `apps/web/src/stores/clinical.ts`: `enrollSubject` action, `getEcrfFieldsForVersion`, and ledger tracking.
  - `apps/web/src/components/persona/CrcFormRenderer.vue`: Subject enrollment modal, dynamic form rendering, live edit checks with Warning vs. Discrepancy badges, and Re-Consent Gate blocking modal.
  - `apps/web/tests/test_ecrf_renderer.py`: Web contract and integration tests.
  - `apps/web/tests/components/CrcFormRenderer.spec.ts`: Component unit tests.
* **Verification Tests:** Verified via `uv run pytest apps/web/tests/test_ecrf_renderer.py -o addopts=""` and `pnpm --filter web test`.
