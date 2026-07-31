<template>
  <div class="modal-backdrop" style="position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0, 0, 0, 0.5); display: flex; align-items: center; justify-content: center; z-index: 1000;">
    <div class="modal-content card" style="background: var(--card-bg); width: 450px; padding: 24px; border-radius: 8px; box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);">
      <header style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px;">
        <h3 style="margin: 0; font-size: 16px;">Export Inspection Log</h3>
        <button class="btn-close" @click="$emit('close')" style="background: none; border: none; font-size: 20px; cursor: pointer; color: var(--text);">×</button>
      </header>

      <div class="form-group" style="margin-bottom: 16px;">
        <label style="font-size: 12px; font-weight: 600; margin-bottom: 6px; display: block;">Export Format</label>
        <div style="display: flex; gap: 16px; margin-top: 8px;">
          <label style="display: flex; align-items: center; gap: 8px; cursor: pointer; font-size: 13px;">
            <input type="radio" v-model="exportFormat" value="CSV" /> CSV
          </label>
          <label style="display: flex; align-items: center; gap: 8px; cursor: pointer; font-size: 13px;">
            <input type="radio" v-model="exportFormat" value="PDF" /> PDF
          </label>
          <label style="display: flex; align-items: center; gap: 8px; cursor: pointer; font-size: 13px;">
            <input type="radio" v-model="exportFormat" value="JSON" /> JSON
          </label>
        </div>
      </div>

      <div class="form-group" style="margin-bottom: 16px;">
        <label style="font-size: 12px; font-weight: 600; margin-bottom: 6px; display: block;">Date Range Bounds</label>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 8px;">
          <div>
            <label style="font-size: 11px; color: var(--text-muted); display: block; margin-bottom: 4px;">Start Date</label>
            <input
              type="date"
              v-model="startDate"
              class="export-start-date"
              style="width: 100%; padding: 8px; border-radius: 4px; border: 1px solid var(--border); background: var(--bg); color: var(--text); font-size: 13px;"
            />
          </div>
          <div>
            <label style="font-size: 11px; color: var(--text-muted); display: block; margin-bottom: 4px;">End Date</label>
            <input
              type="date"
              v-model="endDate"
              class="export-end-date"
              style="width: 100%; padding: 8px; border-radius: 4px; border: 1px solid var(--border); background: var(--bg); color: var(--text); font-size: 13px;"
            />
          </div>
        </div>
      </div>

      <footer style="display: flex; justify-content: flex-end; gap: 12px; margin-top: 24px;">
        <button class="btn btn-secondary" @click="$emit('close')">Cancel</button>
        <button class="btn btn-primary" @click="handleExport" :disabled="exporting">
          {{ exporting ? "Exporting..." : "Download Export" }}
        </button>
      </footer>
    </div>
  </div>
</template>

<script setup>
import { ref } from "vue";
import { useAuditorStore } from "../../stores/auditor";

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
