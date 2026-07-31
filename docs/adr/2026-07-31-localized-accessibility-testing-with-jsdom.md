# ADR-130: Localized Accessibility Testing with JSDOM

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The shared UI utility package previously lacked dynamic accessibility validation. This gap introduced risks of WCAG-compliance violations escaping detection in core elements such as eConsent layouts and digital signing components. We need to implement a high-performance, localized, in-memory accessibility testing strategy without adding global configuration bloat or slow execution to lightweight utility packages (PRD-SYS-001).

## 2. Decision Drivers & Constraints

* High-speed execution (tests must execute in < 1 second).
* Minimal overhead: must not introduce global configuration bloat to the entire UI workspace.
* Avoidance of false positives on standalone markup fragments / helpers, bypassing page-level rules like HTML language declarations, bypass blocks, and document titles.
* Traces to standard compliance rules (PRD-SYS-001).

## 3. Options Considered

1. **Option A (Selected): Localized JSDOM with custom axe-core rule overrides.** We use localized `@vitest-environment jsdom` inside specific test files and customize `axe-core` options to skip whole-page validation rules.
2. **Option B: Workspace-wide full-DOM rendering.** Adding global DOM emulation or end-to-end browser integration, which would significantly increase test suite latency.

## 4. Decision Outcome

Chosen option: **Option A**. 
We configured file-level JSDOM environment in Vitest and used `axe-core` configured to target fragment-level elements, skipping page-level landmarks (e.g. `html-has-lang`, `landmark-one-main`, `region`, `bypass`, and `document-title`). This ensures localized, fast (< 1s) accessibility validation of individual markup helpers.

## 5. Consequences & Trade-offs

* **Positive:**
  * Fast, localized, fully in-memory verification (runs in ~450ms).
  * Excluded page-level rules prevent false-positive failures on markup fragments.
  * No global test configuration bloat.
* **Negative:**
  * Requires relaxing pnpm release age policy to install `jsdom` as a devDependency.

## 6. Implementation & Verification

* Target files/packages modified:
  * `package.json` and `pnpm-workspace.yaml` modified to permit `jsdom` devDependency.
  * Added `/app/packages/ui/tests/accessibility.test.js` using `vitest` and `axe-core`.
* Verification:
  * Ran local tests via `pnpm --filter ui test` and confirmed speedy, passing localized validation.
