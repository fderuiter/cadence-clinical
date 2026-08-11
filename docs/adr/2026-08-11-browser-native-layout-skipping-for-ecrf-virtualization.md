# ADR-2168: Browser-Native Layout-Skipping for eCRF Virtualization

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In large-scale clinical trials, Electronic Case Report Forms (eCRFs) can contain hundreds of fields. In ADR-256, we introduced a viewport-driven DOM recycler that dynamically mounts and unmounts child inputs of offscreen fields to limit memory overhead and prevent Out-Of-Memory (OOM) browser crashes on tablets and iPads.

However, unmounting offscreen input elements completely from the DOM has several serious downsides:

1. It breaks native keyboard focus navigation (using the Tab key), as offscreen fields no longer exist in the DOM. This impairs accessibility and violates WCAG 2.1 continuous-tab-navigation requirements.
2. It complicates state synchronization, because any unsaved state inside input components must be meticulously preserved in a central store before unmounting and restored upon remounting.
3. It relies heavily on JS runtime execution (IntersectionObserver and ResizeObserver) to continuously perform DOM insertions and removals, adding main-thread scripting overhead.

We need a virtualization strategy that minimizes rendering and layout calculation overhead for offscreen fields while keeping the inputs present in the DOM for native accessibility, state persistence, and keyboard tab-navigation.

This decision directly addresses the following requirements:

- **PRD-CRF-001:** CRF metadata-driven rendering and layout structures.
- **PRD-CRF-015:** In-Memory Accessibility Auditing and compliance with WCAG 2.1 keyboard focus routing.

## 2. Decision Drivers & Constraints

- **Driver 1 (Accessibility & Keyboard Focus):** Support seamless native browser Tab focus navigation across all form fields (onscreen and offscreen).
- **Driver 2 (Rendering Performance):** Avoid layout calculations and rendering paint operations for offscreen elements to minimize CPU and GPU rendering overhead.
- **Driver 3 (Layout Stability):** Maintain accurate viewport heights and scroll positions during rapid scrolling without causing content jumpiness or cumulative layout shifts (CLS).
- **Driver 4 (State Preservation):** Retain natural component and DOM input state without requiring complex, performance-intensive serialization and central store synchronization.

## 3. Options Considered

### Option 1: Viewport DOM Recycling with Guarded Unmounting (Baseline)

- **Overview:** Dynamically mount/unmount child inputs based on IntersectionObserver callbacks, caching dynamic heights in a central Pinia store to set container placeholders.
- **Pros:**
  - ✅ Extreme reduction in active DOM node count (<100 nodes).
- **Cons:**
  - ❌ Completely breaks browser-native keyboard focus and sequential Tab navigation to offscreen elements.
  - ❌ High complexity in preserving and restoring temporary form states (unsaved edits, pristine/dirty flags, focus state).
  - ❌ Continuous DOM insertion/deletion thrashing on the main thread during scrolling.

### Option 2: Browser-Native Layout-Skipping via CSS `content-visibility: auto` (Selected)

- **Overview:** Transition virtualization to browser-native layout-skipping. By applying `content-visibility: auto` and `contain-intrinsic-size` to the field wrapper component (`ClinicalFormField.vue`), the browser natively skips the rendering, layout, and paint computations for offscreen fields. The offscreen fields and their inputs remain fully mounted in the DOM.
- **Pros:**
  - ✅ Retains all inputs in the DOM tree, enabling native sequential keyboard Tab navigation and screen-reader access to offscreen fields.
  - ✅ Eliminates state restoration complexity, as elements are never unmounted and naturally retain their local state (e.g., cursor position, selection, incomplete edits).
  - ✅ Substantially reduces JS scripting overhead since layout-skipping is handled natively and highly optimized in modern browser engines.
  - ✅ Incorporates dynamic `contain-intrinsic-size: auto [height]` based on measured/cached pixel heights to prevent cumulative layout shifts when elements go offscreen.
- **Cons:**
  - ❌ Browser support is restricted to modern chromium, webkit, and gecko engines, though older browsers gracefully fallback to standard rendering with no layout-skipping.

## 4. Decision Outcome

**Chosen Option:** Option 2. This browser-native layout-skipping approach using CSS `content-visibility: auto` satisfies both accessibility compliance (WCAG 2.1 keyboard focus navigation) and rendering performance goals. Offscreen fields are natively bypassed by the browser's layout engine while still residing in the DOM, eliminating complex custom JS DOM recycling and Pinia store sync code.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Perfect native keyboard focus navigation and screen-reader accessibility across the entire form.
  - Simplified component design, removing JS unmounting and complex guarded-recycling logic.
  - Significant reduction in frame-drops and scroll lag due to native layout optimization.
- **Negative Impact / Technical Debt:**
  - Standard DOM node count is higher than Option 1, but modern browsers comfortably handle thousands of unpainted DOM nodes.
  - Requires fallback placeholder heights (`44px` minimum) to prevent layout collapse before the first size calculation.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/ui/src/components/clinical/ClinicalFormField.vue`
- **Verification Plan:**
  - Automated tests in `apps/web/tests/viewport_recycler.test.js` verify that components remain mounted offscreen, layout size containment is set correctly, and standard keyboard focus operates safely.
  - Layout stability validated using the Chrome DevTools Rendering/Layout Shift overlay during fast continuous scrolling.
