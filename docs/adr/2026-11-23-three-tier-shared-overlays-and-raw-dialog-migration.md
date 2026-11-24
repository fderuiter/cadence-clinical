# ADR-2659: Three-Tier Shared Overlay Architecture and Raw Dialog Migration

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Related:** ADR-2545, ADR-2652, ADR-2653, ADR-2655, ADR-2657, ADR-2658, UX-3 (`docs/ux/CLINICAL_CANVAS.md`)
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 regulated signing locks and credential clearing on dismissal), PRD-CRF-015 (WCAG 2.2 Level AA SC 1.4.10 Reflow, SC 2.1.1 Keyboard, SC 2.1.2 No Keyboard Trap, SC 2.4.3 Focus Order, SC 2.5.8 Target Size, and WAI-ARIA `dialog` / `alertdialog` patterns)

---

## 1. Context & Problem Statement

While ADR-2653, ADR-2655, and ADR-2657 standardized electronic signature dialogs, LIFO focus coordination, and shared sign-out confirmation modals in `packages/ui`, two structural gaps remained across `apps/web`, `apps/subject-portal`, and `packages/ui`:

1. **Viewport-Tiered Clinical Ergonomics:** Static `100vh` / `90vh` heights clipped primary attestation and submission controls beneath dynamic browser toolbars, virtual keyboards, and notched mobile safe areas (`320px–480px`), as well as short landscape tablet viewports (`1024x416`). Conversely, on wide desktop workspaces (`1280px+`), `ClinicalDrawer` always behaved as a blocking modal slide-over with a scrim and `inert` background siblings—preventing clinicians from inspecting side-by-side eCRF, protocol, or TMF canvas context per `docs/ux/CLINICAL_CANVAS.md` (UX-3).
2. **Residual Raw Dialog Debt:** Remaining raw dialog and drawer instances across `32` files in `apps/web/src` and `7` files in `apps/subject-portal/src` still rendered ad-hoc `.modal-backdrop` / `.drawer-overlay` containers or manual `useFocusTrap` wiring, sheltered by `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST` and `useFocusTrap` exemptions in `eslint.config.mjs`.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA & Multi-Viewport Ergonomics (PRD-CRF-015):** Overlays must reflow cleanly at `320px–480px` and short landscape heights without clipping primary action buttons (`SC 1.4.10`), avoid iOS/iPadOS Safari auto-zoom on form controls (`font-size >= 16px`), meet touch-target minimums (`44px` staff / `48px` participant), and allow non-blocking keyboard navigation between `<main>` and docked desktop inspectors at `1280px+` (`SC 2.1.1`, `SC 2.1.2`, `SC 2.4.3`).
- **21 CFR Part 11 Signing Safeguards (PRD-SYS-001):** Migrated dialogs must preserve `:dismissible` locks while electronic signatures or regulated mutations are in flight, clear entered credentials upon close or cancellation, and preserve existing DOM IDs, ARIA labels, and `defineExpose` contracts.
- **Zero-Exemption Static Governance:** Lint rules and harmony tests must reject new raw overlay markup unconditionally across both `apps/web` and `apps/subject-portal`.

## 3. Options Considered

### Option 1: Upgrade `ClinicalModal` / `ClinicalDrawer` to Three-Tier Shared Overlays and Retire All Raw Overlay Allowlists (Selected)

- **Overview:** Enable `viewport-fit=cover` in both HTML entrypoints; pair `vh` fallbacks with `dvh` dynamic viewport sizing; implement three-tier responsive behavior in `ClinicalModal` and `ClinicalDrawer` (`320px–480px` `90dvh` safe-area bottom sheets, `768px–1279px` modal slide-overs/dialogs with short-landscape scrolling, and `1280px+` non-modal `dockable` side-by-side drawers backed by `modal: isModal` in `useFocusTrap`); migrate all remaining raw dialogs across `apps/web` and `apps/subject-portal` with zero-emoji `lucide-vue-next` header icons; and delete `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST` and `useFocusTrap` exemptions from `eslint.config.mjs`.
- **Pros:**
  - Centralizes safe-area insets, dynamic viewport sizing, touch input `16px` sizing, Part 11 dismissal locks, and docked desktop inspector semantics inside `packages/ui`.
  - Eliminates per-file allowlist maintenance and prevents regressions via `custom-vue/no-raw-overlays` and `design_system_harmony.test.js`.
- **Cons:**
  - Requires migrating all legacy `.vue` overlay templates across both applications in a single coordinated change.

### Option 2: Retain `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST` and Patch Viewport Bugs File-by-File

- **Overview:** Keep existing raw `.modal-backdrop` and `.drawer-overlay` implementations in place while adding ad-hoc `dvh` and media-query overrides only to reported screens.
- **Pros:**
  - Smaller initial diff across legacy feature components.
- **Cons:**
  - Leaves inconsistent Escape/focus-trap behavior, duplicated CSS across dozens of components, and blocked `<main>` regions on `1280px+` desktop drawers.

## 4. Decision Outcome

Selected **Option 1**:

1. **Safe-Area & Dynamic Viewport Foundation (`vh` + `dvh`):** Added `viewport-fit=cover` to `apps/web/index.html` and `apps/subject-portal/index.html`, paired `vh` fallbacks with `dvh` across shared primitives and migrated consumers, and enforced `font-size >= 16px` (`1rem`) on `<input>`, `<select>`, and `<textarea>` elements inside overlays on touch viewports (`<= 1024px` / `pointer: coarse`).
2. **Three-Tier Shared Overlays (`packages/ui`):**
   - **Mobile Bottom Sheet (`320px–480px`):** `ClinicalModal` and `ClinicalDrawer` anchor to the bottom edge at `max-height: 90dvh` with scrollable bodies and `env(safe-area-inset-bottom, 0px)` footer padding.
   - **Tablet Slide-Over & Modal (`768px–1279px`):** Retains modal focus containment (`aria-modal="true"`, scrim, background `inert`, `Tab` cycle) with `44px` (`apps/web`) and `48px` (`apps/subject-portal`) touch targets and `92dvh` short-landscape (`max-height: 500px`) form scrolling.
   - **Desktop Dockable Inspector (`1280px+`):** `ClinicalDrawer` supports `dockable` mode (`useFocusTrap` `modal: isModal`), omitting the backdrop scrim, clearing `aria-modal`, and leaving `<main>` and sibling regions interactive and free of `inert`, `aria-hidden="true"`, and `Tab` trapping.
3. **Complete Raw Dialog Migration & Lint Gate:** Migrated all remaining raw overlays across `apps/web` and `apps/subject-portal` onto `ClinicalModal` and `ClinicalDrawer` (with `OptionalTeleport` for `:teleport="false"` inline mounts and `lucide-vue-next` header icons), and removed `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST` and `useFocusTrap` exemptions from `eslint.config.mjs`.

## 5. Consequences & Trade-offs

- **Positive:** Every modal and drawer in `apps/web` and `apps/subject-portal` inherits three-tier responsive geometry, safe-area padding, keyboard/focus coordination, and Part 11 dismissal locks from `packages/ui`, while `1280px+` clinical inspectors support non-blocking side-by-side canvas workflows.
- **Trade-off & Guardrail:** Raw `.modal-overlay`, `.modal-backdrop`, and `.drawer-overlay` containers outside `packages/ui/src/components/` now fail `custom-vue/no-raw-overlays` and `design_system_harmony.test.js` immediately; all new dialogs and drawers must compose `ClinicalModal` or `ClinicalDrawer`.

## 6. Implementation & Verification

- `packages/ui/src/components/clinical/ClinicalModal.vue`, `packages/ui/src/components/clinical/ClinicalDrawer.vue`, and `packages/ui/src/composables/useFocusTrap.js`
- `eslint.config.mjs` (`custom-vue/no-raw-overlays`) and `packages/ui/tests/design_system_harmony.test.js`
- `packages/ui/tests/shared_components.test.js` and `packages/ui/tests/useFocusTrap.test.js`
- Verified via `pnpm lint`, `pnpm -r typecheck`, `pnpm --filter ui test`, `pnpm --filter subject-portal test`, `pnpm --filter web test`, and multi-viewport headless Chromium checks (`320x640`, `390x844`, `1024x416`, `1280x800`).
