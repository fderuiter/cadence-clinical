# ADR-130: In-Memory JSDOM Environment for Localized UI Accessibility Testing

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @jules
* **Deciders:** @engineering-lead, @quality-officer

---

## 1. Context & Problem Statement

To prevent regressions and ensure compliance with accessibility standards (WCAG 2.1 AA) across our shared UI component library, we need an automated, robust verification mechanism. Manual visual audits are time-consuming and error-prone. While we statically analyze templates using ESLint plugins, dynamic behavior and rendered DOM trees are not validated. Therefore, we need an in-memory DOM environment to mount and execute accessibility audits programmatically on synthesized markup without browser-automation overhead, satisfying requirements under Trace-1.

## 2. Decision Drivers & Constraints

* **Trace-1 Integration:** Direct compliance mapping to Trace-1 for automated accessibility verification of clinical platform components.
* **In-Memory Speed:** The suite must run extremely fast and locally within existing Vitest unit tests, avoiding the need to spin up and manage headless browsers.
* **Component-Level Scope:** Ability to isolate HTML fragments and bypass whole-page rules (such as page language or landmark requirements) to prevent false positives.

## 3. Options Considered

1. **Option A (Selected):** Add `jsdom` to the shared workspace package as a dynamic, in-memory testing environment, combining it with `axe-core` assertions inside Vitest.
2. **Option B (Alternative):** Run end-to-end browser-based accessibility tests via Playwright for all component variants, which is slower, heavier, and increases CI pipeline time substantially.

## 4. Decision Outcome

Chosen option: Option A. Integrating `jsdom` allows us to run localized `axe-core` checks directly inside Vitest unit/integration tests (`@vitest-environment jsdom`). This provides high fidelity DOM rendering inside Node.js while keeping verification time under a second, satisfying Trace-1 efficiently.

## 5. Consequences & Trade-offs

* **Positive:**
  * Fast feedback loop for developers during local development.
  * Component-level accessibility checking prevents regression of aria attributes and keyboard controls.
  * Fully integrated into the existing Vitest configuration.
* **Negative:**
  * Colors and layout-dependent calculations (like overlapping elements or true contrast) are not fully evaluated by JSDOM. This is mitigated by manual checks on final builds.

## 6. Implementation & Verification

* **Modified packages:** Added `jsdom` dependency to root `package.json` and updated workspace lockfiles.
* **Verification tests:** Created localized verification tests under `packages/ui/tests/accessibility.test.js` covering normalizer helpers and digital signing layouts.
* **Validation:** Verified successfully by running local tests and running `python3 scripts/validate_adrs.py`.
