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
          class="btn-close"
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

      <div class="form-group" style="margin-bottom: 16px">
        <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
        <label
          style="
            font-size: 12px;
            font-weight: 600;
            margin-bottom: 6px;
            display: block;
          "
          >Export Format</label
        >
        <div style="display: flex; gap: 16px; margin-top: 8px">
          <label
            class="touch-target-interactive"
            style="
              display: flex;
              align-items: center;
              gap: 8px;
              cursor: pointer;
              font-size: 13px;
            "
          >
            <input v-model="exportFormat" type="radio" value="CSV" /> CSV
          </label>
          <label
            class="touch-target-interactive"
            style="
              display: flex;
              align-items: center;
              gap: 8px;
              cursor: pointer;
              font-size: 13px;
            "
          >
            <input v-model="exportFormat" type="radio" value="PDF" /> PDF
          </label>
          <label
            class="touch-target-interactive"
            style="
              display: flex;
              align-items: center;
              gap: 8px;
              cursor: pointer;
              font-size: 13px;
            "
          >
            <input v-model="exportFormat" type="radio" value="JSON" /> JSON
          </label>
        </div>
      </div>

      <div class="form-group" style="margin-bottom: 16px">
        <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
        <label
          style="
            font-size: 12px;
            font-weight: 600;
            margin-bottom: 6px;
            display: block;
          "
          >Date Range Bounds</label
        >
        <div class="grid-2-responsive" style="margin-top: 8px">
          <div>
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label
              style="
                font-size: 11px;
                color: var(--text-muted);
                display: block;
                margin-bottom: 4px;
              "
              >Start Date</label
            >
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
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
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label
              style="
                font-size: 11px;
                color: var(--text-muted);
                display: block;
                margin-bottom: 4px;
              "
              >End Date</label
            >
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
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
      </div>

      <!-- Export Progress Display -->
      <div
        v-if="taskPipeline.isProcessing.value"
        class="export-progress-panel"
        style="
          margin-top: 16px;
          padding: 12px;
          background: var(--bg);
          border: 1px solid var(--border);
          border-radius: 6px;
        "
      >
        <div style="font-size: 12px; font-weight: 600; margin-bottom: 4px">
          {{ taskPipeline.currentStage.value }}
        </div>
        <div
          class="progress-bar-track"
          style="
            height: 6px;
            background: #e2e8f0;
            border-radius: 3px;
            overflow: hidden;
            margin-bottom: 8px;
          "
        >
          <div
            class="progress-bar-fill"
            :style="{
              width: taskPipeline.progressPercent.value + '%',
              height: '100%',
              background: 'var(--primary, #0284c7)',
            }"
          />
        </div>
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
          "
        >
          <span style="font-size: 11px; color: var(--text-muted)">
            {{ taskPipeline.progressPercent.value }}% Completed
          </span>
          <button
            type="button"
            class="btn btn-sm btn-cancel-auditor-export"
            style="
              padding: 2px 8px;
              font-size: 11px;
              border: 1px solid #f87171;
              color: #dc2626;
              background: transparent;
              cursor: pointer;
            "
            @click="cancelExport"
          >
            ⛔ Cancel
          </button>
        </div>
      </div>

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
import { ref, computed } from "vue";
import { useAuditorStore } from "../../stores/auditor";
import { useFocusTrap } from "@/composables/useFocusTrap";
import { useEscapeClose } from "@/composables/useEscapeClose";
import { useTaskPipeline } from "@/composables/useTaskPipeline";

const emit = defineEmits(["close"]);

const modalRef = ref(null);
useFocusTrap(modalRef);
useEscapeClose(() => emit("close"));

const auditorStore = useAuditorStore();
const taskPipeline = useTaskPipeline();

const exportFormat = ref("CSV");
const startDate = ref(auditorStore.filters.dateRange.start);
const endDate = ref(auditorStore.filters.dateRange.end);
const exporting = computed(() => taskPipeline.isProcessing.value);

function cancelExport() {
  taskPipeline.cancelTask("Audit export cancelled by user");
}

async function handleExport() {
  try {
    await taskPipeline.runTask(
      async ({ signal, updateProgress, registerObjectUrl }) => {
        updateProgress(20, "Applying filter bounds to audit ledger...");
        auditorStore.setFilters({
          dateRange: {
            start: startDate.value,
            end: endDate.value,
          },
        });

        if (signal.aborted) return;

        updateProgress(50, "Fetching Part 11 audit logs from server...");
        await auditorStore.fetchAuditLogs();

        if (signal.aborted) return;

        updateProgress(80, `Generating ${exportFormat.value} export file...`);
        await auditorStore.exportAuditTrail(exportFormat.value, {
          signal,
          registerObjectUrl,
        });

        updateProgress(100, "Export completed successfully.");
        emit("close");
      }
    );
  } catch (err) {
    if (taskPipeline.isCancelled.value || err?.name === "AbortError") {
      console.log("Audit export cancelled by user");
    } else {
      console.error("Export failed:", err);
    }
  }
}
</script>
