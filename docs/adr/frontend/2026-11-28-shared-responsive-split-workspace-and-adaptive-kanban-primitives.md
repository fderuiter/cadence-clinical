# ADR-FRONTEND-20261128-shared-responsive-split-workspace-and-adaptive-kanban-primitives: Shared Responsive Split Workspace and Adaptive Kanban Primitives

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Nine master-detail and main-inspector clinical workspaces (`KnowledgeView.vue`, `ProtocolTrainingConsole.vue` via `TrainingModulesReaderTab.vue`, `DocumentManagerView.vue` with `TmfBinderTree.vue`, `ImagingView.vue`, `CecAdjudicationConsole.vue`, `LaySummaryPortal.vue`, `CrfAuthoringCanvas.vue`, `ConsentAuthoringView.vue`, and `RulesView.vue`) previously stacked their master list above the detail or editor pane below `1024px`–`1200px` without focus handoff or viewport containment, forcing clinicians on tablets (`768x1024`, `1024x768`) and mobile devices (`320x640`, `390x844`) to scroll past multi-screen lists to locate opened content. In parallel, `ImagingView.vue`, `CecAdjudicationConsole.vue`, and `LaySummaryPortal.vue` rendered clickable `div` selection cards with `vuejs-accessibility` lint suppressions; `ImagingView.vue`, `CecAdjudicationConsole.vue`, and `CrfAuthoringCanvas.vue` used unbounded `1fr` grid tracks or `align-items: flex-start` / `start` rules that caused horizontal overflow at `320x640` and `390x844`; and `TicketsView.vue` forced a 5-column horizontal scroll Kanban grid with `max-height: calc(100vh - 320px)` that overflowed narrow viewports and crushed `.column-cards` on short `1024x416` landscape viewports (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- **Driver 1:** WCAG 2.2 AA compliance across SC 1.4.10 Reflow (`320x640` and `390x844` zero horizontal overflow), SC 2.1.1 Keyboard (native `<button type="button">` selection controls with `aria-current="true"` and zero `vuejs-accessibility` suppressions), SC 2.4.3 Focus Order (deterministic focus entry into compact drawers and trigger focus restoration on close or `Escape`), and SC 2.4.11 Focus Not Obscured.
- **Driver 2:** Preservation of unsaved authoring state and 21 CFR Part 11 electronic signature reason capture (`ReasonModal` / `ClinicalModal` LIFO `Escape` stack coordination per ADR-2674) inside stateful editors (`CrfAuthoringCanvas.vue`, `ConsentAuthoringView.vue`, and `RulesView.vue`).
- **Driver 3:** Preservation of the `StudyCanvasView.vue` non-modal inline inspector (`UX-3` in `docs/ux/CLINICAL_CANVAS.md`) and enforcement of the initial JS bundle budget (`<= 250 KiB`).

## 3. Options Considered

### Option 1: Shared `ClinicalSplitWorkspace` Primitive, `useResponsiveSplitWorkspace` Composable, and Adaptive `ClinicalTabs` Kanban Stages (Selected)

- **Overview:** Introduce `ClinicalSplitWorkspace.vue` and `useResponsiveSplitWorkspace.js` in `packages/ui` built directly on `ClinicalDrawer.vue`. Render detail panes inline in `minmax(0, 1fr)` / `align-items: stretch` CSS Grid tracks at `>= 1280px`, inside focus-trapped slide-over drawers at `641px`–`1279px`, and inside `90dvh` safe-area bottom sheets (`mobileBottomSheet`) at `<= 640px`, with `keepMounted` state retention for stateful authoring workspaces. Pair `TicketsView.vue` Kanban mode below `1280px` with a `ClinicalTabs` stage selector and `max(240px, calc(100dvh - 200px))` column height bounds.
- **Pros:**
  - Centralizes responsive breakpoint coordination (`1280px` compact drawer, `640px` bottom sheet), focus capture/restoration, `keepMounted` state preservation, and LIFO `Escape` modal stacking in one reusable `packages/ui` primitive.
  - Eliminates horizontal overflow across `320x640` and `390x844` and keeps `.column-cards` height at or above `160px` on `1024x416` landscape viewports.
  - Replaces suppressed `div` selection cards with native `<button type="button">` controls and `aria-current="true"` semantics.
- **Cons:**
  - Adds one component (`ClinicalSplitWorkspace`) and one composable (`useResponsiveSplitWorkspace`) to the `packages/ui` public surface and type declarations.

### Option 2: Per-View Custom Media Queries and Ad-Hoc Drawer Wrappers

- **Overview:** Duplicate `matchMedia` listeners, `ClinicalDrawer` wrappers, and focus restoration logic independently inside each of the nine clinical views.
- **Pros:**
  - Avoids expanding `packages/ui` exports.
- **Cons:**
  - Duplicates breakpoint math, trigger focus tracking, and `keepMounted` / `ReasonModal` LIFO `Escape` wiring across nine workspaces, increasing drift risk and maintenance cost.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing responsive split-pane behavior in `ClinicalSplitWorkspace` and `useResponsiveSplitWorkspace` on top of `ClinicalDrawer` guarantees uniform WCAG 2.2 AA reflow, keyboard focus order, `90dvh` mobile bottom-sheet ergonomics, `keepMounted` editor state preservation, and Part 11 LIFO `Escape` coordination across all nine workspaces without duplicating media-query or focus-handoff code, while leaving `StudyCanvasView.vue` (`UX-3`) untouched.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinicians and participants on `320px`–`1279px` viewports immediately enter focused slide-over drawers or `90dvh` bottom sheets upon selecting an item and return to the exact trigger button on dismissal; `TicketsView.vue` Kanban stages switch cleanly via `ClinicalTabs` below `1280px` with uncrushed `100dvh`-bounded card regions at `1024x416`.
- **Negative Impact / Technical Debt:** Stateful editors using `keepMounted` (`CrfAuthoringCanvas.vue`, `ConsentAuthoringView.vue`, `RulesView.vue`) keep their editor DOM mounted while hidden so unsaved form inputs survive drawer toggles and nested `ReasonModal` dialogs.
- **Mitigation Strategy:** Enforce `packages/ui` export, typecheck, and consumer reachability gates (`design_system_harmony.test.js`, `ui_consumer_reachability.test.js`) alongside automated Vitest and multi-viewport Playwright reflow and focus-restoration suites.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui` (`ClinicalSplitWorkspace.vue`, `ClinicalDrawer.vue`, `useResponsiveSplitWorkspace.js`, `responsive.css`, `index.d.ts`, `README.md`), `apps/web` (`KnowledgeView.vue`, `TrainingModulesReaderTab.vue`, `DocumentManagerView.vue`, `TmfBinderTree.vue`, `ImagingView.vue`, `CecAdjudicationConsole.vue`, `LaySummaryPortal.vue`, `CrfAuthoringCanvas.vue`, `FormSectionContainer.vue`, `ConsentAuthoringView.vue`, `RulesView.vue`, `TicketsView.vue`), and `docs/components/index.md`.
- **Verification Plan:** Verified via `pnpm --filter ui test` (`clinical_split_workspace.test.js`, `design_system_harmony.test.js`, `ui_consumer_reachability.test.js`), `pnpm --filter ui typecheck`, `pnpm --filter web exec vitest run`, `pnpm lint`, `pnpm cadence check adrs`, and Playwright multi-viewport reflow and focus-restoration E2E tests (`apps/web/tests/e2e/split-workspace-reflow.spec.ts`) across `320x640`, `390x844`, `768x1024`, `1024x768`, and `1024x416`.
