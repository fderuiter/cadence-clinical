# ADR-2834: Tokenized Focus Cascade, Scroll Gutters, and Explicit CSS Transitions

- **Status:** Accepted
- **Date:** 2026-11-27
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015, PRD-SYS-001

---

## 1. Context & Problem Statement

Across `packages/ui/tokens.css`, `packages/ui/src/components`, `apps/web`, and `apps/subject-portal`, keyboard focus visibility (WCAG 2.2 SC 2.4.7, SC 1.4.11, and SC 2.4.13) suffered from four structural CSS cascade defects:

1. **Unparameterized `!important` Focus Rules & Programmatic Alert Suppression:** `packages/ui/tokens.css` declared `!important` on `outline`, `outline-offset`, and `box-shadow` inside the global `:where()` `:focus-visible` and `@media (forced-colors: active)` rules, blocking legitimate component-scoped focus geometry overrides. Simultaneously, `:where([tabindex="-1"]):focus:not(:focus-visible) { outline: none; box-shadow: none; }` suppressed `ModalErrorAlert.vue`'s `.modal-error-alert:focus { outline: 2px solid var(--semantic-color-error); }` outline on programmatic `.focus()`.
2. **Overflow Clipping of Outward `5px` Dual Focus Rings:** The global `3px` outline at `2px` offset (`5px` outward footprint) was clipped inside `overflow: hidden` / `overflow-x: auto` containers including `ClinicalContextBar.vue`, `ClinicalDataTable.vue`, `MediaPreviewModal.vue`, `DocumentGrid.vue`, `TicketsView.vue`, `StudyCanvasView.vue` `.zoom-path ol`, `apps/subject-portal/src/style.css` `ul.nav-tabs`, `AggregateSafetyConsole.vue`, `QualityCockpitView.vue`, `TmfBinderTree.vue`, `KnowledgeView.vue`, and `OncologyCalipers.vue`.
3. **Low-Contrast Brand-Blue Focus Rings on Dark Inverse Surfaces:** `--semantic-color-focus-ring` (`#005a9c` in light mode) achieved `< 3.0:1` contrast when rendered directly on permanently dark viewports and footers (`#0f172a` DICOM MPR canvas, `#1e293b` dark radiology toolbars and participant portal footer, and `#026597` dark brand surfaces).
4. **First-Frame Contrast Suppression via `transition: all`:** Ninety-seven `transition: all` declarations across 55 Vue and CSS files interpolated `outline-color`, `outline-width`, `outline-offset`, and `box-shadow` from `0px` / `transparent` over `150ms`-`300ms`, delaying full `>= 3.0:1` focus indicator visibility on keyboard `Tab` arrival.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA & AAA Focus Appearance (`PRD-CRF-015`):** Every interactive keyboard control must render an unclipped, instantaneous (`0ms` onset) two-color focus indicator achieving `>= 3.0:1` contrast against adjacent surfaces across light, dark, system-auto, inverse-dark, and `forced-colors: active` modes.
- **Zero-Specificity `:where()` Architecture & Token Parity (`ADR-2650`, `ADR-2690`):** Shared focus defaults in `packages/ui/tokens.css` must remain inside `:where()` without `!important`, maintain 100% 1:1 token parity across `:root`, `[data-theme="dark"]`, `.dark`, and `@media (prefers-color-scheme: dark)`, and preserve `AppShell.vue` / `StudyCanvasView.vue` `<= 52px` persistent chrome height and `>= 44x44px` hit targets.
- **Zero Runtime Listeners & Static AST Enforcement (`PRD-SYS-001`):** Focus visibility and transition hygiene must be solved purely in CSS and guarded at lint time via ESLint AST rules without adding runtime JavaScript focus listeners.

## 3. Options Considered

### Option 1: Parameterized `:where()` Focus Custom Properties, `.focus-ring-inset` Utility, `6px` Scroll Gutters, `--semantic-color-focus-ring-inverse`, and `custom-vue/no-transition-all` AST Gate (Selected)

- **Overview:** Remove `!important` from `:focus-visible` and `forced-colors` rules in `packages/ui/tokens.css` and parameterize `--focus-ring-width` (`3px`), `--focus-ring-offset` (`2px`), `--focus-ring-color` (`var(--semantic-color-focus-ring)`), and `--focus-ring-halo-color` (`var(--semantic-color-surface)`); exclude `:not(.modal-error-alert):not([role="alert"])` from `[tabindex="-1"]` programmatic focus suppression; add `.focus-ring-inset` (`outline-offset: -3px`, `inset 0 0 0 5px` halo, `-2px` `Highlight` in `forced-colors: active`) for scroll-bounded tables/grids/strips and `6px` focus gutters (`padding: 6px; margin: -6px`) matching `ClinicalTabs.vue` where outward rings fit; introduce `--semantic-color-focus-ring-inverse: #a5b4fc` (`8.96:1` on `#0f172a`, `7.34:1` on `#1e293b`, `3.18:1` on `#026597`) bound via `--focus-ring-color` on dark chrome containers; replace all 97 `transition: all` rules across 55 files with explicit non-focus property lists; and enforce `custom-vue/no-transition-all` across `.css`, `.vue` `<style>`, and `.vue` `<template>` attributes in `eslint.config.mjs`.
- **Pros:**
  - Restores clean CSS custom-property inheritance under `:where()` without specificity escalation or `!important` wars.
  - Eliminates clipped focus rings across all scroll/overflow containers while preserving `<= 52px` top chrome height and `>= 44x44px` pointer targets.
  - Guarantees instantaneous `t=0ms` focus ring rendering and prevents future `transition: all` regressions via ESLint.
- **Cons:**
  - Requires auditing scroll-bounded widgets to choose between `6px` padding gutters (outward ring) and `.focus-ring-inset` (inward ring on flush cells/buttons).

### Option 2: JavaScript Floating Focus Overlay Portal

- **Overview:** Render a `position: fixed` focus rectangle in `document.body` that tracks `focusin`, `scroll`, and `ResizeObserver` events over the active element.
- **Pros:**
  - Bypasses ancestor `overflow: hidden` clipping without CSS gutter adjustments.
- **Cons:**
  - Introduces runtime scroll/resize listener lag, breaks native `forced-colors: active` `Highlight` system colors, and fails inside nested shadow/canvas transformations.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Parameterizing the `:where()` focus cascade with CSS custom properties, pairing `6px` scroll gutters and `.focus-ring-inset` for overflow containers, calibrating `--semantic-color-focus-ring-inverse` (`#a5b4fc`) for dark clinical viewports, and banning `transition: all` via `custom-vue/no-transition-all` achieves zero-runtime-overhead WCAG 2.2 AA/AAA focus visibility across light, dark, auto, inverse, and `forced-colors` modes.

## 5. Consequences & Trade-offs

- **Positive Impact:** Zero `!important` declarations remain on shared focus indicators; `ModalErrorAlert` preserves its `2px solid var(--semantic-color-error)` outline on programmatic `.focus()`; dark imaging (`DicomMprViewer`, `OncologyCalipers`, `DualReaderAdjudicationPanel`) and participant `.portal-footer` surfaces render `#a5b4fc` (`>= 7.34:1` on slate inverse chrome); zero `transition: all` declarations remain across the repository; and `custom-vue/no-transition-all` blocks regressions in CSS and Vue SFCs.
- **Negative Impact / Technical Debt:** Authors writing new CSS transitions must enumerate explicit animated properties (`background-color`, `border-color`, `color`, `opacity`, `transform`, `box-shadow`) instead of `transition: all`.
- **Mitigation Strategy:** `eslint.config.mjs` emits actionable remediation diagnostics on `custom-vue/no-transition-all`, and Vitest (`packages/ui/tests/assets.test.js`, `ModalErrorAlert.test.js`, `ClinicalDataTable.test.js`, `ClinicalContextBar.spec.ts`) plus Playwright (`web-accessibility.spec.ts`, `portal-accessibility.spec.js`) verify unclipped focus geometry, `t=0ms` contrast, and inverse focus contrast.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `eslint.config.mjs`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm format`, `pnpm lint`, `pnpm typecheck`, `pnpm build`, `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, Playwright accessibility E2E suites (`apps/web/tests/e2e/web-accessibility.spec.ts`, `apps/subject-portal/tests/e2e/portal-accessibility.spec.js`), and `pnpm cadence check --pre-commit`.
