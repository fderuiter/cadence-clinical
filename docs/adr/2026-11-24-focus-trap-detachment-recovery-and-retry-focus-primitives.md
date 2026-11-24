# ADR-2675: Focus-Trap Detachment Recovery, Shared Retry Focus Primitives, and Inline Focus-Handoff AST Gates

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA SC 2.1.1 Keyboard, SC 2.1.2 No Keyboard Trap, SC 2.4.3 Focus Order, SC 2.4.7 Focus Visible, SC 4.1.3 Status Messages), PRD-SYS-001 (21 CFR Part 11 explicit audit workflow continuity)

---

## 1. Context & Problem Statement

Audits across `packages/ui`, `apps/web`, and `apps/subject-portal` surfaced 17 live `document.activeElement -> BODY` focus drops during reactive `v-if` swaps, async retry transitions, and queue item removals—including 9 focus escapes inside open `role="dialog"` modals and drawers:

1. **Modal & Drawer Child Detachment Escapes (`useFocusTrap.js`):** When an active button inside an open `ClinicalModal` or `ClinicalDrawer` unmounted during a reactive state change (`ContextualHelpDrawer.vue` ticket detail/list switches and SOP/ticket retries, `BatchUploadDrawer.vue` queue item removal and clear, `DocumentManagerView.vue` QC review submission, or `ClinicalDropzone.vue` file removal), browser focus fell back to `document.body` outside the active `aria-modal="true"` dialog.
2. **Repeated Retry-Unmount Focus Drops Across Canvas, Drawer, and Portal Surfaces:** Clicking `[data-testid="canvas-error-retry"]` in `CanvasErrorAlert.vue` (used by `QueriesSection.vue`, `QueriesPanel.vue`, `KernelActPanel.vue`, `KernelLineagePanel.vue`, `CanvasInspector.vue`, and `StudyCanvasView.vue`), `#btn-retry-study-locks` / `#btn-retry-locks-list` in `LocksSection.vue`, `#btn-contextual-help-retry` / `#btn-retry-drawer-tickets` in `ContextualHelpDrawer.vue`, or `#btn-retry-compliance` in `apps/subject-portal/src/features/compliance.js` set a loading flag that immediately unmounted the retry button, dropping focus to `document.body` on both error persistence and content recovery.
3. **Inline Governance & Site-Linking `v-if` Swaps Without AST Regression Gates:** Submitting or returning protocol versions for review, signing Part 11 approvals, or transitioning effectivity rows in `ProtocolVersionsSection.vue`, and linking the final unlinked workspace site in `SitesSection.vue` or `StudySitesPanel.vue`, unmounted the active trigger button without handing focus to incoming controls or `tabindex="-1"` section headings. No ESLint AST rule guarded dropzone remove handlers, error-alert retry buttons, or subject-portal `retry*` functions against reintroducing `BODY` focus drops.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA Conformance (PRD-CRF-015):** Keyboard focus order must remain continuous across reactive DOM updates (`SC 2.4.3`) without trapping `Tab` or setting `inert` outside modal overlays (`SC 2.1.2`, ADR-2593). Programmatically focused `tabindex="-1"` headings must suppress outlines on pointer clicks while preserving `:focus-visible` rings (`SC 2.4.7`, `packages/ui/tokens.css`), and status/alert banners must announce via `role="status"` / `role="alert"` without stealing focus (`SC 4.1.3`).
- **Deterministic Yielding Between Explicit Handoffs and Modal Safety Nets:** `useFocusTrap` must yield to intentional `handoffFocus` / `handoffRetryFocus` calls and child modal activations, acting only after Vue `nextTick` + microtask settlement when `document.activeElement` is still `document.body` and the trap remains topmost on `activeTrapStack`.
- **Bundle Budget & Zero-Regression Enforcement:** `packages/ui` additions must keep the `apps/web` cold-boot JS bundle strictly under the `515 KiB` ceiling enforced by `cold_boot_bundle.spec.ts`, and static ESLint AST checks must prevent unguarded dropzone removals, error-alert retries, and portal retry handlers.

## 3. Options Considered

### Option 1: Combine Stack-Aware `useFocusTrap` Detachment Recovery, Shared `handoffRetryFocus` Primitives, Targeted Component Handoffs, and ESLint AST Gates (Selected)

- **Overview:** Attach a coalesced `MutationObserver` inside `packages/ui/src/composables/useFocusTrap.js` for the active modal container that schedules recovery after `nextTick` + microtask settlement and focuses the first focusable control (or `tabindex="-1"` dialog container) only when `activeTrapStack` top matches and `document.activeElement === document.body`; export `handoffRetryFocus` and `useRetryFocusHandoff` from `packages/ui/src/composables/useInlineFocusHandoff.js` (re-exported via `packages/ui/src/index.js`, `packages/ui/index.js`, and `packages/ui/index.d.ts`); wire semantic focus handoffs in `ClinicalDropzone.vue`, `CanvasErrorAlert.vue` (plus `tabindex="-1"` headings in `QueriesPanel.vue`, `KernelActPanel.vue`, `KernelLineagePanel.vue`, and `CanvasInspector.vue`), `LocksSection.vue`, `ContextualHelpDrawer.vue`, `BatchUploadDrawer.vue`, `DocumentManagerView.vue`, `ProtocolVersionsSection.vue`, `SitesSection.vue`, `StudySitesPanel.vue`, and `apps/subject-portal/src/features/compliance.js`; and add `custom-vue/require-inline-focus-handoff` in `eslint.config.mjs`.
- **Pros:**
  - Provides defense-in-depth: explicit semantic targets (`handoffFocus` / `handoffRetryFocus`) place focus on the exact adjacent control, retry button, or recovered section heading, while `useFocusTrap` guarantees no future child unmount inside any `ClinicalModal` or `ClinicalDrawer` can strand focus on `document.body`.
  - Eliminates duplicated retry focus boilerplate across `CanvasErrorAlert.vue`, `LocksSection.vue`, `ContextualHelpDrawer.vue`, and `apps/subject-portal`.
  - Static AST checks catch regressions at lint time without runtime overhead.
- **Cons:**
  - Requires awaiting `finally` busy-flag resets before invoking `handoffFocus` so newly rendered controls are no longer `:disabled` when focusability is evaluated.

### Option 2: Rely Solely on Per-Component Manual `.focus()` Calls Without Shared Primitives or `useFocusTrap` Recovery

- **Overview:** Add bespoke `nextTick(() => document.querySelector(...).focus())` snippets at each of the 17 defect sites without extending `useFocusTrap.js`, `useInlineFocusHandoff.js`, or `eslint.config.mjs`.
- **Pros:**
  - No changes to `packages/ui` exports or `index.d.ts`.
- **Cons:**
  - Leaves open modals vulnerable to future child-unmount focus escapes, duplicates error-vs-recovery retry inspection logic across two apps, and lacks lint-time regression enforcement.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Combining semantic inline/retry handoff primitives (`handoffFocus`, `handoffRetryFocus`) with stack-aware `MutationObserver` detachment recovery in `useFocusTrap` and ESLint AST gates (`custom-vue/require-inline-focus-handoff`) resolves all 17 focus drops at both the component UX level and the shared overlay infrastructure layer while preserving non-trapping inline behavior outside modals.

## 5. Consequences & Trade-offs

- **Positive Impact:** All 17 `document.activeElement -> BODY` drops across drawers, dropzones, eTMF QC workflows, Clinical Canvas panels/sections, and Subject Portal compliance retries are eliminated; open `role="dialog"` containers automatically recover focus if any focused descendant unmounts; and `custom-vue/require-inline-focus-handoff` guards against regressions in CI.
- **Negative Impact / Technical Debt:** `packages/ui` exports two additional composable helpers (`handoffRetryFocus`, `useRetryFocusHandoff`) requiring `pnpm --filter ui build` when `packages/ui` changes.
- **Mitigation Strategy:** `packages/ui/tests/useFocusTrap.test.js`, `packages/ui/tests/design_system_harmony.test.js`, `packages/ui/tests/shared_components.test.js`, `apps/web/tests/focus_trap_recovery_and_inline_handoffs.spec.ts`, `apps/web/tests/composables/canvasPrimitives.spec.ts`, `apps/web/tests/cold_boot_bundle.spec.ts`, and `apps/subject-portal/tests/inline-focus-and-keyed-dom.test.js` continuously verify focus recovery, export parity, AST lint gates, and bundle budgets.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `eslint.config.mjs`, `docs/adr/`.
- **Verification Plan:** Verified via `pnpm --filter "./packages/*" build`, `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm --filter web exec vitest run tests/cold_boot_bundle.spec.ts`, `pnpm lint`, Cadence CLI ADR/documentation checks, and headless Chromium Playwright verification across web and participant portal flows.
