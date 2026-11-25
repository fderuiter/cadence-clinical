# ADR-2676: Active Horizontal Scroll Alignment, Non-Blocking Overflow Cues, and Sticky Navigation Bars

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level A & AA SC 1.4.10 Reflow, SC 2.3.3 Animation from Interactions, SC 2.4.7 Focus Visible, SC 2.4.11 Focus Not Obscured Minimum, SC 2.5.8 Target Size Minimum, and SC 4.1.2 Name, Role, Value compliance)

---

## 1. Context & Problem Statement

Across `packages/ui`, `apps/web`, and `apps/subject-portal`, horizontally scrolling and sticky navigation surfaces exhibited four orientation and reflow gaps on narrow (`320px-375px`) and tablet (`641px-1279px`) viewports:

1. **Clipped Active Breadcrumbs & Tabs:** Deep Clinical Canvas hierarchy crumbs in `ClinicalContextBar.vue` and later participant tabs (`/documents`, `/wallet`) in `apps/subject-portal` (`TabBar.vue`) clipped off-screen on `320px-375px` viewports without scrolling the active `[aria-current]` item into view on initial mount or route navigation.
2. **Silent Horizontal Overflow:** Neither `ClinicalContextBar.vue` nor `TabBar.vue` surfaced visual start/end scroll cues when horizontal overflow occurred, leaving off-screen ancestor crumbs and trailing portal tabs undiscoverable on touch devices that hide scrollbars.
3. **Lost Hierarchy Context & Tall Sticky Chrome on Staff Tablets:** `#shell-context-bar` in `apps/web/src/components/AppShell.vue` scrolled out of view during long eCRF, query, and monitoring workflows, while keeping `.app-header` permanently expanded across `641px-1279px` tablets alongside a sticky context bar would consume excessive vertical viewport space.
4. **Scrolling Desktop/Tablet Portal Rail:** `.portal-navigation` in `apps/subject-portal/src/style.css` scrolled away during long multi-page informed consent (`ConsentView`) and ePRO journeys at `>= 768px`.

## 2. Decision Drivers & Constraints

- **Shared, Reduced-Motion-Safe Scroll Alignment:** Reuse a single zero-dependency composable across `ClinicalContextBar` and `TabBar` that aligns active items horizontally without altering vertical page scroll, degrades safely in JSDOM/SSR environments, and honors `prefers-reduced-motion: reduce` (WCAG 2.2 SC 2.3.3).
- **Non-Blocking Overflow Affordances & Accessible Mobile Condensation:** Keep start/end overflow cues decorative (`aria-hidden="true"`, `pointer-events: none`) so underlying touch targets remain `>= 44x44px` (`>= 48px` in `apps/subject-portal`), and condense ancestor crumb prefixes (`Studies`, `Site `) via `.sr-only` below `768px` so accessible link names remain intact (WCAG 2.2 SC 2.5.8, SC 4.1.2).
- **WCAG 2.2 SC 2.4.11 & SC 1.4.10 Sticky Offset Coordination:** Coordinate sticky `.app-header` and `#shell-context-bar` heights through `apps/web/src/utils/stickyChromeOffset.ts` (`--shell-sticky-offset`, `--shell-header-height`, `--shell-context-bar-height`) so `scroll-padding-top` and sticky section headers never obscure focused controls ([ADR-2665](2026-11-24-dynamic-sticky-chrome-offset-and-unobscured-focus.md)), while releasing sticky chrome on short viewports (`max-height: 499px`) for 400% zoom reflow.
- **Strict Design Token & Iconography Compliance:** Use only shared semantic CSS tokens ([ADR-2675](2026-11-24-remediate-hardcoded-colors-and-enforce-css-var-lints.md)) and zero-emoji vector/Lucide iconography ([ADR-2328](2026-08-26-enforce-zero-emoji-lucide-iconography.md)), preserving `packages/ui` cold-boot bundle budgets ([ADR-2672](2026-11-24-decouple-heavy-ui-barrels-and-gate-cold-boot-bundle.md)).

## 3. Options Considered

### Option 1: Shared `useActiveScrollAlignment` Composable in `@cadence/ui`, Non-Blocking Edge Cues, Coordinated Sticky Context Bar + Tablet Header Collapse, and Sticky Tablet/Desktop Portal Rail (Selected)

- **Overview:**
  1. Export `useActiveScrollAlignment`, `alignActiveScrollItem`, `computeScrollOverflowState`, and `prefersReducedMotion` from `@cadence/ui` (`packages/ui/src/composables/useActiveScrollAlignment.js` and `packages/ui/index.d.ts`).
  2. Wire `ClinicalContextBar.vue` to `useActiveScrollAlignment`, render non-blocking SVG chevron + gradient fade start/end cues (`pointer-events: none`, `aria-hidden="true"`), and condense `< 768px` ancestor crumbs (`Studies` icon-only `>= 44x44px` pill and `Site ` prefix hidden via `.sr-only`).
  3. Make `#shell-context-bar` sticky (`top: var(--shell-header-height, 0px)`) in `AppShell.vue`, collapse `.app-header` (`is-header-collapsed`) on downward scroll across `641px-1279px` viewports (re-expanding on upward scroll, menu open, `focusin`, or `>= 1280px` resize), synchronize live context-bar height via `updateShellStickyContextBarHeight` / `clearShellStickyContextBarHeight` in `stickyChromeOffset.ts`, and release sticky positioning at `@media (max-height: 499px)`.
  4. Wire `apps/subject-portal` `TabBar.vue`, `navigation.js`, and `router/index.js` to `useActiveScrollAlignment` / `alignActiveScrollItem`, add mobile (`<= 767px`) `ChevronLeft` / `ChevronRight` edge cues inside `.nav-tabs-container` while keeping `.nav-tabs` as the bottom scroll container, and make `.portal-navigation` sticky at `>= 768px` (`@media (min-width: 768px)` with `@media (max-height: 499px)` release).
- **Pros:**
  - Eliminates duplicated scroll-alignment and overflow-detection math across staff and participant navigation surfaces.
  - Preserves study/site/participant orientation during vertical scroll while reclaiming tablet vertical space and keeping `--shell-sticky-offset` synchronized for WCAG 2.2 SC 2.4.11 focus visibility.
  - Preserves full accessible link names on condensed mobile breadcrumbs and baseline mobile `TabBar` geometry (`375x812` bottom docking and `overflow-x: auto`).
- **Cons:**
  - Requires `ResizeObserver` / `MutationObserver` coordination between `AppShell.vue` and `stickyChromeOffset.ts` so header collapse transitions keep sticky offsets accurate.

### Option 2: Native `Element.scrollIntoView()` Calls Per Component and Permanently Expanded Sticky Chrome

- **Overview:** Invoke `scrollIntoView({ inline: "center" })` separately inside `ClinicalContextBar.vue` and `TabBar.vue` without overflow cues, and stack a permanently expanded sticky `.app-header` above `#shell-context-bar` on all viewports.
- **Pros:**
  - Smaller component diff with no shared composable or header collapse state.
- **Cons:**
  - `scrollIntoView()` can trigger unintended vertical container scrolling during route transitions, provides no visual overflow affordance for off-screen items, leaves ancestor crumbs wide on `320px` screens, and consumes `> 100px` of permanent vertical chrome on `768px` tablets.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing horizontal active-item alignment and overflow-cue state in `@cadence/ui` while coordinating sticky `#shell-context-bar` offsets with `stickyChromeOffset.ts` and `641px-1279px` downward-scroll header collapse satisfies orientation, touch-target, focus-not-obscured, and reflow requirements across both applications without duplicating scroll math or violating semantic token gates.

## 5. Consequences & Trade-offs

- **Positive Impact:** Active crumbs and participant tabs automatically scroll into view on initial load (`auto`) and subsequent navigation (`smooth`, or `auto` under `prefers-reduced-motion: reduce`); non-blocking chevron + fade cues reveal clipped horizontal items; staff users retain sticky hierarchy context while reclaiming vertical tablet space on downward scroll; and tablet/desktop participants retain sticky left-rail navigation during long consent and ePRO flows.
- **Negative Impact / Technical Debt:** `AppShell.vue` maintains scroll-direction and `ResizeObserver` listeners to coordinate `.is-header-collapsed` with `stickyChromeOffset.ts`.
- **Mitigation Strategy:** Encapsulated sticky height calculation in unit-tested `stickyChromeOffset.ts` helpers (`isShortViewportChromeReleased`, `updateShellStickyContextBarHeight`, `clearShellStickyContextBarHeight`) and verified across unit and Playwright E2E accessibility suites at `320px` and `768px`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/components/index.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter "./packages/*" build`, `pnpm --filter ui test`, `pnpm --filter subject-portal test`, `pnpm --filter web test`, `pnpm lint`, `pnpm typecheck`, `pnpm build`, `pnpm build:demo`, Playwright E2E accessibility suites (`apps/web/tests/e2e/web-accessibility.spec.ts` and `apps/subject-portal/tests/e2e/portal-accessibility.spec.js`), and `pnpm cadence check` quality gates.
