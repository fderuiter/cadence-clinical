# ADR-138: Vue SPA Component Smoke Test and Vitest Pipeline Integration

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As Cadence Clinical transitions to a modern Vue 3 SPA architecture, we require automated smoke tests and Vitest configuration setup to ensure all foundational components mount cleanly without runtime failures or regressions.

This decision addresses the following requirements:
* **PRD-SYS-001**: System reliability, automated CI testing, and UI component quality gates.

## 2. Decision Drivers & Constraints

* **Vue 3 SPA Testing:** Unit/integration tests for Vue components require JSDOM environment setup and `@vue/test-utils` integration.
* **Vitest Multi-Project Resolution:** Configuration paths (`setupFiles`) must resolve paths reliably across workspace root and package subdirectories.

## 3. Options Considered

1. **JSDOM-based Component Smoke Testing in Vitest (Selected):** Configure `apps/web/vitest.config.js` with `fileURLToPath` setup resolution and add `trivial_component_smoke.test.js`.
2. **Manual Component Verification:** Rely on manual browser checks. Rejected due to lack of automated CI enforcement.

## 4. Decision Outcome

We choose **Option 1 (JSDOM-based Component Smoke Testing in Vitest)**.
* **Configuration:** Update `apps/web/vitest.config.js` and `apps/web/vite.config.js` with absolute `setupFiles` resolution.
* **Smoke Tests:** Add `apps/web/tests/trivial_component_smoke.test.js`.

## 5. Consequences & Trade-offs

* **Positive:**
  * Automated JSDOM component rendering tests run cleanly in <4 seconds.
* **Negative:**
  * Requires JSDOM polyfills for window objects in `apps/web/tests/setup.js`.

## 6. Implementation & Verification

* **Implementation Files:**
  * `apps/web/tests/trivial_component_smoke.test.js`
  * `apps/web/vitest.config.js`
  * `apps/web/vite.config.js`
* **Verification Tests:**
  * `(cd apps/web && npx -y vitest --run)`
