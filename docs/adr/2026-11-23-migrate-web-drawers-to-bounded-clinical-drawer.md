# ADR-2661: Migrate Web Drawers to Bounded `ClinicalDrawer` and Enforce Viewport Reflow

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.1 SC 1.4.10 Reflow at 320px, WCAG 2.2 AA dialog focus containment, and tablet/mobile drawer scrollability), PRD-SYS-001 (21 CFR Part 11 electronic signature and GxP change-reason reachability inside clinical drawers and nested modals)

---

## 1. Context & Problem Statement

Following the modal dialog consolidation in ADR-2653, eleven slide-over drawers in `apps/web/src/components` remained on bespoke `.drawer-overlay`, `.drawer-backdrop`, and `.drawer-panel` wrappers (`ContextualHelpDrawer.vue`, `OnboardingChecklistDrawer.vue`, `SynonymImportDrawer.vue`, `UpversioningImpactDrawer.vue`, `BulkIpKitReconciliationDrawer.vue`, `EhrReconciliationDrawer.vue`, `UsdmConceptStagingDrawer.vue`, `QuizTemplateDrawer.vue`, `ReadabilityDrawer.vue`, `ExportJobDrawer.vue`, and `ShareDrawer.vue`), while `BatchUploadDrawer.vue` and `TicketDetailDrawer.vue` still kept secondary banners, dropzones, or `.resolve-panel` forms outside `.clinical-drawer-body` or mounted raw nested `.modal-backdrop` dialogs. On iPad landscape viewports with an open virtual keyboard (`1024x416`) and narrow mobile viewports (`320x640` and `390x844`), fixed `100vh` sizing combined with stacked headers, context strips, scanner cards, metric grids, filter toolbars, and sticky footers crushed the scrollable drawer body below `120px` (or `0px`), clipped Part 11 signature and reason inputs, and caused horizontal overflow at `320px`. In addition, `SynonymImportDrawer.vue` rendered with `position: static` instead of a fixed focus-trapped overlay, and `custom-vue/no-raw-overlays` in `eslint.config.mjs` still exempted `drawer-*` overlays when `useFocusTrap` was imported.

## 2. Decision Drivers & Constraints

- **Bounded Persistent Chrome & Dynamic Viewport Sizing (PRD-CRF-015):** `ClinicalDrawer.vue` must size panels with `100dvh` (`100vh` fallback), enforce `min-height: 0` on `.clinical-drawer-body`, limit persistent chrome to a single `#header` slot (`<= 52px` on short viewports) and one optional `#footer` slot (removing `#subheader`), present as a `90dvh` bottom sheet with `env(safe-area-inset-bottom)` at `<= 480px` width, and switch to panel-level vertical scrolling on `@media (max-height: 540px)` short viewports.
- **Reachable GxP & Part 11 Controls at 320px and 1024x416 (PRD-SYS-001):** Every drawer in `apps/web` must preserve `>= 160px` of visible scrollable body height at `768x1024` and `1024x768`, `>= 120px` at `320x640`, `390x844`, and `1024x416`, zero horizontal overflow (`scrollWidth <= clientWidth`) at `320px`, `>= 16px` (`1rem`) touch form control font size, `>= 44px` close/action hit targets, and Lucide vector iconography in drawer shells. Nested confirmation, rejection, query, and attachment dialogs inside drawers must use `ClinicalModal` so LIFO `Escape` closes only the nested modal.
- **Zero-Bypass AST Enforcement:** `custom-vue/no-raw-overlays` must reject raw `drawer-*` overlay wrappers even when `useFocusTrap` is imported, and `BulkIpKitReconciliationDrawer.vue`, `EhrReconciliationDrawer.vue`, and `TicketDetailDrawer.vue` must be removed from `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST`.

## 3. Options Considered

### Option 1: Extend `ClinicalDrawer` with Bounded Chrome & Responsive Bottom-Sheet/Short-Viewport Modes, Migrate All 13 Web Drawers & Nested Modals, and Tighten ESLint Guardrails (Selected)

- **Overview:** Enhance `packages/ui/src/components/clinical/ClinicalDrawer.vue` (`100dvh`/`100vh`, `min-height: 0` body, `keepMounted`, Lucide `X` close button, removal of `#subheader`, `@media (max-width: 480px)` `90dvh` bottom sheet, `@media (max-height: 540px)` compact `<= 52px` header and panel scroll, `@media (max-width: 1024px), (pointer: coarse)` `>= 16px` input font floor, and trailing `@media (prefers-reduced-motion: reduce)` animation suppression); migrate all 11 remaining bespoke drawers onto `ClinicalDrawer` and move secondary context bars, scanner cards, responsive `.metrics-grid` blocks, filter toolbars, domain pills, and `.resolve-panel` forms inside `.clinical-drawer-body` across all 13 `apps/web` drawers; reflow kit rows in `BulkIpKitReconciliationDrawer.vue` into stacked record cards at `<= 480px`; migrate nested `.modal-backdrop` dialogs in `BatchUploadDrawer.vue`, `BulkIpKitReconciliationDrawer.vue`, `EhrReconciliationDrawer.vue`, and `TicketDetailDrawer.vue` onto `ClinicalModal`; shrink `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST` by 3 files; and remove the `useFocusTrap` exemption for `drawer-*` overlays in `eslint.config.mjs`.
- **Pros:**
  - ✅ Centralizes `100dvh` sizing, `<= 480px` bottom-sheet presentation, `@media (max-height: 540px)` short-viewport scrolling, focus trapping, LIFO `Escape` coordination, and `16px` touch input sizing in `ClinicalDrawer.vue`.
  - ✅ Ensures all 13 `*Drawer.vue` components in `apps/web/src/components` render as fixed focus-trapped dialogs with zero `320px` horizontal overflow and reachable Part 11 signature/reason controls.
  - ✅ Closes the `useFocusTrap` lint loophole for `drawer-*` overlays and shrinks `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST`.
- **Cons:**
  - ❌ Requires rebuilding `packages/ui` (`pnpm --filter ui build`) and adding `lucide-vue-next` as a catalog peer/runtime dependency of `packages/ui` and `apps/web`.

### Option 2: Patch Individual CSS Media Queries Across Bespoke Drawer Wrappers

- **Overview:** Retain bespoke `.drawer-overlay` and `.drawer-backdrop` templates across the 11 unmigrated drawers and duplicate `100dvh`, `90dvh`, and `max-height: 540px` media queries per component while keeping the `useFocusTrap` ESLint bypass.
- **Pros:**
  - ✅ Avoids modifying `packages/ui`.
- **Cons:**
  - ❌ Duplicates responsive drawer mechanics and focus-trap wiring across 13 components, leaves `#subheader` chrome unbounded in `ClinicalDrawer`, and preserves raw-overlay lint exemptions.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Standardizing bounded persistent chrome (`#header` + optional `#footer`), `100dvh` sizing, `90dvh` mobile bottom sheets, and `@media (max-height: 540px)` panel scrolling directly in `ClinicalDrawer.vue` while migrating all 13 `apps/web` drawers and their nested modals eliminates viewport-crushing sticky chrome and enforces uniform WCAG 2.2 AA and 21 CFR Part 11 behavior through AST and Playwright E2E gates.

## 5. Consequences & Trade-offs

- **Positive Impact:** Every `apps/web` drawer preserves `>= 160px` of scrollable body height on tablet viewports (`768x1024`, `1024x768`) and `>= 120px` on mobile and virtual-keyboard viewports (`320x640`, `390x844`, `1024x416`) with zero horizontal overflow at `320px`; `BulkIpKitReconciliationDrawer` keeps `#barcode-scanner-input`, `#pharmacist-signature-id`, `#reason-for-change-input`, and `.btn-submit-batch` scrollable into view during live barcode entry; `SynonymImportDrawer` mounts as a fixed modal overlay; and nested drawer dialogs participate in LIFO `Escape` dismissal via `ClinicalModal`.
- **Negative Impact / Technical Debt:** Non-drawer legacy modal wrappers that remain on `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST` continue to be tracked for separate modal cleanup slices.
- **Mitigation Strategy:** Guarded by `eslint.config.mjs` (`custom-vue/no-raw-overlays`), `packages/ui/tests/design_system_harmony.test.js`, `packages/ui/tests/shared_components.test.js`, `apps/web/tests/drawer_responsiveness.spec.js`, and `apps/web/tests/e2e/drawer-reflow.spec.ts`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `eslint.config.mjs`, `docs/components/modal.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm lint`, `pnpm --filter web typecheck`, `pnpm --filter web exec vitest run`, `pnpm --filter web exec playwright test tests/e2e/drawer-reflow.spec.ts --project=accessibility`, targeted `web-accessibility.spec.ts` regression suites, and Cadence CLI documentation/ADR checks.
