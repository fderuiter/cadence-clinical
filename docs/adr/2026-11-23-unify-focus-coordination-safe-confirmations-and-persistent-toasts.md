# ADR-2653: Unify Modal Focus Coordination, Safe Confirmation Dialogs, and Persistent Accessible Toasts

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 explicit audit justification and regulated action safeguards), PRD-CRF-015 (WCAG 2.2 Level AA SC 2.1.1 Keyboard, SC 2.1.2 No Keyboard Trap, SC 2.2.1 Timing Adjustable, SC 2.4.3 Focus Order, SC 4.1.2 Name, Role, Value, SC 4.1.3 Status Messages, and WAI-ARIA `alertdialog` pattern)

---

## 1. Context & Problem Statement

Audits across `packages/ui`, `apps/web`, and `apps/subject-portal` surfaced three systemic overlay accessibility and regulated workflow defects:

1. **Occluded Focus Traps and Orphaned Focus Restoration:** `AppShell.vue` and standalone `StudyCanvasView.vue` toggled the `Ctrl+K` / `Cmd+K` command palette without consulting `useFocusTrap`'s active modal stack, while `CanvasPalette.vue` rendered inline at `z-index: 50` instead of teleporting to `document.body` at the `z-index: 9999` modal layer. Pressing `Ctrl+K` while a `ClinicalModal` or `ClinicalDrawer` was open pushed a hidden focus trap onto the stack and marked the visible front dialog `inert`. In addition, `trapTabFocus` excluded interactive action/dismiss controls inside `[data-focus-trap-exempt="true"]` toast regions while a modal was open, and `deactivate()` dropped focus to `document.body` whenever the originating trigger became hidden or unmounted (such as closing `#mobile-nav-menu` or `CommandPaletteOverlay` ahead of a cancelled route-leave confirmation).
2. **Unsafe Confirmation Defaults and Pre-Filled Part 11 Prompts:** `ConfirmationDialogHost.vue` focused `.btn-confirm` on open and `confirm.js` defaulted `variant` to `"danger"` while overwriting concurrent requests instead of queueing them. Pressing `Enter` once on an unsaved eCRF discard warning immediately discarded draft clinical data. `ClinicalModal.vue` also lacked an explicit `ariaDescribedby` prop for `role="alertdialog"` descriptions, prompt inputs lacked `<form>` submission and validation-error refocusing, and two 21 CFR Part 11 justification prompts (`SynonymImportDrawer.vue` and `LabGovernanceConsole.vue`) pre-populated `defaultValue` strings instead of requiring manual entry.
3. **Auto-Dismissed Error Alerts and Hit-Test Occlusion:** Both `apps/web` (`ToastHost.vue` + `toasts.js`) and `apps/subject-portal` (`widgets/toast.js` + `style.css`) auto-dismissed `error` alerts on short timers without hover/focus pause support, announced routine status messages with `assertive` live-region semantics or overwrote a single `#toast-container` node, rendered 16 untitled web toasts with empty `<strong class="toast-title">` nodes, and left hidden `.toast-notification` elements hit-testable (`opacity: 0` without `pointer-events: none; visibility: hidden;`) over bottom action buttons in the participant portal.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA & WAI-ARIA Conformance (PRD-CRF-015):** Error alerts must persist until dismissed (`SC 2.2.1`), timed notifications must pause on pointer hover or keyboard focus (`SC 2.2.1`), `role="alert"` (`aria-live="assertive"`) must be separated from `role="status"` (`aria-live="polite"`) (`SC 4.1.3`), `role="alertdialog"` confirmations must reference their message via `aria-describedby` and focus the non-destructive cancel control first (`SC 2.4.3`, WAI-ARIA `alertdialog`), modal `Tab` cycling must reach exempt toast controls (`SC 2.1.1`, `SC 2.1.2`), and closing overlays must always restore focus to a visible element (`SC 2.4.3`).
- **21 CFR Part 11 Data Integrity & Explicit Justification (PRD-SYS-001):** Unsaved eCRF discard confirmations must never execute destructive actions on a single accidental `Enter`, concurrent confirmation/prompt requests must queue in FIFO order rather than silently cancelling earlier promises, and Part 11 audit-reason prompts must require explicit operator text entry rather than pre-filled boilerplate.
- **Backward-Compatible Application Contracts:** Existing `confirmDialog`, `promptDialog`, `useToastStore`, and `showToast` call signatures across `apps/web` and `apps/subject-portal` must remain intact while sharing primitives from `packages/ui`.

## 3. Options Considered

### Option 1: Centralize Focus Coordination, `ariaDescribedby`, and `ClinicalToastHost` in `packages/ui` (Selected)

- **Overview:** Export `hasActiveModalFocusTrap` and `getTopActiveModalContainer` from `packages/ui/src/composables/useFocusTrap.js`, bridge `trapTabFocus` across `[data-focus-trap-exempt="true"]` focusable controls, and add visible fallback focus restoration (`[aria-controls]` controller -> `#btn-open-palette` / `#btn-mobile-menu-toggle` -> `#main-content` / `<main>`) in `deactivate()`; add `ariaDescribedby` to `ClinicalModal.vue`; export a shared `ClinicalToastHost.vue` component from `packages/ui` with persistent `error` alerts, hover/focus timer pause and resume, split `alert` vs `status` ARIA roles, `v-if="toast.title"` guarding, semantic token contrast, and `prefers-reduced-motion` support; teleport `CanvasPalette.vue` to `document.body` at `z-index: 9999` with `aria-busy`, `Home`/`End` navigation, active key retention, and subject lookup status notes; update `confirm.js` (FIFO queue, `"info"` default variant) and `ConfirmationDialogHost.vue` (`.btn-cancel` initial focus, `aria-describedby="confirm-dialog-message"`, `<form>` prompt wrapper with `#confirm-dialog-prompt-input` validation refocus); clear pre-filled `defaultValue` strings on Part 11 prompts; add titles to all 16 untitled web toasts; and adopt `ClinicalToastHost` across both apps while disabling hidden `.toast-notification` hit-testing in `apps/subject-portal/src/style.css`.
- **Pros:**
  - Establishes a single tested implementation in `packages/ui` for focus-trap state queries, modal-to-toast `Tab` bridging, fallback focus restoration, `alertdialog` descriptions, and accessible toast lifecycle management across both applications.
  - Preserves existing `confirmDialog`, `promptDialog`, `useToastStore`, and `showToast` call signatures across all clinical modules.
- **Cons:**
  - Teleported `CanvasPalette.vue` renders under `document.body` rather than inline inside component wrappers, requiring component unit tests that inspect `#canvas-palette` DOM nodes to query `document.body`.

### Option 2: Patch Overlay, Dialog, and Toast Defect Sites Independently Per Application

- **Overview:** Add local `z-index` tweaks, custom timer wrappers, and per-app focus guards separately inside `apps/web` and `apps/subject-portal` without extending `packages/ui`.
- **Pros:**
  - Avoids rebuilding `packages/ui/dist/` or updating the shared component catalog.
- **Cons:**
  - Duplicates live-region, timer-pause, and focus-restoration logic across two apps while leaving `useFocusTrap` unable to coordinate modal state or `Tab` access to exempt toast controls.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing modal focus-trap introspection, exempt-region `Tab` bridging, visible focus restoration, `ClinicalModal` `ariaDescribedby`, and `ClinicalToastHost` in `packages/ui` eliminates the root causes across both clinician web and participant portal surfaces while keeping every existing store and widget call signature backward-compatible.

## 5. Consequences & Trade-offs

- **Positive Impact:** Pressing `Ctrl+K` / `Cmd+K` during an active modal or drawer no longer creates an occluded focus trap; destructive confirmations default focus to `.btn-cancel` and announce `#confirm-dialog-message`; concurrent confirmation/prompt requests resolve deterministically in FIFO order; Part 11 reason prompts require explicit operator input; all error toasts persist until dismissed while timed status toasts pause on hover or focus and remain reachable via `Tab` during active modals; and hidden participant-portal toast styles never intercept clicks.
- **Negative Impact / Technical Debt:** `packages/ui` grows from 24 to 25 exported Vue components (`ClinicalToastHost`) and exports two additional composable helpers (`hasActiveModalFocusTrap`, `getTopActiveModalContainer`) that must be rebuilt into `packages/ui/dist/` when modified.
- **Mitigation Strategy:** `packages/ui/tests/design_system_harmony.test.js`, `packages/ui/tests/useFocusTrap.test.js`, `packages/ui/tests/shared_components.test.js`, `apps/web/tests/`, and `apps/subject-portal/tests/` enforce export parity, WCAG 2.2 AA contrast, focus-trap coordination, FIFO confirmation queueing, and toast persistence/pause semantics in CI.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/adr/`, `docs/components/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm --filter web typecheck`, `pnpm lint`, `pnpm format:check`, `pnpm build`, `pnpm build:demo`, Playwright headless Chromium verification, and Cadence CLI ADR/markdown/de-identification checks.
