# ClinicalDataTable

The `ClinicalDataTable` component provides an accessible, performant tabular viewer for clinical observation records, subject lists, SDTM datasets, and form submissions.

## Features

- **Keyboard-Accessible Column Sorting**: Native `<button type="button">` sort controls inside sortable `<th scope="col">` headers with conditional `aria-sort` (`none`, `ascending`, `descending`), two-color `:focus-visible` indicators, and directional glyphs (`▲`, `▼`, `⇅`).
- **Focusable Row Activation & Selection**: Keyboard-activatable rows (`tabindex="0"`, `Enter` / `Space`) with interactive-descendant guards so row checkboxes (`aria-selected` when `selectable` is enabled) and slotted action buttons never double-fire `row-click`.
- **Programmatic Naming & Polite Pagination**: Unique `useId()` heading linked via `aria-labelledby` and `<caption class="sr-only">`, `type="button"` page controls, `role="status"` / `aria-live="polite"` page announcements, and automatic `currentPage` clamping when filtered datasets shrink.
- **Custom Cell Slotting**: Slot templates for formatting clinical codes, flags, or status pills.

## Usage Example

```vue
<script setup>
import { ref } from "vue";
import { ClinicalDataTable } from "ui";

const columns = [
  { key: "subject_id", label: "Subject ID", sortable: true },
  { key: "site_id", label: "Site", sortable: true },
  { key: "status", label: "Enrollment Status", sortable: true },
  { key: "enrolled_at", label: "Enrollment Date", sortable: true },
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
    :columns="columns"
    :data="subjects"
    :pageSize="10"
    :selectable="true"
    @row-click="handleRowClick"
  />
</template>
```
