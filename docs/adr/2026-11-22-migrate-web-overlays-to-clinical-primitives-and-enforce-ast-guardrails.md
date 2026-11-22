# ADR-2821: Migrate Web Overlays to Clinical Primitives and Enforce AST Guardrails

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.1 / 2.2 AA keyboard focus containment, contrast, and reduced-motion compliance), PRD-SYS-001 (21 CFR Part 11 regulated submission and QC review dismissal locks)

---

## 1. Context & Problem Statement

Following the centralization of shared focus-containment primitives (`ClinicalModal`, `ClinicalDrawer`, `useFocusTrap`, and `useEscapeClose`) in `packages/ui` (ADR-2545), 50 modal and drawer overlays across 20 Vue components in `apps/web` still rendered raw `<div class="modal-backdrop">`, `<div class="modal-overlay">`, and slide-over `<aside>`/`<div>` containers without focus traps, background `inert`/`aria-hidden="true"` isolation, LIFO `Escape` stacking, or trigger focus restoration. In addition, 8 `apps/web` files carried 14 `eslint-disable vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions` backdrop suppressions, `ClinicalModal.vue` and `ClinicalDrawer.vue` lacked component-scoped `@media (prefers-reduced-motion: reduce)` guards and explicit modal header surface tokens against global `.modal-header` styles, and ESLint/design-system harmony suites had no rule preventing new raw overlays from re-entering `apps/web`.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 AA Overlay Containment, Contrast & Reduced Motion (PRD-CRF-015):** Every modal dialog and slide-over drawer in `apps/web` must isolate background siblings (`inert` and `aria-hidden="true"`), trap `Tab`/`Shift+Tab`, restore trigger focus on close, maintain AA header contrast, keep `ToastHost` live alerts audible/non-`inert`, and disable CSS transitions/animations under `prefers-reduced-motion: reduce`.
- **Regulated Action Dismissal Locks & LIFO Stacking (PRD-SYS-001):** In-flight QC approvals (`DocumentManagerView.vue`), batch training assignments and bulk DOA sign-offs (`ProtocolTrainingConsole.vue`), database lock commits (`DataLockView.vue`), and e-signature/mutation flows must lock `Escape` and backdrop dismissal while submitting, and stacked overlays (`PdfPreviewModal` over `DocumentManagerView` `ClinicalDrawer`) must dismiss top-most first on `Escape`.
- **Zero Raw Overlay Regression or Accessibility Lint Suppressions:** Custom Vue template AST rules and design-system harmony tests must block raw overlay `<div>` patterns and backdrop `vuejs-accessibility` suppressions across `apps/web/src`.

## 3. Options Considered

### Option 1: Migrate All `apps/web` Overlays to `ClinicalModal`/`ClinicalDrawer` and Enforce `custom-vue/no-raw-overlays` AST & Harmony Guards (Selected)

- **Overview:** Enhance `ClinicalModal.vue` (`modelValue`/`open`/`isOpen` v-model compatibility, `overlayId`, `ariaLabel`/`ariaLabelledby`, `headerClass`/`bodyClass`/`footerClass`, `showHeader`/`showFooter`, explicit `.modal-header` surface/text tokens, and `@media (prefers-reduced-motion: reduce)`) and `ClinicalDrawer.vue` (`@media (prefers-reduced-motion: reduce)`), migrate all 50 raw overlays across 20 `apps/web` components to `ClinicalModal` and `ClinicalDrawer`, delete all 14 backdrop `vuejs-accessibility` suppressions across 8 `apps/web` files, add the `custom-vue/no-raw-overlays` Vue template AST rule in `eslint.config.mjs`, and add source-level guard assertions in `packages/ui/tests/design_system_harmony.test.js`.
- **Pros:**
  - ✅ Satisfies PRD-CRF-015 and PRD-SYS-001 uniformly across live clinical routes (`/etmf`, `/training`, `/data-lock`) and demo consoles.
  - ✅ Eliminates all 14 `vuejs-accessibility` backdrop suppressions in `apps/web` and prevents regressions at lint and unit-test time.
- **Cons:**
  - ❌ Requires updating `packages/ui` TypeScript declarations (`packages/ui/index.d.ts`) and rebuilding `packages/ui` before running `apps/web` verification suites.

### Option 2: Attach `useFocusTrap` Manually to Existing Raw `<div class="modal-backdrop">` Elements

- **Overview:** Keep bespoke modal/drawer markup across all 20 `apps/web` components and wire `useFocusTrap` + `useEscapeClose` individually per overlay.
- **Pros:**
  - ✅ Preserves per-view wrapper markup unchanged.
- **Cons:**
  - ❌ Duplicates dialog ARIA wiring, backdrop click guards, dismissal locks, and reduced-motion styles across 50 overlays while leaving `vuejs-accessibility` suppressions in place.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Migrating every `apps/web` modal and drawer onto `ClinicalModal` and `ClinicalDrawer` while enforcing `custom-vue/no-raw-overlays` in ESLint and `design_system_harmony.test.js` establishes a single accessible, motion-safe, GxP-compliant overlay architecture with zero lint suppressions.

## 5. Consequences & Trade-offs

- **Positive Impact:** Uniform focus trapping, `inert` background isolation, LIFO `Escape` handling, submission dismissal locks, `prefers-reduced-motion: reduce` support, WCAG 2.2 AA modal header contrast, and automated AST/harmony enforcement across `apps/web`.
- **Negative Impact / Technical Debt:** Custom-header dialogs opting into `:show-header="false"` must provide `aria-label` or `aria-labelledby` explicitly so the dialog retains an accessible name.
- **Mitigation Strategy:** Enforced via `packages/ui` SSR/DOM unit tests, `apps/web` Vitest focus-containment tests, and Playwright + `axe-core` WCAG 2.2 AA E2E tests.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `eslint.config.mjs`, `docs/components/modal.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm -r lint`, `pnpm --filter web typecheck`, `pnpm build`, `pnpm --filter web build:demo`, `pnpm -r test`, `pnpm --filter web test:e2e`, and `pnpm cadence check`.
