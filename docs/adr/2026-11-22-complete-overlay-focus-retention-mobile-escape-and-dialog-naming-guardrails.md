# ADR-2822: Complete Overlay Focus Retention, Mobile Escape Dismissal, and Dialog Naming Guardrails

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 AA keyboard operability, focus order, focus not obscured, and programmatic dialog/control names), PRD-SYS-001 (21 CFR Part 11 submission dismissal locks)

---

## 1. Context & Problem Statement

Following the migration of `apps/web` raw overlays onto `ClinicalModal` and `ClinicalDrawer` (ADR-2821), four residual keyboard and screen-reader gaps remained across shared focus primitives, guided tours, mobile navigation, and legacy dialogs:

1. Advancing `OnboardingTour` from modal Step 1 (`aria-modal="true"`) to anchored non-modal Step 2 (`#nav-studies`) deactivated `useFocusTrap`, which unconditionally restored focus to the external trigger or dropped `document.activeElement` to `document.body`, and lacked a WCAG G59 shortcut to move focus directly to the anchored target.
2. Opening `#mobile-nav-menu` from `#btn-mobile-menu-toggle` in `AppShell.vue` ignored `Escape` key dismissal and did not restore focus to `#btn-mobile-menu-toggle` (WCAG 2.1.1 / G90 / ARIA14).
   3.Several migrated `ClinicalModal` forms (`CellGeneTherapyCocConsole`, `QualityCockpitView`, `RactMatrixViewer`, `DataLockView`, `ParticipantWalletView`, `CrcFormRenderer`, `CtmsView`, `CecAdjudicationConsole`, `ClinicalGanttVisualizer`, `KnowledgeView`, `ContextualHelpDrawer`) still relied on `vuejs-accessibility` suppressions or lacked dynamic `:dismissible="!busy"` locks during async Part 11/token/custody mutations (`KnowledgeView`, `StatisticalComputingConsole`, `CellGeneTherapyCocConsole`), and 15 legacy `useFocusTrap` `role="dialog"` containers lacked `aria-labelledby`.
3. `custom-vue/no-raw-overlays` in `eslint.config.mjs` did not yet enforce accessible names (`aria-labelledby` or `aria-label`) on native `role="dialog"` / `role="alertdialog"` elements.

## 2. Decision Drivers & Constraints

- **Modal-to-Non-Modal Focus Retention Without Obscuring Targets (PRD-CRF-015):** Anchored `OnboardingTour` steps must remain non-modal (`aria-modal` unset, `#nav-studies` non-`inert`) while retaining focus inside `.onboarding-popover-card` on step transitions and offering an explicit keyboard shortcut (`F6` / `.btn-tour-focus-target`) to inspect the anchored target (WCAG 2.4.3 / G59, avoiding F85).
- **LIFO Escape Dismissal Across Shell Navigation & Dialogs:** `#mobile-nav-menu` must participate in the shared `useEscapeClose` LIFO stack, collapse on `Escape`, update `aria-expanded="false"`, and return focus to `#btn-mobile-menu-toggle`.
- **Zero Unnamed Dialogs or Unlabeled Overlay Controls:** Every `ClinicalModal`, `ClinicalDrawer`, and native `role="dialog"` / `role="alertdialog"` across the repository (`131 / 131`) must expose a programmatic name, labeled controls with zero `vuejs-accessibility` suppressions inside clinical overlays, and dynamic `:dismissible="!busy"` guards during regulated mutations.
- **ToastHost Exemption Preserved (ADR-2545):** `ToastHost` remains exempt from `inert` and `aria-hidden="true"` so live clinical alerts stay audible above modal dialogs.

## 3. Options Considered

### Option 1: Extend `useFocusTrap` `restoreFocus` Resolution, Wire `OnboardingTour` & `AppShell`, Label All Overlay Controls, and Enforce Dialog Naming in `custom-vue/no-raw-overlays` (Selected)

- **Overview:** Allow `useFocusTrap` `restoreFocus` to accept a boolean, `Ref`, or getter (`false` suppresses trigger restoration when transitioning from modal to non-modal tour steps while preserving external trigger tracking for final dismissal); update `OnboardingTour.vue` and `OnboardingChecklistDrawer.vue` to retain popover focus across step changes and expose `.btn-tour-focus-target` (`F6`); wire `useEscapeClose` onto `#mobile-nav-menu` in `AppShell.vue`; label all modal/drawer controls, remove remaining modal `vuejs-accessibility` suppressions, bind `:dismissible="!busy"` on async mutation modals, add `aria-labelledby` across all 15 native `role="dialog"` modals, and enforce `missingDialogName` in `eslint.config.mjs` paired with Playwright `Tab`/`Shift+Tab`/`Escape`/focus-return checks in `web-accessibility.spec.ts`.
- **Pros:**
  - ✅ Eliminates focus drops to `document.body` on `OnboardingTour` step transitions while preserving non-modal anchored targets and restoring trigger focus on close.
  - ✅ Brings unnamed/untrapped overlays to `0 / 118` in `apps/web` (`0 / 131` repo-wide) with compile-time ESLint AST enforcement and real-browser Playwright focus-containment verification.
- **Cons:**
  - ❌ Requires rebuilding `packages/ui/dist` (`pnpm --filter ui build`) and maintaining updated TypeScript declarations in `packages/ui/index.d.ts`.

### Option 2: Keep `useFocusTrap` Active on Anchored `OnboardingTour` Steps

- **Overview:** Force `aria-modal="true"` and `useFocusTrap` onto every `OnboardingTour` step, including anchored workspace steps.
- **Pros:**
  - ✅ Avoids `useFocusTrap` deactivation between Step 1 and Step 2.
- **Cons:**
  - ❌ Marks background navigation targets (`#nav-studies`) `inert` and `aria-hidden="true"`, preventing users from interacting with the highlighted workspace control during guided tours.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Supporting reactive/getter `restoreFocus` suppression in `useFocusTrap` cleanly decouples modal trap release from final overlay dismissal, preserving non-modal anchored tour interactivity while guaranteeing 100% keyboard focus retention, `Escape` dismissal, programmatic naming, and Playwright containment coverage across `apps/web`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full WCAG 2.2 AA keyboard operability across `/etmf`, `/training` (`showBatchModal` and `showBulkSignModal`), `/ctms`, `/data-lock`, `OnboardingTour`, and `#mobile-nav-menu`, backed by ESLint AST (`custom-vue/no-raw-overlays`) and Playwright E2E containment gates.
- **Negative Impact / Technical Debt:** Native `role="dialog"` / `role="alertdialog"` elements outside `packages/ui` now fail lint unless `aria-label` or `aria-labelledby` is explicitly bound.
- **Mitigation Strategy:** Prefer `ClinicalModal` and `ClinicalDrawer` from `packages/ui`, which wire `role`, `aria-modal`, `aria-labelledby`, `useFocusTrap`, and `dismissible` automatically.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `eslint.config.mjs`, `docs/components/modal.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm -r lint`, `pnpm --filter web typecheck`, `pnpm -r test`, `pnpm --filter web test:e2e`, `pnpm build`, `pnpm --filter web build:demo`, and `pnpm cadence check`.
