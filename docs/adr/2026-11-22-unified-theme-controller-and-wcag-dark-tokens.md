# ADR-2651: Unified Theme Controller, Pre-Paint Bootstrap, and Full-Parity WCAG 2.2 AA Dark Tokens

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-CRF-015

---

## 1. Context & Problem Statement

Although `packages/ui/tokens.css` introduced initial dark-theme token overrides under [ADR-2166](2026-08-08-standardize-semantic-token-layer-across-platforms.md), [ADR-2646](2026-11-22-standardize-24px-target-tokens-and-dual-focus-rings.md), and [ADR-2650](2026-11-22-consolidate-shared-ui-css-aliases-and-table-numerals.md), three structural gaps remained across the clinician web application (`apps/web`), the participant portal (`apps/subject-portal`), and the VitePress documentation site (`docs/`):

1. **Incomplete Dark Overrides & Auto-Dark Selector Drift:** `:root` declared 118 color and shadow tokens (`16` `--color-*`, `4` `--shadow-*`, `96` `--semantic-color-*`, and `2` `--semantic-shadow-*`), but `[data-theme="dark"]` overrode only a subset and `@media (prefers-color-scheme: dark)` gated auto-dark behind `:root[data-theme="auto"]` with 23 fewer overrides than `[data-theme="dark"]`. Without any runtime script setting `data-theme="auto"`, system-dark users received mixed light and dark tokens.
2. **Dark Foreground, Solid Action, and Non-Text Border Contrast Gaps:** Several dark semantic pairs, solid action and wallet fills paired with `--semantic-color-surface` as text, consumer rules in `apps/web/src/style.css`, `apps/subject-portal/src/style.css`, `packages/ui/src/components/clinical/AiActionButton.vue`, and `apps/web/src/components/ai/AiLabel.vue`, and semantic AI and status borders (`--semantic-color-ai-border` plus info, warning, success, and error borders) fell below WCAG 2.2 Level AA thresholds (`4.5:1` under SC 1.4.3 and `3.0:1` under SC 1.4.11).
3. **No Shared Theme Controller or CSP-Compliant Pre-Paint Bootstrap:** Neither `apps/web` nor `apps/subject-portal` exposed a user-facing `light` / `dark` / `auto` switcher or synchronized appearance state with VitePress (`vitepress-theme-appearance`) when co-hosted on the same origin (`scripts/stage-portal-demos.js`), and inline `<script>` tags in `index.html` are prohibited by `script-src 'self'` enforced by `PublicBundleValidatorService.cs` ([ADR-2497](2026-11-20-dual-layer-gates-for-source-and-public-bundles.md)).

## 2. Decision Drivers & Constraints

- **Full-Parity CSS Cascade Without Selector Drift (`PRD-CRF-015`):** Every `:root` color and shadow token must have an explicit dark-mode override, and `:root:not([data-theme="light"])` inside `@media (prefers-color-scheme: dark)` must match `:root[data-theme="dark"], [data-theme="dark"], .dark` token-for-token so uninitialized (`auto`) documents render identically to explicit dark mode when the OS prefers dark.
- **Strict `script-src 'self'` Pre-Paint Bootstrapping (`PRD-SYS-001`):** Both `apps/web/index.html` and `apps/subject-portal/index.html` must apply `data-theme`, `.dark`, and `color-scheme` via a same-origin ES module in `<head>` with zero `'unsafe-inline'` or script hashes.
- **WCAG 2.2 Level AA & Clinical UI Standards (`PRD-CRF-015`):** All semantic foreground/surface pairs and solid action/badge/wallet pairs must achieve `>= 4.5:1` contrast in both light and dark modes; `--semantic-color-border-control`, `--semantic-color-ai-border`, and semantic status borders must achieve `>= 3.0:1` non-text contrast while passive `--semantic-color-border` remains subtle; the shared two-color `:focus-visible` indicator ([ADR-2646](2026-11-22-standardize-24px-target-tokens-and-dual-focus-rings.md)) must preserve its `#ffffff` (`--semantic-color-text-inverse`) inner halo across light, brand, and dark surfaces while `--semantic-color-text-on-solid` flips to `#0f172a` in dark mode; and the shared `ThemeSwitcher` must use crisp `lucide-vue-next` vector icons (`1.75px` stroke width) with zero Unicode emojis and `44px` (`compact`, preserving `<= 52px` persistent staff top chrome) / `48px` (`comfortable`) hit targets.

## 3. Options Considered

### Option 1: Unified CSS Cascade + Shared `packages/ui` Theme Controller + Same-Origin `<head>` Bootstrap Module (Selected)

- **Overview:** Complete all 118 color and shadow overrides (plus all 22 shared color aliases, 140 entries total) identically across `[data-theme="dark"]` / `.dark` and `@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) }`; flip `--semantic-color-text-on-solid` to `#0f172a` against brightened dark-mode solid action/wallet fills while binding the shared `:focus-visible` `2px` inner halo to `--semantic-color-text-inverse` (`#ffffff`); recalibrate AI and status borders to `>= 3.0:1`; export `useTheme`, `initTheme`, and a Lucide `ThemeSwitcher` from `packages/ui` synchronized with `vitepress-theme-appearance`, `storage` events, and `prefers-color-scheme` media changes; load `/src/theme-bootstrap.js` in `<head>` of both SPAs; replace surface-as-text rules in consumer stylesheets; and keep `axe-core` / `jsdom` strictly in `devDependencies` per [ADR-2537](2026-11-22-split-ui-vitest-matcher-and-enforce-demo-bundle-budgets.md).
- **Pros:**
  - Eliminates CSS cascade drift between explicit dark mode, `.dark` VitePress subtrees, and OS-driven `auto` mode even before JavaScript executes.
  - Satisfies `PublicBundleValidatorService.cs` (`script-src 'self'`) while keeping co-hosted VitePress docs, clinician demo (`/demo/`), and participant portal (`/subject-portal/`) synchronized across tabs and reloads.
  - Enforces `>= 4.5:1` text contrast, `>= 3.0:1` control/status/AI border contrast, and `>= 6.34:1` dual focus-ring contrast across both light and dark modes in Vitest and Playwright `axe-core` suites.
- **Cons:**
  - Adds `lucide-vue-next` to the shared workspace catalog as an externalized runtime icon dependency for `packages/ui` and both SPAs.

### Option 2: Component-Scoped CSS Media Queries Without a Shared Theme Controller

- **Overview:** Rely solely on `@media (prefers-color-scheme: dark)` inside `tokens.css` and individual Vue components without `data-theme` attributes, `localStorage` synchronization, or an interactive switcher.
- **Pros:**
  - Requires no JavaScript state or header UI control.
- **Cons:**
  - Prevents clinicians working in brightly lit or dim ward environments from overriding OS color scheme per browser profile and leaves VitePress's appearance toggle out of sync with co-hosted demos.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Combining a zero-drift CSS cascade (`:root:not([data-theme="light"])` matching `[data-theme="dark"]` and `.dark` across all 118 color and shadow tokens) with a shared `packages/ui` theme controller, Lucide `ThemeSwitcher`, and same-origin `<head>` bootstrap module satisfies WCAG 2.2 AA contrast, clinical two-profile touch sizing, VitePress co-hosting parity, and `script-src 'self'` security gates simultaneously.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinicians and trial participants gain deterministic `light`, `dark`, and `auto` theme control with persistent cross-app synchronization, zero pre-mount theme flash, `>= 4.5:1` dark-mode legibility across badges, modals, alerts, and wallets, and `>= 3.0:1` non-text boundary visibility on AI and status containers.
- **Negative Impact / Technical Debt:** Legacy views outside `AppShell`, `PortalHeader`, `AiLabel`, `AiActionButton`, and global `style.css` remain governed by ESLint's `custom-vue/no-inline-hex` gate rather than refactored in this change.
- **Mitigation Strategy:** `eslint.config.mjs` restricts hex-color masking strictly to exact theme selector blocks in `packages/ui/tokens.css`, while `design_system_harmony.test.js`, `assets.test.js`, `theme_controller.test.js`, and Playwright E2E `axe-core` suites fail closed on any token parity drift or light/dark contrast regression.

## 6. Implementation & Verification

- **Affected Packages / Applications:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/.vitepress/theme`, `docs/components/`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`, `eslint.config.mjs`, `pnpm-workspace.yaml`.
- **Verification Plan:**
  - Vitest token parity, relative luminance (`>= 4.5:1` text and `>= 3.0:1` border/focus), ESLint hex-masking, pre-paint bootstrap, and `ThemeSwitcher` / `useTheme` suites (`packages/ui/tests/design_system_harmony.test.js`, `packages/ui/tests/assets.test.js`, `packages/ui/tests/theme_controller.test.js`, `apps/web/tests/theme-bootstrap.test.js`, `apps/subject-portal/tests/theme-bootstrap.test.js`).
  - Headless Chromium `@axe-core/playwright` light and dark WCAG 2.2 AA E2E suites (`apps/web/tests/e2e/web-accessibility.spec.ts` and `apps/subject-portal/tests/e2e/portal-accessibility.spec.js`) plus `pnpm docs:build && pnpm docs:stage-demos` and `pnpm cadence check` public-bundle, ADR, markdown, README, dependency, and de-identification gates.
