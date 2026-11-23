# ADR-2654: Shared Viewport Tier Composable, 52px Consolidated Shell Chrome, and Adaptive Slide-Over Inspector Drawer

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (regulated clinical workspace ergonomics and GxP context continuity), PRD-CRF-015 (WCAG 2.2 Level AA keyboard focus containment, `Escape` dismissal, touch target sizing, and tabular alignment)

---

## 1. Context & Problem Statement

Auditing `apps/web` across Tier-1 clinical tablet and compact laptop viewports (`768px` to `1279px`) surfaced four ergonomic bottlenecks for bedside study coordinators and field monitors:

1. **Stacked sticky chrome (`103px` to `198px`):** `AppShell.vue` wrapped `.app-header` across two rows above `860px` while `StudyCanvasView.vue` stacked a second sticky `.topbar` (`51px`), leaving little vertical room when an on-screen tablet keyboard opens.
2. **Cramped three-pane docking (`383px` workspace width):** `StudyCanvasView.vue` locked `.panes` to `14rem minmax(0, 1fr) 20rem` down to `961px` and `packages/ui/responsive.css` promoted desktop grids at `1024px`, squeezing the central workspace to `383px` whenever `CanvasInspector.vue` auto-expanded on selection.
3. **Unpinned eCRF identifiers and save controls:** `EcrfItemTable.vue` (`min-width: 52rem`) and `ParticipantFormsPanel.vue` (`min-width: 34rem`) scrolled primary row headers and the `.ecrf-form-row` save action bar off-screen during horizontal and vertical scrolling.
4. **Sub-`44px` staff controls and auto-zooming touch inputs:** Fifteen interactive controls across `AppShell`, `StudyCanvasView`, `CanvasInspector`, `EcrfLiveWorkspace`, and `CtmsLiveWorkspace` measured `18px` to `34px` tall, and `<16px` `<input>` / `<select>` controls triggered automatic viewport zoom on iPadOS Safari.

## 2. Decision Drivers & Constraints

- **Preserve Clinical Canvas UX Invariants (`UX-1`, `UX-2`, `UX-3`, `UX-5`):** `#canvas-zoom-path` and `#btn-ai-assistance` must remain visible and addressable (`UX-2`) without stacking a second sticky bar, and resizing between tablet (`768px–1279px`) and wide desktop (`>=1280px`) must preserve active `NodeRef` selection and inspector verb state (`UX-3`, `UX-5`).
- **Two-Profile Touch Ergonomics & Zero Auto-Zoom:** Keep `--touch-target-min: 48px` for participant-facing flows (`apps/subject-portal`) while standardizing `--touch-target-staff: 44px` (`--semantic-touch-target-staff`) and `16px` (`1rem`) minimum input/select font size across clinical staff workspaces (`apps/web`).
- **Zero Emojis / Vector Iconography:** Use `lucide-vue-next` (`16px` or `20px`, `1.75px` stroke, `aria-hidden="true"`) across shared drawers, shell actions, and rail navigation with zero Unicode emojis or text dingbats.
- **WCAG 2.2 AA Focus & Contrast Compliance:** Slide-over drawers must trap focus via `useFocusTrap`, dismiss on `Escape`, maintain `>= 4.5:1` text contrast and `>= 3:1` control border contrast (`--semantic-color-border-control`), and keep one `<main>` landmark.

## 3. Options Considered

### Option 1: Shared `useViewportTier` Composable, Consolidated `52px` `AppShell` Teleport Slot, `60px` Rail, and `ClinicalDrawer` Slide-Over Inspector (Selected)

- **Overview:**
  - Add `--breakpoint-wide: 1280px`, `--shell-chrome-height: 52px`, and `--touch-target-staff: 44px` (plus `--semantic-*` aliases) to `packages/ui/tokens.css`.
  - Align desktop grid helpers in `packages/ui/responsive.css` to `1280px` and add `.sticky-col-start`, `.sticky-col-corner`, `.sticky-table-footer`, `.sticky-action-bar`, and `.touch-target-staff`.
  - Export `useViewportTier` from `packages/ui` (`mobile` `<768px`, `tablet` `768px–1279px`, `desktop` `>=1280px`) and bind `ClinicalDrawer.vue` widths to `--semantic-drawer-width-sm` / `--semantic-drawer-width-md` / `--semantic-drawer-width-xl` with a `lucide-vue-next` `X` close icon.
  - Consolidate `AppShell.vue` into one non-wrapping `<=52px` sticky header and teleport `#canvas-zoom-path` and `#btn-ai-assistance` from `StudyCanvasView.vue` into `AppShell` host containers.
  - Render `#canvas-navigator` as a collapsible `60px` (`3.75rem`) Lucide icon rail and mount `CanvasInspector` inside `ClinicalDrawer` across `768px–1279px`, docking all three panes (`14rem`, fluid workspace, `20rem` / `2.75rem`) at `>=1280px`.
  - Pin primary identifier headers and `.ecrf-form-row` save controls in `EcrfItemTable.vue` and `ParticipantFormsPanel.vue`, and enforce `>=44px` height and `>=16px` touch input text across audited staff controls.
- **Pros:**
  - Keeps sticky top chrome at or below `52px` across `768px` to `1280px+` while preserving `UX-2` zoom-path addressability.
  - Guarantees `>=480px` workspace inner width across `768px` to `1279px` even when inspecting a participant or `NodeRef`.
  - Centralizes viewport tier boundaries, drawer width tokens, and sticky table helpers in `packages/ui` for reuse across clinical workspaces.
- **Cons:**
  - Requires Vue `<Teleport>` coordination between `AppShell.vue` and `StudyCanvasView.vue` with a standalone fallback when `StudyCanvasView` is mounted outside `AppShell` in isolated unit tests.

### Option 2: Per-View CSS Media Queries Only Without Shared Viewport Composable or `ClinicalDrawer` Reuse

- **Overview:** Hide `.topbar` on narrow viewports with CSS media queries and build a custom fixed overlay inside `StudyCanvasView.vue` for the inspector.
- **Pros:**
  - Avoids updating `packages/ui` exports.
- **Cons:**
  - Hiding `.topbar` violates `UX-2` (persistent zoom path), duplicates focus-containment and `Escape`-dismissal logic already hardened in `ClinicalDrawer` (ADR-2544, ADR-2821), and leaves `responsive.css` `1024px` desktop breakpoints out of sync with the `1280px` three-pane threshold.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Sharing `useViewportTier`, `1280px` / `52px` / `44px` tokens, sticky table utilities, and `ClinicalDrawer` width bindings in `packages/ui` eliminates stacked chrome and sub-`480px` tablet workspaces while upholding `UX-2`, `UX-3`, and WCAG 2.2 Level AA focus containment.

## 5. Consequences & Trade-offs

- **Positive Impact:** Tablet and compact laptop users (`768px–1279px`) gain a `<=52px` single sticky header, a `>=480px` workspace alongside a `60px` collapsible stage/section rail, focus-trapped slide-over `NodeRef` inspection, pinned eCRF/matrix row identifiers and save bars, and `>=44px` / `16px` touch-safe controls.
- **Negative Impact / Technical Debt:** `StudyCanvasView.vue` maintains conditional inline vs. `ClinicalDrawer` mounting keyed off `useViewportTier().isDesktop`, so `inspectorVerb` is lifted to `StudyCanvasView` (`v-model:verb`) to preserve active verb state across viewport tier transitions.
- **Mitigation Strategy:** Dedicated unit tests in `packages/ui/tests/useViewportTier.test.js` and `apps/web/tests/views/TabletWorkspaceLayout.spec.js` verify tier transitions, teleported zoom-path controls, `ClinicalDrawer` focus/Escape behavior, state preservation across `1024px` <-> `1280px` resizes, and sticky table classes.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `docs/ux/CLINICAL_CANVAS.md`, `docs/components/index.md`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm -r lint`, `pnpm build`, `pnpm docs:build`, headless Chromium viewport audits (`768px`–`1280px`), and `pnpm cadence check --base origin/main`.
