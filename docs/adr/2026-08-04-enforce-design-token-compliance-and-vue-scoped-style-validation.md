# ADR-256: Enforce design token compliance and Vue scoped style validation

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Currently, the Cadence Clinical Platform relies on raw CSS and Vue single-file components (SFCs) without automated structural styling validation. While Prettier ensures consistent code formatting, it cannot detect semantic violations, invalid design token usage, hardcoded layout patterns, or layout-breaking viewport styles. This gap introduces the risk of design system drift and visual regressions across clinical and patient-facing web portals.

To address this, we need to enforce that 100% of stylesheets and Vue single-file components adhere to designated design tokens, prevent UI regressions, and maintain WCAG 2.5.5 touch target accessibility. Traced to system requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints

* Ensure strict adherence to centralized design system tokens for colors, spacing, and typography.
* Enforce mobile-first responsive media query scales (preferring `min-width` over layout-breaking `max-width` rules).
* Satisfy WCAG 2.5.5 touch target size requirements (minimum interactive heights of 44px) to ensure accessibility across mobile devices.
* Minimize performance overhead under continuous integration, keeping the check execution time under 1.5 seconds.
* Satisfy GxP traceability requirements (PRD-SYS-001).

## 3. Options Considered

1. **Option A (Custom Python-based AST and Regex Validator):** Build a lightweight, high-performance style scanner using Python regex and BeautifulSoup to parse Vue SFC blocks.
2. **Option B (Third-party Stylelint with custom JavaScript plugins):** Integrate Stylelint and write complex AST rule plugins in JavaScript.

## 4. Decision Outcome

Chosen option: **Option A (Custom Python-based AST and Regex Validator)** because it executes in under 1.5 seconds, requires zero extra Node.js devDependencies, and allows immediate integration into the pre-existing Python tooling suite. This approach perfectly satisfies GxP trace PRD-SYS-001 by enforcing strict layout and design token safety during CI checks.

### Key Aspects:
* Color guardrails: Block direct hex codes, rgb/rgba, HSL, and standard CSS literals. Require `var(--color-*)`.
* Spacing guardrails: Enforce spacing tokens (`var(--spacing-*)`) for paddings, margins, gaps, and relative positioning.
* Touch target minimums: Enforce minimum interactive heights of 44px (using `var(--touch-target-min)`).
* Media queries: Validate mobile-first media query definitions.

## 5. Consequences & Trade-offs

* **Positive:** Strict style and accessibility validation blocks design drift early.
* **Positive:** Fast execution time (<1.5s) preserves the 15-second CI pipeline budget.
* **Negative:** Developers must write compliant CSS and use design tokens instead of raw values, slightly increasing initial development overhead but vastly improving maintainability.

## 6. Implementation & Verification

* Added `scripts/validate_styles.py` and `scripts/auto_fix_styles.py` to automate style linting.
* Integrated the validator into the root `package.json` under `"lint"` and `"check"` scripts.
* Verified that all UI packages and applications pass validation cleanly.
