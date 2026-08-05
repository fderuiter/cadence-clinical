# ADR-140: Clinical Code Lookup Input Helper in Shared UI Package

- **Status:** Accepted
- **Date:** 2026-07-31
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Phase 19 terminology integration and eCRF consolidation require standard accessible HTML rendering for clinical concept code lookups with real-time feedback indicators (loading, valid, invalid, degraded). Standardizing `createClinicalLookupInput` in `packages/ui/index.js` ensures consistent UX and ARIA accessibility across eCRF rendering.

## 2. Decision Drivers & Constraints

- Ensure accessible `role="status"` and `aria-live="polite"` feedback for screen readers.
- Consolidate debounced lookup state management across eCRF field components.
- System requirement compliance: PRD-SYS-001.

## 3. Options Considered

1. **Shared Package UI Helper Export (Selected)**: Export `createClinicalLookupInput` from `packages/ui/index.js` with Vitest suite.
2. Ad-hoc template strings in individual Vue views without shared utility functions.

## 4. Decision Outcome

Chosen option 1 because exporting `createClinicalLookupInput` from `@cadence/ui` promotes reuse, reduces duplicate layout code, and enforces standard clinical field styling.

## 5. Consequences & Trade-offs

- **Positive**: Shared implementation covered by unit tests in `packages/ui/tests/index.test.js`.
- **Positive**: Consistent status icons (`⏳`, `✅`, `❌`, `⚠️`) and ARIA live regions.
- **Negative**: Requires export maintenance within `@cadence/ui`.

## 6. Implementation & Verification

- Modified `packages/ui/index.js` and `packages/ui/tests/index.test.js`.
- Updated `apps/web/src/views/EcrfView.vue` to leverage shared debouncing and lookup helpers.
