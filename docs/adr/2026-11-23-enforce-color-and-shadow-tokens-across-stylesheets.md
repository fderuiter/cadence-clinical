# ADR-2652: Enforce Shared Color and Shadow Tokens Across Stylesheets, Scripts, VitePress, and CLI Gates

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA contrast and centralized clinical design system token discipline), PRD-SYS-001 (21 CFR Part 11 regulated UI determinism)

---

## 1. Context & Problem Statement

Although [ADR-2820](2026-11-19-recalibrate-shared-color-tokens-and-wcag-22-aa-contrast-gates.md) calibrated `packages/ui/tokens.css` for WCAG 2.2 Level AA contrast and banned raw `#hex` color literals via `custom-vue/no-inline-hex`, four enforcement and token-coverage gaps allowed untokenized colors and heavy elevation shadows to accumulate across the frontend workspace:

1. **Color Function & Named-Color Lint Bypass:** `custom-vue/no-inline-hex` in `eslint.config.mjs` scanned only `#hex` strings inside `.css` files and Vue `<style>` / `<template>` blocks, allowing 374 CSS `rgb()`/`rgba()` literals, 37 `<template>` color functions, and 74 `white` declarations across 84+ stylesheets and Vue components to bypass dark-theme and contrast governance.
2. **Script, `<canvas>`, and VitePress Theme Coverage Gaps:** ESLint omitted Vue `<script>` blocks, JS/TS source modules (including 25 `#hex`/`rgba()` HTML5 `<canvas>` literals in `DicomMprViewer.vue`, badge style strings in `NotificationsView.vue`, and HTML table builder strings in `IcfSectionEditor.vue`), and `docs/.vitepress/theme/custom.css`.
3. **Untokenized Heavy Drop Shadows:** Over 210 `box-shadow` declarations applied ad-hoc multi-layer `rgba()` drop shadows instead of crisp `1px` borders or centralized `--shadow-*` / `--semantic-shadow-*` elevation tokens.
4. **Pre-Commit CLI Gate Omission:** `FrontendQualityGateService.cs` and `QualityGates.cs` omitted `.css` from `EslintExtensions`, causing `pnpm cadence check --pre-commit` to skip staged `.css` blobs.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA Across Light & Dark Themes (PRD-CRF-015):** Every color, scrim, translucent status tint, focus ring, and elevation shadow must adapt predictably across `:root`, `[data-theme="dark"]` / `.dark`, and `@media (prefers-color-scheme: dark) { :root[data-theme="auto"] }` without altering calibrated foreground/surface contrast ratios.
- **Clinical Canvas Crafted Execution (`CLINICAL_CANVAS.md`):** Surfaces should rely primarily on crisp `1px solid var(--semantic-color-border)` boundaries and restrained `--semantic-shadow-sm` through `--semantic-shadow-xl` tokens rather than heavy bespoke shadows.
- **HTML5 `<canvas>` Compatibility:** Browser 2D `<canvas>` contexts (`fillStyle`, `strokeStyle`, `CanvasGradient.addColorStop`) reject raw CSS `var(--*)` references and require runtime custom property resolution via `getComputedStyle(document.documentElement)`.
- **Fail-Closed Pre-Commit & CI Guardrails:** Both `pnpm lint` and `pnpm cadence check --pre-commit` must block regressions across `.vue`, `.js`, `.ts`, and `.css` files (including VitePress theme stylesheets).

## 3. Options Considered

### Option 1: Extend Paired Scrim, Tint, Focus-Ring, Imaging, and Shadow Tokens in `packages/ui/tokens.css` and Enforce Full Color & Shadow Linting Across Stylesheets, Scripts, VitePress, and CLI Gates (Selected)

- **Overview:** Extend `packages/ui/tokens.css` with paired light/dark scrims (`--semantic-color-scrim*`), surface and status translucent tints (`--semantic-color-*-tint`, `--semantic-color-*-border-tint`), imaging canvas tokens (`--semantic-color-imaging-*`), focus rings, drawer shadows, and `--semantic-shadow-*` elevation aliases. Expand `eslint.config.mjs` to reject `#hex`, `rgb()`/`rgba()`/`hsl()`/`hsla()`, CSS named colors (allowing `transparent`, `currentColor`, and `inherit`), and untokenized `box-shadow` rules (allowing `none` resets, tokenized `--*shadow*` variables, and zero-blur tokenized C40 focus halos per [ADR-2646](2026-11-22-standardize-24px-target-tokens-and-dual-focus-rings.md)) across `.css` (including `docs/.vitepress/theme/custom.css` with `--vp-*` variable support), Vue `<template>`/`<script>`/`<style>`, and JS/TS source modules. Add `.css` to `FrontendQualityGateService.EslintExtensions`, update `QualityGates.cs` and CLI docs, and migrate all untokenized colors, `<canvas>` lookups, and heavy shadows across `packages/ui`, `apps/web`, `apps/subject-portal`, and `docs/.vitepress/theme`.
- **Pros:**
  - Closes every static and pre-commit bypass vector while preserving existing WCAG 2.2 AA contrast ratios and layout geometry.
  - Keeps HTML5 `<canvas>` viewports (`DicomMprViewer.vue`) theme-token-governed via `getComputedStyle(document.documentElement)`.
- **Cons:**
  - Touches ~90 frontend stylesheets and Vue components in a coordinated migration.

### Option 2: Lint Only `.css` Files for `rgb()`/`rgba()` Without Covering `<script>`, Named Colors, `box-shadow`, or Pre-Commit `.css` Blobs

- **Overview:** Add a regex check for `rgba?()` on `apps/**/*.css` and `packages/**/*.css` only.
- **Pros:**
  - Smaller initial diff.
- **Cons:**
  - Leaves `hsl()`, named colors (`white`), `<template>`/`<script>` style strings, `<canvas>` palettes, untokenized `box-shadow` declarations, `docs/.vitepress/theme/custom.css`, and staged `.css` pre-commit checks unguarded.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Pairing complete light/dark scrim, tint, focus-ring, imaging, and elevation tokens in `packages/ui/tokens.css` with comprehensive ESLint and `cadence check --pre-commit` enforcement eliminates all ad-hoc color and shadow drift permanently.

## 5. Consequences & Trade-offs

- **Positive Impact:** Uniform light/dark theme fidelity across `packages/ui`, `apps/web`, `apps/subject-portal`, and `docs/.vitepress/theme`, crisp `1px` surface boundaries with standardized elevation tokens, and immediate fail-closed feedback in both `pnpm lint` and `cadence check --pre-commit`.
- **Negative Impact / Technical Debt:** Legitimate new raw colors or shadow scales must be added solely inside the `:root` and dark-theme blocks of `packages/ui/tokens.css`, and `<canvas>` drawing routines must resolve CSS custom properties through `getComputedStyle(document.documentElement)`.
- **Mitigation Strategy:** Mask `:root` and dark-theme blocks in `packages/ui/tokens.css` within `eslint.config.mjs`, allow `--vp-*` tokens in `docs/.vitepress/`, and cover every rule branch in `packages/ui/tests/assets.test.js` and `PreCommitGuardrailsTests.cs`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/.vitepress/theme`, `packages/tooling_core`, `packages/cli`, `eslint.config.mjs`, `package.json`, `docs/cli.md`, `docs/CI.md`, `docs/components/index.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`, `tests/Cadence.UnitTests`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm lint`, `pnpm --filter web typecheck`, `pnpm test`, `pnpm build`, `pnpm build:demo`, `pnpm docs:build`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`, `pnpm --filter subject-portal test:e2e`, `pnpm --filter web test:e2e`, and `pnpm cadence check --base main`.
