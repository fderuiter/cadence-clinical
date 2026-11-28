# ClinicalDataTable, ClinicalRecordCard & use2DGrid

`packages/ui` provides three coordinated tabular and matrix primitives for clinical observation records, CTMS/RTSM/eTMF queues, participant portal compliance records, and two-dimensional protocol matrices ([ADR-FRONTEND-20261128](../adr/frontend/2026-11-28-slot-projected-clinicalrecordcard-mobile-mode-and-shared-use2dgrid.md)):

1. **`ClinicalDataTable`**: Accessible, sortable, paginated dataset table that automatically projects rows into `ClinicalRecordCard` items at `<= 480px` viewport or container widths.
2. **`ClinicalRecordCard`**: Standalone and slot-projected clinical record card supporting `compact` and `touch` density profiles, monospace or `EntityLink` (`NodeRef`) identifiers, semantic status badges, `<dl>` tabular key-value grids, and `position: relative` action footers.
3. **`use2DGrid`**: Shared 2D ARIA grid composable managing roving `tabindex` (`0` active, `-1` inactive), 4-way arrow keys, `Home`/`End`, `Ctrl+Home`/`Ctrl+End`, `PageUp`/`PageDown`, coordinate clamping, and inline-edit `Escape` focus restoration.

## ClinicalDataTable Features

- **Vector Column Sorting & Target Sizing**: Native `<button type="button">` sort controls inside sortable `<th scope="col">` headers with conditional `aria-sort` (`none`, `ascending`, `descending`), two-color `:focus-visible` inset rings, `44px` (`compact`/`comfortable`) and `48px` (`touch`) minimum target heights, and `16px` `lucide-vue-next` sort icons (`ArrowUpDown`, `ArrowUp`, `ArrowDown`) at `1.75px` stroke width (enforced by `custom-vue/no-unicode-sort-glyphs`).
- **Automatic `<= 480px` `ClinicalRecordCard` Projection & Mobile Sort Bar**: Controlled via `mobileMode` (`"auto"` default | `"cards"` | `"scroll"` | `"table"`) and `mobileBreakpoint` (`480`). When projected into cards, `ClinicalDataTable` reuses existing `#cell-<key>`, `#masked-row`, `#after-row` (`{ row, index, colSpan, isCardView }`), `#card-footer`, and `#empty` slots without duplicating view markup, and renders a mobile sort selector + direction toggle bar (`data-testid="clinical-mobile-sort-bar"`) whenever any column declares `sortable: true`. Columns can optionally set `cardRole` (`"identifier" | "title" | "status" | "actions" | "field" | "select" | "hidden"`), `mono`, and `fullWidth`.
- **Sticky Columns, Sticky Headers & Density Profiles**: Columns support `sticky: "left" | "right"` so primary subject, site, or document identifiers stay pinned (`position: sticky; left: 0`) during horizontal panning on tablet/desktop viewports (`>= 768px`), paired with `stickyHeader` and `density` (`"compact" | "comfortable" | "touch"`). `.table-wrapper` establishes `position: relative` so `.sr-only` context spans inside action buttons remain contained within scroll bounds (enforced by `custom-vue/require-scroll-wrapper-relative`).
- **Focusable Row Activation, Selection & Masked Rows**: Keyboard-activatable rows/cards (`tabindex="0"`, `Enter` / `Space`), `#masked-row` redacted state (`isRowMasked`), and interactive-descendant guards so checkboxes and slotted action buttons never double-fire `row-click`.
- **Programmatic Naming & Pagination**: Unique `useId()` heading (or `ariaLabel` when `hideHeader` is enabled) linked via `aria-labelledby` and `<caption>` (`captionVisible` or `.sr-only`), client or `externalPagination` modes, `44px`/`48px` `.pagination-btn` controls, and polite status announcements.

## ClinicalDataTable Usage Example

```vue
<script setup>
import { ref } from "vue";
import { ClinicalDataTable } from "ui";

const columns = [
  {
    key: "subject_id",
    label: "Subject ID",
    sortable: true,
    sticky: "left",
    rowHeader: true,
    cardRole: "identifier",
  },
  { key: "site_id", label: "Site", sortable: true, mono: true },
  {
    key: "status",
    label: "Enrollment Status",
    sortable: true,
    cardRole: "status",
  },
  { key: "enrolled_at", label: "Enrollment Date", sortable: true, mono: true },
];

const subjects = ref([
  {
    id: "SUBJ-001",
    subject_id: "SUBJ-001",
    site_id: "SITE-101",
    status: "ENROLLED",
    enrolled_at: "2026-01-15",
  },
  {
    id: "SUBJ-002",
    subject_id: "SUBJ-002",
    site_id: "SITE-101",
    status: "SCREENED",
    enrolled_at: "2026-01-18",
  },
]);

const handleRowClick = (row) => {
  console.log("Selected subject:", row);
};
</script>

<template>
  <ClinicalDataTable
    title="Enrolled Subjects"
    density="compact"
    sticky-header
    :columns="columns"
    :data="subjects"
    :page-size="10"
    :selectable="true"
    @row-click="handleRowClick"
  />
</template>
```

## Standalone ClinicalRecordCard Example

```vue
<script setup>
import { ClinicalRecordCard } from "ui";

const fields = [
  { key: "site", label: "Site", value: "SITE-104", mono: true },
  { key: "visit_date", label: "Target Date", value: "2026-11-30", mono: true },
];
</script>

<template>
  <ClinicalRecordCard
    density="touch"
    identifier="VISIT-204"
    :node-ref="{ kind: 'Visit', id: 'VISIT-204' }"
    title="Interim Monitoring Visit"
    status="Scheduled"
    :fields="fields"
  >
    <template #footer>
      <button type="button" class="btn btn-secondary">
        Review<span class="sr-only"> VISIT-204</span>
      </button>
    </template>
  </ClinicalRecordCard>
</template>
```

## Shared use2DGrid Example (2D Clinical Matrices)

True two-dimensional matrices (such as Schedule of Activities, participant casebooks, eTMF completeness matrices, and repeating log grids) retain `<table>` semantics across viewports with `.scrollable-table-wrapper--matrix`, sticky row headers (`position: sticky; left: 0`), and `use2DGrid`:

```vue
<script setup>
import { ref } from "vue";
import { use2DGrid } from "ui";

const containerRef = ref(null);
const rows = ref(["Demographics", "Vital Signs"]);
const visits = ref(["Screening", "Day 1", "Week 4"]);

const { getCellTabindex, selectCell, handleCellKeyDown } = use2DGrid(
  () => rows.value.length,
  () => visits.value.length,
  { containerRef }
);
</script>

<template>
  <div
    ref="containerRef"
    class="scrollable-table-wrapper scrollable-table-wrapper--matrix"
  >
    <table role="grid" aria-label="Schedule Matrix">
      <tbody>
        <tr v-for="(rowName, rIdx) in rows" :key="rowName">
          <th scope="row" style="position: sticky; left: 0">{{ rowName }}</th>
          <td
            v-for="(visit, cIdx) in visits"
            :key="visit"
            :data-row="rIdx"
            :data-col="cIdx"
            :tabindex="getCellTabindex(rIdx, cIdx)"
            @click="selectCell(rIdx, cIdx, false)"
            @keydown="handleCellKeyDown($event, rIdx, cIdx)"
          >
            Scheduled
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
```
