# ADR-2655: Standardize Semantic Role Tokens, Dark-Theme Parity, and ESLint AST Color Gates

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA contrast across light, dark, and system-auto themes), PRD-SYS-051 (Design-system token discipline and fail-closed static quality gates)

---

## 1. Context & Problem Statement

Across `packages/ui`, `apps/web`, and `apps/subject-portal`, five token-governance gaps remained after [ADR-2650](2026-11-22-consolidate-shared-ui-css-aliases-and-table-numerals.md) and [ADR-2654](2026-11-23-prune-global-web-css-and-standardize-gxp-status-badges.md):

1. **Interactive Brand Blue Conflated with Body Ink:** Root shell containers (`AppShell.vue`, `StudyCanvasView.vue`), clinical headings, table headers, form labels, and Subject Portal metrics bound `color` to `var(--primary)` / `var(--semantic-color-primary)` (`#026597` light / `#38bdf8` dark) instead of neutral primary ink (`--text-main` / `--semantic-color-text`), painting static clinical typography in interactive brand blue.
2. **Missing Non-AI Workflow & Regulatory Badge Roles:** Non-AI candidate, in-progress, and regulatory badges lacked dedicated paired light/dark role tokens, encouraging ad-hoc tint overrides or tempting misuse of `--semantic-color-ai*` reserved by [ADR-2635](2026-10-20-ai-in-the-canvas-is-labelled-scoped-and-ends-in-a-proposal.md) (UX-8).
3. **Dark & System-Auto Theme Parity and Contrast Gaps:** Thirty-two non-inverse semantic tokens in `:root` lacked `:root[data-theme="dark"]` and `@media (prefers-color-scheme: dark) { :root[data-theme="auto"] }` overrides, while hardcoded `white`, `black`, `rgb()`, `rgba()`, and `hsl()` literals and uncalibrated dark info/danger fills (`packages/ui/index.js` solid danger button, dark `--semantic-color-info-bg-soft` / `--semantic-color-info-light`, and light warning text over solid fills) fell below WCAG 2.2 AA `4.5:1` contrast.
4. **Script & Medical Imaging `<canvas>` Hex Literals:** Vue `<script>` blocks (`DicomMprViewer.vue`, `IcfSectionEditor.vue`, `NotificationsView.vue`) embedded 48 raw hex strings, while HTML5 `<canvas>` 2D drawing contexts cannot consume unresolved `var(--...)` custom property references directly.
5. **Narrow ESLint & Pre-Commit Coverage:** `custom-vue/no-inline-hex` only inspected template/style hex patterns, skipping `<script>` AST literals, `.js`/`.ts` modules, `rgb()`/`rgba()`/`hsl()`/`hsla()` functions, and named `white`/`black` CSS declarations; meanwhile `FrontendQualityGateService.cs` omitted `.css` from `EslintExtensions`, allowing staged `.css` color regressions to bypass `--pre-commit` stdin linting.

## 2. Decision Drivers & Constraints

- **Semantic Ink vs Interactive Brand Separation:** Neutral ink tokens (`--text-main`, `--semantic-color-text`, `--semantic-color-text-dark`, `--semantic-color-text-body`) must render clinical prose, headings, labels, and tabular data, reserving `--primary` / `--semantic-color-primary` strictly for interactive controls, active navigation states, links, and brand pills.
- **1:1 Light, Dark, and System-Auto Parity at WCAG 2.2 AA (`>= 4.5:1` / `>= 3:1`):** Every non-inverse color, badge, backdrop, shadow, and watermark token in `:root` must have explicit dark (`:root[data-theme="dark"], .dark`) and system-auto (`@media (prefers-color-scheme: dark) { :root[data-theme="auto"] }`) counterparts delivering `>= 4.5:1` normal-text contrast and `>= 3:1` control boundary contrast.
- **Concrete Theme Resolution for `<canvas>` 2D Contexts:** Medical imaging viewports (`DicomMprViewer.vue`) must resolve active theme custom properties to concrete hex/RGB values at draw time and re-render on `data-theme` / `prefers-color-scheme` changes without ever passing `var(--...)` into `CanvasRenderingContext2D`.
- **Fail-Closed AST & Pre-Commit Enforcement:** ESLint and `cadence check eslint --pre-commit` must reject any hardcoded color literal (`#hex`, `rgb()`, `rgba()`, `hsl()`, `hsla()`, named `white`/`black` CSS declarations) across `.vue` (`<template>`, `<script>`, `<style>`), `.js`, `.ts`, and `.css` files outside `packages/ui/tokens.css` theme blocks.

## 3. Options Considered

### Option 1: Role-Separated Semantic Tokens, 1:1 Dark/Auto Overlays, Runtime Theme Resolver for `<canvas>`, and Full-Stack ESLint + Pre-Commit `.css` Gates (Selected)

- **Overview:** Decouple neutral ink from `--primary` across `AppShell.vue`, `StudyCanvasView.vue`, clinical workspaces, and `apps/subject-portal`; add paired light/dark/auto `--semantic-badge-{candidate,in-progress,regulatory}-{bg,text,border}` tokens and `.clinical-badge--{candidate,in-progress,regulatory}` modifiers alongside `--semantic-color-error-interactive{,-hover}`, `--semantic-color-text-on-accent`, `--semantic-color-imaging-highlight`, `--semantic-color-backdrop*`, `--semantic-color-surface-glass`, `--semantic-color-inverse-tint*`, and `--semantic-watermark-auditor-pattern`; achieve 1:1 parity across `:root[data-theme="dark"]` and `:root[data-theme="auto"]` for all non-inverse tokens; introduce `resolveThemeToken` / `observeActiveThemeChanges` in `apps/web/src/utils/themeColorResolver.ts` for `DicomMprViewer.vue`; upgrade `custom-vue/no-inline-hex` in `eslint.config.mjs` to check `.vue` templates/scripts/styles, `.js`/`.ts` AST literals, and `.css` rules for `#hex`, `rgb/rgba/hsl/hsla`, and `white`/`black` declarations; add `".css"` to `FrontendQualityGateService.EslintExtensions`; and lock dark-mode contrast in Vitest and Playwright `axe-core` suites.
- **Pros:**
  - Enforces semantic role clarity, eliminates all 32 dark/auto token parity gaps, and guarantees `>= 4.5:1` light and dark contrast across shared UI, clinician views, and the Subject Portal.
  - Provides concrete theme-reactive colors to HTML5 `<canvas>` viewports while eliminating all 48 `<script>` hex literals.
  - Prevents color-literal regressions in `.vue`, `.js`, `.ts`, and `.css` files both in CI and during `cadence check eslint --pre-commit`.
- **Cons:**
  - Translucent backdrops, shadows, and SVG watermarks must be declared as named tokens in `packages/ui/tokens.css` rather than inline `rgba(...)` expressions in component styles.

### Option 2: Restrict Color Linting to CSS Hex Codes and Patch Individual Dark-Mode Views Ad Hoc

- **Overview:** Keep `custom-vue/no-inline-hex` scoped only to `#hex` in templates/styles, leave `rgba()` / `white` / `<script>` literals permitted, and override broken dark-mode selectors per component.
- **Pros:**
  - Fewer token additions in `packages/ui/tokens.css`.
- **Cons:**
  - Leaves `rgba()`/`hsl()`/`white` dark-mode contrast regressions, `<script>` hex drift, unmapped `:root[data-theme="auto"]` tokens, and unstaged/staged `.css` pre-commit bypasses open.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing every color, badge, translucent backdrop, and shadow primitive in `packages/ui/tokens.css` with 1:1 light/dark/auto parity, resolving `<canvas>` colors from the active theme, and enforcing zero color literals across `.vue`, `.js`, `.ts`, and `.css` in both ESLint and `FrontendQualityGateService` eliminates dark-mode contrast regressions structurally.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinician and participant views render prose and metrics in high-contrast neutral ink while reserving brand blue for interactive affordances; non-AI workflow and regulatory badges use dedicated `>= 4.5:1` paired tokens with zero `--semantic-color-ai*` leakage; `DicomMprViewer.vue` dynamically tracks theme switches with concrete colors; and ESLint + `--pre-commit` fail closed on any hardcoded color literal across markup, scripts, and stylesheets.
- **Negative Impact / Technical Debt:** Component authors introducing new translucent overlays or elevation treatments must promote a semantic token into `packages/ui/tokens.css` (across `:root`, `dark`, and `auto` blocks) rather than writing inline `rgba(...)` values.
- **Mitigation Strategy:** `packages/ui/tests/assets.test.js` and `packages/ui/tests/design_system_harmony.test.js` validate 1:1 light/dark/auto parity and `>= 4.5:1` / `>= 3:1` contrast automatically whenever tokens are added.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `packages/usdm-schemas` (`./epro` `zod/mini` subpath keeping staged `/subject-portal/assets/` within the `920 KiB` `public-bundle` budget alongside the expanded light/dark/auto token sheets), `apps/web`, `apps/subject-portal`, `packages/tooling_core`, `tests/Cadence.UnitTests`, `eslint.config.mjs`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm exec eslint --max-warnings 0 .`, `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, Playwright `web-accessibility.spec.ts` and `portal-accessibility.spec.js` (including `data-theme="dark"` and `data-theme="auto"` checks), `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter PreCommitGuardrailsTests`, and `pnpm cadence check` (`adrs`, `markdown`, `readmes`, `eslint`, `prettier`, `deid`, `public-bundle`).
