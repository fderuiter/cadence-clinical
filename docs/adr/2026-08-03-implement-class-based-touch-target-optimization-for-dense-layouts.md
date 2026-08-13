# ADR-254: Implement class-based touch target optimization for dense layouts

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, rigid global stylesheets enforced a minimum height of `44px` on all native buttons, select dropdowns, textareas, and input elements using high-precedence overrides. While this ensured tablet and mobile touch-target compliance (satisfying WCAG 2.1 AA and in-memory accessibility requirements under **PRD-CRF-015**), it severely impacted administrative workflows on desktop screens. Desktop users navigating dense tabular document grids and multi-column clinical forms faced excessive vertical scrolling and layout distortion due to the inflated elements.

To resolve this, we have moved from global, element-level overrides to an opt-in helper class model. This allows desktop layouts to default to compact, highly dense structures, while dynamically scaling to touch-friendly sizes (44px) on mobile and tablet viewport simulations.

## 2. Decision Drivers & Constraints

- **Driver 1:** Preservation of touch-target compliance (minimum height/width of 44px) on touch devices and mobile/tablet viewport simulations to satisfy WCAG 2.1 AA accessibility regulations (**PRD-CRF-015** / **Trace-31**).
- **Driver 2:** High density and visual compactness for administrative desktop workflows on tabular document grids and multi-column clinical forms, preventing layout distortion.
- **Driver 3:** Zero external UI utility framework dependencies (like Tailwind CSS) for layout spacing to keep clinical templates lightweight.

## 3. Options Considered

### Option 1: Global Element-Level CSS Overrides

- **Overview:** Enforce `min-height: 44px` globally across all native tags (`button`, `select`, `textarea`, `input`) across all viewports.
- **Pros:**
  - ✅ Simplest implementation with 100% automatic touch target compliance on all screens.
- **Cons:**
  - ❌ Causes layout inflation and excessive vertical scrolling on desktop screens.
  - ❌ Severe negative impact on administrative and clinical coordination workflows on desktop.

### Option 2: Opt-In Helper Class Model with Dynamic Viewport Toggling (Selected)

- **Overview:** Remove global overrides and implement explicit touch target utility classes (such as `.touch-target`, `.min-touch-target`, and `.touch-target-interactive`) leveraging the CSS custom property `--touch-target-min: 44px`. Combine this with dynamic viewport-based Vue class bindings (e.g. `:class="{ 'touch-target-interactive': designerStore.viewport !== 'desktop' }"`) to activate optimization classes only on mobile and tablet viewports.
- **Pros:**
  - ✅ Guarantees 100% compact, high-density structures on desktop screens.
  - ✅ Dynamically scales touch targets to a fully compliant `44px` on simulated mobile/tablet viewports.
  - ✅ Zero external design library or CSS utility framework overhead.
- **Cons:**
  - ❌ Requires explicit class bindings on interactive input widgets.

## 4. Decision Outcome

Chosen option: **Option 2 (Opt-In Helper Class Model with Dynamic Viewport Toggling)** because it resolves the layout distortion and workflow issues on desktop while fully preserving WCAG 2.1 AA accessibility and touch target compliance on tablet and mobile viewports, satisfying **PRD-CRF-015**.

## 5. Consequences & Trade-offs

- **Positive Impact:** No layout distortion on dense administrative tables and multi-column grids on desktop viewports. Accessibility targets are perfectly met on touch screens.
- **Negative Impact / Technical Debt:** Requires maintenance of dynamic class bindings on native interactive inputs (`input`, `select`, `textarea`, etc.) within the CRF renderer components.
- **Mitigation Strategy:** Automated component unit tests explicitly verify the injection and removal of touch-target helper classes during viewport transitions.

## 6. Implementation & Verification

- **Affected Repositories / Services / Files:**
  - `apps/web/src/style.css` & `apps/subject-portal/src/style.css`: Removed high-precedence global tag overrides.
  - `packages/ui/responsive.css`: Introduced standard utility classes (`.touch-target`, `.min-touch-target`, `.touch-target-interactive`) and compact padding/margin spacing helpers (`.compact-layout`, `.compact-padding`).
  - `apps/web/src/components/crf/CanvasFieldWidget.vue`: Configured dynamic Vue `:class` bindings matching the active Pinia viewport state (`designerStore.viewport !== 'desktop'`).
  - `apps/web/src/components/crf/CrfAuthoringCanvas.vue`: Added `.touch-simulation-active` layout helper.
- **Verification Plan:**
  - Verified via `CrfAuthoringCanvas.spec.ts` unit tests asserting correct viewport class switching.
  - Checked using `pnpm test` across all single-page applications.
