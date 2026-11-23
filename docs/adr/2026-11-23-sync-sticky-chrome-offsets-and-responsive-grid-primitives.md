# ADR-2658: Sync Sticky Chrome Offsets, Short-Viewport Sticky Release, and Responsive Grid Primitives

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA SC 2.4.11 Focus Not Obscured, SC 1.4.10 Reflow, and SC 1.4.4 Resize Text), PRD-SYS-001 (GxP clinical workspace legibility across tablet, desktop, and magnified viewports)

---

## 1. Context & Problem Statement

Across `apps/web`, `AppShell.vue` measured `.app-header` height locally on `.app-shell` (`--shell-header-height`) and `StudyCanvasView.vue` measured `.topbar` locally on `#study-canvas` (`--canvas-topbar-height`), while `apps/web/src/style.css` applied a static `scroll-padding-top: 88px` to `html` alone and omitted `scroll-padding-top` on `main`. When `.app-header` and `.topbar` stacked on Clinical Canvas routes or wrapped at narrow widths, combined sticky chrome exceeded `88px` and obscured keyboard-focused controls (WCAG 2.2 SC 2.4.11). Below `500px` viewport height (`320x256` and `640x400` magnified viewports), sticky headers consumed most of the vertical workspace instead of scrolling with the document.

At the same time, shared grid helpers in `packages/ui/responsive.css` used `1fr` tracks instead of `minmax(0, 1fr)` and only reset `grid-column` spans on direct `.responsive-grid > *` children below `1024px`, leaving multi-span field `div`s nested inside `<fieldset style="display: contents">` in `CrcFormRenderer.vue` side-by-side at `768x1024` and `320x640` and allowing `#query-flag-vssbp` to collide with `#vsdpb`. Horizontal table scroll wrappers (`.scrollable-table-wrapper`, `.ecrf-table-scroll`, `.matrix-table-card`, `.observation-table-scroll`, `.casebook-matrix-scroll`) lacked `position: relative`, allowing `position: absolute` `.sr-only` labels to escape scrollable containers and stretch document `scrollWidth`, while fixed `min-width` declarations in `CtmsDemoWorkspace.vue`, `TicketsView.vue`, `SsuGreenlightConsole.vue`, `ProtocolTrainingConsole.vue`, `ScopeLockControls.vue`, and `OverviewView.vue` caused horizontal page overflow at `320x640`, `900x768`, and `200%` root font scaling (`32px` at `320px`).

## 2. Decision Drivers & Constraints

- **Unobscured Keyboard & Programmatic Focus (WCAG 2.2 SC 2.4.11):** Native focus scrolling and programmatic focus handoffs must clear both `.app-header` and `.topbar` without introducing `!important` rules into `apps/web/src/style.css` and while preserving `window` document-level scrolling.
- **Short-Viewport & 200%-Zoom Workspace Preservation (WCAG 2.2 SC 1.4.10 & SC 1.4.4):** `.app-header` must stay compact (`<= 52px`) across standard tablet and desktop viewports (`>= 768px`), wrap badges and controls cleanly at `320px` width under `32px` (`200%`) root font scaling, and release sticky positioning alongside `.topbar` below `500px` viewport height so `320x256` and `640x400` viewports retain full vertical workspace height.
- **Centralized Grid & Shrink-Safety Primitives (ADR-2328 / ADR-2382):** Shared grid, `.clinical-input`, `.query-flag`, and `.scrollable-table-wrapper` primitives belong in `packages/ui` (`responsive.css` and `tokens.css`) while leaving base color, font-size, and iconography token definitions untouched.

## 3. Options Considered

### Option 1: Live `:root` `--sticky-chrome-offset` Synchronization, `< 500px` Sticky Release, and Shared `minmax(0, 1fr)` / Relative Scroll Primitives (Selected)

- **Overview:** Coordinate `ResizeObserver` measurements from `AppShell.vue` and `StudyCanvasView.vue` through `apps/web/src/utils/stickyChromeOffset.ts` to publish live `--shell-header-height`, `--canvas-topbar-height`, and `--sticky-chrome-offset` (active sticky height plus `12px` focus-ring clearance, collapsing sticky header height to `0px` when viewport height `<= 499px` or viewport width `<= 640px`) on `:root`, paired with `scroll-padding-top: var(--sticky-chrome-offset, 88px)` on `html` and `main` and focus-in clearance nudges when scrolled controls sit behind active sticky chrome; release `.app-header`, `.topbar`, `.navigator`, and `#canvas-inspector` inside `@media (max-height: 499px), (max-width: 640px)` while keeping `.app-header` single-row (`<= 52px`) across `>= 768px` (including compact `DemoResetButton.vue` shell labels); upgrade `packages/ui/responsive.css` grids to `minmax(0, 1fr)` with `.responsive-grid fieldset > *` mobile span resets and `position: relative` on `.scrollable-table-wrapper`; apply `min-width: 0` to `.clinical-input` controls and `flex-shrink: 0` to `.query-flag` in `packages/ui/tokens.css`; and add `position: relative` scroll containment plus bounded widths across eCRF, Observation, CTMS, SSU, Training, Tickets, and Overview views.
- **Pros:**
  - Keeps `--sticky-chrome-offset` accurate across dynamic header wrapping, route transitions between standalone views and `StudyCanvasView`, and short-viewport sticky release without `!important` in `apps/web/src/style.css`.
  - Fixes `<fieldset style="display: contents">` multi-span eCRF stacking, input/query-flag collisions, and `.sr-only` scroll-width leaks once in `packages/ui` for all consumers.
- **Cons:**
  - Requires lifecycle cleanup (`onUnmounted`) in `AppShell.vue` and `StudyCanvasView.vue` so unmounting `StudyCanvasView` immediately drops `--canvas-topbar-height` back to `0px`.

### Option 2: Increase Static `html { scroll-padding-top }` to `140px` and Patch Individual Form Fields

- **Overview:** Hardcode a larger static `scroll-padding-top` in `apps/web/src/style.css` and add per-view media queries in `CrcFormRenderer.vue`.
- **Pros:**
  - Avoids `ResizeObserver` coordination on `:root`.
- **Cons:**
  - Over-pads standalone routes and short viewports (`140px` consumes over half of a `256px` viewport), fails when `.app-header` wraps at `200%` text zoom, and leaves shared `.responsive-grid` and `.scrollable-table-wrapper` defects unaddressed.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Publishing live measured sticky offsets to `:root`, releasing sticky bars below `500px` viewport height, and fixing grid track shrinkage, nested `fieldset` span resets, and relative scroll-wrapper containment in `packages/ui` resolves focus obscuration and horizontal reflow defects systematically while preserving `window` scrolling and `48px` / `24px` target-size guarantees.

## 5. Consequences & Trade-offs

- **Positive Impact:** Focused controls stay unobscured below sticky chrome across desktop, tablet, and magnified viewports; `320x256` and `640x400` viewports scroll headers out of view; multi-span eCRF fields stack cleanly below `1024px` without query-flag collisions; and all 16 live web routes maintain `scrollWidth <= clientWidth` at `320x640`, `900x768`, and `200%` root font scaling.
- **Negative Impact / Technical Debt:** Custom horizontal scroll wrappers introduced outside `.scrollable-table-wrapper` must declare `position: relative` whenever descendants render `.sr-only` text.
- **Mitigation Strategy:** `packages/ui/tests/design_system_harmony.test.js`, `apps/web/tests/sticky_chrome_offset.test.js`, and `apps/web/tests/e2e/web-accessibility.spec.ts` enforce grid track rules, `:root` offset synchronization, short-viewport sticky release, eCRF stacking, and `320px` / `200%`-zoom reflow gates in CI.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter web test`, Playwright E2E checks in `apps/web/tests/e2e/web-accessibility.spec.ts`, `pnpm exec eslint --max-warnings 0 .`, `pnpm cadence check adrs`, `pnpm cadence check markdown`, and `pnpm cadence check deid`.
