# ADR-2822: Extend Shared Responsive Primitives, Rem Typography Tokens, and Sticky Scroll Padding

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA SC 2.4.11 Focus Not Obscured (Minimum), SC 1.4.10 Reflow at 320x256, and SC 1.4.4 Resize Text up to 200%)

---

## 1. Context & Problem Statement

Across `packages/ui`, `apps/web`, and `apps/subject-portal`, three WCAG 2.2 Level AA presentation gaps remained on zoomed (`400%` / `320x256`) and scaled-font (`200%` / `html { font-size: 32px }`) viewports:

1. **Sticky Chrome Occlusion (SC 2.4.11):** `AppShell.vue` (`.app-header`) and `StudyCanvasView.vue` (`.topbar`) rendered sticky bars without publishing their active heights to `document.documentElement` or setting `scroll-padding-top` on `html` and `main` (WCAG Technique C43). On `320x256` viewports, secondary `.header-controls` and the sticky `.topbar` also expanded sticky chrome past `25%` (`64px`) of viewport height.
2. **320px Two-Dimensional Overflow & Clipped Panes (SC 1.4.10):** Shared grid tracks (`repeat(12, 1fr)`, `1fr 2fr`) in `packages/ui/responsive.css` used `1fr` (`minmax(auto, 1fr)`) instead of `minmax(0, 1fr)` (WCAG Technique C32), `< 1024px` span resets missed fields nested inside `<fieldset style="display: contents">` (`CrcFormRenderer.vue`), `.clinical-input` / `.clinical-radio-grid` lacked `min-width: 0; max-width: 100%` (WCAG Technique C38), and `DocumentManagerView.vue` locked `.document-manager-layout` to a desktop horizontal split (`height: calc(100vh - 180px); overflow: hidden; margin: -24px`) at narrow widths (Failure F102).
3. **Hardcoded `px` Font Sizes & `transition: all` (SC 1.4.4):** `--font-size-*` tokens in `packages/ui/tokens.css`, `body` in `apps/subject-portal/src/style.css`, and component/view font declarations used `px` instead of `rem`, form controls lacked `font: inherit` (Failure F80), and `transition: all` animated box geometry during text resize (Failure F69).

## 2. Decision Drivers & Constraints

- **WCAG 2.2 AA Focus Visibility, 320x256 Reflow, and 200% Text Scaling (PRD-CRF-015):** Keyboard focus must never be completely obscured by sticky headers, sticky chrome must stay `<= 25%` (`<= 64px`) at `320x256`, live clinical routes and Subject Portal views must reflow to `<= 320px` width without horizontal page scrolling or clipped content, and `200%` root font scaling must scale all text and form controls proportionally.
- **Preserve Shared Token & Accessibility Guarantees:** Keep the 20-component `packages/ui` export surface, `--touch-target-min: 48px`, calibrated AA text/border contrast (`ADR-2820`), `font-variant-numeric: tabular-nums` on data tables, local horizontal scroll wrappers on multi-column clinical visit/casebook matrices, and `@media (prefers-reduced-motion: reduce)` guards.

## 3. Options Considered

### Option 1: Centralize `rem` Tokens, `minmax(0, 1fr)` Grid Primitives, and `ResizeObserver`-Synced Scroll Padding (Selected)

- **Overview:** Convert `--font-size-2xs`..`--font-size-3xl` in `packages/ui/tokens.css` and app/component font declarations to `rem`, enforce `font: inherit` and `min-width: 0; max-width: 100%` on clinical/form controls, replace `transition: all` with explicit visual property transitions, upgrade `packages/ui/responsive.css` grids to `minmax(0, 1fr)` with nested `<fieldset>` span resets plus `.grid-cards-responsive` and `.split-pane-responsive` utilities, publish `--shell-header-height` and `--canvas-topbar-height` onto `document.documentElement` with `scroll-padding-top` on `html` and `main`, fold compact header controls into `#mobile-nav-menu` below `768px` width or `480px` height while making `.topbar` static on compact viewports, and stack split-pane/grid layouts cleanly across `apps/web` and `apps/subject-portal`.
- **Pros:**
  - ✅ Satisfies WCAG Techniques C43, C31, C32, and C38 uniformly across `packages/ui`, all 14 live `apps/web` routes, and all 7 `apps/subject-portal` views.
  - ✅ Eliminates Failure F102, F80, and F69 at the shared token/primitive layer without altering backend contracts or clinical matrix semantics.
- **Cons:**
  - ❌ Requires updating unit assertions in `packages/ui/tests/assets.test.js` and `apps/web/tests/components/AppShell.spec.js` and rebuilding `packages/ui` before downstream verification.

### Option 2: Add Route-Specific CSS Overrides Only in `apps/web`

- **Overview:** Patch individual route stylesheets with `px` media-query overrides and fixed `scroll-padding-top` constants while leaving `packages/ui` tokens and `apps/subject-portal` untouched.
- **Pros:**
  - ✅ Smaller diff in `packages/ui`.
- **Cons:**
  - ❌ Leaves hardcoded `px` `--font-size-*` tokens and `1fr` intrinsic-minimum grid tracks in `packages/ui`, fails `200%` root font scaling in `apps/subject-portal`, and lets fixed `scroll-padding-top` drift from actual sticky header geometry.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Fixing typography units, intrinsic grid/control sizing, and sticky scroll-padding synchronization at the shared primitive and shell layers resolves SC 2.4.11, SC 1.4.10, and SC 1.4.4 systematically across both `apps/web` and `apps/subject-portal`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Zero sticky-header focus occlusion across forward and reverse keyboard traversal, `<= 64px` sticky chrome at `320x256`, single-column `320px` reflow across all 14 live web routes and 7 Subject Portal views, and proportional `200%` root font scaling across text and form controls.
- **Negative Impact / Technical Debt:** New sticky shell bars introduced in the future must publish their active height CSS variable (or remain `position: static` on compact viewports) to stay synchronized with `scroll-padding-top`.
- **Mitigation Strategy:** Verified by `packages/ui/tests/assets.test.js`, `apps/web/tests/components/AppShell.spec.js`, and automated Playwright accessibility/reflow checks.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `packages/ui/README.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, Playwright 320px reflow / focus-occlusion / 200% text-scaling checks, `pnpm -r lint`, `pnpm --filter web typecheck`, and `cadence check adrs`.
