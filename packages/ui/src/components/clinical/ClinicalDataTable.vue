<script setup>
import {
  computed,
  defineAsyncComponent,
  onBeforeUnmount,
  onMounted,
  ref,
  useId,
  watch,
} from "vue";
import { ArrowDown, ArrowUp, ArrowUpDown } from "lucide-vue-next";

const ClinicalRecordCard = defineAsyncComponent(
  () => import("./ClinicalRecordCard.vue")
);

const CLINICAL_ICON_STROKE_WIDTH = 1.75;

const props = defineProps({
  columns: {
    type: Array,
    required: true,
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
  captionVisible: {
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
  density: {
    type: String,
    default: "comfortable",
    validator: (val) => ["compact", "comfortable", "touch"].includes(val),
  },
  mobileMode: {
    type: String,
    default: "auto",
    validator: (val) => ["auto", "cards", "scroll", "table"].includes(val),
  },
  mobileBreakpoint: {
    type: Number,
    default: 480,
  },
  stickyHeader: {
    type: Boolean,
    default: false,
  },
  interactiveRows: {
    type: Boolean,
    default: true,
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

const rootRef = ref(null);
const rawHeadingId = useId();
const headingId = computed(
  () => `clinical-data-table-title-${rawHeadingId || "default"}`
);
const mobileSortSelectId = computed(
  () => `clinical-data-table-mobile-sort-${rawHeadingId || "default"}`
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

function readInitialMobileViewport() {
  if (typeof window === "undefined") return false;
  const bp = props.mobileBreakpoint || 480;
  if (
    typeof window.innerWidth === "number" &&
    window.innerWidth > 0 &&
    window.innerWidth <= bp
  ) {
    return true;
  }
  if (typeof window.matchMedia === "function") {
    try {
      const mql = window.matchMedia(`(max-width: ${bp}px)`);
      if (mql && mql.matches) return true;
    } catch {
      // Ignore matchMedia errors in synthetic test environments
    }
  }
  return false;
}

const isMobileViewport = ref(readInitialMobileViewport());
const isNarrowContainer = ref(false);
let mediaQueryList = null;
let mediaQueryListener = null;
let windowResizeListener = null;
let resizeObserver = null;

function syncViewportMatches() {
  isMobileViewport.value = readInitialMobileViewport();
}

onMounted(() => {
  syncViewportMatches();
  if (typeof window !== "undefined") {
    windowResizeListener = () => syncViewportMatches();
    window.addEventListener("resize", windowResizeListener, { passive: true });
    if (typeof window.matchMedia === "function") {
      try {
        mediaQueryList = window.matchMedia(
          `(max-width: ${props.mobileBreakpoint || 480}px)`
        );
        mediaQueryListener = () => syncViewportMatches();
        if (typeof mediaQueryList.addEventListener === "function") {
          mediaQueryList.addEventListener("change", mediaQueryListener);
        } else if (typeof mediaQueryList.addListener === "function") {
          mediaQueryList.addListener(mediaQueryListener);
        }
      } catch {
        mediaQueryList = null;
      }
    }
  }

  if (typeof ResizeObserver === "function" && rootRef.value) {
    resizeObserver = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const width =
          entry.contentRect?.width ??
          rootRef.value?.getBoundingClientRect?.().width ??
          0;
        if (width > 0) {
          isNarrowContainer.value = width <= (props.mobileBreakpoint || 480);
        }
      }
    });
    resizeObserver.observe(rootRef.value);
  }
});

onBeforeUnmount(() => {
  if (typeof window !== "undefined" && windowResizeListener) {
    window.removeEventListener("resize", windowResizeListener);
  }
  if (mediaQueryList && mediaQueryListener) {
    if (typeof mediaQueryList.removeEventListener === "function") {
      mediaQueryList.removeEventListener("change", mediaQueryListener);
    } else if (typeof mediaQueryList.removeListener === "function") {
      mediaQueryList.removeListener(mediaQueryListener);
    }
  }
  if (resizeObserver) {
    resizeObserver.disconnect();
    resizeObserver = null;
  }
});

const shouldProjectCards = computed(() => {
  if (props.mobileMode === "cards") return true;
  if (props.mobileMode === "scroll" || props.mobileMode === "table") {
    return false;
  }
  return isMobileViewport.value || isNarrowContainer.value;
});

const effectiveCardDensity = computed(() => {
  if (props.density === "touch") return "touch";
  return "compact";
});

const sortableColumns = computed(() =>
  props.columns.filter((col) => Boolean(col && col.sortable))
);

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

const handleMobileSortSelect = (event) => {
  const nextKey = event?.target?.value ?? "";
  sortKey.value = nextKey;
  if (!nextKey) {
    sortOrder.value = "asc";
  }
  emit("sort-change", { key: sortKey.value, order: sortOrder.value });
};

const toggleMobileSortOrder = () => {
  if (!sortKey.value && sortableColumns.value.length > 0) {
    sortKey.value = sortableColumns.value[0].key;
    sortOrder.value = "asc";
  } else {
    sortOrder.value = sortOrder.value === "asc" ? "desc" : "asc";
  }
  emit("sort-change", { key: sortKey.value, order: sortOrder.value });
};

const activeSortableColumnLabel = computed(() => {
  const match = sortableColumns.value.find((col) => col.key === sortKey.value);
  return match ? match.label : "column";
});

const mobileSortDirectionLabel = computed(() => {
  if (!sortKey.value) return "Toggle sort direction";
  const nextDir = sortOrder.value === "asc" ? "descending" : "ascending";
  return `Sort ${activeSortableColumnLabel.value} ${nextDir}`;
});

const getAriaSort = (column) => {
  if (!column.sortable) return undefined;
  if (sortKey.value !== column.key) return "none";
  return sortOrder.value === "asc" ? "ascending" : "descending";
};

const getStickyClasses = (column, isHeader = false) => {
  if (!column?.sticky) return null;
  if (column.sticky === "left") {
    return isHeader
      ? ["col-sticky", "col-sticky-left", "col-header-sticky"]
      : ["col-sticky", "col-sticky-left"];
  }
  if (column.sticky === "right") {
    return isHeader
      ? ["col-sticky", "col-sticky-right", "col-header-sticky"]
      : ["col-sticky", "col-sticky-right"];
  }
  return null;
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

const STATUS_KEY_RE =
  /^(status|statusLabel|state|completeness_status|tier|lifecycle)$/i;
const ACTION_KEY_RE = /^(actions?|inspection|controls|action_buttons)$/i;
const IDENTIFIER_KEY_RE =
  /^(id|.*_id|code|.*_code|siteCode|site_code|subject.*|number|seq|sequence|version|kit.*|lot.*|ref|artifact_code|timestamp)$/i;
const METRIC_ONLY_KEY_RE = /(date|time|_at$|count|window|amount|score)/i;

const cardColumnLayout = computed(() => {
  const cols = props.columns.filter((c) => c && c.cardRole !== "hidden");
  const used = new Set();

  const selectCol =
    cols.find((c) => c.cardRole === "select" || c.key === "select") || null;
  if (selectCol) used.add(selectCol.key);

  let statusCol =
    cols.find((c) => !used.has(c.key) && c.cardRole === "status") || null;
  if (!statusCol && cols.length >= 3) {
    statusCol =
      cols.find((c) => !used.has(c.key) && STATUS_KEY_RE.test(c.key)) || null;
  }
  if (statusCol) used.add(statusCol.key);

  let actionsCol =
    cols.find((c) => !used.has(c.key) && c.cardRole === "actions") || null;
  if (!actionsCol) {
    for (let i = cols.length - 1; i >= 0; i--) {
      const candidate = cols[i];
      if (used.has(candidate.key)) continue;
      if (
        ACTION_KEY_RE.test(candidate.key) ||
        (!candidate.label && /action/i.test(candidate.key))
      ) {
        actionsCol = candidate;
        break;
      }
    }
  }
  if (actionsCol) used.add(actionsCol.key);

  let identifierCol =
    cols.find((c) => !used.has(c.key) && c.cardRole === "identifier") || null;
  let titleCol =
    cols.find((c) => !used.has(c.key) && c.cardRole === "title") || null;

  const remaining = cols.filter(
    (c) =>
      !used.has(c.key) &&
      c.key !== identifierCol?.key &&
      c.key !== titleCol?.key
  );

  if (!identifierCol && !titleCol && remaining.length > 0) {
    const first = remaining[0];
    if (first.rowHeader || IDENTIFIER_KEY_RE.test(first.key)) {
      identifierCol = first;
      if (
        remaining.length > 1 &&
        !METRIC_ONLY_KEY_RE.test(remaining[1].key) &&
        remaining.length > 2
      ) {
        titleCol = remaining[1];
      }
    } else {
      titleCol = first;
    }
  } else if (identifierCol && !titleCol && remaining.length > 1) {
    if (!METRIC_ONLY_KEY_RE.test(remaining[0].key)) {
      titleCol = remaining[0];
    }
  }

  if (identifierCol) used.add(identifierCol.key);
  if (titleCol) used.add(titleCol.key);

  const fieldCols = cols.filter((c) => !used.has(c.key));

  return {
    selectCol,
    identifierCol,
    titleCol,
    statusCol,
    actionsCol,
    fieldCols,
  };
});

function buildCardFieldsForRow(row) {
  return cardColumnLayout.value.fieldCols.map((col) => ({
    key: col.key,
    label: col.label || col.key,
    value: row ? row[col.key] : undefined,
    mono: Boolean(col.mono),
    align: col.align,
    numeric: col.align === "right",
    fullWidth: Boolean(col.fullWidth),
  }));
}

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
    ref="rootRef"
    :class="[
      'clinical-data-table-container',
      `clinical-data-table-container--${density}`,
      {
        'clinical-data-table-container--plain': plain,
        'clinical-data-table-container--cards': shouldProjectCards,
      },
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

    <template v-if="shouldProjectCards">
      <div
        v-if="sortableColumns.length > 0"
        class="mobile-sort-bar"
        data-testid="clinical-mobile-sort-bar"
      >
        <div class="mobile-sort-field">
          <label :for="mobileSortSelectId" class="mobile-sort-label">
            Sort
          </label>
          <select
            :id="mobileSortSelectId"
            class="mobile-sort-select"
            :value="sortKey"
            @change="handleMobileSortSelect"
          >
            <option value="">Default order</option>
            <option
              v-for="col in sortableColumns"
              :key="col.key"
              :value="col.key"
            >
              {{ col.label }}
            </option>
          </select>
        </div>
        <button
          type="button"
          class="mobile-sort-order-btn"
          :aria-label="mobileSortDirectionLabel"
          @click="toggleMobileSortOrder"
        >
          <ArrowUp
            v-if="sortKey && sortOrder === 'asc'"
            :size="16"
            :stroke-width="CLINICAL_ICON_STROKE_WIDTH"
            aria-hidden="true"
            focusable="false"
          />
          <ArrowDown
            v-else-if="sortKey && sortOrder === 'desc'"
            :size="16"
            :stroke-width="CLINICAL_ICON_STROKE_WIDTH"
            aria-hidden="true"
            focusable="false"
          />
          <ArrowUpDown
            v-else
            :size="16"
            :stroke-width="CLINICAL_ICON_STROKE_WIDTH"
            aria-hidden="true"
            focusable="false"
          />
          <span>{{
            !sortKey ? "Order" : sortOrder === "asc" ? "Asc" : "Desc"
          }}</span>
        </button>
      </div>

      <div
        :class="['card-list-wrapper', wrapperClass]"
        :data-testid="wrapperTestId || undefined"
        role="region"
        :aria-label="ariaLabel || undefined"
        :aria-labelledby="effectiveRegionLabelledBy"
      >
        <p v-if="captionVisible" class="table-caption-visible">
          {{ title }}
        </p>

        <div
          v-if="paginatedData.length === 0"
          :id="tableId || undefined"
          :data-testid="tableTestId || undefined"
          class="empty-card-state"
        >
          <slot name="empty">
            {{ emptyMessage }}
          </slot>
        </div>

        <div
          v-else
          :id="tableId || undefined"
          :data-testid="tableTestId || undefined"
          class="clinical-record-card-list"
        >
          <ClinicalRecordCard
            v-for="(row, idx) in paginatedData"
            :key="getRowKey(row, idx)"
            :density="effectiveCardDensity"
            :masked="checkRowMasked(row, idx)"
            :selected="isRowSelected(row)"
            :interactive="interactiveRows && !checkRowMasked(row, idx)"
            :card-id="resolveRowId(row, idx) || ''"
            :card-class="resolveRowClass(row, idx) || ''"
            :card-attrs="resolveRowAttrs(row, idx) || null"
            :identifier="
              cardColumnLayout.identifierCol
                ? String(row[cardColumnLayout.identifierCol.key] ?? '')
                : ''
            "
            :title="
              cardColumnLayout.titleCol
                ? String(row[cardColumnLayout.titleCol.key] ?? '')
                : ''
            "
            :status="
              cardColumnLayout.statusCol
                ? String(row[cardColumnLayout.statusCol.key] ?? '')
                : ''
            "
            :fields="buildCardFieldsForRow(row)"
            @card-click="emit('row-click', row)"
          >
            <template v-if="selectable || cardColumnLayout.selectCol" #select>
              <slot
                v-if="cardColumnLayout.selectCol"
                :name="'cell-' + cardColumnLayout.selectCol.key"
                :row="row"
                :value="row[cardColumnLayout.selectCol.key]"
                :index="idx"
                :is-card-view="true"
              />
              <input
                v-else
                type="checkbox"
                :checked="isRowSelected(row)"
                :aria-label="'Select row ' + (row.id ?? idx + 1)"
                @click.stop
                @change="toggleRowSelection(row)"
              />
            </template>

            <template v-if="checkRowMasked(row, idx)" #masked>
              <slot
                name="masked-row"
                :row="row"
                :index="idx"
                :col-span="totalColumnSpan"
              >
                Restricted clinical record
              </slot>
            </template>

            <template v-if="cardColumnLayout.identifierCol" #identifier>
              <slot
                :name="'cell-' + cardColumnLayout.identifierCol.key"
                :row="row"
                :value="row[cardColumnLayout.identifierCol.key]"
                :index="idx"
                :is-card-view="true"
              >
                {{ row[cardColumnLayout.identifierCol.key] }}
              </slot>
            </template>

            <template v-if="cardColumnLayout.titleCol" #title>
              <slot
                :name="'cell-' + cardColumnLayout.titleCol.key"
                :row="row"
                :value="row[cardColumnLayout.titleCol.key]"
                :index="idx"
                :is-card-view="true"
              >
                {{ row[cardColumnLayout.titleCol.key] }}
              </slot>
            </template>

            <template v-if="cardColumnLayout.statusCol" #status>
              <slot
                :name="'cell-' + cardColumnLayout.statusCol.key"
                :row="row"
                :value="row[cardColumnLayout.statusCol.key]"
                :index="idx"
                :is-card-view="true"
              >
                <span class="clinical-badge">{{
                  row[cardColumnLayout.statusCol.key]
                }}</span>
              </slot>
            </template>

            <template
              v-for="fieldCol in cardColumnLayout.fieldCols"
              :key="fieldCol.key"
              #[`field-${fieldCol.key}`]
            >
              <slot
                :name="'cell-' + fieldCol.key"
                :row="row"
                :value="row[fieldCol.key]"
                :index="idx"
                :is-card-view="true"
              >
                {{
                  row[fieldCol.key] !== undefined &&
                  row[fieldCol.key] !== null &&
                  row[fieldCol.key] !== ""
                    ? row[fieldCol.key]
                    : "—"
                }}
              </slot>
            </template>

            <template v-if="$slots['after-row']" #after-body>
              <slot
                name="after-row"
                :row="row"
                :index="idx"
                :col-span="totalColumnSpan"
                :is-card-view="true"
              />
            </template>

            <template
              v-if="cardColumnLayout.actionsCol || $slots['card-footer']"
              #footer
            >
              <slot
                v-if="$slots['card-footer']"
                name="card-footer"
                :row="row"
                :index="idx"
              />
              <slot
                v-else-if="cardColumnLayout.actionsCol"
                :name="'cell-' + cardColumnLayout.actionsCol.key"
                :row="row"
                :value="row[cardColumnLayout.actionsCol.key]"
                :index="idx"
                :is-card-view="true"
              >
                {{ row[cardColumnLayout.actionsCol.key] }}
              </slot>
            </template>
          </ClinicalRecordCard>
        </div>
      </div>
    </template>

    <div
      v-else
      :class="['table-wrapper', 'focus-ring-inset', wrapperClass]"
      :data-testid="wrapperTestId || undefined"
      role="region"
      :aria-label="ariaLabel || undefined"
      :aria-labelledby="effectiveRegionLabelledBy"
      tabindex="0"
    >
      <table
        :id="tableId || undefined"
        :class="[
          'clinical-table',
          { 'clinical-table--sticky-header': stickyHeader },
          tableClass,
        ]"
        :data-testid="tableTestId || undefined"
        :aria-label="!labelledBy && ariaLabel ? ariaLabel : undefined"
        :aria-labelledby="effectiveTableLabelledBy"
      >
        <caption :class="captionVisible ? 'table-caption-visible' : 'sr-only'">
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
                getStickyClasses(col, true),
                col.headerClass,
              ]"
              scope="col"
              :aria-sort="getAriaSort(col)"
              @click="handleHeaderCellClick($event, col)"
            >
              <button
                v-if="col.sortable"
                type="button"
                class="sort-button focus-ring-inset"
                @click="toggleSort(col)"
                @keydown="handleSortKeydown($event, col)"
              >
                <span>{{ col.label }}</span>
                <span class="sort-icon" aria-hidden="true">
                  <ArrowUp
                    v-if="sortKey === col.key && sortOrder === 'asc'"
                    :size="16"
                    :stroke-width="CLINICAL_ICON_STROKE_WIDTH"
                    aria-hidden="true"
                    focusable="false"
                  />
                  <ArrowDown
                    v-else-if="sortKey === col.key && sortOrder === 'desc'"
                    :size="16"
                    :stroke-width="CLINICAL_ICON_STROKE_WIDTH"
                    aria-hidden="true"
                    focusable="false"
                  />
                  <ArrowUpDown
                    v-else
                    :size="16"
                    :stroke-width="CLINICAL_ICON_STROKE_WIDTH"
                    aria-hidden="true"
                    focusable="false"
                  />
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
                'focus-ring-inset',
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
                  :data-label="col.label"
                  :class="[
                    'data-cell',
                    'row-header-cell',
                    { numeric: col.align === 'right' },
                    getStickyClasses(col, false),
                    col.cellClass,
                  ]"
                >
                  <slot
                    :name="'cell-' + col.key"
                    :row="row"
                    :value="row[col.key]"
                    :index="idx"
                    :is-card-view="false"
                  >
                    {{ row[col.key] }}
                  </slot>
                </th>
                <td
                  v-else
                  :data-label="col.label"
                  :class="[
                    'data-cell',
                    { numeric: col.align === 'right' },
                    getStickyClasses(col, false),
                    col.cellClass,
                  ]"
                >
                  <slot
                    :name="'cell-' + col.key"
                    :row="row"
                    :value="row[col.key]"
                    :index="idx"
                    :is-card-view="false"
                  >
                    {{ row[col.key] }}
                  </slot>
                </td>
              </template>
            </tr>
            <slot
              name="after-row"
              :row="row"
              :index="idx"
              :col-span="totalColumnSpan"
              :is-card-view="false"
            />
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
  position: relative;
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
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 8px 12px;
  padding: 12px 16px;
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
}

.table-header-right {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
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
  font-variant-numeric: tabular-nums;
  font-variant-numeric: var(--font-variant-numeric-tabular);
}

.table-wrapper {
  position: relative;
  overflow-x: auto;
  width: 100%;
  min-width: 0;
  max-width: 100%;
}

.card-list-wrapper {
  position: relative;
  width: 100%;
  min-width: 0;
  max-width: 100%;
  padding: 12px;
  box-sizing: border-box;
}

.clinical-data-table-container--plain .card-list-wrapper {
  padding: 4px 0;
}

.clinical-record-card-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  max-width: 100%;
}

.empty-card-state {
  padding: 24px 16px;
  text-align: center;
  color: var(--semantic-color-text-muted);
  font-style: italic;
}

.mobile-sort-bar {
  position: relative;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 10px 12px;
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
}

.clinical-data-table-container--plain .mobile-sort-bar {
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-sm);
  margin-bottom: 8px;
}

.mobile-sort-field {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  flex: 1 1 160px;
}

.mobile-sort-label {
  font-size: var(--font-size-2xs);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--semantic-color-text-secondary);
  white-space: nowrap;
}

.mobile-sort-select {
  flex: 1;
  min-width: 0;
  min-height: 44px;
  padding: 8px 10px;
  font-size: 1rem;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--radius-sm);
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  box-sizing: border-box;
}

.mobile-sort-order-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-height: 44px;
  min-width: 44px;
  padding: 8px 12px;
  font-size: 0.8125rem;
  font-weight: 600;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--radius-sm);
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  cursor: pointer;
  box-sizing: border-box;
}

.mobile-sort-order-btn:not(:disabled):hover {
  background-color: var(--semantic-color-surface-header);
}

.clinical-data-table-container--touch .mobile-sort-select,
.clinical-data-table-container--touch .mobile-sort-order-btn {
  min-height: var(--semantic-touch-target-min, 48px);
  min-width: var(--semantic-touch-target-min, 48px);
}

.table-caption-visible {
  padding: 10px 16px;
  margin: 0;
  font-size: 0.875rem;
  font-weight: 600;
  text-align: left;
  color: var(--semantic-color-text-secondary);
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
}

.clinical-table {
  width: 100%;
  border-collapse: collapse;
  text-align: left;
  font-size: 14px;
  font-variant-numeric: tabular-nums;
}

.clinical-data-table-container--compact .clinical-table {
  font-size: 13px;
}

.clinical-data-table-container--touch .clinical-table {
  font-size: 15px;
}

.col-header {
  padding: 10px 16px;
  font-weight: 600;
  color: var(--semantic-color-text-muted);
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
  user-select: none;
}

.clinical-data-table-container--compact .col-header {
  padding: 8px 12px;
}

.clinical-data-table-container--touch .col-header {
  padding: 12px 16px;
}

.col-header.sortable {
  padding: 0;
}

.sort-button {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  min-height: 44px;
  min-width: 44px;
  padding: 10px 16px;
  border: 0;
  background: transparent;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
  box-sizing: border-box;
}

.clinical-data-table-container--compact .sort-button {
  padding: 8px 12px;
}

.clinical-data-table-container--touch .sort-button {
  min-height: var(--semantic-touch-target-min, 48px);
  min-width: var(--semantic-touch-target-min, 48px);
  padding: 12px 16px;
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
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
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
  outline: var(--focus-ring-width, 3px) solid
    var(--focus-ring-color, var(--semantic-color-accent));
  outline-offset: -3px;
  box-shadow: inset 0 0 0 5px
    var(--focus-ring-halo-color, var(--semantic-color-text-on-solid));
}

.pagination-btn:focus-visible,
.mobile-sort-order-btn:focus-visible {
  outline: var(--focus-ring-width, 3px) solid
    var(--focus-ring-color, var(--semantic-color-accent));
  outline-offset: var(--focus-ring-offset, 2px);
  box-shadow: 0 0 0 2px
    var(--focus-ring-halo-color, var(--semantic-color-text-on-solid));
}

@media (forced-colors: active) {
  .sort-button:focus-visible,
  .data-row:focus-visible,
  .table-wrapper:focus-visible {
    outline: 2px solid Highlight;
    outline-offset: -2px;
    box-shadow: none;
  }

  .pagination-btn:focus-visible,
  .mobile-sort-order-btn:focus-visible {
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

.clinical-data-table-container--compact .data-cell {
  padding: 8px 12px;
}

.clinical-data-table-container--touch .data-cell {
  padding: 14px 16px;
}

.row-header-cell {
  font-weight: 500;
  text-align: left;
}

.col-sticky-left {
  position: sticky;
  left: 0;
  z-index: 1;
  background-color: var(--semantic-color-surface);
  box-shadow: inset -1px 0 0 var(--semantic-color-border);
}

.col-sticky-right {
  position: sticky;
  right: 0;
  z-index: 1;
  background-color: var(--semantic-color-surface);
  box-shadow: inset 1px 0 0 var(--semantic-color-border);
}

.col-header.col-sticky-left,
.col-header.col-sticky-right {
  z-index: 2;
  background-color: var(--semantic-color-surface-muted);
}

.clinical-table--sticky-header thead .col-header,
.clinical-table--sticky-header thead .col-select {
  position: sticky;
  top: 0;
  z-index: 2;
  background-color: var(--semantic-color-surface-muted);
}

.clinical-table--sticky-header thead .col-header.col-sticky-left,
.clinical-table--sticky-header thead .col-header.col-sticky-right {
  z-index: 4;
}

.data-row:hover .col-sticky-left,
.data-row:hover .col-sticky-right,
.data-row.selected .col-sticky-left,
.data-row.selected .col-sticky-right {
  background-color: var(--semantic-color-primary-light);
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
  flex-wrap: wrap;
  justify-content: flex-end;
  align-items: center;
  gap: 8px 12px;
  padding: 10px 16px;
  background: var(--semantic-color-surface-muted);
  border-top: 1px solid var(--semantic-color-border);
}

.pagination-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 44px;
  min-width: 44px;
  padding: 8px 14px;
  font-size: 13px;
  font-weight: 500;
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border-radius: 4px;
  cursor: pointer;
  box-sizing: border-box;
}

.clinical-data-table-container--touch .pagination-btn {
  min-height: var(--semantic-touch-target-min, 48px);
  min-width: var(--semantic-touch-target-min, 48px);
  padding: 10px 16px;
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
  font-variant-numeric: tabular-nums;
  font-variant-numeric: var(--font-variant-numeric-tabular);
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
