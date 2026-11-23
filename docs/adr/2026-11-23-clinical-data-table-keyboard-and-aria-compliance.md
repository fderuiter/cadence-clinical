# ADR-2655: Keyboard Operability and WAI-ARIA Semantics in `ClinicalDataTable`

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level A & AA SC 1.3.1 Info and Relationships, SC 2.1.1 Keyboard, SC 2.4.7 Focus Visible, SC 4.1.2 Name, Role, Value, and SC 4.1.3 Status Messages compliance)

---

## 1. Context & Problem Statement

`packages/ui/src/components/clinical/ClinicalDataTable.vue` is the shared tabular primitive exported from `ui` for `apps/web` and `apps/subject-portal`. Despite the shared UI WCAG mandate in [ADR-2309](2026-08-02-implement-wcag-compliance-in-shared-clinical-ui-primitives.md), `ClinicalDataTable.vue` exhibited four accessibility defects:

1. **Mouse-Only Column Sorting and Unconditional `aria-sort` (WCAG 2.2 SC 2.1.1, SC 4.1.2):** `<th>` headers bound `@click="toggleSort(col)"` directly without focusable `<button type="button">` controls, preventing keyboard-only clinicians and monitors from sorting columns, while static columns exposed `aria-sort="none"` despite not being sortable.
2. **Mouse-Only Row Activation and Missing `aria-selected` (WCAG 2.2 SC 2.1.1, SC 4.1.2):** `<tr class="data-row">` bound `@click="emit('row-click', row)"` without `tabindex="0"`, `Enter`/`Space` keyboard handlers, interactive-child target guards, or conditional `aria-selected` when `selectable` was enabled.
3. **Hardcoded Region Label and Silent Pagination Updates (WCAG 2.2 SC 1.3.1, SC 4.1.3):** `.table-wrapper` hardcoded `aria-label="Clinical Data Table"` instead of linking to the visible `title` heading via `useId()` and `aria-labelledby`, `<table>` omitted `<caption class="sr-only">`, pagination buttons lacked `type="button"`, `.pagination-info` updated without `role="status"` / `aria-live="polite"`, and shrinking filtered datasets could leave `currentPage` above `totalPages`.
4. **Missing Automated `axe-core` and Keyboard Regression Coverage:** `packages/ui/tests/shared_components.test.js` exercised SSR output and setup functions directly without DOM keyboard dispatch or `toBeAccessible` (`axe-core`) assertions across `packages/ui`, `apps/web`, and `apps/subject-portal`.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level A & AA Compliance (PRD-CRF-015, ADR-2309):** Satisfy SC 1.3.1 (Techniques ARIA13, ARIA16, G115), SC 2.1.1 (Techniques G202, G90), SC 2.4.7 (Technique C45), SC 4.1.2 (Technique G108), and SC 4.1.3 (Technique ARIA22) with zero third-party runtime dependencies.
- **Zero Breaking API Changes:** Preserve all existing props (`columns`, `data`, `pageSize`, `selectable`, `title`), events (`select`, `row-click`), and `#cell-*` slot contracts while keeping semantic `<table>` reading navigation intact in NVDA, JAWS, and VoiceOver.
- **Cross-Workspace Verification:** Verify `ClinicalDataTable` via `toBeAccessible` (`ui/vitest`) in `packages/ui`, `apps/web`, and `apps/subject-portal`.

## 3. Options Considered

### Option 1: Semantic `<table>` Enhancement with Native Sort Buttons, Focusable Row Activation, `useId` Naming, and Polite Pagination Status (Selected)

- **Overview:**
  1. Render `<button type="button" class="sort-button">` inside sortable `<th scope="col">` elements, bind `aria-sort` (`none`, `ascending`, `descending`) only when `col.sortable` is true, and add two-color `:focus-visible` indicators (`--semantic-color-accent` + `--semantic-color-text-on-solid` inner halo plus `@media (forced-colors: active)` `Highlight` outlines).
  2. Add `tabindex="0"` and `Enter`/`Space` handlers on `<tr class="data-row">` guarded by `event.target === event.currentTarget` (and interactive-descendant filtering on `@click` so row checkboxes and slotted buttons never double-fire `row-click`), and bind `aria-selected` only when `selectable` is true.
  3. Generate a unique heading ID with Vue `useId()`, link `.table-wrapper` (`role="region"`) and `<table>` via `aria-labelledby`, add `<caption class="sr-only">`, add `type="button"` to pagination buttons, expose `role="status"` and `aria-live="polite"` on `.pagination-info`, and clamp `currentPage` whenever `totalPages` shrinks.
- **Pros:**
  - Preserves native HTML `<table>` screen-reader table traversal commands without custom 2D arrow-key routing or runtime bundle growth.
  - Eliminates keyboard traps, double-fired `row-click` events from nested checkboxes/buttons, and out-of-range empty pages after filtering.
  - Works consistently across Chromium, Firefox, Safari, and JSDOM `axe-core` suites.
- **Cons:**
  - Focusable rows (`tabindex="0"`) add sequential `Tab` stops proportional to `pageSize`, and some screen readers announce `aria-selected` primarily in `grid` roles (mitigated by explicit labeled row selection checkboxes).

### Option 2: Convert `ClinicalDataTable` to a Full WAI-ARIA `role="grid"` Widget

- **Overview:** Replace semantic `<table>` roles with `role="grid"`, `role="row"`, and `role="gridcell"` managed by a custom 2D roving `tabindex` controller.
- **Pros:**
  - Collapses the table body to a single `Tab` stop and standardizes `aria-selected` announcements across desktop screen readers.
- **Cons:**
  - Overrides standard virtual-cursor table reading commands in NVDA, JAWS, and VoiceOver for read-mostly clinical datasets and complicates arbitrary `#cell-*` slot controls.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Enhancing the semantic `<table>` primitive with `<button type="button">` sort headers, guarded keyboard row activation, `useId()` region/caption naming, and `role="status"` live pagination satisfies WCAG 2.2 Level A & AA while preserving screen-reader table reading modes and backward compatibility across `packages/ui`, `apps/web`, and `apps/subject-portal`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Sortable columns, selectable/clickable rows, table regions, and pagination status updates in `ClinicalDataTable` are fully operable by keyboard and announced accurately by assistive technologies, backed by `axe-core` assertions in `packages/ui`, `apps/web`, and `apps/subject-portal`.
- **Negative Impact / Technical Debt:** Developers adding custom non-semantic interactive elements inside `#cell-*` slots must use semantic interactive elements (`<button>`, `<a>`, `<input>`) so row-activation guards recognize them automatically (already enforced by `vuejs-accessibility/no-static-element-interactions`).
- **Mitigation Strategy:** Keep `pageSize` bounded (default `10`) to limit row `Tab` stops and enforce `toBeAccessible` coverage in component and application test suites.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/components/data-table.md`, `docs/components/index.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter web exec vitest run tests/accessibility.test.js`, `pnpm --filter subject-portal exec vitest run tests/clinical-data-table-a11y.test.js`, `pnpm lint`, `pnpm test`, headless Chromium keyboard/focus verification, and `pnpm cadence check`.
