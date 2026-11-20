# ADR-2545: Centralize Focus Containment Primitives

- **Status:** Accepted
- **Date:** 2026-11-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.1 AA keyboard focus containment and accessibility auditing), PRD-SYS-001 (21 CFR Part 11 electronic signature and mandatory lock workflows)

---

## 1. Context & Problem Statement

Even after introducing shared focus-trap primitives in `packages/ui` (ADR-2544), overlays across `packages/ui`, `apps/web`, and `apps/subject-portal` lacked automatic background sibling `inert` and `aria-hidden="true"` isolation up to `document.body`, a shared `ClinicalDrawer` slide-over primitive, live-alert `ToastHost` exemption above modal backdrops, and removal of duplicate composable shims in `apps/web/src/composables/`. Inline `ClinicalQueryPanel.vue` also trapped `Tab` navigation inside an inline region, violating WCAG 2.2 SC 2.1.2, while several overlays retained nested `<main>` landmarks or `vuejs-accessibility` lint suppressions.

## 2. Decision Drivers & Constraints

- **WCAG 2.1 / 2.2 AA Focus & Inert Isolation (PRD-CRF-015):** Active modal dialogs and drawers must isolate background `document.body` siblings with `inert` and `aria-hidden="true"` while keeping live status/alert containers (`[data-focus-trap-exempt="true"]`, `.toast-host`, `#toast-container`) audible and visible, and inline regions (`ClinicalQueryPanel`, anchored `OnboardingTour` steps) must not trap unmodified `Tab`.
- **21 CFR Part 11 Lock Guardrails (PRD-SYS-001):** Mandatory PIN entry modals and active file-upload dialogs must contain focus while dynamically disabling `Escape` and backdrop dismissal.
- **Zero Duplicate Primitives or Lint Suppressions:** All overlay focus containment and LIFO `Escape` coordination must come from `packages/ui` without `vuejs-accessibility` suppressions or nested `<main>` landmarks.

## 3. Options Considered

### Option 1: Centralize Background `inert` Reconciliation, `ClinicalDrawer`, and LIFO `Escape` Stacking in `packages/ui` (Selected)

- **Overview:** Extend `useFocusTrap` in `packages/ui` to reconcile `inert` and `aria-hidden="true"` across ancestor siblings up to `document.body` (exempting `[data-focus-trap-exempt="true"]` live-alert hosts), export `trapTabFocus(event, container)` alongside `getFocusableElements`, deduplicate `Tab` and `Escape` keyboard events via `WeakSet` tracking and `defaultPrevented` checks across `window`/`document` listeners, coordinate `useEscapeClose` with `useFocusTrap` via a shared monotonic LIFO stack, import compiled `packages/ui/dist/index.css` in `apps/web/src/style.css`, introduce `ClinicalDrawer.vue`, enhance `ClinicalModal.vue` (`dismissible`, `closeOnBackdrop`, `initialFocus`, `restoreFocus`, `teleport`), retire `apps/web/src/composables/{useFocusTrap,useEscapeClose}.js` and `apps/subject-portal/src/components/focus-trap.js` (including inline focusable selector fallbacks in `apps/subject-portal/src/features/signature.js`), free `Tab` exit from inline `ClinicalQueryPanel.vue`, and remove all overlay `vuejs-accessibility` suppressions.
- **Pros:**
  - ✅ Satisfies PRD-CRF-015 and PRD-SYS-001 across stacked modals, drawers, anchored tours, and inline panels.
  - ✅ Preserves live toast announcements by teleporting `ToastHost.vue` to `document.body` with `data-focus-trap-exempt="true"`.
- **Cons:**
  - ❌ Requires rebuilding `packages/ui` (`pnpm --filter ui build`) before running downstream `apps/web` tests that resolve `packages/ui/dist`.

### Option 2: Manage `inert` and `aria-hidden` Per-Application in App Shells

- **Overview:** Toggle `inert` manually on `#app` inside `apps/web` and `apps/subject-portal` stores whenever individual modals open.
- **Pros:**
  - ✅ Minimal changes to `packages/ui`.
- **Cons:**
  - ❌ Fails to handle stacked overlays, inline test harnesses, non-teleported drawers, or shared `packages/ui` components uniformly.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing reactive `inert`/`aria-hidden` sibling isolation, `ClinicalDrawer`, `ClinicalModal`, and LIFO `Escape` coordination in `packages/ui` enforces WCAG 2.1/2.2 AA compliance and 21 CFR Part 11 dismissal locks uniformly across `packages/ui`, `apps/web`, and `apps/subject-portal`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Single shared source of truth in `packages/ui` for modal and drawer accessibility, zero `vuejs-accessibility` suppressions across overlays, audible/visible live alerts over modal backdrops, and non-trapping inline query panels.
- **Negative Impact / Technical Debt:** Persistent in-DOM overlays in `apps/subject-portal` (`SyncDrawer`, `SignatureModal`) still bind explicit closed-state `:inert` and `:aria-hidden` attributes alongside `useFocusTrap`.
- **Mitigation Strategy:** Covered by automated Vitest unit suites in `packages/ui`, `apps/web`, and `apps/subject-portal` plus Playwright + axe-core E2E keyboard tests.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/components/modal.md`, `docs/components/index.md`.
- **Verification Plan:** Verified via `pnpm -r lint`, `pnpm --filter web typecheck`, `pnpm -r test`, `pnpm --filter subject-portal test:e2e`, and `pnpm cadence check`.
