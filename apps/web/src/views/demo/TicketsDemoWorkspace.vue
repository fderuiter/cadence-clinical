<template>
  <div v-if="mode === 'header-actions'" class="demo-export-actions">
    <button
      type="button"
      class="btn btn-secondary"
      title="Export Regulatory Audit Trail CSV"
      @click="emit('export', 'csv')"
    >
      <svg
        xmlns="http://www.w3.org/2000/svg"
        width="15"
        height="15"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        class="btn-icon-svg"
        aria-hidden="true"
      >
        <path d="M12 3v12" />
        <path d="m7 10 5 5 5-5" />
        <path d="M5 21h14" />
      </svg>
      <span>Export CSV</span>
    </button>
    <button
      type="button"
      class="btn btn-secondary"
      title="Export Regulatory Audit Trail JSON"
      @click="emit('export', 'json')"
    >
      <svg
        xmlns="http://www.w3.org/2000/svg"
        width="15"
        height="15"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        class="btn-icon-svg"
        aria-hidden="true"
      >
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
        <path d="M14 2v6h6" />
        <path
          d="M10 12a1 1 0 0 0-1 1v1a1 1 0 0 1-1 1 1 1 0 0 1 1 1v1a1 1 0 0 0 1 1"
        />
        <path
          d="M14 18a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1 1 1 0 0 1-1-1v-1a1 1 0 0 0-1-1"
        />
      </svg>
      <span>Export JSON</span>
    </button>
  </div>

  <div
    v-else-if="mode === 'bulk-bar' && selectedTicketCount > 0"
    class="bulk-action-bar"
  >
    <div class="bulk-info">
      <span class="bulk-count-badge"
        >{{ selectedTicketCount }} ticket(s) selected</span
      >
    </div>

    <div class="bulk-actions">
      <div class="bulk-action-group">
        <ClinicalInput
          id="tickets-batch-status"
          :model-value="batchStatus"
          as="select"
          label="Batch target status"
          hide-label
          density="compact"
          :show-query-flag="false"
          control-class="filter-select batch-select"
          @update:model-value="emit('update:batchStatus', String($event ?? ''))"
        >
          <option value="" disabled>Change Status...</option>
          <option value="OPEN">Open</option>
          <option value="IN_PROGRESS">In Progress</option>
          <option value="WAITING_ON_SITE">Waiting on Site</option>
          <option value="WAITING_ON_SPONSOR">Waiting on Sponsor</option>
          <option value="RESOLVED">Resolved</option>
          <option value="CLOSED">Closed</option>
        </ClinicalInput>
        <button
          type="button"
          class="btn btn-sm btn-primary"
          :disabled="!batchStatus"
          @click="emit('batch-transition', batchStatus)"
        >
          Apply Status
        </button>
      </div>

      <div class="bulk-action-group">
        <ClinicalInput
          id="tickets-batch-severity"
          :model-value="batchSeverity"
          as="select"
          label="Batch target severity"
          hide-label
          density="compact"
          :show-query-flag="false"
          control-class="filter-select batch-select"
          @update:model-value="
            emit('update:batchSeverity', String($event ?? ''))
          "
        >
          <option value="" disabled>Change Severity...</option>
          <option value="CRITICAL">Critical</option>
          <option value="MAJOR">Major</option>
          <option value="MINOR">Minor</option>
        </ClinicalInput>
        <button
          type="button"
          class="btn btn-sm btn-primary"
          :disabled="!batchSeverity"
          @click="emit('batch-severity', batchSeverity)"
        >
          Apply Severity
        </button>
      </div>

      <button
        type="button"
        class="btn btn-sm btn-secondary"
        @click="emit('clear-selection')"
      >
        Clear Selection
      </button>
    </div>
  </div>
</template>

<script setup>
import { ClinicalInput } from "ui";

defineOptions({
  name: "TicketsDemoWorkspace",
});

defineProps({
  mode: {
    type: String,
    default: "bulk-bar",
  },
  selectedTicketCount: {
    type: Number,
    default: 0,
  },
  batchStatus: {
    type: String,
    default: "",
  },
  batchSeverity: {
    type: String,
    default: "",
  },
});

const emit = defineEmits([
  "export",
  "update:batchStatus",
  "update:batchSeverity",
  "batch-transition",
  "batch-severity",
  "clear-selection",
]);
</script>

<style scoped>
.demo-export-actions {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  min-width: 0;
  max-width: 100%;
}

.filter-select {
  padding: 6px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.82rem;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
}

.bulk-action-bar {
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  border-radius: 8px;
  padding: 12px 18px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
}

.bulk-info {
  display: flex;
  align-items: center;
  gap: 10px;
}

.bulk-count-badge {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-info);
  background: var(--semantic-color-info-light);
  padding: 4px 10px;
  border-radius: 12px;
  font-variant-numeric: var(--font-variant-numeric-tabular);
}

.btn-icon-svg {
  flex-shrink: 0;
}

.bulk-actions {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
  min-width: 0;
  max-width: 100%;
}

.bulk-action-group {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
}

.batch-select {
  font-size: 0.82rem;
  padding: 5px 10px;
}

.btn {
  padding: 8px 14px;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  border: 1px solid transparent;
  transition: all 0.15s ease-in-out;
}

.btn-sm {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 5px 10px;
  font-size: 0.8rem;
}

.btn-primary {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}

.btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-primary-dark);
}

.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text-body);
}

.btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}
</style>
