# ADR-251: Centralized Accessibility Matcher and 80 Percent Coverage Gate

* **Status:** Accepted
* **Date:** 2026-08-02
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Prior to this decision, the frontend continuous integration (CI) pipeline did not enforce automated code coverage thresholds. Additionally, the custom `toBeAccessible` testing matcher (which wraps `axe-core`) was coupled to the main web application (`apps/web`). This led to duplicated testing setups and configuration fragmentation in the patient portal (`apps/subject-portal`), increasing the risk of accessibility regressions and lowering overall engineering standards. 

To satisfy clinical, GxP, and WCAG 2.1 compliance requirements across all clinical and patient-facing applications, a centralized testing utility framework is needed along with an enforced **80% code coverage gate** in CI.

## 2. Decision Drivers & Constraints

* **Compliance Requirements:** Ensure absolute WCAG 2.1 compliance across patient-facing (`apps/subject-portal`) and investigator-facing (`apps/web`) interfaces (PRD-SYS-001).
* **Engineering Uniformity:** Standardize on an 80% test coverage gate (statements, branches, functions, lines) globally, aligning the frontend with our existing backend standard.
* **Dry Principle:** Eliminate duplicated code, boilerplate, and dependency overhead in individual applications' testing environments.

## 3. Options Considered

1. **Option A (Selected):** Extract the accessibility testing matcher to a shared package (`packages/ui/accessibility-matcher.js`), expose it through the package entrypoint, and configure both applications to import the centralized helper. Enforce a hard 80% Vitest coverage gate in both workspace apps.
2. **Option B (Alternative):** Maintain independent copies of the `toBeAccessible` matcher and `axe-core` dependencies in each frontend application, relying on manual checklist verification to ensure coverage remains acceptable.

## 4. Decision Outcome

Chosen option: **Option A**. This approach provides a single source of truth for custom assertions and guarantees that code coverage cannot regress below the 80% threshold. It cleanly separates shared component architecture utilities from application-specific code, making our clinical platform modular and robust.

## 5. Consequences & Trade-offs

* **Positive:**
  * Zero-duplication testing setup across all current and future frontend applications.
  * Automated block on PRs if test coverage drops below 80%.
  * Standardized assertions ensure consistent accessibility auditing.
* **Negative:**
  * Developers must write high-quality, comprehensive tests alongside all new UI components, which may slightly increase initial development time but significantly lowers regression costs.

## 6. Implementation & Verification

* **Target files/packages modified:**
  * `packages/ui/accessibility-matcher.js` — Core centralized matcher wrapping `axe-core`.
  * `packages/ui/index.js` — Exposes the matcher globally.
  * `apps/web/tests/setup.js` & `apps/subject-portal/tests/setup.js` — Unified registration of the matcher.
  * `apps/web/vitest.config.js` & `apps/subject-portal/vite.config.js` — Configure Vitest to enforce 80% thresholds.
* **Verification:**
  * Ran local test suite: `pnpm -r test` runs and measures coverage above 80%.
  * Validation checks pass cleanly.

