# ClinicalDataTable

The `ClinicalDataTable` component provides an accessible, performant tabular viewer for clinical observation records, subject lists, SDTM datasets, and form submissions.

## Features

- **Column Sorting**: Clickable column headers with visual directional indicators (`▲`, `▼`, `⇅`).
- **Pagination**: Configurable page sizes with keyboard-navigable page controls.
- **Row Selection**: Optional checkbox selection with parent emit handlers.
- **Custom Cell Slotting**: Slot templates for formatting clinical codes, flags, or status pills.

## Usage Example

```vue
<script setup>
import { ref } from "vue";
import { ClinicalDataTable } from "@cadence/ui";

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
