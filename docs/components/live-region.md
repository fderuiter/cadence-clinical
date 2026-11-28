# ClinicalLiveRegion & useStatusAnnouncer

The `ClinicalLiveRegion` component and `useStatusAnnouncer` composable in `ui` (`packages/ui`) provide WCAG 2.2 SC 4.1.3 (Status Messages), W3C Technique ARIA22, and W3C Technique ARIA19 compliant live-region announcements across `apps/web` and `apps/subject-portal` ([ADR-FRONTEND-20261128-centralize-shared-live-region-primitives-and-ast-gates](../adr/frontend/2026-11-28-centralize-shared-live-region-primitives-and-ast-gates.md)).

## Architectural Guarantees

- **Persistent Pre-Mounted Containers (ARIA22 & ARIA19)**: `ClinicalLiveRegion` keeps both `[role="status"][aria-live="polite"]` and `[role="alert"]` containers mounted in the DOM from initial component mount, toggling `.sr-only` visibility and text content rather than conditionally mounting live regions via `v-if` or mutating `role` on the fly.
- **Sibling `#pills` and `#actions` Slots Outside Atomic Text**: Filter context badges (`#pills`) and interactive recovery controls (`#actions`, such as `Reset All Filters` or `Retry`) render as sibling flex containers outside the `aria-atomic="true"` live message span so screen readers announce concise status or error prose without reading button labels aloud as part of the alert sentence.
- **Debounced Search & Duplicate-Tick Re-Announcement**: `useStatusAnnouncer` debounces rapid keystroke filter updates (`debounceMs`, default `180ms`), delivers errors immediately on the assertive channel, and clears consecutive identical messages across ticks (`""` -> `nextTick` -> `message`) so repeated filter resets or repeated validation errors are announced every time.
- **Tokenized Visual Variants & Tabular Numerals**: Supports `variant="strip"` (compact bordered filter/status bar above tables), `variant="banner"`, `variant="inline"`, and `variant="sr-only"` (visually hidden announcer), `1.75px` stroke `ClinicalIcon` vectors (`aria-hidden="true"`), and `tabular-nums` alignment on match counts.

## Usage Example: Filter Status Strip (`DocumentsHubView` Pattern)

```vue
<script setup>
import { computed, ref } from "vue";
import { ClinicalDataTable, ClinicalLiveRegion, useStatusAnnouncer } from "ui";

const searchQuery = ref("");
const activeStudyId = ref("CADENCE-101");
const isLoading = ref(false);
const allDocuments = ref([
  { id: "DOC-001", title: "Clinical Protocol v2.0" },
  { id: "DOC-002", title: "Investigator Brochure" },
]);

const filteredDocuments = computed(() => {
  const q = searchQuery.value.trim().toLowerCase();
  if (!q) return allDocuments.value;
  return allDocuments.value.filter((doc) =>
    doc.title.toLowerCase().includes(q)
  );
});

const hasActiveFilters = computed(() => Boolean(searchQuery.value.trim()));

const statusSummary = computed(() => {
  if (isLoading.value) return "Loading clinical documents...";
  const q = searchQuery.value.trim();
  return q
    ? `Showing ${filteredDocuments.value.length} of ${allDocuments.value.length} documents matching '${q}'`
    : `Showing all ${allDocuments.value.length} documents`;
});

const { politeMessage } = useStatusAnnouncer({
  debounceMs: 160,
  statusSource: statusSummary,
});

function resetFilters() {
  searchQuery.value = "";
}
</script>

<template>
  <ClinicalLiveRegion
    id="documents-filter-status"
    variant="strip"
    politeness="polite"
    tone="neutral"
    icon="check-circle"
    :active="hasActiveFilters || isLoading"
    :message="politeMessage || statusSummary"
  >
    <template #pills>
      <span>Study: {{ activeStudyId }}</span>
    </template>
    <template v-if="hasActiveFilters" #actions>
      <button type="button" class="btn btn-sm" @click="resetFilters">
        Reset All Filters
      </button>
    </template>
  </ClinicalLiveRegion>

  <ClinicalDataTable
    title="Clinical Documents"
    :columns="[
      { key: 'id', label: 'Document ID', sortable: true },
      { key: 'title', label: 'Title', sortable: true },
    ]"
    :data="filteredDocuments"
    :loading="isLoading"
  />
</template>
```

## Usage Example: GxP Outcome & Error Alert Banner

```vue
<template>
  <ClinicalLiveRegion
    id="study-locks-outcome"
    variant="banner"
    :politeness="outcome?.error ? 'assertive' : 'polite'"
    :tone="outcome?.error ? 'error' : 'success'"
    :active="Boolean(outcome)"
    :message="outcomeMessage"
  >
    <template v-if="outcome?.error" #actions>
      <button type="button" class="btn btn-secondary" @click="retryLock">
        Retry Lock Check
      </button>
    </template>
  </ClinicalLiveRegion>
</template>
```

## ESLint AST Enforcement Gates

`eslint.config.mjs` enforces three custom Vue template AST rules across `packages/ui`, `apps/web`, and `apps/subject-portal`:

1. `custom-vue/no-conflicting-live-region`: Rejects `role="alert"` (static or dynamic branch) paired with `aria-live="polite"` or `aria-live="off"`, and rejects `role="status"` paired with `aria-live="assertive"`.
2. `custom-vue/no-status-role-on-errors`: Rejects `role="status"` on error or validation banners (`error`, `invalid`, `failure` identifiers in `id`, `class`, or `data-testid`), requiring `role="alert"` or `ClinicalLiveRegion` with `politeness="assertive"`.
3. `custom-vue/no-conditional-status-mount`: Rejects mounting `role="status"` or `aria-live="polite"` elements via `v-if`, `v-else-if`, or `v-else` outside the shrink-only count-ratcheted legacy allowlist (`scripts/eslint/allowlists/live-regions.mjs`).
