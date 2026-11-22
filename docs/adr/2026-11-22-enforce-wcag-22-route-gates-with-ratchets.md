# ADR-2822: Enforce WCAG 2.2 AA Route Gates and Accessibility Ratchets

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA contrast, label-in-name, target size, and route-level accessibility gates), PRD-SYS-001 (21 CFR Part 11 regulated clinical workspace quality gates)

---

## 1. Context & Problem Statement

Following the token recalibration in ADR-2820 and overlay primitive migration in ADR-2821, `packages/ui/accessibility-matcher.js` still defaulted to `wcag2a`/`wcag2aa`/`wcag21a`/`wcag21aa` without `wcag22aa`, `packages/ui/tests/shared_components.test.js` only asserted `toBeAccessible` on a subset of shared Vue components, Playwright suites across `apps/web` and `apps/subject-portal` configured `AxeBuilder` inconsistently across views and themes, live routes in `apps/web` were not dynamically gated from the router availability source of truth, and neither pre-release demo route WCAG violations nor `vuejs-accessibility` ESLint disable directives were ratcheted against regression (ADR-0001, ADR-251, ADR-2501, ADR-2534).

## 2. Decision Drivers & Constraints

- **Unified WCAG 2.2 Level AA & SC 1.4.11 Verification (PRD-CRF-015):** Both Vitest component assertions (`toBeAccessible`) and Playwright browser gates (`ui/playwright`) must evaluate `wcag22aa` alongside `wcag2a`, `wcag2aa`, `wcag21a`, and `wcag21aa`, plus >= 3:1 interactive control boundary contrast (SC 1.4.11) in light and dark themes.
- **Dynamic Router Coverage & Live/Demo Tier Ratcheting (PRD-SYS-001):** Every route in `apps/web/src/router/index.js` must be classified via `apps/web/src/router/availability.js`: all 13 live routes and all 7 subject-portal views enforce zero violations in light and dark modes, while all 28 pre-release demo routes are governed by a shrink-only baseline (`ArchitectureBaselines/demo-wcag-violations.baseline.txt`).
- **Zero Unratcheted Accessibility Lint Suppressions:** Unused ESLint disable comments must error immediately, `packages/ui`, `apps/subject-portal`, and live `apps/web` routes must carry zero `vuejs-accessibility` suppressions, and remaining demo-component suppressions must be ratcheted via `ArchitectureBaselines/eslint-a11y-disables.baseline.txt`.

## 3. Options Considered

### Option 1: Shared `ui` WCAG 2.2 Matcher + `ui/playwright` Helper + Dynamic Route & ESLint Ratchets (Selected)

- **Overview:** Add `"wcag22aa"` (`WCAG_22_AA_TAGS`) and non-clobbering custom rule merging to `packages/ui/accessibility-matcher.js`, export `ui/playwright` (`createWcag22AxeBuilder`, `runWcag22Audit`, `auditInteractiveControlBorders`, `getControlBorderContrast`, `formatAccessibilityViolations`), assert `toBeAccessible` across all 20 shared Vue components in `packages/ui`, remediate live `/etmf` H44/G208/C42 and dark-theme token contrast findings, refactor `web-accessibility.spec.ts` and `apps/subject-portal` E2E specs onto `ui/playwright`, and enforce shrink-only baselines for demo WCAG violations and `vuejs-accessibility` disable directives in `FrontendQualityGateService` and `BaselineRatchetService`.
- **Pros:**
  - Enforces zero WCAG 2.2 AA and SC 1.4.11 regressions across all 20 shared UI components, all 13 live clinical routes, and all 7 subject-portal views in both themes.
  - Prevents new routes or unapproved `vuejs-accessibility` suppressions from bypassing CI gates while ratcheting pre-release demo debt downward.
- **Cons:**
  - Requires keeping `packages/ui` built before running downstream `apps/web` and `apps/subject-portal` tests.

### Option 2: Static Hand-Curated Route Lists Without Demo or ESLint Ratchets

- **Overview:** Maintain fixed route arrays inside Playwright specs and rely on manual code review for `vuejs-accessibility` disable comments.
- **Pros:**
  - Lower initial setup effort.
- **Cons:**
  - Allows newly added router paths, dark-theme contrast regressions, and lint suppressions to accumulate silently.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing WCAG 2.2 AA tags and SC 1.4.11 border contrast auditing in `packages/ui` while pairing zero-tolerance live-route gates with shrink-only demo and ESLint ratchets guarantees continuous accessibility compliance without blocking incremental cleanup of pre-release demo surfaces.

## 5. Consequences & Trade-offs

- **Positive Impact:** Complete WCAG 2.2 AA coverage across shared components, live web routes, and subject-portal views in light and dark themes, compact >= 24x24px eTMF tree export targets (SC 2.5.8), explicit H44 filter labels and G208 button names on `/etmf`, and deterministic C# guardrail enforcement in `pnpm cadence check`.
- **Negative Impact / Technical Debt:** Pre-release demo routes retain tracked baseline entries until promoted to live tier.
- **Mitigation Strategy:** Promoting a route from `DEMO_ONLY_ROUTE_NAMES` removes its demo exemption automatically and enforces zero WCAG 2.2 AA violations and zero `vuejs-accessibility` suppressions.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `packages/tooling_core`, `ArchitectureBaselines/`, `eslint.config.mjs`, `docs/adr/`.
- **Verification Plan:** Verified via `pnpm --filter ui test`, `pnpm -r lint`, `pnpm --filter web test:e2e`, `pnpm --filter subject-portal test:e2e`, `dotnet test`, and `pnpm cadence check`.
