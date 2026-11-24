# ADR-2673: Active-Item Scroll Alignment, Overflow Cues, and Sticky Navigation Bars

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (Shared UI Architecture and Cross-Route Clinical Context Continuity), PRD-CRF-015 (WCAG 2.2 Level A & AA SC 1.4.4 Resize Text, SC 1.4.10 Reflow, SC 2.3.3 Animation from Interactions, SC 2.4.11 Focus Not Obscured, SC 2.5.3 Label in Name, and SC 2.5.8 Target Size Minimum compliance)

---

## 1. Context & Problem Statement

Across `packages/ui`, `apps/web`, and `apps/subject-portal`, three navigation orientation and scroll ergonomics gaps affected narrow phones, `200%` browser zoom, and tablet reading workflows:

1. **Clipped Active Crumb and Selected Tab on Narrow Viewports and `200%` Zoom:** `ClinicalContextBar.vue` (`#shell-context-bar`) and `TabBar.vue` (`.nav-tabs`) relied on raw horizontal overflow (`overflow-x: auto`) with `scrollLeft` left at `0`. On `320px`–`480px` viewports or `200%` text zoom, `[aria-current="page"]` in scoped standalone routes (`#/ecrf`, `#/etmf`, `#/tickets`) and `[aria-selected="true"]` in later participant portal tabs (`#/consent`, `#/wallet`, `#/lay-summaries`) initialized off-screen without visual overflow cues, and ancestor prefixes (`Studies` and `Site`) consumed scarce mobile width before study/site codes appeared.
2. **Scrolling Away Clinical Context and Excessive Tablet Chrome in `apps/web`:** Unlike `StudyCanvasView.vue`, which pins `.zoom-path` at `position: sticky; top: var(--shell-header-height, 56px)`, `AppShell.vue` left `#shell-context-bar` in static document flow so vertical scrolling hid study/site/participant scope. On `< 1280px` tablets (`768x1024`, `1024x768`), keeping both `.app-header` and `#shell-context-bar` pinned simultaneously would exceed `96px` of persistent top chrome.
3. **Scrolling Away Participant Sidebar Navigation in `apps/subject-portal`:** At `768px+`, `.portal-navigation` stretched in a flex row without sticky positioning, scrolling `.nav-tabs` and `.participant-info-card` out of the viewport during long informed-consent (`ConsentView`) and task reads.

## 2. Decision Drivers & Constraints

- **Shared, Motion-Safe Scroll Alignment (`packages/ui`):** Centralize active-item horizontal alignment and overflow boundary detection in `packages/ui` with zero runtime npm dependencies, SSR safety (`typeof window === "undefined"`), `ResizeObserver` reactivity, and `prefers-reduced-motion: reduce` compliance (`behavior: "auto"`).
- **WCAG 2.2 AA & Design System Guardrails:** Preserve `44px` hit areas in `apps/web` and `48px` hit areas in `apps/subject-portal`, retain exact accessible names (`Studies`, `Site <code>`) without static `aria-label` overrides (SC 2.5.3), keep WAI-ARIA `tablist` child ownership clean (`role="tablist"` containing only `li > button[role="tab"]`), use `16px` `1.75`-stroke `aria-hidden="true"` chevrons with `pointer-events: none` surface fades, coordinate `:root` `--sticky-chrome-offset` and `< 500px` short-viewport sticky release (`ADR-2663`), and avoid modifying `packages/ui/tokens.css` or synchronous `apps/web` cold-boot chunk boundaries.
- **Persistent Top Chrome Budget (`<= 52px` on `< 1280px` Tablets):** Keep `.app-header` + `#shell-context-bar` sticky on desktop (`>= 1280px`) matching `StudyCanvasView.vue`, while collapsing `.app-header` on downward scroll below `1280px` (and immediately re-expanding on upward scroll, top-of-page, `focusin` per SC 2.4.11, or `#mobile-nav-menu` / `#header-secondary-menu` open) so persistent top chrome stays `<= 52px`.

## 3. Options Considered

### Option 1: Shared `useActiveScrollAlignment` Composable, Decorative Edge Cues, `.sr-only` Mobile Ancestor Condensation, Sticky Bars, and `< 1280px` Header Collapse (Selected)

- **Overview:**
  1. Export `useActiveScrollAlignment`, `alignActiveScrollItem`, `computeScrollOverflowState`, and `prefersReducedMotion` from `packages/ui` (`src/composables/useActiveScrollAlignment.js`) to center off-screen `[aria-current="page"]` or `[aria-selected="true"]` items within horizontal scroll containers on mount, active-key updates, and `ResizeObserver` geometry changes, and track reactive `canScrollStart` / `canScrollEnd` flags.
  2. Render `pointer-events: none`, `aria-hidden="true"` start/end surface-gradient + `16px` (`1.75` stroke) chevron cues outside `<ol>` in `ClinicalContextBar.vue` and inside `.nav-tabs-container` (sibling to `<ul class="nav-tabs" role="tablist">`) in `TabBar.vue`, plus align `.nav-tabs` inside `showView()` (`navigation.js`) before `handoffFocus()` so cold loads and hash changes reveal the selected tab while preserving main-heading focus handoff.
  3. Below `768px` in `ClinicalContextBar.vue`, condense ancestor text (`Studies` label and `Site ` prefix) via `.sr-only` styling while keeping `16px` folder/building SVG icons, monospace `SITE-*` codes, `>= 44x44px` pill hit areas, and full DOM `textContent` / accessible names.
  4. Pin `#shell-context-bar` (`position: sticky; top: var(--shell-header-height, 0px)`, releasing at `@media (max-height: 499px)`) in `AppShell.vue` while keeping `LazyClinicalContextBar` deferred off the cold-boot path, synchronize `#shell-context-bar` and collapsed/expanded `.app-header` heights with `stickyChromeOffset.ts` (`updateShellStickyContextBarHeight` / `updateShellStickyHeaderHeight`), and collapse `.app-header` during downward scroll on `< 1280px` viewports (`--shell-header-height: 0px`) so `#shell-context-bar` stays compact (`<= 52px`). At `>= 768px` in `apps/subject-portal/src/style.css`, anchor `.portal-navigation` with `position: sticky; top: var(--spacing-xl, 24px); align-self: flex-start; max-height: calc(100vh - 48px); overflow-y: auto`.
- **Pros:**
  - Solves initial and dynamic horizontal clipping across both applications with one tested composable and imperative helper.
  - Preserves `axe-core` `tablist` rules, SSR markup compatibility, `handoffFocus` heading focus handoff, cold-boot bundle budgets, and WCAG 2.2 SC 2.4.11 (`focusin` header re-expansion and `:root` `--sticky-chrome-offset` focus clearance).
  - Keeps tablet top chrome `<= 52px` during downward clinical reading without losing scope awareness.
- **Cons:**
  - Introduces a wrapper `.nav-tabs-container` around `ul.nav-tabs` in `TabBar.vue` so fixed mobile positioning and edge overlays sit outside `role="tablist"`.

### Option 2: Multi-Row Wrapping Breadcrumbs and Tabs on Narrow Screens

- **Overview:** Allow `.clinical-context-list` and `.nav-tabs` to wrap across two or three rows instead of scrolling horizontally.
- **Pros:**
  - Avoids horizontal scroll state tracking.
- **Cons:**
  - Violates the `<= 52px` sticky chrome budget on mobile/tablet viewports and consumes vertical reading space on `320x640` phones and `200%` zoom.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing scroll alignment and overflow boundary tracking in `packages/ui` while pairing non-blocking chevron cues, `.sr-only` mobile ancestor condensation, sticky navigation bars, and `< 1280px` downward-scroll header collapse satisfies every mobile, `200%` zoom, and tablet ergonomic requirement within WCAG 2.2 AA and Cadence Clinical design tokens.

## 5. Consequences & Trade-offs

- **Positive Impact:** Active breadcrumbs and selected portal tabs are immediately visible on cold loads, route transitions, and viewport resizes; overflowing strips expose clear directional cues that clear at scroll boundaries; standalone staff workflows and participant consent/task screens retain visible navigation context during vertical scroll while keeping `:root` `--sticky-chrome-offset` calibrated.
- **Negative Impact / Technical Debt:** `AppShell.vue` tracks window scroll direction and header/context-bar `ResizeObserver` heights to coordinate `--shell-header-height` and `--sticky-chrome-offset`; keyboard focus entering `.app-header` must immediately clear `isHeaderCollapsed`.
- **Mitigation Strategy:** Attach explicit `@focusin="handleHeaderFocusIn"` on `.app-header`, disable collapse when `#mobile-nav-menu` or `#header-secondary-menu` is open or viewport width is `>= 1280px`, release sticky bars at `@media (max-height: 499px)`, and verify behavior across unit and Playwright E2E suites.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/components/index.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm lint`, `pnpm -r typecheck`, Playwright E2E checks across `320x640`, `390x844`, `480x800`, `768x1024`, `1024x768`, and `200%` zoom (`apps/subject-portal` and `apps/web`), and `pnpm cadence check`.
