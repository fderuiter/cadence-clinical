# ADR-2816: eCRF Field-Level Revision Audit Trail History UI Components and API Integration

- **Status:** Accepted
- **Date:** 2026-10-12
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Under 21 CFR Part 11 and GxP standards (PRD-SYS-001), clinical trial electronic Case Report Forms (eCRFs) must maintain an immutable, attributable, and auditable history of all field-level value changes. Clinical users (CRCs, CRAs, and Data Managers) require the ability to inspect the revision history of individual form fields directly within the eCRF UI—including previous values, modification timestamps, user identities, reasons for change, and Merkle block cryptographic verification hashes.

## 2. Decision Drivers & Constraints

- **Driver 1:** 21 CFR Part 11 & GxP Compliance: Immutable, attributable field-level audit trail visual inspection (visual diffs, change reasons, user IDs, ISO timestamps, and Merkle block/predecessor hashes).
- **Driver 2:** Accessible and Reusable UI Components: `ClinicalFieldAuditFlag.vue` and `EcrfFieldAuditModal.vue` exported from `packages/ui` with proper ARIA attributes (`aria-label`, `aria-controls`, `aria-expanded`, keyboard shortcuts).
- **Driver 3:** Clean Integration with `CrcFormRenderer.vue` and `executionApi.ts` without regressions in form rendering performance.

## 3. Options Considered

### Option 1: Reusable Shared UI Audit Flag and Modal Dialog Components (Selected)

- **Overview:**
  - Expose API client method `getFieldAuditHistory(formId, fieldId)` in `apps/web/src/api/executionApi.ts`.
  - Create reusable `ClinicalFieldAuditFlag.vue` and `EcrfFieldAuditModal.vue` components in `packages/ui` and export them from package root.
  - Wire `ClinicalFieldLayout.vue` and form field components (`ClinicalInput`, `ClinicalRadioGroup`, `ClinicalLookupInput`, `ClinicalFormField`) to emit `@open-audit`.
  - Handle field audit inspection in `CrcFormRenderer.vue` to fetch revision entries and render the modal dialog.
- **Pros:**
  - ✅ Reusable across all eCRF rendering contexts and clinical persona views.
  - ✅ Strict 21 CFR Part 11 and GxP compliance with visual diffs and Merkle proof presentation.
  - ✅ Fully accessible and covered by automated Vitest suite.
- **Cons:**
  - ❌ Requires event forwarding through form field wrapper components.

### Option 2: Inline Modal and Fetch Logic directly in CrcFormRenderer

- **Overview:** Embed audit flag buttons and modal markup directly in `CrcFormRenderer.vue` without reusable component abstractions.
- **Pros:**
  - ✅ Less component layering.
- **Cons:**
  - ❌ Duplicated code across eCRF renderers, poor modularity, and harder UI component unit testing.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 enforces clean separation of concerns, enables UI reuse across eClinical applications, and provides complete 21 CFR Part 11 audit trail inspection.

## 5. Consequences & Trade-offs

- **Positive Impact:** Standardized field-level audit trail visibility across eCRFs, compliant with 21 CFR Part 11 GxP requirements.
- **Negative Impact / Technical Debt:** Requires `packages/ui` rebuild when component contracts change.
- **Mitigation Strategy:** Re-building `packages/ui` via build step and automated test suite coverage.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/web`, `packages/ui`, `apps/execution`
- **Verification Plan:**
  - Executed Vitest unit test suite `apps/web/tests/components/ClinicalFieldAuditComponents.spec.ts`.
  - Verified quality gates using `pnpm cadence check`.
