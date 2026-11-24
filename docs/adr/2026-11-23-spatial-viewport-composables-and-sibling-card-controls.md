# ADR-2671: Spatial Viewport Composables and Sibling Card Controls

Also known as: ADR-2823, ADR-2824 (former numbers)

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level A & AA SC 2.5.7 Dragging Movements, SC 2.1.1 Keyboard, and SC 4.1.2 Name/Role/Value compliance), PRD-SYS-001 (21 CFR Part 11 regulated workflow integrity)

---

## 1. Context & Problem Statement

Clinicians relying on switch interfaces, trackballs, eye-gaze pointers, voice control, or screen readers encountered two recurring accessibility barriers across Cadence Clinical spatial viewports, grids, and authoring cards:

1. **Path-Dependent Dragging & Keyboard-Inoperable Spatial Viewports (WCAG 2.2 SC 2.5.7, SC 2.1.1):** Zoomed media inspection (`MediaPlayer.vue`), anatomical site marking (`BodyMapWidget.vue`), DICOM MPR lesion calipers (`DicomMprViewer.vue` and `OncologyCalipers.vue`), eCRF cross-section field reordering (`CrfAuthoringCanvas.vue` and `FormSectionContainer.vue`), SoA matrix multi-cell range selection (`SoAMatrixEditor.vue`), and Gantt visit inspection (`ClinicalGanttVisualizer.vue`) relied on pointer-down/move/up dragging or pointer-only hover coordinates, with `BodyMapWidget.vue` emitting `NaN%` when activated via keyboard (`clientX: 0, clientY: 0`).
2. **Nested Interactive Elements Inside `role="button"` Containers (WCAG 2.2 SC 4.1.2 Failure F59):** Card and header containers (`CanvasFieldWidget.vue`, `FormSectionContainer.vue`, `QuizTemplateDrawer.vue`, and `ImagingView.vue`) attached `role="button"` or static `@click` handlers around descendant buttons/inputs, obscuring child control semantics in assistive technology accessibility trees.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level A & AA Compliance (PRD-CRF-015):** Every drag-dependent spatial interaction must expose a single-pointer non-dragging alternative (Technique G219) and full keyboard operability (Technique G202), canvases and SVG diagrams must expose programmatic accessible names (Techniques G82, ARIA6, ARIA14), 2D matrices must implement roving `tabindex` (`use2DGrid`), and no `<button>` or `<input>` may be nested inside a `role="button"` ancestor.
- **21 CFR Part 11 Compliance Boundary (PRD-SYS-001):** Oncology RECIST 1.1 caliper measurements (`lengthMm > 2.0`) and 21 CFR Part 11 sign-off workflows must preserve existing validation gates and API schemas without regressing mouse or touch drag gestures.
- **Cold-Boot Bundle Budget:** Adding shared spatial primitives to `packages/ui` must keep `apps/web`'s initial cold-boot JS bundle within its `<= 515 kB` raw / `<= 167 kB` gzip budget.

## 3. Options Considered

### Option 1: Centralize `useAccessibleViewport` and `ViewportPanPad` in `packages/ui`, Adopt `use2DGrid` & Two-Click Range/Endpoint Patterns, and Enforce Sibling Card Controls (Selected)

- **Overview:** Add `useAccessibleViewport` and `ViewportPanPad.vue` to `packages/ui` (providing coarse/fine arrow-key reticle stepping, 4-way + center `>= 24x24px` directional pan buttons, two-click endpoint placement, and throttled polite live announcements) while using direct `export { ... } from "./src/index.js"` re-exports in `packages/ui/index.js` and keeping Vite's `preload-helper` in `ui-shell` so Rollup tree-shakes unused helpers and lazy chunks from `apps/web` cold boot. Integrate `useAccessibleViewport` across `MediaPlayer.vue`, `BodyMapWidget.vue`, and `DicomMprViewer.vue`, add manual caliper entry and `<form>` submission to `OncologyCalipers.vue`, add cross-section move buttons and a Section `<select>` to `CrfAuthoringCanvas.vue` / `FormSectionContainer.vue` / `designer.ts`, wire `use2DGrid` and a single-pointer `Range Select` toggle into `SoAMatrixEditor.vue`, make `ClinicalGanttVisualizer.vue` SVG and visit nodes keyboard-accessible, refactor card wrappers to plain containers with sibling `<button type="button">` controls (plus interactive descendant guards in `keyboardClick.js`), and lock remediated components into `custom-vue/no-vuejs-a11y-suppression` in `eslint.config.mjs`.
- **Pros:**
  - ✅ Satisfies PRD-CRF-015 (WCAG 2.2 SC 2.5.7, SC 2.1.1, SC 2.5.8, SC 4.1.2) uniformly while preserving mouse/touch drag gestures and PRD-SYS-001 signature workflows.
  - ✅ Keeps `apps/web` cold-boot JS within budget and prevents `vuejs-accessibility` lint-suppression regressions at pre-commit time.
- **Cons:**
  - ❌ Adds explicit sibling buttons and toolbar toggles (`ViewportPanPad`, `Range Select`, cross-section move controls) that must be styled with compact responsive tokens.

### Option 2: Patch Individual Components Ad-Hoc Without Shared Spatial Primitives or Lint Ratchets

- **Overview:** Add component-local arrow-key listeners in `MediaPlayer.vue`, `BodyMapWidget.vue`, and `DicomMprViewer.vue` while keeping existing `role="button"` card wrappers and lint suppressions.
- **Pros:**
  - ✅ Avoids introducing new exports in `packages/ui`.
- **Cons:**
  - ❌ Duplicates reticle clamping and live-region throttling logic across packages, leaves nested interactive `role="button"` trees intact, and provides no ESLint suppression ratchet.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing `useAccessibleViewport` and `ViewportPanPad` in `packages/ui` alongside `use2DGrid`, sibling card buttons, and `custom-vue/no-vuejs-a11y-suppression` enforcement eliminates single-pointer dragging and nested-button barriers systematically while preserving PRD-SYS-001 compliance and cold-boot bundle budgets.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full single-pointer and keyboard parity across zoomed media, body maps, DICOM MPR calipers, CRF authoring sections, SoA matrices, Gantt visit pathways, and template/series cards, backed by Vitest, `axe-core`, and ESLint AST enforcement.
- **Negative Impact / Technical Debt:** Tests asserting card activation must target the explicit `.field-select-btn` / `.section-select-btn` / `.series-item` button elements rather than clicking inert outer card `<div>` wrappers.
- **Mitigation Strategy:** Keep sibling selection buttons visually aligned with card titles using semantic tokens from `packages/ui/tokens.css` and cover all keyboard and single-pointer flows in Vitest and Playwright suites.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web` (including `apps/web/vite.config.js` keeping Vite's `preload-helper` in `ui-shell` so lazy `vendor-auth` stays excluded from cold boot), `eslint.config.mjs`, `docs/components/index.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `packages/ui/tests/useAccessibleViewport.test.js`, `packages/ui/tests/design_system_harmony.test.js`, `apps/web/tests/components/SpatialAndSiblingAccessibility.spec.ts`, `apps/web/tests/components/KeyboardNavigation.spec.js`, `apps/web/tests/cold_boot_bundle.spec.ts`, Playwright `axe-core` WCAG 2.2 AA suites (`apps/web/tests/e2e/web-accessibility.spec.ts`), `pnpm build`, `pnpm -r lint`, and `pnpm cadence check`.
