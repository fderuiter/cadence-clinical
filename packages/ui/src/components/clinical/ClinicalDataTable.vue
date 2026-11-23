<script setup>
import { computed, ref, useId, watch } from "vue";

const props = defineProps({
  columns: {
    type: Array,
    required: true,
    // Format: [{ key: 'id', label: 'ID', sortable: true }, ...]
  },
  data: {
    type: Array,
    required: true,
  },
  pageSize: {
    type: Number,
    default: 10,
  },
  selectable: {
    type: Boolean,
    default: false,
  },
  title: {
    type: String,
    default: "Clinical Dataset",
  },
});

const emit = defineEmits(["select", "row-click"]);

const rawHeadingId = useId();
const headingId = computed(
  () => `clinical-data-table-title-${rawHeadingId || "default"}`
);

const sortKey = ref("");
const sortOrder = ref("asc");
const currentPage = ref(1);
const selectedRows = ref(new Set());

const toggleSort = (column) => {
  if (!column.sortable) return;
  if (sortKey.value === column.key) {
    sortOrder.value = sortOrder.value === "asc" ? "desc" : "asc";
  } else {
    sortKey.value = column.key;
    sortOrder.value = "asc";
  }
};

const getAriaSort = (column) => {
  if (!column.sortable) return undefined;
  if (sortKey.value !== column.key) return "none";
  return sortOrder.value === "asc" ? "ascending" : "descending";
};

const getSortIcon = (column) => {
  if (sortKey.value !== column.key) return "⇅";
  return sortOrder.value === "asc" ? "▲" : "▼";
};

const handleSortKeydown = (event, column) => {
  if (!column.sortable) return;
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    toggleSort(column);
  }
};

const sortedData = computed(() => {
  if (!sortKey.value) return props.data;
  return [...props.data].sort((a, b) => {
    const valA = a[sortKey.value] ?? "";
    const valB = b[sortKey.value] ?? "";
    if (valA === valB) return 0;
    const modifier = sortOrder.value === "asc" ? 1 : -1;
    return valA > valB ? modifier : -modifier;
  });
});

const effectivePageSize = computed(() =>
  props.pageSize > 0 ? props.pageSize : 10
);

const totalPages = computed(
  () => Math.ceil(sortedData.value.length / effectivePageSize.value) || 1
);

watch(
  totalPages,
  (nextTotalPages) => {
    if (currentPage.value > nextTotalPages) {
      currentPage.value = nextTotalPages;
    } else if (currentPage.value < 1) {
      currentPage.value = 1;
    }
  },
  { immediate: true }
);

const paginatedData = computed(() => {
  const safePage = Math.min(Math.max(1, currentPage.value), totalPages.value);
  const start = (safePage - 1) * effectivePageSize.value;
  return sortedData.value.slice(start, start + effectivePageSize.value);
});

const getRowKey = (row) =>
  row && row.id !== undefined && row.id !== null && row.id !== ""
    ? row.id
    : row;

const isRowSelected = (row) => selectedRows.value.has(getRowKey(row));

const toggleRowSelection = (row) => {
  const key = getRowKey(row);
  if (selectedRows.value.has(key)) {
    selectedRows.value.delete(key);
  } else {
    selectedRows.value.add(key);
  }
  emit("select", Array.from(selectedRows.value));
};

const INTERACTIVE_ROW_DESCENDANT_SELECTOR =
  'button, a, input, select, textarea, label, summary, [role="button"], [role="link"], [role="checkbox"], [role="switch"], [role="menuitem"], [tabindex]';

const isRowActivationTarget = (event) => {
  const { target, currentTarget } = event;
  if (!target || !currentTarget) return false;
  if (target === currentTarget) return true;
  if (typeof target.closest !== "function") return false;
  return target.closest(INTERACTIVE_ROW_DESCENDANT_SELECTOR) === currentTarget;
};

const handleRowClick = (event, row) => {
  if (!isRowActivationTarget(event)) return;
  emit("row-click", row);
};

const handleRowKeydown = (event, row) => {
  if (event.target !== event.currentTarget) return;
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    emit("row-click", row);
  }
};
</script>

<template>
  <div class="clinical-data-table-container">
    <div class="table-header">
      <h3 :id="headingId" class="table-title">
        {{ title }}
      </h3>
      <span class="table-meta">{{ sortedData.length }} total record(s)</span>
    </div>

    <div
      class="table-wrapper"
      role="region"
      :aria-labelledby="headingId"
      tabindex="0"
    >
      <table class="clinical-table" :aria-labelledby="headingId">
        <caption class="sr-only">
          {{
            title
          }}
        </caption>
        <thead>
          <tr>
            <th v-if="selectable" class="col-select" scope="col">
              <span class="sr-only">Select</span>
            </th>
            <th
              v-for="col in columns"
              :key="col.key"
              :class="['col-header', { sortable: col.sortable }]"
              scope="col"
              :aria-sort="getAriaSort(col)"
            >
              <button
                v-if="col.sortable"
                type="button"
                class="sort-button"
                @click="toggleSort(col)"
                @keydown="handleSortKeydown($event, col)"
              >
                <span>{{ col.label }}</span>
                <span class="sort-icon" aria-hidden="true">
                  {{ getSortIcon(col) }}
                </span>
              </button>
              <div v-else class="header-content">
                <span>{{ col.label }}</span>
              </div>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(row, idx) in paginatedData"
            :key="row.id ?? idx"
            :class="['data-row', { selected: isRowSelected(row) }]"
            tabindex="0"
            :aria-selected="
              selectable ? (isRowSelected(row) ? 'true' : 'false') : undefined
            "
            @click="handleRowClick($event, row)"
            @keydown="handleRowKeydown($event, row)"
          >
            <td v-if="selectable" class="col-select" @click.stop>
              <input
                type="checkbox"
                :checked="isRowSelected(row)"
                :aria-label="'Select row ' + (row.id ?? idx + 1)"
                @change="toggleRowSelection(row)"
              />
            </td>
            <td v-for="col in columns" :key="col.key" class="data-cell">
              <slot :name="'cell-' + col.key" :row="row" :value="row[col.key]">
                {{ row[col.key] }}
              </slot>
            </td>
          </tr>
          <tr v-if="paginatedData.length === 0">
            <td
              :colspan="columns.length + (selectable ? 1 : 0)"
              class="empty-cell"
            >
              No clinical records found.
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="totalPages > 1" class="table-pagination">
      <button
        type="button"
        class="pagination-btn"
        :disabled="currentPage === 1"
        aria-label="Previous page"
        @click="currentPage--"
      >
        Previous
      </button>
      <span
        class="pagination-info"
        role="status"
        aria-live="polite"
        aria-atomic="true"
      >
        Page {{ currentPage }} of {{ totalPages }}
      </span>
      <button
        type="button"
        class="pagination-btn"
        :disabled="currentPage === totalPages"
        aria-label="Next page"
        @click="currentPage++"
      >
        Next
      </button>
    </div>
  </div>
</template>

<style scoped>
.clinical-data-table-container {
  display: flex;
  flex-direction: column;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.table-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
}

.table-title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.table-meta {
  font-size: 13px;
  color: var(--semantic-color-text-muted);
}

.table-wrapper {
  overflow-x: auto;
  width: 100%;
}

.clinical-table {
  width: 100%;
  border-collapse: collapse;
  text-align: left;
  font-size: 14px;
  font-variant-numeric: tabular-nums;
}

.col-header {
  padding: 10px 16px;
  font-weight: 600;
  color: var(--semantic-color-text-muted);
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
  user-select: none;
}

.col-header.sortable {
  padding: 0;
}

.sort-button {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  min-height: var(--semantic-control-target-min, 24px);
  padding: 10px 16px;
  border: 0;
  background: transparent;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.sort-button:hover {
  background: var(--semantic-color-surface-header);
}

.header-content {
  display: flex;
  align-items: center;
  gap: 6px;
}

.sort-icon {
  font-size: 11px;
  color: var(--semantic-color-text-muted);
}

.data-row {
  border-bottom: 1px solid var(--semantic-color-border);
  transition: background-color 0.15s ease;
}

.data-row:hover {
  background: var(--semantic-color-primary-light);
  cursor: pointer;
}

.data-row.selected {
  background: var(--semantic-color-primary-light);
}

.sort-button:focus-visible,
.data-row:focus-visible,
.table-wrapper:focus-visible {
  outline: 3px solid var(--semantic-color-accent);
  outline-offset: -3px;
  box-shadow: inset 0 0 0 2px var(--semantic-color-text-on-solid);
}

.pagination-btn:focus-visible {
  outline: 3px solid var(--semantic-color-accent);
  outline-offset: 2px;
  box-shadow: 0 0 0 2px var(--semantic-color-text-on-solid);
}

@media (forced-colors: active) {
  .sort-button:focus-visible,
  .data-row:focus-visible,
  .table-wrapper:focus-visible {
    outline: 2px solid Highlight;
    outline-offset: -2px;
    box-shadow: none;
  }

  .pagination-btn:focus-visible {
    outline: 2px solid Highlight;
    outline-offset: 2px;
    box-shadow: none;
  }
}

.data-cell {
  padding: 10px 16px;
  color: var(--semantic-color-text);
  font-variant-numeric: tabular-nums;
}

.col-select {
  width: 40px;
  text-align: center;
  padding: 10px 8px;
}

.empty-cell {
  padding: 32px;
  text-align: center;
  color: var(--semantic-color-text-muted);
  font-style: italic;
}

.table-pagination {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 12px;
  padding: 10px 16px;
  background: var(--semantic-color-surface-muted);
  border-top: 1px solid var(--semantic-color-border);
}

.pagination-btn {
  padding: 6px 12px;
  font-size: 13px;
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border-radius: 4px;
  cursor: pointer;
}

.pagination-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.pagination-btn:not(:disabled):hover {
  background: var(--semantic-color-surface-header);
}

.pagination-info {
  font-size: 13px;
  color: var(--semantic-color-text-muted);
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  border: 0;
}
</style>
