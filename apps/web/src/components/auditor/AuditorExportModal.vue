<template>
  <ClinicalModal
    :is-open="isOpen"
    :teleport="false"
    :dismissible="!exporting"
    size="sm"
    title="Export Inspection Log"
    title-id="auditor-export-modal-title"
    aria-label="Export Inspection Log Modal"
    close-label="Close export modal"
    dialog-class="modal-content card"
    @close="$emit('close')"
  >
    <fieldset
      class="form-group"
      style="margin: 0 0 16px 0; padding: 0; border: none"
    >
      <legend
        style="
          font-size: 12px;
          font-weight: 600;
          margin-bottom: 6px;
          display: block;
        "
      >
        Export Format
      </legend>
      <div style="display: flex; flex-wrap: wrap; gap: 16px; margin-top: 8px">
        <label
          for="auditor-export-format-csv"
          class="touch-target-interactive"
          style="
            display: flex;
            align-items: center;
            gap: 8px;
            cursor: pointer;
            font-size: 13px;
          "
        >
          <input
            id="auditor-export-format-csv"
            v-model="exportFormat"
            type="radio"
            value="CSV"
          />
          CSV
        </label>
        <label
          for="auditor-export-format-pdf"
          class="touch-target-interactive"
          style="
            display: flex;
            align-items: center;
            gap: 8px;
            cursor: pointer;
            font-size: 13px;
          "
        >
          <input
            id="auditor-export-format-pdf"
            v-model="exportFormat"
            type="radio"
            value="PDF"
          />
          PDF
        </label>
        <label
          for="auditor-export-format-json"
          class="touch-target-interactive"
          style="
            display: flex;
            align-items: center;
            gap: 8px;
            cursor: pointer;
            font-size: 13px;
          "
        >
          <input
            id="auditor-export-format-json"
            v-model="exportFormat"
            type="radio"
            value="JSON"
          />
          JSON
        </label>
      </div>
    </fieldset>

    <fieldset class="form-group" style="margin: 0; padding: 0; border: none">
      <legend
        style="
          font-size: 12px;
          font-weight: 600;
          margin-bottom: 6px;
          display: block;
        "
      >
        Date Range Bounds
      </legend>
      <div class="grid-2-responsive" style="margin-top: 8px">
        <div>
          <label
            for="auditor-export-start-date"
            style="
              font-size: 11px;
              color: var(--semantic-color-text-secondary);
              display: block;
              margin-bottom: 4px;
            "
            >Start Date</label
          >
          <input
            id="auditor-export-start-date"
            v-model="startDate"
            type="date"
            class="export-start-date touch-target-interactive"
            style="
              width: 100%;
              padding: 8px;
              border-radius: 4px;
              border: 1px solid var(--border);
              background: var(--semantic-color-surface-muted);
              color: var(--semantic-color-text);
              font-size: 13px;
            "
          />
        </div>
        <div>
          <label
            for="auditor-export-end-date"
            style="
              font-size: 11px;
              color: var(--semantic-color-text-secondary);
              display: block;
              margin-bottom: 4px;
            "
            >End Date</label
          >
          <input
            id="auditor-export-end-date"
            v-model="endDate"
            type="date"
            class="export-end-date touch-target-interactive"
            style="
              width: 100%;
              padding: 8px;
              border-radius: 4px;
              border: 1px solid var(--border);
              background: var(--semantic-color-surface-muted);
              color: var(--semantic-color-text);
              font-size: 13px;
            "
          />
        </div>
      </div>
    </fieldset>

    <template #footer>
      <footer
        style="
          display: flex;
          justify-content: flex-end;
          gap: 12px;
          width: 100%;
          flex-wrap: wrap;
        "
      >
        <button
          type="button"
          class="btn btn-secondary touch-target-interactive"
          @click="$emit('close')"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary touch-target-interactive"
          :disabled="exporting"
          @click="handleExport"
        >
          {{ exporting ? "Exporting..." : "Download Export" }}
        </button>
      </footer>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { ref } from "vue";
import { useAuditorStore } from "../../stores/auditor";
import { ClinicalModal } from "ui";

defineProps({
  isOpen: {
    type: Boolean,
    default: true,
  },
});

const emit = defineEmits(["close"]);

const auditorStore = useAuditorStore();

const exportFormat = ref("CSV");
const startDate = ref(auditorStore.filters.dateRange.start);
const endDate = ref(auditorStore.filters.dateRange.end);
const exporting = ref(false);

async function handleExport() {
  exporting.value = true;
  try {
    // Apply bounds to the store before exporting
    auditorStore.setFilters({
      dateRange: {
        start: startDate.value,
        end: endDate.value,
      },
    });
    // Fetch logs to make sure we have filtered logs matching constraints
    await auditorStore.fetchAuditLogs();
    // Export with the specified format
    await auditorStore.exportAuditTrail(exportFormat.value);
    emit("close");
  } catch (err) {
    console.error("Export failed:", err);
  } finally {
    exporting.value = false;
  }
}
</script>
