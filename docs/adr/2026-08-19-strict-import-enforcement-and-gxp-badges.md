# ADR-113: Strict Import Enforcement and Componentized GxP Badges

* **Status:** Accepted
* **Date:** 2026-08-19
* **Authors:** @jules
* **Deciders:** @jules, @fpderutier

---

## 1. Context & Problem Statement
Developers frequently re-implement local variations of timeout and debounce/delay structures in application views like search inputs and data-entry views. This copy-paste pattern introduces inconsistencies, leaks timer contexts, and risks losing focus state or argument scopes during rapid input in safety-critical clinical views. Similarly, GxP and regulatory compliance badges (e.g., 21 CFR Part 11) have historically been copy-pasted as static HTML strings, leading to layout drift and non-standard markup. To preserve visual uniformity and guarantee audit trail integrity, we need to strictly enforce shared component imports and prevent any local duplication. This addresses requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **Driver 1:** Enforce absolute consistency of critical user experience behaviors (such as data-entry input delays) across clinical modules.
* **Driver 2:** Prevent developer errors from local copy-pasting of low-level debounce timers.
* **Driver 3:** Ensure standard compliance/regulatory visual labels are single-source-of-truth.
* **Driver 4:** Avoid introducing complex bundle dependencies or creating additional workspace packages.

## 3. Options Considered
### Option 1: Manual Review and Code Guidelines
* **Overview:** Rely on pull request reviews and documentation to ensure developers use the shared `ui` library.
* **Pros:** No changes to tooling or build steps.
* **Cons:** Fragile, prone to human oversight, doesn't guarantee build failure upon violations.

### Option 2: Automated Linting Rules and Shared UI Badge/Debounce Primitives
* **Overview:** Block local `debounce` function declarations using ESLint `no-restricted-syntax`, replace local custom timers with a centralized context-preserving `debounce` utility, and render standard GxP badges using standard template definitions exported by packages/ui/index.js.
* **Pros:**
  * ✅ Hard gate via ESLint blocks any unauthorized local debounce definitions.
  * ✅ Standardizes badge markup across Vue headers and dynamic compiled tables.
  * ✅ Preserves focus, runtime scopes, and all calling arguments.
* **Cons:** Requires updating existing custom timer implementations and adding ESLint bypass rules for the shared utility library itself.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Enforces programmatic correctness at build time, meeting critical clinical and regulatory standards (PRD-SYS-001) while resolving focus issues and reducing technical debt.

## 5. Consequences & Trade-offs
* **Positive Impact:** Programmatically guarantees that zero copy-pasted timers can bypass verification. Standardizes regulatory status visuals.
* **Negative Impact / Technical Debt:** Requires a specific rule exclusion in packages/ui/index.js where the actual `debounce` function is declared.
* **Mitigation Strategy:** Use `eslint-disable-next-line` comment carefully targeted to the specific library declaration only.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/ui`, `apps/web`
* **Verification Plan:**
  * Verify eslint check fails if local `debounce` is declared.
  * Verify all unit tests for ui package and web app pass perfectly under vitest.
