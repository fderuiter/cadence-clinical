# ADR-2657: Three-Tier Shared Overlay Architecture and Raw Dialog Migration

- **Status:** Accepted
- **Date:** 2026-11-23
- **Deciders:** Web & Design System Architecture, Clinical UX & Accessibility Governance
- **Related:** ADR-2545, ADR-2652, ADR-2653, ADR-2655, UX-3 (`docs/ux/CLINICAL_CANVAS.md`)

## Context

While ADR-2653 and ADR-2655 migrated electronic signature modals and standardized LIFO focus coordination in `packages/ui`, two structural gaps remained across `apps/web`, `apps/subject-portal`, and `packages/ui`:

1. **Viewport-Tiered Clinical Ergonomics:** Fixed `100vh` / `90vh` heights clipped primary attestation and submission controls beneath dynamic browser toolbars, virtual keyboards, and notched mobile safe areas (`320px–480px`), as well as short landscape tablet viewports (`1024x416`). Conversely, on wide desktop workspaces (`1280px+`), `ClinicalDrawer` always behaved as a blocking modal slide-over with a scrim and `inert` background siblings—preventing clinicians from inspecting side-by-side eCRF, protocol, or TMF canvas context per `docs/ux/CLINICAL_CANVAS.md` (UX-3).
2. **Residual Raw Dialog Debt:** `62` raw dialog/drawer instances across `32` files in `apps/web/src` and `7` files in `apps/subject-portal/src` still rendered ad-hoc `.modal-backdrop` / `.drawer-overlay` containers or manual `useFocusTrap` wiring, sheltered by `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST` and `useFocusTrap` exemptions in `eslint.config.mjs`.

## Decision

1. **Safe-Area & Dynamic Viewport Sizing (`vh` + `dvh`):**
   - Add `viewport-fit=cover` to both `apps/web/index.html` and `apps/subject-portal/index.html`.
   - Pair `vh` fallbacks with `dvh` progressive enhancements across `ClinicalModal.vue`, `ClinicalDrawer.vue`, `packages/ui/responsive.css`, `apps/subject-portal/src/style.css`, and migrated overlay consumers.
   - Enforce `font-size >= 16px` (`1rem`) on `<input>`, `<select>`, and `<textarea>` controls inside shared overlays on touch viewports (`<= 1024px` / `pointer: coarse`) so iOS/iPadOS Safari never triggers viewport auto-zoom during 21 CFR Part 11 signing or data entry.

2. **Three-Tier Responsive Overlay Architecture in `packages/ui`:**
   - **Mobile Bottom Sheet (`320px–480px`):** `ClinicalModal` and `ClinicalDrawer` anchor to the bottom edge with `max-height: 90dvh`, rounded top corners, scrollable bodies (`min-height: 0; overflow-y: auto`), sticky action footers padded with `env(safe-area-inset-bottom, 0px)`, and zero horizontal overflow.
   - **Tablet Slide-Over & Modal Tier (`768px–1279px`):** `ClinicalDrawer` retains modal slide-over focus containment (`aria-modal="true"`, scrim, background `inert`, `Tab` cycle) while sizing interactive controls to `44px` in `apps/web` (`--touch-target-min-staff`) and `48px` in `apps/subject-portal` (`--touch-target-min-patient`). Short landscape viewports (`max-height: 500px`) cap dialog height at `92dvh` with scrollable form bodies (`:has-form-layout`) so primary submit actions remain reachable.
   - **Desktop Dockable Inspector (`1280px+`):** `ClinicalDrawer` adds `dockable: Boolean` (paired with `isModal?: MaybeRefOrGetter<boolean>` in `useFocusTrap`). When `dockable` is enabled at `>= 1280px`, `ClinicalDrawer` omits the backdrop scrim, clears `aria-modal`, and skips `useFocusTrap` sibling isolation—leaving `<main>` and peer workspace regions free of `inert`, free of `aria-hidden="true"`, and free of `Tab` trapping while still supporting `Escape` dismissal and trigger focus restoration.

3. **Complete Raw Dialog Migration & Zero-Exemption Lint Gate:**
   - Migrate all remaining raw modal and drawer dialogs across `apps/web` and `apps/subject-portal` onto `ClinicalModal` or `ClinicalDrawer` (preserving `:dismissible` Part 11 locks, credential clearing, DOM IDs, ARIA labels, `defineExpose` contracts, and inline `:teleport="false"` rendering via `OptionalTeleport`).
   - Replace Unicode emojis and multiplication glyphs (`✕`, `×`, `✖`) in migrated overlay headers and close buttons with accessible `lucide-vue-next` icons.
   - Remove `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST` and `useFocusTrap` exemptions from `custom-vue/no-raw-overlays` in `eslint.config.mjs` so raw overlay containers outside `packages/ui/src/components/` fail lint unconditionally.

## Consequences

- **Positive:** Every modal and drawer in `apps/web` and `apps/subject-portal` inherits consistent three-tier responsive geometry, safe-area padding, keyboard/focus coordination, and Part 11 dismissal locks from `packages/ui`, while desktop clinical inspectors support non-blocking side-by-side canvas workflows.
- **Guardrail:** New feature dialogs cannot introduce raw `.modal-overlay`, `.modal-backdrop`, or `.drawer-overlay` markup in application code—both ESLint (`custom-vue/no-raw-overlays`) and `packages/ui/tests/design_system_harmony.test.js` enforce zero raw overlays.

## Verification

- `packages/ui/tests/shared_components.test.js` & `packages/ui/tests/useFocusTrap.test.js`
- `packages/ui/tests/design_system_harmony.test.js`
- `pnpm lint`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, and multi-viewport Chromium checks (`320x640`, `390x844`, `1024x416`, `1280x800`).
