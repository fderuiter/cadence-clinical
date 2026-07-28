# ADR-0001: Hybrid ESLint and In-Memory Accessibility Auditing

* **Status:** Accepted
* **Date:** 2026-07-28
* **Authors:** @jules
* **Deciders:** @engineering-lead, @quality-officer

---

## 1. Context & Problem Statement
Manual compliance checklists are prone to human error, allowing visual inconsistencies, missing ARIA tags, and keyboard navigation issues to pass undetected into production. We need to eliminate manual accessibility regression audits before code changes are merged into production without adding browser testing overhead.

This decision implements requirements under Trace-1.

## 2. Decision Drivers & Constraints
* **Driver 1:** Catch accessibility violations during development automatically.
* **Driver 2:** Run entirely in-memory without starting a headless browser or complex browser automation pipeline.
* **Driver 3:** Minimize the overhead so that execution time of package tests does not increase by more than 15%.

## 3. Options Considered
### Option 1: Browser-based automated accessibility auditing (e.g. Playwright + axe-playwright)
* **Overview:** Starts a browser to render components and runs accessibility checks on the live browser DOM.
* **Pros:**
  * ✅ Real layout and color-contrast calculations are fully evaluated.
* **Cons:**
  * ❌ Requires installing/starting browsers, making the pipeline heavier.
  * ❌ Slower execution times (violates Constraint of <15% overhead).

### Option 2: Hybrid Static ESLint and In-Memory JSDOM/Axe Accessibility Auditing
* **Overview:** Use ESLint with `eslint-plugin-vuejs-accessibility` to statically analyze standard Vue templates during linting/development, combined with in-memory `axe-core` DOM assertions on rendered HTML fragments inside our existing `vitest` unit tests using a JSDOM environment.
* **Pros:**
  * ✅ Extremely fast (runs in under 1 second).
  * ✅ Catches both static syntax violations (missing `for` on labels, missing keyboard events) and rendered DOM violations (such as invalid ARIA labels or element hierarchy).
  * ✅ Runs entirely in-memory in JSDOM, matching our existing test runner configuration.
* **Cons:**
  * ❌ Color contrast cannot be fully calculated as JSDOM does not compute full visual layout.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides immediate feedback to developers both statically (via IDE linting) and dynamically (via the test suite), meeting all speed and lightweight constraints perfectly.

## 5. Consequences & Trade-offs
* **Positive Impact:** Zero manual regression checks required for core accessibility issues. Immediate IDE feedback.
* **Negative Impact / Technical Debt:** Requires disabling page-level axe rules (e.g. document-title, landmark checks) for isolated in-memory component fragments.
* **Mitigation Strategy:** Page-level rules are disabled only in the isolated tests, while component-specific structure is strictly audited.

## 6. Implementation & Verification
* **Affected Repositories / Services:** Root workspace, `packages/ui`
* **Verification Plan:**
  * Static linter checks run using `pnpm lint`
  * Dynamic DOM assertions run using `pnpm test` on `/app/packages/ui/tests/a11y.test.js`
