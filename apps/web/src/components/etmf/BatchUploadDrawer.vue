<!-- apps/web/src/components/etmf/BatchUploadDrawer.vue -->
<template>
  <div
    v-if="isOpen"
    class="batch-drawer-overlay"
    role="presentation"
    @click.self="handleClose"
  >
    <div
      ref="drawerRef"
      class="batch-drawer"
      role="dialog"
      aria-modal="true"
      aria-labelledby="batch-drawer-title"
      tabindex="-1"
    >
      <div class="drawer-header">
        <div style="display: flex; align-items: center; gap: 10px">
          <span style="font-size: 24px">📤</span>
          <div>
            <h3
              id="batch-drawer-title"
              style="margin: 0; font-size: 18px; font-weight: 700"
            >
              Batch Ingestion &amp; AI Auto-Classification
            </h3>
            <p
              style="
                margin: 2px 0 0 0;
                font-size: 12px;
                color: var(--semantic-color-text-secondary);
              "
            >
              Drag and drop up to 50 regulatory documents for DIA TMF Reference
              Model v3.2.0 classification
            </p>
          </div>
        </div>
        <button
          type="button"
          class="btn btn-secondary btn-close-batch-drawer"
          style="padding: 4px 10px; font-size: 14px; cursor: pointer"
          aria-label="Close batch upload drawer"
          @click="handleClose"
        >
          ×
        </button>
      </div>

      <div class="drawer-body">
        <div
          v-if="!canStage"
          id="batch-stage-role-note"
          class="alert alert-error"
          role="note"
          style="margin-bottom: 16px"
        >
          {{ ETMF_STAGE_ROLE_REASON }}
        </div>

        <!-- Error Banner -->
        <div
          v-if="batchError"
          class="alert alert-error"
          style="margin-bottom: 16px"
        >
          ⚠️ {{ batchError }}
        </div>

        <!-- Drag & Drop Dropzone -->
        <ClinicalDropzone
          class="dropzone"
          multiple
          accept=".pdf,.doc,.docx,.txt"
          :disabled="isProcessing"
          icon="📁"
          prompt-text="Drop files here or browse to upload"
          hint-text="Supports PDF, DOCX, TXT • Max 50 files • 100 MB per file • 500 MB per batch"
          button-label="Browse files to upload"
          input-aria-label="Files to upload"
          @files-selected="addFilesToQueue"
        />

        <!-- 21 CFR Part 11 Change Justification -->
        <div class="reason-section">
          <ClinicalInput
            id="batch-reason-input"
            v-model="reasonForChange"
            label="21 CFR Part 11 Reason for Ingestion"
            required
            density="compact"
            :show-query-flag="false"
            control-class="form-control"
            placeholder="e.g. Site 101 regulatory documents initial filing for CRA review"
          />
        </div>

        <!-- Progress Tracker Bar -->
        <div v-if="isProcessing || completedCount > 0" class="progress-section">
          <div
            style="
              display: flex;
              justify-content: space-between;
              font-size: 12px;
              font-weight: 600;
              margin-bottom: 4px;
            "
          >
            <span>Batch Ingestion Progress</span>
            <span
              >{{ completedCount }} / {{ queue.length }} Files Staged ({{
                overallProgress
              }}%)</span
            >
          </div>
          <div class="progress-bar-track">
            <div
              class="progress-bar-fill"
              :style="{ width: overallProgress + '%' }"
            />
          </div>
        </div>

        <!-- Batch Upload Items Queue Table -->
        <div v-if="queue.length > 0" class="queue-section">
          <div
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              margin-bottom: 8px;
            "
          >
            <h4 style="margin: 0; font-size: 13px; font-weight: 600">
              Upload Queue ({{ queue.length }} / 50 Files)
            </h4>
            <div style="display: flex; gap: 8px">
              <button
                v-if="!isProcessing && queue.length > 0"
                type="button"
                class="btn btn-secondary btn-sm"
                style="font-size: var(--font-size-2xs); padding: 4px 8px"
                @click="clearQueue"
              >
                Clear Queue
              </button>
            </div>
          </div>

          <ClinicalDataTable
            title="Batch Upload Queue"
            hide-header
            plain
            :columns="queueColumns"
            :data="queue"
            :row-key="(item, idx) => `${item.file.name}-${idx}`"
            :page-size="Math.max(queue.length, 50)"
            wrapper-class="queue-table-wrapper"
            table-class="queue-table"
            row-class="queue-row"
          >
            <template #cell-filename="{ row }">
              <span class="font-mono" :title="row.file.name">{{
                row.file.name
              }}</span>
            </template>
            <template #cell-size="{ row }">
              {{ formatFileSize(row.file.size) }}
            </template>
            <template #cell-status="{ row }">
              <span class="badge" :class="getStatusBadgeClass(row.status)">
                {{ formatClinicalStatus(row.status) }}
              </span>
            </template>
            <template #cell-classification="{ row }">
              <div v-if="row.report" class="classification-preview">
                <span class="font-mono text-bold">{{
                  row.report.primary_classification.artifact_code
                }}</span>
                <span class="text-muted">
                  ({{ row.report.primary_classification.artifact_type }})</span
                >
              </div>
              <span v-else class="text-muted">—</span>
            </template>
            <template #cell-confidence="{ row }">
              <span
                v-if="row.report"
                class="badge"
                :class="getConfidenceBadgeClass(row.report.confidence_tier)"
              >
                {{
                  Math.round(
                    row.report.primary_classification.confidence * 100
                  )
                }}% {{ formatClinicalEnum(row.report.confidence_tier) }}
              </span>
              <span v-else class="text-muted">—</span>
            </template>
            <template #cell-action="{ row, index }">
              <button
                v-if="row.report"
                type="button"
                class="btn btn-secondary btn-xs btn-inspect-item"
                style="font-size: var(--font-size-2xs); padding: 2px 6px"
                @click="inspectItem(row)"
              >
                Inspect<span class="sr-only"> {{ row.file.name }}</span>
              </button>
              <ClinicalIconButton
                v-if="!isProcessing && row.status === 'PENDING'"
                class="btn btn-danger btn-xs btn-remove-item"
                style="
                  min-width: 24px;
                  min-height: 24px;
                  font-size: var(--font-size-2xs);
                  padding: 2px 6px;
                  margin-left: 4px;
                "
                :label="`Remove ${row.file.name} from upload queue`"
                @click="removeItem(index)"
              >
                ×
              </ClinicalIconButton>
            </template>
          </ClinicalDataTable>
        </div>
      </div>

      <!-- Drawer Footer Actions -->
      <div class="drawer-footer">
        <button
          type="button"
          class="btn btn-secondary"
          style="padding: 8px 16px; font-size: 13px"
          :disabled="isProcessing"
          @click="handleClose"
        >
          Cancel
        </button>
        <button
          v-if="isProcessing"
          type="button"
          class="btn btn-warning"
          style="padding: 8px 16px; font-size: 13px; margin-left: 8px"
          @click="cancelProcessing"
        >
          Stop Batch Processing
        </button>
        <button
          v-else
          type="button"
          class="btn btn-primary btn-submit-batch"
          style="padding: 8px 16px; font-size: 13px; margin-left: 8px"
          :disabled="
            !canStage ||
            queue.length === 0 ||
            !reasonForChange ||
            reasonForChange.length < 10
          "
          :title="!canStage ? ETMF_STAGE_ROLE_REASON : undefined"
          @click="startBatchUpload"
        >
          Start Batch AI Auto-Classification &amp; Staging
        </button>
      </div>
    </div>

    <!-- Inspection Modal Drawer for Individual Batch Item -->
    <div
      v-if="inspectedItem"
      class="item-inspection-overlay"
      role="presentation"
      @click.self="inspectedItem = null"
    >
      <div
        ref="inspectionCardRef"
        class="item-inspection-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="item-inspection-title"
        tabindex="-1"
      >
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 12px;
          "
        >
          <h4 id="item-inspection-title" style="margin: 0; font-size: 14px">
            AI Classification Preview: {{ inspectedItem.file.name }}
          </h4>
          <button
            type="button"
            class="btn btn-secondary btn-xs"
            aria-label="Close classification preview"
            @click="inspectedItem = null"
          >
            ✕
          </button>
        </div>
        <div v-if="inspectedItem.report" class="inspection-details">
          <p>
            <strong>Predicted Artifact Code:</strong>
            <code class="font-mono">{{
              inspectedItem.report.primary_classification.artifact_code
            }}</code>
          </p>
          <p>
            <strong>Artifact Type:</strong>
            {{ inspectedItem.report.primary_classification.artifact_type }}
          </p>
          <p>
            <strong>Zone:</strong> Zone
            {{ inspectedItem.report.primary_classification.zone }} - Section
            {{ inspectedItem.report.primary_classification.section }}
          </p>
          <p>
            <strong>Confidence Score:</strong>
            {{
              Math.round(
                inspectedItem.report.primary_classification.confidence * 100
              )
            }}% ({{ formatClinicalEnum(inspectedItem.report.confidence_tier) }})
          </p>
          <p>
            <strong>QC Recommendation:</strong>
            <span class="badge status-approved">{{
              formatClinicalEnum(inspectedItem.report.qc_recommendation)
            }}</span>
          </p>
          <div
            v-if="inspectedItem.report.ai_generation_manifest"
            class="provenance-summary-card"
            style="
              margin-top: 10px;
              background: var(--semantic-color-surface-muted);
              padding: 10px;
              border-radius: 4px;
              font-size: 12px;
            "
          >
            <strong>AI Classification Provenance</strong>
            <dl
              v-if="inspectedProvenanceRows.length > 0"
              style="
                margin: 6px 0 8px 0;
                display: grid;
                grid-template-columns: max-content 1fr;
                gap: 4px 12px;
              "
            >
              <template
                v-for="attr in inspectedProvenanceRows"
                :key="attr.label"
              >
                <dt
                  style="
                    color: var(--semantic-color-text-secondary);
                    font-weight: 600;
                  "
                >
                  {{ attr.label }}
                </dt>
                <dd style="margin: 0">{{ attr.value }}</dd>
              </template>
            </dl>
            <SupportDetails
              summary-label="Technical provenance record"
              :detail="
                JSON.stringify(
                  inspectedItem.report.ai_generation_manifest,
                  null,
                  2
                )
              "
            />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onBeforeUnmount } from "vue";
import {
  ClinicalDataTable,
  ClinicalDropzone,
  ClinicalIconButton,
  ClinicalInput,
  handoffFocus,
  registerDraftSource,
  useFocusTrap,
} from "ui";
import SupportDetails from "../canvas/SupportDetails.vue";
import { useEtmfStore, arrayBufferToBase64 } from "../../stores/etmf.js";
import { useAuthStore } from "../../stores/auth.js";
import { useToastStore } from "../../stores/toasts.js";
import {
  canStageEtmfQc,
  ETMF_STAGE_ROLE_REASON,
} from "../../canvas/authority.ts";
import {
  extractAiProvenanceAttributes,
  formatClinicalEnum,
  formatClinicalStatus,
} from "../../utils/clinicalLabels";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  studyId: {
    type: String,
    default: "",
  },
  siteId: {
    type: String,
    default: null,
  },
});

const emit = defineEmits(["close", "batch-completed", "dirty-change"]);

const etmfStore = useEtmfStore();
const authStore = useAuthStore();
const toastStore = useToastStore();

const hasActiveAuth = computed(() =>
  Boolean(
    authStore.isAuthenticated ||
    (Array.isArray(authStore.rawRoles) && authStore.rawRoles.length > 0)
  )
);
const canStage = computed(
  () => !hasActiveAuth.value || canStageEtmfQc(authStore)
);

const DEFAULT_BATCH_REASON =
  "Site regulatory documents batch ingestion for CRA Quality Control";
const drawerRef = ref(null);
const inspectionCardRef = ref(null);
const queue = ref([]);

const queueColumns = [
  {
    key: "filename",
    label: "File Name",
    cellClass: "filename-cell font-mono",
  },
  { key: "size", label: "Size", cellClass: "size-cell" },
  { key: "status", label: "Status" },
  { key: "classification", label: "Predicted Classification" },
  { key: "confidence", label: "Confidence" },
  { key: "action", label: "Action" },
];
const reasonForChange = ref(DEFAULT_BATCH_REASON);
const batchError = ref("");
const isProcessing = ref(false);
const inspectedItem = ref(null);
const cancelRequested = ref(false);

const isBatchDirty = computed(() =>
  Boolean(
    props.isOpen &&
    (isProcessing.value ||
      queue.value.some((item) => item.status !== "STAGED") ||
      reasonForChange.value.trim() !== DEFAULT_BATCH_REASON)
  )
);

const unregisterDraftSource = registerDraftSource({
  flush: () => {},
  hasDrafts: () => isBatchDirty.value,
});

watch(
  isBatchDirty,
  (dirty) => {
    emit("dirty-change", dirty);
  },
  { immediate: true }
);

onBeforeUnmount(() => {
  unregisterDraftSource();
});

const inspectedProvenanceRows = computed(() =>
  extractAiProvenanceAttributes(
    inspectedItem.value?.report?.ai_generation_manifest
  )
);

const completedCount = computed(() => {
  return queue.value.filter((i) => i.status === "STAGED").length;
});

const overallProgress = computed(() => {
  if (queue.value.length === 0) return 0;
  return Math.round((completedCount.value / queue.value.length) * 100);
});

function addFilesToQueue(files) {
  batchError.value = "";
  if (files.length === 0) return;

  if (queue.value.length + files.length > 50) {
    batchError.value =
      "Maximum limit of 50 documents per batch transaction exceeded.";
    toastStore.push({
      title: "Batch Limit Exceeded",
      message:
        "Batch limit exceeded: Maximum 50 documents per transaction permitted.",
      tone: "error",
    });
    return;
  }

  const MAX_FILE_SIZE = 100 * 1024 * 1024; // 100 MB
  const MAX_BATCH_SIZE = 500 * 1024 * 1024; // 500 MB

  let currentBatchSize = queue.value.reduce(
    (acc, item) => acc + item.file.size,
    0
  );

  for (const file of files) {
    if (file.size > MAX_FILE_SIZE) {
      batchError.value = `File '${file.name}' exceeds the 100 MB maximum size limit.`;
      toastStore.push({
        title: "File Size Limit Exceeded",
        message: `File '${file.name}' exceeds 100 MB limit.`,
        tone: "error",
      });
      continue;
    }

    if (currentBatchSize + file.size > MAX_BATCH_SIZE) {
      batchError.value = "Total batch size exceeds 500 MB limit.";
      toastStore.push({
        title: "Batch Size Limit Exceeded",
        message: "Total batch transaction size cannot exceed 500 MB.",
        tone: "error",
      });
      break;
    }

    currentBatchSize += file.size;

    // Run quick local prediction preview

    queue.value.push({
      file,
      status: "PENDING",
      report: null,
      error: null,
    });
  }
}

function removeItem(index) {
  const priorPendingCount = queue.value
    .slice(0, index)
    .filter((item) => item?.status === "PENDING").length;
  queue.value.splice(index, 1);
  void handoffFocus(
    [
      () => {
        const buttons = drawerRef.value?.querySelectorAll?.(".btn-remove-item");
        if (!buttons?.length) return null;
        const btnIndex = Math.min(priorPendingCount, buttons.length - 1);
        return buttons[btnIndex] || null;
      },
      ".btn-remove-item",
      ".clinical-dropzone-browse-btn",
    ],
    {
      container: drawerRef,
      includeDefaultFallbacks: false,
    }
  );
}

function clearQueue() {
  queue.value = [];
  batchError.value = "";
  void handoffFocus([".clinical-dropzone-browse-btn"], {
    container: drawerRef,
    includeDefaultFallbacks: false,
  });
}

function inspectItem(item) {
  inspectedItem.value = item;
}

function formatFileSize(bytes) {
  if (!bytes) return "0 B";
  const k = 1024;
  const sizes = ["B", "KB", "MB", "GB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + " " + sizes[i];
}

function getStatusBadgeClass(status) {
  switch (status) {
    case "PENDING":
      return "status-draft";
    case "CLASSIFYING":
      return "status-review";
    case "STAGED":
      return "status-approved";
    case "FAILED":
      return "status-hold";
    default:
      return "status-draft";
  }
}

function getConfidenceBadgeClass(tier) {
  switch (tier) {
    case "HIGH":
      return "status-approved";
    case "MEDIUM":
      return "status-review";
    case "LOW":
      return "status-hold";
    default:
      return "status-draft";
  }
}

async function readFileAsText(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result || ""));
    reader.onerror = () =>
      reject(
        new Error(
          `"${file.name}" could not be read: ${reader.error?.message || "unknown read error"}`
        )
      );
    reader.readAsText(file.slice(0, 65536));
  });
}

async function readFileAsBase64(file) {
  if (typeof file.arrayBuffer === "function") {
    const buffer = await file.arrayBuffer();
    if (!buffer || buffer.byteLength === 0) {
      throw new Error(`"${file.name}" is empty; no document bytes to stage.`);
    }
    return arrayBufferToBase64(buffer);
  }
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const buffer = reader.result;
      if (!buffer || buffer.byteLength === 0) {
        reject(
          new Error(`"${file.name}" is empty; no document bytes to stage.`)
        );
        return;
      }
      resolve(arrayBufferToBase64(buffer));
    };
    reader.onerror = () =>
      reject(
        new Error(
          `"${file.name}" could not be read: ${reader.error?.message || "unknown read error"}`
        )
      );
    reader.readAsArrayBuffer(file);
  });
}

async function startBatchUpload() {
  if (!canStage.value) {
    batchError.value = ETMF_STAGE_ROLE_REASON;
    return;
  }
  if (queue.value.length === 0) return;
  if (!reasonForChange.value || reasonForChange.value.length < 10) {
    batchError.value =
      "A mandatory 21 CFR Part 11 reason of at least 10 characters is required.";
    return;
  }
  const targetStudyId = (
    props.studyId ||
    etmfStore.currentStudyId ||
    ""
  ).trim();
  if (!targetStudyId) {
    batchError.value =
      "Select an active study before staging documents for Technical QC.";
    return;
  }
  const targetSiteId =
    (props.siteId || etmfStore.currentSiteId || "").trim() || null;

  isProcessing.value = true;
  cancelRequested.value = false;
  batchError.value = "";

  for (let i = 0; i < queue.value.length; i++) {
    if (cancelRequested.value) {
      toastStore.push({
        title: "Batch Upload Cancelled",
        message: "Batch processing cancelled by user.",
        tone: "warning",
      });
      break;
    }

    const item = queue.value[i];
    item.status = "CLASSIFYING";

    try {
      const [textContent, contentBase64] = await Promise.all([
        readFileAsText(item.file),
        readFileAsBase64(item.file),
      ]);
      if (!textContent && !contentBase64) {
        throw new Error(
          `"${item.file.name}" is empty; no document content to stage.`
        );
      }

      // Classify and stage document into TECHNICAL_QC with full binary payload
      const stagedResult = await etmfStore.stageQcDocument({
        filename: item.file.name,
        content: textContent,
        content_base64: contentBase64,
        mime_type: item.file.type || "application/pdf",
        study_id: targetStudyId,
        site_id: targetSiteId,
        reason_for_change: reasonForChange.value,
      });

      item.status = "STAGED";
      item.report = stagedResult.intelligence_report || stagedResult;
    } catch (err) {
      item.status = "FAILED";
      item.error = err.message || "Failed to classify/stage document";
      toastStore.push({
        title: "Document Staging Failed",
        message: `Failed to stage '${item.file.name}': ${item.error}`,
        tone: "error",
      });
    }
  }

  isProcessing.value = false;
  toastStore.push({
    title: "Batch Ingestion Complete",
    message: `Batch Ingestion complete: ${completedCount.value} / ${queue.value.length} documents staged for CRA QC.`,
    tone: "success",
  });

  emit("batch-completed", queue.value);
}

function cancelProcessing() {
  cancelRequested.value = true;
}

function handleClose() {
  if (isProcessing.value) {
    cancelProcessing();
  }
  emit("close");
}

useFocusTrap(drawerRef, {
  isOpen: () => props.isOpen,
  onEscape: handleClose,
});

useFocusTrap(inspectionCardRef, {
  isOpen: () => Boolean(inspectedItem.value),
  onEscape: () => {
    inspectedItem.value = null;
  },
});
</script>

<style scoped>
.batch-drawer-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: var(--semantic-color-backdrop);
  z-index: 1100;
  display: flex;
  justify-content: flex-end;
}

.batch-drawer {
  width: min(100vw, 780px);
  height: 100%;
  background: var(--semantic-color-surface);
  display: flex;
  flex-direction: column;
  box-shadow: var(--shadow-drawer-left);
  animation: slideIn 0.2s ease-out;
}

@keyframes slideIn {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 18px 24px;
  border-bottom: 1px solid var(--border);
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.dropzone {
  border: 2px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 28px;
  text-align: center;
  background: var(--semantic-color-surface-muted);
  cursor: pointer;
  transition: all 0.2s ease;
}

.dropzone:hover,
.dropzone.is-dragging {
  border-color: var(--semantic-color-interactive-primary);
  background: var(--semantic-color-info-bg-soft);
}

.dropzone-icon {
  font-size: 36px;
  display: block;
  margin-bottom: 8px;
}

.dropzone-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.browse-link {
  color: var(--semantic-color-interactive-primary);
  text-decoration: underline;
}

.dropzone-hint {
  margin: 4px 0 0 0;
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
}

.reason-section {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.progress-section {
  background: var(--semantic-color-surface-header);
  padding: 12px 16px;
  border-radius: 6px;
}

.progress-bar-track {
  height: 8px;
  background: var(--semantic-color-surface-header);
  border-radius: 4px;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  background: var(--semantic-color-interactive-primary);
  transition: width 0.3s ease;
}

.queue-section {
  display: flex;
  flex-direction: column;
}

.queue-table-wrapper {
  max-height: 280px;
  overflow-y: auto;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.queue-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
}

.queue-table th,
.queue-table td {
  padding: 8px 12px;
  text-align: left;
  border-bottom: 1px solid var(--border);
}

.queue-table th {
  background: var(--semantic-color-surface-muted);
  font-weight: 600;
  position: sticky;
  top: 0;
  z-index: 1;
}

.filename-cell {
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.drawer-footer {
  padding: 16px 24px;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
  background: var(--semantic-color-surface-muted);
}

.item-inspection-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: var(--semantic-color-backdrop-soft);
  z-index: 1200;
  display: flex;
  align-items: center;
  justify-content: center;
}

.item-inspection-card {
  width: 480px;
  max-width: 90vw;
  background: var(--semantic-color-surface);
  border-radius: 8px;
  padding: 20px;
  box-shadow: var(--shadow-md);
}
</style>
