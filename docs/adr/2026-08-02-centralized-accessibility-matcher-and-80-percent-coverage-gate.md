# ADR-251: Centralized Accessibility Matcher and 80 Percent Coverage Gate

- **Status:** Accepted
- **Date:** 2026-08-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Maintaining strict Web Content Accessibility Guidelines (WCAG) 2.1 Compliance is a fundamental business requirement for patient-facing (eCOA/ePRO) clinical software platforms. Previously, our custom `toBeAccessible` testing matcher (which wraps `axe-core`) was coupled tightly to the main clinical dashboard web application (`apps/web`). This structure led to duplicate testing configurations, fragmented implementations in the patient portal (`apps/subject-portal`), and lack of automated enforcement gates.

Additionally, our frontend continuous integration (CI) pipelines lacked enforced code coverage thresholds, introducing risks of regression and code quality fragmentation. We need a clean, unified engineering pattern to centralize testing utilities and enforce an 80% code coverage threshold in CI to guarantee quality assurance and accessibility compliance under system requirement PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Maintainability & DRY Principle:** Testing assertions should not be copy-pasted across isolated frontend applications.
- **WCAG 2.1 Compliance Enforcement:** Guardrails must ensure that both professional portal and patient portal widgets are strictly audited for screen-readers, contrast ratio, and layout (PRD-SYS-001).
- **Standardized Code Quality:** Aligning frontend test suites with the existing 80% coverage standard enforced on the backend.
- **Developer Velocity:** Centralized matchers must be easily importable with zero boilerplate.

## 3. Options Considered

### Option 1: Fragmented Testing Setups (Status Quo)

Keep custom testing setup files separately configured inside each application workspace, running independent coverage checks without rigid gating rules.

- **Pros:**
  - ✅ Quick setup for individual applications.
- **Cons:**
  - ❌ Severe duplicate configurations across `apps/web` and `apps/subject-portal`.
  - ❌ Lack of uniform accessibility criteria or global rulesets.
  - ❌ No automated mechanism blocks pull requests with low-quality or untested code.

### Option 2: Shared Testing Package with Enforced Gating (Selected)

Extract custom Jest/Vitest matchers (e.g. `toBeAccessible`) to a shared workspace package (`packages/ui`), expose them globally, and configure rigid Vitest coverage threshold gates requiring 80% coverage for lines, functions, branches, and statements.

- **Pros:**
  - ✅ Perfect architectural separation of concerns (dry utility package).
  - ✅ Guarantees 80% test coverage gate is automatically executed in CI.
  - ✅ Allows easy global extension of WCAG rules across all current and future portals.
- **Cons:**
  - ❌ Minor build config orchestration required via `pnpm` workspaces.

## 4. Decision Outcome

**Chosen Option:** Option 2 (Option A).
By packaging the custom `toBeAccessible` matcher within `packages/ui` and distributing it as a shared dependency, we eliminate configuration fragmentation. Incorporating a mandatory Vitest 80% coverage gate guarantees that all newly authored components and routes are thoroughly validated, keeping our platform compliant with PRD-SYS-001.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Standardized accessibility testing setup across all client modules.
  - Fail-closed gates in CI block any contributions failing to maintain coverage or containing accessibility violations.
- **Negative Impact / Technical Debt:**
  - Workspace packages have slight compile/resolve overhead during developer bootstrap.
- **Mitigation Strategy:**
  - Pre-configured scripts automate bootstrapping via `pnpm` workspace hooks.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/ui`: Centralized the `toBeAccessible` matcher and updated public entrypoints (`packages/ui/accessibility-matcher.js`, `packages/ui/index.js`).
  - `apps/web`: Replaced local matcher setup with shared package imports in `apps/web/tests/setup.js` and `apps/web/vitest.config.js`.
  - `apps/subject-portal`: Standardized configuration in `apps/subject-portal/tests/setup.js` and `apps/subject-portal/vite.config.js` to import `toBeAccessible` from `@cadence/ui`.
  - `package.json` & individual config files: Configured the Vitest coverage thresholds.
- **Verification Plan:**
  - Running `pnpm run test` executes unified unit tests verifying coverage meets the 80% statement, branch, and function thresholds.
  - Running `uv run python scripts/validate_adrs.py` validates the format, requirements mapping, and chronology of this ADR.
