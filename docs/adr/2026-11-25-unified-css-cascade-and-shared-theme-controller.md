# ADR-2690: Unified CSS Theme Cascade, Non-Text Border Recalibration, and Shared Theme Controller

- **Status:** Accepted
- **Date:** 2026-11-25
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015, PRD-SYS-001

---

## 1. Context & Problem Statement

Across `packages/ui/tokens.css`, `apps/web`, `apps/subject-portal`, and `docs/.vitepress`, light, dark, and system-auto color modes had accumulated cascade, contrast, and persistence gaps:

1. **Incomplete Dark Overrides & Media-Query Drift:** Several `:root` shadow and inverse chrome tokens (`--semantic-shadow-sm`, `--semantic-shadow-xl`, and `--semantic-color-*-inverse*`) lacked explicit overrides in `:root[data-theme="dark"]` and `@media (prefers-color-scheme: dark)`. Furthermore, `@media (prefers-color-scheme: dark)` targeted only `:root[data-theme="auto"]`, leaving unset `<html>` elements in light mode even when the operating system requested dark mode unless JavaScript ran first.
2. **Surface-as-Text Pairings & Control Theme-Switch Flash:** Several solid-fill badges and banners paired `--semantic-color-surface` (which flips from `#ffffff` in light mode to `#1e293b` in dark mode) or `--semantic-color-text-on-solid` (`#ffffff`) against dark-mode pastel status/AI tokens (`#c084fc`, `#4ade80`, `#fbbf24`, `#f87171`) or wallet gradients (`#0369a1` / `#0284c7`), dropping contrast below `4.5:1` (WCAG 2.2 SC 1.4.3). Additionally, `.clinical-input` transitioned `border-color` and `background-color` without `color 0.2s`, causing a brief `1.04:1` text-against-background flash during theme switches.
3. **Sub-3:1 Non-Text Status & AI Borders:** Semantic status and AI callout borders (`--semantic-color-ai-border`, `--semantic-color-info-border{,-accent}`, `--semantic-color-divider-accent`, `--semantic-color-warning-border{,-strong}`, `--semantic-color-success-border{,-soft}`, `--semantic-color-error-border{,-soft}`) fell below `3.0:1` non-text contrast (WCAG 2.2 SC 1.4.11) on light or dark surfaces.
4. **Missing Shared Theme Switcher & Pre-Paint Bootstrap:** Only `docs/.vitepress` exposed a user-facing theme toggle (`vitepress-theme-appearance`), while `apps/web` and `apps/subject-portal` lacked a shared `light` / `dark` / `auto` switcher, pre-paint `script-src 'self'` bootstrap script, cross-tab `storage` listener, and live `prefers-color-scheme` synchronization.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA Contrast (`PRD-CRF-015`):** All canonical semantic normal-text pairs (`>= 69` token and consumer pairs) must maintain `>= 4.5:1` contrast (SC 1.4.3) across light, dark, and auto-dark modes, and semantic status/AI/control boundaries must achieve `>= 3.0:1` non-text contrast (SC 1.4.11) while keeping passive layout dividers (`--semantic-color-border`: `#e2e8f0` light / `#334155` dark) visually subtle.
- **Strict Content-Security-Policy (`script-src 'self'`) & Zero-FOUC Startup (`PRD-SYS-001`):** `apps/web` and `apps/subject-portal` forbid `'unsafe-inline'` scripts, and `CADENCE_E2E_BUILD=1` sets Rollup `codeSplitting: false`, requiring a synchronous same-origin `<script src="%BASE_URL%theme-bootstrap.js"></script>` asset in `<head>` that applies `data-theme`, `.dark`, and `color-scheme` before first paint without creating multiple Rollup entry chunks.
- **Zero-Emoji Vector Iconography & Shared Storage Key (`ADR-2658`, `ADR-2677`):** Theme controls must use `lucide-vue-next` stroke icons (`Sun`, `Moon`, `Monitor` at `stroke-width="1.75"`, `aria-hidden="true"`), respect `AppShell.vue`'s single-row `<= 52px` header budget (`compact` `44px` hit target) and `apps/subject-portal`'s `48px` eCOA touch target, and synchronize on `vitepress-theme-appearance` across staff web, participant portal, and VitePress docs.

## 3. Options Considered

### Option 1: Unified `tokens.css` Cascade, Dedicated `on-ai` / `on-status` Ink Tokens, Same-Origin `theme-bootstrap.js` Asset, and Shared `useTheme` + `ClinicalThemeToggle` (Selected)

- **Overview:** Add dark/auto overrides for every color and shadow token in `packages/ui/tokens.css` `:root`; unify `@media (prefers-color-scheme: dark)` on `:root:not([data-theme="light"]), :root[data-theme="auto"]` and `html:not([data-theme="light"]), html[data-theme="auto"] { color-scheme: dark; }` alongside `:root[data-theme="dark"], [data-theme="dark"], .dark`; add `--semantic-color-text-on-ai` and `--semantic-color-text-on-status` (`#ffffff` light / `#0f172a` dark), recalibrate dark wallet fills (`#075985` / `#0c4a6e`), recalibrate AI/info/warning/success/error borders to `>= 3.0:1`, and add `color 0.2s` to `.clinical-input`; export `themeController.js`, `useTheme`, and `ClinicalThemeToggle` from `packages/ui` (and `ui/theme`); emit same-origin `theme-bootstrap.js` via `createThemeBootstrapPlugin()` in `scripts/vite-hoist-csp.mjs` loaded in `<head>` of `apps/web/index.html` and `apps/subject-portal/index.html`; mount `ClinicalThemeToggle` in `AppShell.vue` and `PortalHeader.vue`; and synchronize `docs/.vitepress/theme/index.js`.
- **Pros:**
  - Guarantees 100% 1:1 token parity across `:root` color/shadow tokens, `[data-theme="dark"]`, `.dark`, and `@media (prefers-color-scheme: dark)` (`:root:not([data-theme="light"])`).
  - Eliminates first-paint theme flash under strict `script-src 'self'`, keeps `CADENCE_E2E_BUILD=1` single-chunk Rollup builds intact, and automatically registers `theme-bootstrap.js` in `apps/subject-portal` offline service-worker precache manifests.
  - Shares one `vitepress-theme-appearance` contract across `apps/web`, `apps/subject-portal`, and `docs/.vitepress` with live cross-tab `storage` and OS `prefers-color-scheme` updates.
- **Cons:**
  - Requires consumer rules that render solid AI or status pill fills (`AiLabel.vue`, `ProtocolDigitizationView.vue`, `TmfBinderTree.vue`, `apps/subject-portal/src/style.css`) to pair `--semantic-color-text-on-ai` / `--semantic-color-text-on-status` rather than `--semantic-color-surface`.

### Option 2: Component-Local Dark Media Queries and Per-App Inline Theme Scripts

- **Overview:** Patch low-contrast badges with component-scoped `@media (prefers-color-scheme: dark)` rules and add SHA-256 CSP script hashes for inline `<script>` blocks in each `index.html`.
- **Pros:**
  - Avoids adding a Vite bootstrap plugin.
- **Cons:**
  - Violates centralized semantic token governance (`ADR-2675`), drifts whenever bootstrap logic changes (breaking CSP hashes), and duplicates theme state across apps.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing 1:1 `:root` / dark / auto-dark token parity, `>= 3.0:1` semantic border recalibration, and `>= 4.5:1` solid-fill ink tokens in `packages/ui/tokens.css`, paired with a zero-dependency `themeController.js` compiled into a synchronous same-origin `theme-bootstrap.js` `<head>` script and reactive `useTheme` + `ClinicalThemeToggle` primitives, delivers flash-free WCAG 2.2 AA compliance across staff web, participant portal, and VitePress docs under `script-src 'self'`.

## 5. Consequences & Trade-offs

- **Positive Impact:** `packages/ui` exports 26 shared Vue components (adding `ClinicalThemeToggle`) and shared theme controller helpers (`useTheme`, `bootstrapTheme`, `subscribeThemeSync`, `THEME_STORAGE_KEY`, `THEME_MODES`, and `ui/theme`); zero `:root` color or shadow tokens lack dark/auto overrides; all canonical semantic text pairs achieve `>= 4.5:1` contrast and all semantic status/AI/control borders achieve `>= 3.0:1` contrast across light and dark modes; `.clinical-input` transitions foreground `color 0.2s` smoothly; and `apps/web`, `apps/subject-portal`, and `docs/.vitepress` stay synchronized on `vitepress-theme-appearance`.
- **Negative Impact / Technical Debt:** `scripts/vite-hoist-csp.mjs` now serves and emits `theme-bootstrap.js` from `packages/ui/src/utils/themeController.js` at dev/build time, coupling the bootstrap asset name across `index.html` and Vite config plugins.
- **Mitigation Strategy:** `packages/ui/tests/assets.test.js`, `packages/ui/tests/design_system_harmony.test.js`, `packages/ui/tests/theme_controller.test.js`, and Playwright accessibility suites in `apps/web` and `apps/subject-portal` enforce 100% token parity, contrast floors, CSP-clean pre-paint bootstrap execution, persistence across reloads, cross-tab `storage` sync, and live `prefers-color-scheme` reactivity.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `scripts/vite-hoist-csp.mjs`, `scripts/vite-strip-demo-fixtures.mjs`, `docs/.vitepress`, `docs/components/index.md`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test` (`assets.test.js`, `design_system_harmony.test.js`, `theme_controller.test.js`, `ui_consumer_reachability.test.js`), `pnpm --filter ui typecheck`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm lint`, `pnpm typecheck`, `pnpm docs:build && node scripts/stage-portal-demos.js && pnpm cadence check public-bundle`, `pnpm cadence check adrs markdown readmes deid duplication prettier`, and Playwright E2E accessibility verification in `apps/web` and `apps/subject-portal`.
