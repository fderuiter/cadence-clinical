# ADR-2651: Shared Workspace Shell Primitives, Consolidated Study Chrome, and Three-Tier Responsive Panes

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA responsive reflow across 320px–1440px, 44px–48px touch targets, focus containment, and label-in-name parity), PRD-SYS-001 (21 CFR Part 11 clinical workspace integrity, UX-2 zoom paths, and UX-3/UX-5 universal verbs)

---

## 1. Context & Problem Statement

Tablet site coordinators (`768x1024`, `1024x768`), mobile clinical monitors (`320x640`, `390x844`), and wide-desktop data managers (`1280x800`, `1440x900`) faced four structural workspace bottlenecks across `packages/ui` and `apps/web`:

1. **Stacked Sticky Top Chrome on Study Routes:** Entering `/studies/:studyCode` stacked `AppShell.vue`'s global header (`56px+`) above `StudyCanvasView.vue`'s sticky `.topbar` (`50px+`), consuming over `106px` of vertical viewport budget before clinical rows appeared.
2. **Rigid Fixed-Width Multi-Column Sidebars Below `1280px`:** `StudyCanvasView.vue` (`240px` navigator + `320px` inspector), `DocumentManagerView.vue` (`280px` binder tree, including when embedded inside `StudyCanvasView`), `DocumentsHubView.vue` (`260px` folder tree + `360px` inspector), and `KnowledgeView.vue` (`240px` category rail + `340px` article list + reader) kept sidebars inline on `768px` and `1024px` tablets or compressed primary clinical workspaces below `480px`, while narrow `320px`–`390px` viewports clipped toolbar buttons in `DocumentGrid.vue`.
3. **Unscoped Legacy Shell CSS Overrides:** `apps/web/src/style.css` retained unscoped `.app-header`, `.app-sidebar`, `.persona-select`, `.app-container`, and global `main` padding selectors that collided with component-scoped workspace shells.
4. **Inconsistent Decorative Unicode Glyphs:** Shell headers, collapsible rails, pane triggers, and document/knowledge workspaces mixed platform-dependent Unicode glyphs (`☰`, `✕`, `▸`, `▾`, `📁`, `📄`) instead of uniform `1.75px`-stroke vector icons paired with `Inter` and `JetBrains Mono` clinical typography.

## 2. Decision Drivers & Constraints

- **Single `<= 52px` Sticky Primary Bar (PRD-CRF-015):** Both standard routes and `/studies/:studyCode` routes must present a single sticky primary bar `<= 52px` tall across `320x640` through `1440x900`, while preserving standalone `.topbar` rendering when `StudyCanvasView` mounts outside `AppShell` in isolated unit tests.
- **Three-Tier Responsive Workspace Contract:**
  - **Wide desktop (`>= 1280px`):** Inline navigation and inspector panes (`320px` `--shell-inspector-width`).
  - **Tablet (`768px`–`1279px`):** `60px` `ClinicalIconRail` (`StudyCanvasView`) or slide-over `ClinicalDrawer` (`position="left"` / `position="right"`) preserving `>= 480px` `#canvas-workspace` inner width at `768x1024` and `1024x768`.
  - **Phone (`< 768px`):** Bottom-sheet `ClinicalDrawer` (`position="bottom"` / `mobileBottomSheet`, capped at `--drawer-bottom-max-height: 90dvh`) with wrappable action toolbars and zero clipped buttons at `320x640` and `390x844`.
- **Kernel & Part 11 Invariants (PRD-SYS-001):** UX-2 zoom paths (`#canvas-zoom-path`), UX-3/UX-5 universal verbs (`Trace`, `Impact`, `Act`, `Explain`, `Assist`), `NodeRef` deep-links, and 21 CFR Part 11 signatures must remain unchanged, and `apps/subject-portal` plus backend services remain untouched.

## 3. Options Considered

### Option 1: Build Shared `ClinicalIconRail`, `ClinicalSplitWorkspace`, Bottom-Sheet `ClinicalDrawer`, Shell Tokens, and `lucide-vue-next` Primitives in `packages/ui` (Selected)

- **Overview:** Pin `lucide-vue-next` (`1.75px` uniform stroke, `aria-hidden="true"`) in `pnpm-workspace.yaml`, `packages/ui`, and `apps/web` (externalized from `packages/ui/dist`); add `--font-sans` (`Inter`), `--font-mono` (`JetBrains Mono`), `--shell-header-height` (`48px`), `--shell-header-max-height` (`52px`), `--shell-rail-collapsed-width` (`60px`), `--shell-rail-expanded-width` (`224px`), `--shell-sidebar-width` (`280px`), `--shell-inspector-width` (`320px`), `--shell-workspace-min-width` (`480px`), `--breakpoint-wide` (`1280px`), `--breakpoint-mobile-sheet` (`480px`), `--touch-hit-area-min` (`44px`), and `--drawer-bottom-max-height` (`90dvh`) plus `.clinical-action-toolbar` and `.clinical-mono-id` to `packages/ui/tokens.css` and `packages/ui/responsive.css`; extend `ClinicalDrawer.vue` with `position="bottom"` and `mobileBottomSheet`; export `useWorkspaceTier`, `ClinicalIconRail`, and `ClinicalSplitWorkspace` from `packages/ui`; remove unscoped shell overrides from `apps/web/src/style.css`; project `StudyCanvasView`'s `#canvas-zoom-path` and `#btn-ai-assistance` into `AppShell`'s `#shell-study-context` via `<Teleport>`; and migrate `StudyCanvasView`, `CanvasInspector`, `DocumentManagerView` (`TmfBinderTree`, `DocumentGrid`), `DocumentsHubView` (`DocumentsFolderTree`), and `KnowledgeView` onto the shared primitives.
- **Pros:**
  - ✅ Eliminates duplicate sticky bars on study routes and reclaims `>= 480px` clinical workspace width on `768px` and `1024px` tablets.
  - ✅ Centralizes focus-trapped slide-over and `<= 90dvh` bottom-sheet behavior in `packages/ui` (`ClinicalDrawer` + `ClinicalSplitWorkspace`) without duplicating media-query plumbing across views.
  - ✅ Standardizes accessible `1.75px` `lucide-vue-next` iconography and `JetBrains Mono` clinical identifiers (`NodeRef`, SHA-256 digests, protocol versions, artifact IDs).
- **Cons:**
  - ❌ Adds `lucide-vue-next` as a catalog dependency of `packages/ui` and `apps/web`, requiring Rollup externalization in `packages/ui/vite.config.js` so `packages/ui/dist` remains dependency-free.

### Option 2: Add Per-View `@media` Overrides Without Shared `packages/ui` Primitives

- **Overview:** Write bespoke `@media (max-width: 1279px)` and `@media (max-width: 767px)` CSS and drawer state inside each view in `apps/web`.
- **Pros:**
  - ✅ Avoids adding new components to `packages/ui`.
- **Cons:**
  - ❌ Duplicates breakpoint state, focus trapping, and bottom-sheet styling across four views, leaves embedded `DocumentManagerView` inside `StudyCanvasView` uncoordinated, and fails `custom-vue/no-raw-overlays` AST guardrails if custom drawer backdrops are introduced.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing `useWorkspaceTier`, `ClinicalIconRail`, `ClinicalSplitWorkspace`, bottom-sheet `ClinicalDrawer`, shell geometry tokens, and `lucide-vue-next` `1.75px` vector icons in `packages/ui` delivers consistent desktop/tablet/mobile workspace ergonomics, single-bar `<= 52px` study chrome, and full WCAG 2.2 AA compliance across standalone and embedded clinical views.

## 5. Consequences & Trade-offs

- **Positive Impact:** Single `<= 52px` sticky header across standard and study routes; `60px` `ClinicalIconRail` + slide-over `CanvasInspector` preserving `>= 480px` `#canvas-workspace` on `768x1024` and `1024x768` tablets; `<= 90dvh` bottom sheets and unclipped wrappable toolbars on `320x640` and `390x844` phones; zero nested `<main>` landmarks inside `AppShell`.
- **Negative Impact / Technical Debt:** Inertia-free `window.matchMedia` stubs in unit test setups that return `matches: false` for all queries require complementary `min-width` / `max-width` checks in `useWorkspaceTier` so unit tests default deterministically to `"desktop"` unless a viewport width mock is installed.
- **Mitigation Strategy:** `useWorkspaceTier` evaluates paired `(min-width)` and `(max-width)` media queries with `window.innerWidth` fallback, covered by `packages/ui/tests/shared_components.test.js` and `apps/web/tests/components/WorkspaceShellPrimitives.spec.js`.
- **Compliance & Regulatory Impact:** Preserves 21 CFR Part 11 electronic signature modals, `X-Change-Reason` audit flows, `NodeRef` citations, UX-2 zoom paths, and UX-3/UX-5 universal verbs (PRD-SYS-001) alongside WCAG 2.2 Level AA focus containment, `>= 44px` touch targets, `aria-hidden="true"` decorative vector icons, and zero `axe-core` serious/critical violations (PRD-CRF-015).

## 6. Implementation & Verification

- **Affected Repositories / Services:** `pnpm-workspace.yaml`, `packages/ui`, `apps/web`, `docs/components/`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter web typecheck`, `pnpm --filter web test:e2e`, 6-viewport headless Chromium geometry verification (`320x640` to `1440x900`), `pnpm lint`, `pnpm build`, `pnpm build:demo`, `pnpm docs:build`, and `pnpm cadence check`.
