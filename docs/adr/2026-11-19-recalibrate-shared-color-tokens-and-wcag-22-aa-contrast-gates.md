# ADR-2817: Recalibrate Shared Color Tokens and WCAG 2.2 AA Contrast Gates

- **Status:** Accepted
- **Date:** 2026-11-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015

---

## 1. Context & Problem Statement

Both the clinical web application (`apps/web`) and participant-facing subject portal (`apps/subject-portal`) consume centralized CSS custom properties from `@cadence/ui` (`packages/ui/tokens.css`). Under `PRD-CRF-015` and [ADR-2388](2026-08-20-targeted-e2e-contrast-enforcement-and-style-token-upgrades.md), Cadence clinical workspaces and portals must satisfy automated accessibility contrast verification. Prior token values (`#64748b` secondary text, `#94a3b8` subtle text, `#2563eb` interactive primary, `#16a34a` / `#10b981` interactive/accent success, `#ca8a04` warning action, `#dc2626` error dark, and `#0284c7` / `#0369a1` wallet gradient) fell below the WCAG 2.2 Success Criterion 1.4.3 (`4.5:1`) contrast floor when rendered over tinted clinical surfaces (`#f8fafc`, `#f1f5f9`, `#eff6ff`) or paired with white button/card labels. In addition, interactive inputs and radio groups shared the passive divider border token (`#e2e8f0`, `1.24:1`), falling below Success Criterion 1.4.11 (`3:1` non-text boundary contrast), while inline hex literals and undefined legacy CSS variable references bypassed lint enforcement in stylesheets and `apps/web` components.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA Compliance:** Deliver `>= 4.5:1` contrast for normal-scale text and status labels across all shared light clinical surfaces (`#ffffff`, `#f8fafc`, `#f1f5f9`, `#eff6ff`) per SC 1.4.3 / Technique G18, `>= 3:1` for large-scale wallet figures per Technique G145, and `>= 3:1` non-text boundary contrast for interactive form controls per SC 1.4.11.
- **Passive Divider Separation:** Preserve low-visual-noise `#e2e8f0` (`--semantic-color-border`) for passive layout dividers and table gridlines while giving interactive inputs, selects, textareas, and radio groups a dedicated control border token.
- **Static & Runtime Enforcement:** Prevent future contrast drift by rejecting hardcoded hex literals and undefined CSS custom properties at lint time and verifying relative luminance ratios and rendered views in CI.

## 3. Options Considered

### Option 1: Recalibrated Shared Semantic Tokens + Dedicated Control Border Token + Automated Contrast Gates (Selected)

- **Overview:** Recalibrate semantic text, interactive, status, AI, and wallet gradient tokens in `packages/ui/tokens.css`, introduce `--color-border-control` / `--semantic-color-border-control` (`#64748b`), migrate `packages/ui`, `apps/subject-portal`, and `apps/web` consumers, promote ESLint hex and undefined-CSS-variable rules to errors across `.vue`, `.css`, and `.js`/`.ts` files, and enforce relative luminance unit tests plus Playwright `axe-core` WCAG 2.2 AA suites.
- **Pros:**
  - Single source of truth in `packages/ui/tokens.css` updates every shared clinical component and consumer view consistently.
  - Separating `--semantic-color-border-control` from `--semantic-color-border` satisfies SC 1.4.11 on controls without heavy visual borders on passive layout dividers.
  - Lint, Vitest luminance assertions, and Playwright `axe-core` gates catch regressions before merge.
- **Cons:**
  - Requires migrating remaining hardcoded hex values and legacy undefined CSS variables across web and subject-portal views.

### Option 2: Per-Component Ad-Hoc Color Overrides Without Lint or Luminance Gates

- **Overview:** Patch individual low-contrast badges and inputs locally in each view without updating `packages/ui/tokens.css` or tightening ESLint rules.
- **Pros:**
  - Smaller initial diff in `packages/ui`.
- **Cons:**
  - Leaves shared tokens non-compliant on `#f1f5f9` and `#eff6ff` surfaces and allows new inline hex colors or undefined CSS variables to reintroduce contrast failures.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing WCAG 2.2 Level AA calibrated semantic tokens and `--semantic-color-border-control` in `packages/ui/tokens.css` paired with strict ESLint, Vitest luminance, and Playwright `axe-core` gates eliminates contrast defects across both applications and prevents regression.

## 5. Consequences & Trade-offs

- **Positive Impact:** All shared semantic text/status pairs achieve `>= 4.5:1` across `#ffffff`, `#f8fafc`, `#f1f5f9`, and `#eff6ff`; interactive controls achieve `>= 3:1` boundary contrast; and undefined CSS custom properties or inline hex colors fail `pnpm lint`.
- **Negative Impact / Technical Debt:** New bespoke colors must be added intentionally to `packages/ui/tokens.css` (and covered by luminance assertions) rather than written inline in components.
- **Mitigation Strategy:** `packages/ui/tokens.css` provides comprehensive surface, status, interactive, inverse, AI, and wallet token tiers documented in `packages/ui/README.md` and `docs/components/index.md`.

## 6. Implementation & Verification

- **Affected Packages / Applications:** `packages/ui`, `apps/subject-portal`, `apps/web`, and root `eslint.config.mjs`.
- **Verification Plan:**
  - Relative luminance contrast ratio assertions and ESLint rule regression tests in `packages/ui/tests/assets.test.js`.
  - Headless Chromium `@axe-core/playwright` WCAG 2.2 Level AA audits in `apps/subject-portal/tests/e2e/portal-accessibility.spec.js` and `apps/web/tests/e2e/web-accessibility.spec.ts` (preserving `.watermark-overlay-container` exclusion per ADR-2388).
