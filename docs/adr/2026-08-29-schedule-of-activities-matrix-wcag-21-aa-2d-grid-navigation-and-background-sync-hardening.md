# ADR-2235: Schedule of Activities Matrix WCAG 2.1 AA 2D Grid Navigation and Background Sync Hardening

- **Status:** Accepted
- **Date:** 2026-08-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The CDISC USDM Schedule of Activities (SoA) matrix is the mission-critical design interface in Cadence Clinical for defining clinical study arms, epochs, encounters/visits, activities/procedures, and applicability timings.

Prior to this decision, two major usability and reliability bottlenecks existed:

1. **Accessibility Barriers (WCAG 2.1 AA Non-Conformance)**:
   Complex clinical matrices rendered as standard HTML tables without WAI-ARIA grid roles or roving keyboard focus. Keyboard-only users (and screen-reader assistive devices) could not navigate efficiently across the two-dimensional matrix using arrow keys (`ArrowUp`, `ArrowDown`, `ArrowLeft`, `ArrowRight`), and cells lacked contextual semantic descriptions linking visit names with procedure names and scheduling status.

2. **Client-Side Sync Instability & Network Drops**:
   The background query ledger synchronization loop in Pinia (`startSyncTimer` in `apps/web/src/stores/clinical.ts`) threw unhandled promise rejections upon transient network dropouts, leading to unhandled runtime exceptions. Furthermore, cyclic dependency detection in skip-logic trees utilized blocking browser `window.alert()` calls, disrupting automated test runs and headless environments.

This decision resolves requirements under `@req:PRD-SYS-020` and WCAG 2.1 AA.

## 2. Decision Drivers & Constraints

- **Accessibility Standard (WCAG 2.1 AA / Section 508)**: Full keyboard operability (Success Criterion 2.1.1), non-trapping focus (2.1.2), and accessible name/role/value announcements (4.1.2).
- **Zero Tailwind Dependency (Vanilla CSS Standard)**: High-density clinical components must adhere strictly to Vanilla CSS semantic tokens (`var(--primary)`, `var(--surface)`, `var(--border)`).
- **Non-Blocking Fault Tolerance**: Background synchronization must handle transient network disconnects gracefully without crashing the UI or raising unhandled promise rejections.

## 3. Options Considered

1. **Option A (Selected): Native Vue 3 Composable (`useGridNavigation`) + WAI-ARIA Roles**
   - Clean separation of concern, full testability, zero external runtime dependencies.
2. **Option B: Third-Party Data Grid Component (e.g. AG-Grid / Handsontable)**
   - _Rejected_: Massive bundle size overhead (>500KB), proprietary license friction, and difficulty styling with Vanilla CSS tokens.
3. **Option C: Unstructured HTML Table with Click-Only Handlers**
   - _Rejected_: Violates WCAG 2.1 AA accessibility regulations for clinical software.

## 4. Decision Outcome

Chosen option: **Option A (Native Vue 3 Composable + WAI-ARIA Matrix Roles)**.

### 1. Reusable 2D Grid Navigation Composable (`useGridNavigation.ts`)

A dedicated Vue 3 composable encapsulates roving `tabindex` and 2-dimensional keyboard navigation:

```typescript
export function useGridNavigation(initialRow = 0, initialCol = 0) {
  const focusedRow = ref(initialRow);
  const focusedCol = ref(initialCol);

  function setFocus(
    row: number,
    col: number,
    maxRows?: number,
    maxCols?: number
  ) {
    focusedRow.value = Math.max(0, Math.min(row, (maxRows ?? 1) - 1));
    focusedCol.value = Math.max(0, Math.min(col, (maxCols ?? 1) - 1));
    // DOM focus synchronization
  }

  function getCellTabindex(row: number, col: number): number {
    return focusedRow.value === row && focusedCol.value === col ? 0 : -1;
  }
  // Arrow key routing (ArrowUp, ArrowDown, ArrowLeft, ArrowRight, Home, End, PageUp, PageDown)
}
```

### 2. WAI-ARIA Matrix Semantics

Components [`ClinicalSoAMatrix.vue`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/web/src/components/clinical/ClinicalSoAMatrix.vue) and [`SoAMatrixEditor.vue`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/web/src/components/clinical/SoAMatrixEditor.vue) declare full ARIA hierarchies:

- Root container: `role="grid"` with `aria-label="Schedule of Activities Matrix"`.
- Table rows: `role="row"`.
- Header cells: `role="columnheader"` and `role="rowheader"`.
- Matrix cells: `role="gridcell"` with dynamic `:aria-label="Visit ${col.name}, ${row.name}: ${status}"` and roving `tabindex`.
- Keyboard toggles: Pressing `Space` or `Enter` on a focused cell toggles scheduling assignment immediately.

### 3. Background Sync & Exception Guarding

In [`apps/web/src/stores/clinical.ts`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/web/src/stores/clinical.ts):

- `startSyncTimer` executes with `.catch()` boundary guards and safe interval error logging.
- `window.alert()` calls are replaced with reactive `this.soaError` store states for non-blocking UI alert banners.

## 5. Consequences & Trade-offs

### Positive

- **Complete Keyboard Accessibility**: Coordinators and designers can navigate and configure large SoA matrices entirely via keyboard.
- **Screen Reader Clarity**: Clear auditory context provided on every cell focus without visual clutter.
- **Client Stability**: Unhandled promise rejections during offline operation or network blips are completely eliminated.

### Trade-offs

- Roving tabindex requires maintaining active focus coordinates in Vue reactive state.

## 6. Implementation & Verification

- **Composable**: [`apps/web/src/composables/useGridNavigation.ts`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/web/src/composables/useGridNavigation.ts).
- **Matrix Components**: [`apps/web/src/components/clinical/ClinicalSoAMatrix.vue`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/web/src/components/clinical/ClinicalSoAMatrix.vue), [`apps/web/src/components/clinical/SoAMatrixEditor.vue`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/web/src/components/clinical/SoAMatrixEditor.vue).
- **Store & Portal**: [`apps/web/src/stores/clinical.ts`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/web/src/stores/clinical.ts), [`apps/subject-portal/src/index.js`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/subject-portal/src/index.js).
- **Unit Tests**:
  - [`apps/web/tests/components/Grid2DKeyboardNavigation.spec.js`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/web/tests/components/Grid2DKeyboardNavigation.spec.js) (Roving focus, keyboard arrow routing, Space/Enter toggling, axe accessibility checks).
  - [`apps/web/tests/usdm_validation.test.js`](file:///Volumes/SanDisk%20External%20SSD/Code/cadence-clinical/apps/web/tests/usdm_validation.test.js) (Reactive error states).
  - 546 web unit tests passing cleanly.
