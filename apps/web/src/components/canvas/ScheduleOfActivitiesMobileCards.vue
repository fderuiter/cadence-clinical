<template>
  <div id="soa-mobile-cards" class="soa-mobile-cards">
    <article
      v-for="(col, colIdx) in soa.columns"
      :key="`card-${col.activity_id}`"
      class="soa-visit-card"
      :data-visit-id="col.activity_id"
    >
      <header class="soa-card-header">
        <div>
          <span class="soa-card-epoch">{{ epochNameFor(col.epoch_id) }}</span>
          <h3 class="soa-card-title">
            <button
              type="button"
              class="soa-card-visit-btn"
              @click="emit('columnClick', col)"
            >
              {{ col.name }}
            </button>
          </h3>
        </div>
        <span class="soa-card-window numeric">
          {{ formatVisitTiming(col) }}
        </span>
      </header>
      <div v-if="col.locations?.length" class="soa-card-locations">
        Locations: {{ col.locations.join(", ") }}
      </div>
      <ul class="soa-card-forms">
        <li
          v-for="row in rowsForColumn(colIdx)"
          :key="`card-${col.activity_id}-${row.collection_spec_id}`"
          class="soa-card-form-item"
        >
          <button
            type="button"
            class="soa-card-form-btn"
            @click="emit('cellClick', row, col)"
          >
            <Check :size="14" aria-hidden="true" />
            <span>{{ row.name }}</span>
          </button>
        </li>
      </ul>
    </article>

    <article
      v-if="unscheduledRows.length"
      class="soa-visit-card"
      data-visit-id="unscheduled"
    >
      <header class="soa-card-header">
        <div>
          <span class="soa-card-epoch">Event-driven / log</span>
          <h3 class="soa-card-title">Unscheduled forms</h3>
        </div>
      </header>
      <ul class="soa-card-forms">
        <li
          v-for="row in unscheduledRows"
          :key="`unsched-${row.collection_spec_id}`"
          class="soa-card-form-item"
        >
          <button
            type="button"
            class="soa-card-form-btn"
            @click="emit('rowClick', row)"
          >
            <Repeat :size="14" aria-hidden="true" />
            <span>{{ row.name }}</span>
          </button>
        </li>
      </ul>
    </article>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";
import { Check, Repeat } from "lucide-vue-next";
import type {
  KernelScheduleOfActivities,
  KernelSoaColumn,
  KernelSoaRow,
} from "../../api/kernelApi";

const props = defineProps<{
  soa: KernelScheduleOfActivities;
}>();

const emit = defineEmits<{
  columnClick: [col: KernelSoaColumn];
  rowClick: [row: KernelSoaRow];
  cellClick: [row: KernelSoaRow, col: KernelSoaColumn];
}>();

const unscheduledRows = computed(() =>
  (props.soa.rows ?? []).filter((r) => !r.columns || r.columns.length === 0)
);

function epochNameFor(epochId: string): string {
  return props.soa.epochs?.find((e) => e.epoch_id === epochId)?.name ?? epochId;
}

function rowsForColumn(colIdx: number): KernelSoaRow[] {
  return (props.soa.rows ?? []).filter((r) =>
    (r.columns ?? []).includes(colIdx)
  );
}

function formatVisitTiming(col: KernelSoaColumn): string {
  if (col.study_day === null || col.study_day === undefined) {
    return "Event-driven";
  }
  const wb = Number(col.window_before ?? 0);
  const wa = Number(col.window_after ?? 0);
  const win = wb || wa ? ` (-${wb}/+${wa}d)` : "";
  return `Day ${col.study_day}${win}`;
}
</script>

<style scoped>
.soa-mobile-cards {
  display: none;
  flex-direction: column;
  gap: 10px;
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.soa-visit-card {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 10px 12px;
  background: var(--semantic-color-surface);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.soa-card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 8px;
}

.soa-card-epoch,
.soa-card-locations,
.soa-card-window {
  font-size: 0.78rem;
  color: var(--neutral-dark);
}

.soa-card-title {
  margin: 2px 0 0;
  font-size: 0.95rem;
}

.soa-card-visit-btn,
.soa-card-form-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 44px;
  padding: 4px 6px;
  border: 0;
  border-radius: 4px;
  background: transparent;
  color: inherit;
  font-weight: 600;
  cursor: pointer;
}

.soa-card-forms {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

@media (max-width: 480px) {
  .soa-mobile-cards {
    display: flex;
  }
}
</style>
