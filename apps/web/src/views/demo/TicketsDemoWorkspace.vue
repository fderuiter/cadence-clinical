<template>
  <div v-if="mode === 'header-actions'" class="demo-export-actions">
    <button
      type="button"
      class="btn btn-secondary"
      title="Export Regulatory Audit Trail CSV"
      @click="emit('export', 'csv')"
    >
      <span class="btn-icon">📊</span> Export CSV
    </button>
    <button
      type="button"
      class="btn btn-secondary"
      title="Export Regulatory Audit Trail JSON"
      @click="emit('export', 'json')"
    >
      <span class="btn-icon">📄</span> Export JSON
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
        <label for="tickets-batch-status" class="sr-only"
          >Batch target status</label
        >
        <select
          id="tickets-batch-status"
          :value="batchStatus"
          class="filter-select batch-select"
          @change="emit('update:batchStatus', $event.target.value)"
        >
          <option value="" disabled>Change Status...</option>
          <option value="OPEN">Open</option>
          <option value="IN_PROGRESS">In Progress</option>
          <option value="WAITING_ON_SITE">Waiting on Site</option>
          <option value="WAITING_ON_SPONSOR">Waiting on Sponsor</option>
          <option value="RESOLVED">Resolved</option>
          <option value="CLOSED">Closed</option>
        </select>
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
        <label for="tickets-batch-severity" class="sr-only"
          >Batch target severity</label
        >
        <select
          id="tickets-batch-severity"
          :value="batchSeverity"
          class="filter-select batch-select"
          @change="emit('update:batchSeverity', $event.target.value)"
        >
          <option value="" disabled>Change Severity...</option>
          <option value="CRITICAL">Critical</option>
          <option value="MAJOR">Major</option>
          <option value="MINOR">Minor</option>
        </select>
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
  align-items: center;
  gap: 10px;
}

.filter-select {
  padding: 6px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.82rem;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
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
}

.bulk-actions {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.bulk-action-group {
  display: flex;
  align-items: center;
  gap: 8px;
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
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover {
  background: var(--semantic-color-interactive-primary);
}

.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text-body);
}

.btn-secondary:hover {
  background: var(--semantic-color-surface-header);
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
