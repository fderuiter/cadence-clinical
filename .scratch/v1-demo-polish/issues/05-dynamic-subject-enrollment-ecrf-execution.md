# 05: [Site CRC] Dynamic Subject Enrollment & eCRF Visit Execution with Edit Checks

**What to build:**
A streamlined subject enrollment flow and responsive eCRF visit execution interface (`EcrfView.vue`, `CrcFormRenderer.vue`) supporting dynamic subject schema projection, live edit check rule validation, out-of-range observation alerts, and automatic re-consent gating enforcement.

**Blocked by:**
- 01: [Core/Platform] Multi-Engine CADENCE-101 Hero Study Seeding & Dev Cockpit
- 04: [Patient & Site CRC] eConsent, ICF Builder & 21 CFR Part 11 Signature Capture

**Status:** ready-for-agent

## Context & User Story
As a Site CRC, I want to enroll a new subject (e.g. `SUBJ-101-011`), navigate to their Visit 1 (Screening) eCRF, input vital signs and clinical labs, receive real-time feedback when edit check rules fail (e.g. Systolic BP > 180 mmHg or Diastolic > Systolic), and see data entry blocked if re-consent is pending, so that clinical data is captured accurately and compliantly.

## Acceptance Criteria
- [ ] Subject enrollment modal assigns subject ID, site ID, consent date, and arm.
- [ ] `CrcFormRenderer.vue` renders dynamic forms derived from the subject's active protocol schema version.
- [ ] Edit check engine evaluates rules in real-time as fields change, displaying inline error banners and severity badges (Warning vs. Discrepancy).
- [ ] Saving form submissions persists observations to PostgreSQL with 21 CFR Part 11 audit fields (`created_at`, `created_by`, `reason_for_change`).
- [ ] Attempting to record observations for a subject with pending amendment re-consent triggers an explicit Re-Consent Gate blocking modal.
- [ ] Tests in `apps/execution/tests/` and `apps/web/tests/test_ecrf_renderer.py` pass.
