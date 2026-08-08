# ADR-2165: Standardize Clinical Layouts and Align WCAG Compliance

* **Status:** Accepted
* **Date:** 2026-08-08
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Currently, the patient-facing and clinical portals rely on a mix of local style overrides and legacy HTML string layout generators. This fragmented approach has introduced visual misalignment, broken mobile layouts, and compliance vulnerabilities. Specifically, we need to centralize form layouts into shared, token-driven Vue components in our shared UI library (`packages/ui`) to align with requirements under Trace-11.

## 2. Decision Drivers & Constraints

* Ensure interactive targets on mobile screens meet the 48px physical minimum to prevent mis-taps (WCAG compliance, Trace-11).
* Automatically transition clinical and subject portals to a stacked, single-column design on viewports narrower than 1024px.
* Prevent breaking existing end-to-end (E2E) automation pipelines by preserving legacy DOM elements and query/validation selectors.

## 3. Options Considered

1. **Option A (Selected) - Centralized layouts in packages/ui:** Consolidate form layouts and components within `packages/ui/index.js` as clean, reusable, token-driven wrappers and implement standard styles in `packages/ui/responsive.css`.
2. **Option B (Alternative) - Legacy inline generators:** Maintain legacy inline helper functions (`createClinicalInput`, `createClinicalRadioGrid`, etc.) within `apps/subject-portal/index.js` and apply inline styles.

## 4. Decision Outcome

Chosen option: Option A because it satisfies Trace-11, ensures visual consistency across clinical and subject portals, and significantly simplifies visual accessibility testing and auditing.

## 5. Consequences & Trade-offs

* **Positive:** Uniform visual appearance, guaranteed touch target heights, and simplified maintenance of responsive styles.
* **Negative:** Consumed components must reference the centralized UI package, requiring careful coordination during upgrades.

## 6. Implementation & Verification

* Target files modified:
  - `packages/ui/index.js` (centralized form input wrappers)
  - `packages/ui/responsive.css` (enforced minimum 48px touch targets and responsive layouts)
  - `apps/subject-portal/index.js` (integrated centralized input wrappers)
* Verification tests:
  - Subject portal and clinical portal Playwright integration tests run and verify proper layout behaviors.
  - Verification runs with zero layout or accessibility warnings.

