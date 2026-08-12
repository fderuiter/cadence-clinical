# ADR-256: Viewport-Driven DOM Recycler for Large eCRFs

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In large-scale clinical trials, Electronic Case Report Forms (eCRFs) can easily scale to over 500 fields. Rendering all these elements permanently in the DOM causes massive memory overhead, leading to frequent mobile browser memory crashes (OOM) on resource-constrained devices (such as clinical coordinators' iPads). This poses a significant GxP safety, regulatory compliance, and trial efficiency risk.

To preserve the seamless, single-page continuous scroll layout without splitting the form into paginated steps or introducing heavy third-party virtualization libraries (which would violate our architectural constraints), we needed a custom virtualization solution integrated directly into our form layout logic.

This decision addresses the following requirements:

- **PRD-CRF-001:** CRF metadata-driven rendering
- **PRD-CRF-009:** Role-Based Authorization Gates

## 2. Decision Drivers & Constraints

- **Driver 1 (Memory Overhead):** Limit the active DOM element count to under 100 elements even when rendering extremely large, single-page clinical forms.
- **Driver 2 (Visual Performance):** Render off-screen fields slightly before they enter the viewport to avoid noticeable visual flickers or content pop-in (pre-rendering/over-scanning).
- **Driver 3 (Layout Stability):** Prevent layout shifting (CLS) or scrolling jumpiness when elements are dynamically unmounted and remounted during continuous fast scrolling.
- **Driver 4 (Accessibility):** Ensure unmounted/empty layout wrappers always maintain a minimum touch-target height of 44px to satisfy WCAG 2.1 compliance requirements.
- **Driver 5 (State Preservation):** Guarantee that user input values, validation states, and query workflows are never lost when elements are recycled and unmounted.

## 3. Options Considered

### Option 1: Native IntersectionObserver with Over-Scanning & ResizeObserver (Selected)

- **Overview:** Wrap each dynamic clinical form field inside a layout-stable recycling wrapper (`ClinicalFormField.vue`) that dynamically monitors viewport entry and exit using a native browser `IntersectionObserver` with a `200px` root margin for pre-rendering. Active mounted fields are monitored by a `ResizeObserver` to record and cache their exact pixel height; this cached height is then enforced as a placeholder when the child components are unmounted.
- **Pros:**
  - ✅ Restricts the active DOM node count to <100 nodes at any time.
  - ✅ Zero third-party virtualization dependencies.
  - ✅ Root margin of `200px` guarantees smooth scrolling with zero visual flickering.
  - ✅ Dynamic height caching ensures zero layout shifting during mounting/unmounting.
  - ✅ Empty placeholders preserve a minimum height of `44px` for WCAG compliance.
- **Cons:**
  - ❌ Relies on client-side observers which need mock setups in tests.

### Option 2: Heavy Third-Party Virtualization Libraries

- **Overview:** Integrate external packages (e.g., vue-virtual-scroller or similar).
- **Cons:**
  - ❌ Violates our strict third-party registry boundaries and increases GxP validation surface.
  - ❌ Difficult to customize for dynamic forms with fluctuating heights caused by validation errors or expanding query panels.

## 4. Decision Outcome

Chosen option: Option 1. It delivers high-performance virtualization using standard browser APIs, minimizes memory overhead on tablet devices, and guarantees GxP audit data preservation using a completely decoupled Pinia global store (`apps/web/src/stores/clinical.ts`).

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Memory crashes on iPads completely eliminated.
  - Perfect layout stability without any jumpiness or cumulative layout shifts during fast scroll.
  - Zero dependencies added to the shared UI library.
- **Negative Impact:**
  - Testing requires mocking of native browser `IntersectionObserver` and `ResizeObserver` global APIs.

## 6. Implementation & Verification

- **Target Files Modified:**
  - `packages/ui/src/components/clinical/ClinicalFormField.vue`
- **Verification Plan:**
  - Added robust unit and integration tests under `apps/web/tests/viewport_recycler.test.js` validating:
    - Intersection triggers correctly mounting and unmounting child inputs.
    - Dynamic height preservation and cached style assignments.
    - WCAG minimum 44px height enforcement.
    - User input persistence across recycler unmounts/remounts.
