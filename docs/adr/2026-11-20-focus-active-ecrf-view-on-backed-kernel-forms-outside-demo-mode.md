# ADR-2819: Focus Active eCRF View on Backed Kernel Forms Outside Demo Mode

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (audit fields and backed execution boundaries), PRD-EDC-005 (case report form workflow)

---

## 1. Context & Problem Statement

ADR-2622 moved clinical case report form collection onto kernel `CollectionInstance` forms rendered by `EcrfFormPanel.vue` (`/api/v1/execution/ecrf/*`). However, mounting `/ecrf` (`apps/web/src/views/EcrfView.vue`) outside demo mode still exposed five unbacked prototype panels alongside `EcrfFormPanel.vue` — legacy enrollment (`SubjectEnrollmentForm.vue`), sample vitals (`CrcFormRenderer.vue`), static CDASH reference rules, protocol ingestion (`DesignerSchemaPanel.vue`), EHR source reconciliation (`EhrReconciliationDrawer.vue`), and a local role-switcher dropdown — and required clinicians to paste raw enrollment UUIDs rather than resolving the selected participant's kernel enrollment. In demo mode, legacy walkthrough actions also used blocking `window.alert()` dialogs and rendered a link to `/training` even when that screen is unavailable.

## 2. Decision Drivers & Constraints

- **Backed Clinical Truth Only Outside Demo Mode (PRD-SYS-001, ADR-2622):** Non-demo `/ecrf` builds must present only controls backed by Execution and Kernel APIs so clinical staff never enter data into local-only sample forms or hit dead-end refusal toasts.
- **Participant-to-Enrollment Resolution (PRD-EDC-005):** Selecting a study participant on `/ecrf` or navigating from Clinical Canvas (`FormQueryList.vue`, `AppShell.vue`, `CommandPaletteOverlay.vue`) must automatically resolve and populate the participant's kernel `enrollmentId` via `findKernelEnrollmentRef` (`apps/web/src/canvas/kernelStudy.ts`).
- **Non-Blocking Demo Walkthroughs:** Demo builds (`VITE_DEMO_MODE=true`) retain persona walkthrough panels for evaluation while replacing blocking `window.alert()` calls with toast notifications and gating links to unavailable routes behind `isScreenAvailable(...)`.

## 3. Options Considered

### Option 1: Gate Legacy Persona Panels to Demo Mode and Wire Kernel Enrollment Resolution on `/ecrf` (Selected)

- **Overview:** Outside demo builds (`!unbackedActionsEnabled()`), render only the authenticated role badge, `StudySubjectsBar`, a participant selector with role-gated Clinical Canvas handoff (`canManageParticipants`), and `EcrfFormPanel.vue`. Resolve the chosen participant's kernel enrollment via `findKernelEnrollmentRef`, sync `EcrfFormPanel`'s `enrollmentId` field and clear stale `openError` on participant switches, forward `enrollmentId` on Clinical Canvas -> `/ecrf` links, preserve `onBeforeRouteLeave` and `beforeunload` guards for unsaved kernel form edits, and replace `window.alert()` calls in demo composables with `useToastStore()` notifications.
- **Pros:**
  - ✅ Aligns `/ecrf` with ADR-2622 and `PRD-SYS-001` by eliminating unbacked controls and `localStorage` sample drafts outside demo mode.
  - ✅ Removes manual UUID copy-paste by resolving participant kernel enrollments on `/ecrf` and preserving `enrollmentId` across Clinical Canvas navigation.
  - ✅ Preserves demo walkthroughs without blocking `window.alert()` dialogs or broken `/training` links.
- **Cons:**
  - ❌ Legacy unit tests that mounted `EcrfView` outside demo mode expecting `CrcFormRenderer` or `SubjectEnrollmentForm` must explicitly opt into `VITE_DEMO_MODE="true"` when exercising demo-only panels.

### Option 2: Keep Unbacked Panels Mounted Outside Demo Mode Behind Click-Time Refusal Toasts

- **Overview:** Leave all legacy persona panels visible on `/ecrf` and rely solely on `allowUnbackedAction(...)` refusal toasts when clicked.
- **Pros:**
  - ✅ Minimal template changes in `EcrfView.vue`.
- **Cons:**
  - ❌ Clutters the active clinical data-entry workspace with inert prototype forms and requires manual UUID entry for `EcrfFormPanel.vue`.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Focusing `/ecrf` on `StudySubjectsBar`, participant enrollment resolution, and `EcrfFormPanel.vue` outside demo builds satisfies `PRD-SYS-001` and `PRD-EDC-005` while preserving non-blocking demo walkthroughs when `VITE_DEMO_MODE=true`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinical coordinators, investigators, monitors, and data managers see a single kernel-backed eCRF workflow on `/ecrf` with automatic enrollment resolution and direct navigation to and from Clinical Canvas.
- **Negative Impact / Technical Debt:** Legacy persona components (`CrcFormRenderer.vue`, `DesignerSchemaPanel.vue`, `SubjectEnrollmentForm.vue`) remain in `apps/web/src/components/` for demo mode until retired workspace-wide.
- **Mitigation Strategy:** Vitest suites cover both non-demo focused mode and `VITE_DEMO_MODE="true"` behavior, including navigation guards and alert-free toast notifications.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/web` (`EcrfView.vue`, `EcrfFormPanel.vue`, `FormQueryList.vue`, `AppShell.vue`, `CommandPaletteOverlay.vue`, `palette.ts`, `CrcFormRenderer.vue`, `useSchemaIngestion.js`, `useVerification.js`), `apps/web/README.md`, `docs/web/API_MIGRATION.md`.
- **Verification Plan:** Verified via `pnpm -r test`, `pnpm -r lint`, `pnpm --filter web typecheck`, `pnpm build`, `pnpm --filter web build:demo`, and `pnpm cadence check`.
