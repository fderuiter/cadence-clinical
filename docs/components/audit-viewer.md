# AuditLogViewer

The `AuditLogViewer` renders an interactive, chronological 21 CFR Part 11 audit trail feed with before/after value diffing, filtering by action type (`CREATE`, `UPDATE`, `DELETE`, `SIGN`), and text search.

## Features

- **Immutable Audit Record Feed**: ARIA `feed` landmark structure with interactive focusable entries.
- **Visual Value Diffing**: Displays previous vs new values for modified clinical observations.
- **Action Type Filtering**: Filter by specific action tags (`SIGN`, `UPDATE`, etc.).

## Usage Example

```vue
<script setup>
import { ref } from "vue";
import { AuditLogViewer } from "@cadence/ui";

const auditEntries = ref([
  {
    id: "aud_001",
    timestamp: "2026-08-14 14:30:00 UTC",
    user_id: "crc_smith@site101.org",
    action: "UPDATE",
    entity_type: "ClinicalObservation",
    entity_id: "obs_sysbp_01",
    reason_for_change:
      "Correction of transcription typo from source paper chart",
    changes: [{ field: "value", old_val: "220", new_val: "120" }],
  },
]);
</script>

<template>
  <AuditLogViewer
    :logs="auditEntries"
    title="Subject Observation Audit Trail"
  />
</template>
```
