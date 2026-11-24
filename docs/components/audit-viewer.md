# AuditLogViewer

The `AuditLogViewer` renders an interactive, chronological 21 CFR Part 11 audit trail feed with before/after value diffing, filtering by action type (`CREATE`, `UPDATE`, `DELETE`, `SIGN`), and text search.

## Features

- **Immutable Audit Record Feed**: ARIA `feed` landmark structure with interactive focusable entries and in-place masked placeholders (`Blinded` / `NotPermitted`) preserving sequence numbering.
- **Visual Value Diffing**: Displays previous vs new values for modified clinical observations and lifecycle transitions.
- **Action Type Filtering**: Filter by specific action tags (`SIGN`, `UPDATE`, etc.) and search text.
- **Kernel & Domain Adapters (`auditAdapters.js`)**: `normalizeAuditEntry`, `adaptKernelHistoryEntries`, `adaptKernelAuditEntryDetail`, `adaptKernelActivityEntries`, `adaptObservationAuditEntries`, `adaptKnowledgeAuditLogs`, and `adaptTicketAuditLogs` normalise kernel node history, activity stream, and domain audit entries without leaking raw UUIDs, fake digests, or SQL table names into primary copy.
- **Cross-App `<EntityLink>` Chips & `Audit Event` Peek Drawer**: Renders actor, target, attestation, and scope references as keyboard-operable `<EntityLink>` chips and opens a contextual `ClinicalDrawer` (`Audit Event`) with progressive `<SupportDetails>` SHA-256 hash disclosure.

## Usage Example

```vue
<script setup>
import { ref } from "vue";
import { AuditLogViewer } from "ui";

const auditEntries = ref([
  {
    id: "aud_001",
    timestamp: "2026-08-14 14:30:00 UTC",
    user_id: "crc_smith@cadence.clinical",
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
