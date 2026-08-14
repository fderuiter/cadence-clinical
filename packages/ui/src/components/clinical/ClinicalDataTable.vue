<script setup>
import { computed, ref } from "vue";

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

const totalPages = computed(
  () => Math.ceil(sortedData.value.length / props.pageSize) || 1
);

const paginatedData = computed(() => {
  const start = (currentPage.value - 1) * props.pageSize;
  return sortedData.value.slice(start, start + props.pageSize);
});

const isRowSelected = (row) => selectedRows.value.has(row.id || row);

const toggleRowSelection = (row) => {
  const key = row.id || row;
  if (selectedRows.value.has(key)) {
    selectedRows.value.delete(key);
  } else {
    selectedRows.value.add(key);
  }
  emit("select", Array.from(selectedRows.value));
};
</script>

<template>
  <div class="clinical-data-table-container">
    <div class="table-header">
      <h3 class="table-title">{{ title }}</h3>
      <span class="table-meta">{{ sortedData.length }} total record(s)</span>
    </div>

    <div
      class="table-wrapper"
      role="region"
      aria-label="Clinical Data Table"
      tabindex="0"
    >
      <table class="clinical-table">
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
              :aria-sort="
                sortKey === col.key
                  ? sortOrder === 'asc'
                    ? 'ascending'
                    : 'descending'
                  : 'none'
              "
              @click="toggleSort(col)"
            >
              <div class="header-content">
                <span>{{ col.label }}</span>
                <span v-if="col.sortable" class="sort-icon" aria-hidden="true">
                  {{
                    sortKey === col.key
                      ? sortOrder === "asc"
                        ? "▲"
                        : "▼"
                      : "⇅"
                  }}
                </span>
              </div>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(row, idx) in paginatedData"
            :key="row.id || idx"
            :class="['data-row', { selected: isRowSelected(row) }]"
            @click="emit('row-click', row)"
          >
            <td v-if="selectable" class="col-select" @click.stop>
              <input
                type="checkbox"
                :checked="isRowSelected(row)"
                :aria-label="'Select row ' + (row.id || idx + 1)"
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
        class="pagination-btn"
        :disabled="currentPage === 1"
        @click="currentPage--"
        aria-label="Previous page"
      >
        Previous
      </button>
      <span class="pagination-info">
        Page {{ currentPage }} of {{ totalPages }}
      </span>
      <button
        class="pagination-btn"
        :disabled="currentPage === totalPages"
        @click="currentPage++"
        aria-label="Next page"
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
  background: var(--color-surface, #ffffff);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.table-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: var(--color-surface-muted, #f8fafc);
  border-bottom: 1px solid var(--color-border, #e2e8f0);
}

.table-title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  color: var(--color-text, #0f172a);
}

.table-meta {
  font-size: 13px;
  color: var(--color-text-muted, #475569);
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
}

.col-header {
  padding: 10px 16px;
  font-weight: 600;
  color: var(--color-text-muted, #475569);
  background: var(--color-surface-muted, #f8fafc);
  border-bottom: 1px solid var(--color-border, #e2e8f0);
  user-select: none;
}

.col-header.sortable {
  cursor: pointer;
}

.col-header.sortable:hover {
  background: #f1f5f9;
}

.header-content {
  display: flex;
  align-items: center;
  gap: 6px;
}

.sort-icon {
  font-size: 11px;
  color: var(--color-text-muted, #475569);
}

.data-row {
  border-bottom: 1px solid var(--color-border, #e2e8f0);
  transition: background-color 0.15s ease;
}

.data-row:hover {
  background: var(--color-primary-light, #e0f2fe);
  cursor: pointer;
}

.data-row.selected {
  background: #e0f2fe;
}

.data-cell {
  padding: 10px 16px;
  color: var(--color-text, #0f172a);
}

.col-select {
  width: 40px;
  text-align: center;
  padding: 10px 8px;
}

.empty-cell {
  padding: 32px;
  text-align: center;
  color: var(--color-text-muted, #475569);
  font-style: italic;
}

.table-pagination {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 12px;
  padding: 10px 16px;
  background: var(--color-surface-muted, #f8fafc);
  border-top: 1px solid var(--color-border, #e2e8f0);
}

.pagination-btn {
  padding: 6px 12px;
  font-size: 13px;
  border: 1px solid var(--color-border, #e2e8f0);
  background: var(--color-surface, #ffffff);
  border-radius: 4px;
  cursor: pointer;
}

.pagination-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.pagination-btn:not(:disabled):hover {
  background: #f1f5f9;
}

.pagination-info {
  font-size: 13px;
  color: var(--color-text-muted, #475569);
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
