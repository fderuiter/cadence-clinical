<script setup>
import { computed, ref, useId, watch } from "vue";

const props = defineProps({
  columns: {
    type: Array,
    required: true,
    // Format: [{ key: 'id', label: 'ID', sortable: true, headerClass?: '', cellClass?: '' }, ...]
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
  hideHeader: {
    type: Boolean,
    default: false,
  },
  labelledBy: {
    type: String,
    default: "",
  },
  ariaLabel: {
    type: String,
    default: "",
  },
  plain: {
    type: Boolean,
    default: false,
  },
  containerClass: {
    type: [String, Array, Object],
    default: "",
  },
  wrapperClass: {
    type: [String, Array, Object],
    default: "",
  },
  wrapperTestId: {
    type: String,
    default: "",
  },
  tableId: {
    type: String,
    default: "",
  },
  tableClass: {
    type: [String, Array, Object],
    default: "",
  },
  tableTestId: {
    type: String,
    default: "",
  },
  tbodyId: {
    type: String,
    default: "",
  },
  rowKey: {
    type: [String, Function],
    default: "id",
  },
  rowId: {
    type: Function,
    default: null,
  },
  rowClass: {
    type: [String, Function],
    default: null,
  },
  rowAttrs: {
    type: Function,
    default: null,
  },
  selectedRowKeys: {
    type: Array,
    default: null,
  },
  emptyMessage: {
    type: String,
    default: "No clinical records found.",
  },
  isRowMasked: {
    type: Function,
    default: null,
  },
  externalPagination: {
    type: Boolean,
    default: false,
  },
  page: {
    type: Number,
    default: null,
  },
  totalRecords: {
    type: Number,
    default: null,
  },
  hasMore: {
    type: Boolean,
    default: false,
  },
  loadingMore: {
    type: Boolean,
    default: false,
  },
  loadMoreLabel: {
    type: String,
    default: "Load more",
  },
  loadMoreId: {
    type: String,
    default: "",
  },
  externalSort: {
    type: Boolean,
    default: false,
  },
  initialSortKey: {
    type: String,
    default: "",
  },
  initialSortOrder: {
    type: String,
    default: "asc",
  },
});

const emit = defineEmits([
  "select",
  "row-click",
  "page-change",
  "load-more",
  "sort-change",
]);

const rawHeadingId = useId();
const headingId = computed(
  () => `clinical-data-table-title-${rawHeadingId || "default"}`
);
const effectiveTableLabelledBy = computed(
  () => props.labelledBy || (props.ariaLabel ? undefined : headingId.value)
);
const effectiveRegionLabelledBy = computed(() =>
  props.ariaLabel ? undefined : headingId.value
);
const srOnlyHeadingText = computed(() =>
  props.labelledBy ? `${props.title} table` : props.title
);

const sortKey = ref(props.initialSortKey || "");
const sortOrder = ref(props.initialSortOrder || "asc");
const currentPage = ref(props.page && props.page > 0 ? props.page : 1);
const selectedRows = ref(new Set());

watch(
  () => props.page,
  (nextPage) => {
    if (typeof nextPage === "number" && nextPage > 0) {
      currentPage.value = nextPage;
    }
  }
);

watch(
  () => props.selectedRowKeys,
  (nextSelected) => {
    if (Array.isArray(nextSelected)) {
      selectedRows.value = new Set(nextSelected);
    }
  },
  { immediate: true }
);

const toggleSort = (column) => {
  if (!column.sortable) return;
  if (sortKey.value === column.key) {
    sortOrder.value = sortOrder.value === "asc" ? "desc" : "asc";
  } else {
    sortKey.value = column.key;
    sortOrder.value = "asc";
  }
  emit("sort-change", { key: sortKey.value, order: sortOrder.value });
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

const handleHeaderCellClick = (event, column) => {
  if (!column.sortable) return;
  if (
    event?.target &&
    typeof event.target.closest === "function" &&
    event.target.closest("button")
  ) {
    return;
  }
  toggleSort(column);
};

const sortedData = computed(() => {
  if (!sortKey.value || props.externalSort) return props.data;
  return [...props.data].sort((a, b) => {
    if (props.isRowMasked && (props.isRowMasked(a) || props.isRowMasked(b))) {
      return 0;
    }
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

const displayTotalCount = computed(() =>
  typeof props.totalRecords === "number"
    ? props.totalRecords
    : sortedData.value.length
);

const totalPages = computed(() => {
  const count = props.externalPagination
    ? displayTotalCount.value
    : sortedData.value.length;
  return Math.ceil(count / effectivePageSize.value) || 1;
});

watch(
  totalPages,
  (nextTotalPages) => {
    if (props.externalPagination) return;
    if (currentPage.value > nextTotalPages) {
      currentPage.value = nextTotalPages;
    } else if (currentPage.value < 1) {
      currentPage.value = 1;
    }
  },
  { immediate: true }
);

const paginatedData = computed(() => {
  if (props.externalPagination) {
    return sortedData.value;
  }
  const safePage = Math.min(Math.max(1, currentPage.value), totalPages.value);
  const start = (safePage - 1) * effectivePageSize.value;
  return sortedData.value.slice(start, start + effectivePageSize.value);
});

const totalColumnSpan = computed(
  () => props.columns.length + (props.selectable ? 1 : 0)
);

const getRowKey = (row, idx) => {
  if (typeof props.rowKey === "function") {
    return props.rowKey(row, idx);
  }
  const keyName = props.rowKey || "id";
  if (
    row &&
    row[keyName] !== undefined &&
    row[keyName] !== null &&
    row[keyName] !== ""
  ) {
    return row[keyName];
  }
  return idx !== undefined ? idx : row;
};

const resolveRowId = (row, idx) =>
  typeof props.rowId === "function" ? props.rowId(row, idx) : undefined;

const resolveRowClass = (row, idx) =>
  typeof props.rowClass === "function"
    ? props.rowClass(row, idx)
    : props.rowClass || undefined;

const resolveRowAttrs = (row, idx) =>
  typeof props.rowAttrs === "function" ? props.rowAttrs(row, idx) : undefined;

const checkRowMasked = (row, idx) =>
  typeof props.isRowMasked === "function"
    ? Boolean(props.isRowMasked(row, idx))
    : false;

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

const goToPage = (nextPage) => {
  const clamped = Math.min(Math.max(1, nextPage), totalPages.value);
  if (clamped === currentPage.value) return;
  currentPage.value = clamped;
  emit("page-change", clamped);
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

const handleRowClick = (event, row, idx) => {
  if (checkRowMasked(row, idx)) return;
  if (!isRowActivationTarget(event)) return;
  emit("row-click", row);
};

const handleRowKeydown = (event, row, idx) => {
  if (checkRowMasked(row, idx)) return;
  if (event.target !== event.currentTarget) return;
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    emit("row-click", row);
  }
};
</script>

<template>
  <div
    :class="[
      'clinical-data-table-container',
      { 'clinical-data-table-container--plain': plain },
      containerClass,
    ]"
  >
    <div v-if="!hideHeader" class="table-header">
      <h3 :id="headingId" class="table-title">
        {{ title }}
      </h3>
      <div class="table-header-right">
        <slot name="header-actions" />
        <span class="table-meta">{{ displayTotalCount }} total record(s)</span>
      </div>
    </div>
    <span v-else-if="!ariaLabel" :id="headingId" class="sr-only">
      {{ srOnlyHeadingText }}
    </span>

    <div
      :class="['table-wrapper', wrapperClass]"
      :data-testid="wrapperTestId || undefined"
      role="region"
      :aria-label="ariaLabel || undefined"
      :aria-labelledby="effectiveRegionLabelledBy"
      tabindex="0"
    >
      <table
        :id="tableId || undefined"
        :class="['clinical-table', tableClass]"
        :data-testid="tableTestId || undefined"
        :aria-label="!labelledBy && ariaLabel ? ariaLabel : undefined"
        :aria-labelledby="effectiveTableLabelledBy"
      >
        <caption class="sr-only">
          {{
            title
          }}
        </caption>
        <thead>
          <tr>
            <th v-if="selectable" class="col-select" scope="col">
              <slot name="header-select">
                <span class="sr-only">Select</span>
              </slot>
            </th>
            <th
              v-for="col in columns"
              :key="col.key"
              :class="[
                'col-header',
                { sortable: col.sortable, numeric: col.align === 'right' },
                col.headerClass,
              ]"
              scope="col"
              :aria-sort="getAriaSort(col)"
              @click="handleHeaderCellClick($event, col)"
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
                <slot :name="'head-' + col.key" :column="col">
                  <span>{{ col.label }}</span>
                </slot>
              </div>
            </th>
          </tr>
        </thead>
        <tbody
          :id="tbodyId || undefined"
          :data-vue-managed="tbodyId ? 'true' : undefined"
        >
          <template
            v-for="(row, idx) in paginatedData"
            :key="getRowKey(row, idx)"
          >
            <tr
              v-if="checkRowMasked(row, idx)"
              :id="resolveRowId(row, idx)"
              v-bind="resolveRowAttrs(row, idx)"
              class="masked-row"
            >
              <td :colspan="totalColumnSpan" class="data-cell masked-cell">
                <slot
                  name="masked-row"
                  :row="row"
                  :index="idx"
                  :col-span="totalColumnSpan"
                >
                  Restricted clinical record
                </slot>
              </td>
            </tr>
            <tr
              v-else
              :id="resolveRowId(row, idx)"
              :class="[
                'data-row',
                { selected: isRowSelected(row) },
                resolveRowClass(row, idx),
              ]"
              v-bind="resolveRowAttrs(row, idx)"
              tabindex="0"
              :aria-selected="
                selectable ? (isRowSelected(row) ? 'true' : 'false') : undefined
              "
              @click="handleRowClick($event, row, idx)"
              @keydown="handleRowKeydown($event, row, idx)"
            >
              <td v-if="selectable" class="col-select" @click.stop>
                <input
                  type="checkbox"
                  :checked="isRowSelected(row)"
                  :aria-label="'Select row ' + (row.id ?? idx + 1)"
                  @change="toggleRowSelection(row)"
                />
              </td>
              <template v-for="col in columns" :key="col.key">
                <th
                  v-if="col.rowHeader"
                  scope="row"
                  :class="[
                    'data-cell',
                    'row-header-cell',
                    { numeric: col.align === 'right' },
                    col.cellClass,
                  ]"
                >
                  <slot
                    :name="'cell-' + col.key"
                    :row="row"
                    :value="row[col.key]"
                    :index="idx"
                  >
                    {{ row[col.key] }}
                  </slot>
                </th>
                <td
                  v-else
                  :class="[
                    'data-cell',
                    { numeric: col.align === 'right' },
                    col.cellClass,
                  ]"
                >
                  <slot
                    :name="'cell-' + col.key"
                    :row="row"
                    :value="row[col.key]"
                    :index="idx"
                  >
                    {{ row[col.key] }}
                  </slot>
                </td>
              </template>
            </tr>
          </template>
          <tr v-if="paginatedData.length === 0">
            <td :colspan="totalColumnSpan" class="empty-cell">
              <slot name="empty">
                {{ emptyMessage }}
              </slot>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <slot
      name="pagination"
      :current-page="currentPage"
      :total-pages="totalPages"
      :total-records="displayTotalCount"
      :go-to-page="goToPage"
    >
      <div
        v-if="externalPagination && (hasMore || totalRecords !== null)"
        class="table-pagination"
      >
        <span
          class="pagination-info"
          role="status"
          aria-live="polite"
          aria-atomic="true"
        >
          <template v-if="hasMore || paginatedData.length < displayTotalCount">
            Showing {{ paginatedData.length }} of {{ displayTotalCount }}
          </template>
          <template v-else-if="totalPages > 1">
            Page {{ currentPage }} of {{ totalPages }}
          </template>
          <template v-else> {{ displayTotalCount }} record(s) </template>
        </span>
        <template v-if="hasMore">
          <button
            :id="loadMoreId || undefined"
            type="button"
            class="pagination-btn"
            :disabled="loadingMore"
            @click="emit('load-more')"
          >
            {{ loadingMore ? "Loading..." : loadMoreLabel }}
          </button>
        </template>
        <template v-else-if="totalPages > 1">
          <button
            type="button"
            class="pagination-btn"
            :disabled="currentPage <= 1"
            aria-label="Previous page"
            @click="goToPage(currentPage - 1)"
          >
            Previous
          </button>
          <button
            type="button"
            class="pagination-btn"
            :disabled="currentPage >= totalPages"
            aria-label="Next page"
            @click="goToPage(currentPage + 1)"
          >
            Next
          </button>
        </template>
      </div>
      <div
        v-else-if="!externalPagination && totalPages > 1"
        class="table-pagination"
      >
        <button
          type="button"
          class="pagination-btn"
          :disabled="currentPage === 1"
          aria-label="Previous page"
          @click="goToPage(currentPage - 1)"
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
          @click="goToPage(currentPage + 1)"
        >
          Next
        </button>
      </div>
    </slot>
  </div>
</template>

<style scoped>
.clinical-data-table-container {
  display: flex;
  flex-direction: column;
  min-width: 0;
  max-width: 100%;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: var(--shadow-sm);
}

.clinical-data-table-container--plain {
  border: 0;
  border-radius: 0;
  box-shadow: none;
  background: transparent;
  overflow: visible;
}

.table-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
}

.table-header-right {
  display: flex;
  align-items: center;
  gap: 12px;
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
  min-width: 0;
  max-width: 100%;
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

.sort-button:not(:disabled):hover {
  background: var(--semantic-color-surface-header);
}

.header-content {
  display: flex;
  align-items: center;
  gap: 6px;
}

.sort-icon {
  font-size: var(--font-size-2xs);
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

.row-header-cell {
  font-weight: 500;
  text-align: left;
}

.col-header.numeric,
.data-cell.numeric {
  text-align: right;
}

.masked-cell {
  color: var(--semantic-color-text-muted);
  font-style: italic;
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
