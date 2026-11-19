<template>
  <div
    ref="modalRef"
    class="modal-backdrop"
    role="dialog"
    aria-modal="true"
    aria-label="Export Inspection Log Modal"
    style="
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    "
  >
    <div
      class="modal-content card"
      style="
        background: var(--card-bg);
        width: 450px;
        padding: 24px;
        border-radius: 8px;
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
      "
    >
      <header
        style="
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 20px;
        "
      >
        <h3 style="margin: 0; font-size: 16px">Export Inspection Log</h3>
        <button
          type="button"
          class="btn-close"
          aria-label="Close export modal"
          style="
            background: none;
            border: none;
            font-size: 20px;
            cursor: pointer;
            color: var(--text);
          "
          @click="$emit('close')"
        >
          ×
        </button>
      </header>

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
        <div style="display: flex; gap: 16px; margin-top: 8px">
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
          Date Range Bounds
        </legend>
        <div class="grid-2-responsive" style="margin-top: 8px">
          <div>
            <label
              for="auditor-export-start-date"
              style="
                font-size: 11px;
                color: var(--text-muted);
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
                background: var(--bg);
                color: var(--text);
                font-size: 13px;
              "
            />
          </div>
          <div>
            <label
              for="auditor-export-end-date"
              style="
                font-size: 11px;
                color: var(--text-muted);
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
                background: var(--bg);
                color: var(--text);
                font-size: 13px;
              "
            />
          </div>
        </div>
      </fieldset>

      <footer
        style="
          display: flex;
          justify-content: flex-end;
          gap: 12px;
          margin-top: 24px;
        "
      >
        <button
          class="btn btn-secondary touch-target-interactive"
          @click="$emit('close')"
        >
          Cancel
        </button>
        <button
          class="btn btn-primary touch-target-interactive"
          :disabled="exporting"
          @click="handleExport"
        >
          {{ exporting ? "Exporting..." : "Download Export" }}
        </button>
      </footer>
    </div>
  </div>
</template>

<script setup>
import { ref } from "vue";
import { useAuditorStore } from "../../stores/auditor";
import { useFocusTrap } from "ui";

const emit = defineEmits(["close"]);

const modalRef = ref(null);
useFocusTrap(modalRef, {
  onEscape: () => emit("close"),
});

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
