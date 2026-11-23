# ADR-2646: Standardize 24px Target Tokens and Dual Focus Rings

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015

---

## 1. Context & Problem Statement

Across `packages/ui` and `apps/web`, 32+ stylesheet and component files suppressed browser focus indicators via bare `outline: none` or `outline: 0` rules without a `:focus-visible` replacement (WCAG 2.2 Failure F78), leaving keyboard clinicians either with invisible focus or with `1.15:1` to `2.46:1` focus contrast on dark imaging panels (`#0f172a`, `#090d16`, `#1e293b`), filled brand buttons (`#026597`), and AI suggestion drawers. In parallel, while [ADR-2321](2026-08-03-implement-class-based-touch-target-optimization-for-dense-layouts.md) and [ADR-2526](2026-11-20-idempotent-self-service-participant-expense-claims-and-wallet-disbursements.md) standardized `48px` (`--touch-target-min`) targets on `.clinical-input`, `.touch-target`, and `apps/subject-portal` controls, dense clinical workspaces promoted by [ADR-2643](2026-11-20-wire-web-ctms-training-and-etmf-workspaces-and-signed-completeness-webhooks.md) (`/etmf`, `/training`, `/ctms`, `/ssu`, `/studies/:studyCode`, `/ecrf`, and `/knowledge`) exposed standalone buttons, selects, radios, and checkboxes ranging from `7.8x15px` to `115.64x23px`, falling below WCAG 2.2 Success Criterion 2.5.8 (`24x24` CSS pixels).

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA Target Size (`SC 2.5.8`, Technique C42):** Every standalone non-inline button, input, select, radio control, and checkbox across `packages/ui` and `apps/web` must render at least a `24x24px` pointer target without inflating dense clinical tables to `48px`.
- **Preserve Enhanced Touch Targets & Inline Link Rhythm:** Keep `--touch-target-min: 48px` intact on `.clinical-input`, `.touch-target`, and `apps/subject-portal` controls (`SC 2.5.5`), and exclude inline `<a>` links in prose and breadcrumbs under the `SC 2.5.8` Inline exception.
- **Surface-Agnostic Focus Visibility (`SC 2.4.7`, `SC 1.4.11`, `SC 2.4.11`, Techniques C40 / C45 / G165 / G195):** Focused controls must achieve `>= 3:1` contrast across light (`#ffffff`, `#f8fafc`), brand (`#026597`), and dark (`#1e293b`, `#0f172a`, `#090d16`) surfaces without manual per-surface overrides, avoid container clipping on `overflow: hidden` wrappers, and preserve `Highlight` outlines in `@media (forced-colors: active)`.
- **Automated Static & Runtime Guardrails:** Prevent regression via ESLint AST/CSS enforcement, Vitest relative-luminance assertions, and Playwright `@axe-core/playwright` `target-size` and focus-ring checks.

## 3. Options Considered

### Option 1: Shared 24px Control Target Token, Low-Specificity `:where(...)` Floor, Two-Color `:focus-visible` Ring, and ESLint/Playwright Gates (Selected)

- **Overview:** Define `--control-target-min: 24px` and `--semantic-control-target-min` in `packages/ui/tokens.css`; apply low-specificity `:where(...)` `24x24px` minimums to buttons, inputs, selects, textareas, checkboxes, and radios in `packages/ui/tokens.css` and `apps/web/src/style.css`; update compact shared primitives (`.btn-sm`, `ClinicalRadioGroup`, `AiSuggestionDrawer`, `MediaPlayer`, `ModalErrorAlert`) and workspace overrides; standardize a two-color `:focus-visible` indicator (`2px` `--semantic-color-text-on-solid` `#ffffff` inner halo + `3px` `--semantic-color-accent` outer outline at `outline-offset: 2px`) with a `forced-colors: active` `Highlight` fallback; remove bare `outline: none` rules (scoping programmatic dialog/main containers to `:focus:not(:focus-visible)`); and enforce `custom-vue/no-suppressed-focus-outline` in `eslint.config.mjs` alongside luminance and Playwright `axe-core` tests.
- **Pros:**
  - Delivers `6.34:1` to `19.56:1` focus-indicator contrast across light, brand, and dark surfaces without bespoke dark-panel overrides.
  - Low-specificity `:where(...)` rules protect existing and future controls while allowing `.clinical-input` and `.touch-target` to keep `48px` sizing.
  - Static ESLint checks and Playwright `axe-core` `target-size` audits block regressions at lint and E2E stages.
- **Cons:**
  - Containers that previously set `overflow: hidden` tightly around focusable controls (such as `.ai-diff-box` in `AiSuggestionDrawer.vue`) must allow unclipped focus halos or provide inner padding (`SC 2.4.11`).

### Option 2: Expand `.touch-target` (`48px`) to Every Table Control and Add Manual Dark-Surface Focus Overrides

- **Overview:** Apply `48px` touch targets across all clinical workspace controls and author ad-hoc light focus outlines inside each dark imaging or header component.
- **Pros:**
  - Reuses existing `--touch-target-min` token without introducing a second target tier.
- **Cons:**
  - Doubles row heights in high-density eTMF trees, CTMS matrices, and oncology lesion tables, and leaves future dark surfaces vulnerable to low-contrast outlines.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Pairing `--control-target-min: 24px` (`--semantic-control-target-min`) and low-specificity `:where(...)` defaults with a Technique C40 two-color `:focus-visible` ring (`2px` `#ffffff` inner halo + `3px` `--semantic-color-accent` outer outline) satisfies WCAG 2.2 Level AA (`SC 2.4.7`, `SC 1.4.11`, `SC 2.4.11`, `SC 2.5.8`) across all light, brand, and dark clinical surfaces while preserving compact desktop density and `48px` touch targets where intended.

## 5. Consequences & Trade-offs

- **Positive Impact:** Zero bare `outline: none` / `outline: 0` rules remain across `packages/ui` and `apps/web`; focused controls achieve `>= 6.34:1` contrast across `#ffffff`, `#f8fafc`, `#026597`, `#1e293b`, `#0f172a`, and `#090d16`; Windows Contrast Themes retain native `Highlight` outlines; and all standalone workspace controls meet `>= 24x24px` target sizes.
- **Negative Impact / Technical Debt:** Programmatic focus targets (`tabindex="-1"` dialog panels and main landmarks) must explicitly scope outline suppression to `:focus:not(:focus-visible)` to pass `custom-vue/no-suppressed-focus-outline`.
- **Mitigation Strategy:** The ESLint error message in `eslint.config.mjs` directs authors to the shared `:focus-visible` indicator and `:focus:not(:focus-visible)` pattern for programmatic containers, and `packages/ui/README.md` plus `docs/components/index.md` document the token contract.

## 6. Implementation & Verification

- **Affected Packages / Applications:** `packages/ui`, `apps/web`, root `eslint.config.mjs`, and documentation (`docs/adr/`, `docs/components/index.md`, `packages/ui/README.md`, `docs/DOCUMENTATION_INDEX.md`). `apps/subject-portal` styles from ADR-2526 remain unchanged.
- **Verification Plan:**
  - Relative luminance assertions across `#ffffff`, `#f8fafc`, `#026597`, `#1e293b`, `#0f172a`, and `#090d16` plus synthetic Vue/CSS `custom-vue/no-suppressed-focus-outline` fixture tests in `packages/ui/tests/assets.test.js`.
  - Headless Chromium `@axe-core/playwright` `target-size` audits, `>= 24x24px` bounding-box assertions, and `:focus-visible` dual-ring contrast checks across `/etmf`, `/training`, `/ctms`, `/ssu`, `/studies/CADENCE-101`, `/ecrf`, `/knowledge`, and `/imaging` in `apps/web/tests/e2e/web-accessibility.spec.ts`.
