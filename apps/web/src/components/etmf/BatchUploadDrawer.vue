<!-- apps/web/src/components/etmf/BatchUploadDrawer.vue -->
<template>
  <ClinicalDrawer
    ref="clinicalDrawerRef"
    :model-value="isOpen"
    :teleport="false"
    size="xl"
    :dismissible="!isProcessing"
    overlay-class="batch-drawer-overlay"
    panel-class="batch-drawer"
    aria-labelledby="batch-drawer-title"
    @close="handleClose"
  >
    <template #header>
      <div class="drawer-header">
        <div class="header-title-wrap">
          <UploadCloud
            :size="20"
            class="header-icon"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <h3 id="batch-drawer-title" class="drawer-title">
            Batch Ingestion &amp; AI Auto-Classification
          </h3>
        </div>
        <button
          type="button"
          class="btn btn-secondary btn-close-batch-drawer"
          aria-label="Close batch upload drawer"
          @click="handleClose"
        >
          <X
            :size="20"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
        </button>
      </div>
    </template>

    <div class="drawer-body">
      <p class="drawer-subtitle">
        Drag and drop up to 50 regulatory documents for DIA TMF Reference Model
        v3.2.0 classification
      </p>

      <div
        v-if="!canStage"
        id="batch-stage-role-note"
        class="alert alert-error"
        role="note"
      >
        {{ ETMF_STAGE_ROLE_REASON }}
      </div>

      <!-- Error Banner -->
      <div v-if="batchError" class="alert alert-error error-banner">
        <AlertTriangle
          :size="16"
          class="inline-icon"
          aria-hidden="true"
          :stroke-width="1.75"
          focusable="false"
        />
        <span>{{ batchError }}</span>
      </div>

      <!-- Drag & Drop Dropzone -->
      <ClinicalDropzone
        class="dropzone"
        multiple
        accept=".pdf,.doc,.docx,.txt"
        :disabled="isProcessing"
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
          label-class="reason-label"
          control-class="form-control batch-reason-control"
          placeholder="e.g. Site 101 regulatory documents initial filing for CRA review"
        />
      </div>

      <!-- Progress Tracker Bar -->
      <div v-if="isProcessing || completedCount > 0" class="progress-section">
        <div class="progress-header-row">
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
        <div class="queue-header-row">
          <h4 class="queue-title">
            Upload Queue ({{ queue.length }} / 50 Files)
          </h4>
          <div class="queue-toolbar-actions">
            <button
              v-if="!isProcessing && queue.length > 0"
              type="button"
              class="btn btn-secondary btn-sm btn-clear-queue"
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
                Math.round(row.report.primary_classification.confidence * 100)
              }}% {{ formatClinicalEnum(row.report.confidence_tier) }}
            </span>
            <span v-else class="text-muted">—</span>
          </template>
          <template #cell-action="{ row, index }">
            <button
              v-if="row.report"
              type="button"
              class="btn btn-secondary btn-xs btn-inspect-item"
              @click="inspectItem(row)"
            >
              <FileSearch
                :size="16"
                aria-hidden="true"
                :stroke-width="1.75"
                focusable="false"
              />
              <span>Inspect</span>
              <span class="sr-only"> {{ row.file.name }}</span>
            </button>
            <ClinicalIconButton
              v-if="!isProcessing && row.status === 'PENDING'"
              class="btn btn-danger btn-xs btn-remove-item"
              :label="`Remove ${row.file.name} from upload queue`"
              @click="removeItem(index)"
            >
              <X
                :size="16"
                aria-hidden="true"
                :stroke-width="1.75"
                focusable="false"
              />
            </ClinicalIconButton>
          </template>
        </ClinicalDataTable>
      </div>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <button
          type="button"
          class="btn btn-secondary btn-footer-action"
          :disabled="isProcessing"
          @click="handleClose"
        >
          Cancel
        </button>
        <button
          v-if="isProcessing"
          type="button"
          class="btn btn-warning btn-footer-action"
          @click="cancelProcessing"
        >
          Stop Batch Processing
        </button>
        <button
          v-else
          type="button"
          class="btn btn-primary btn-submit-batch btn-footer-action"
          :disabled="
            !canStage ||
            queue.length === 0 ||
            !reasonForChange ||
            reasonForChange.length < 10
          "
          :title="!canStage ? ETMF_STAGE_ROLE_REASON : undefined"
          @click="startBatchUpload"
        >
          <UploadCloud
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Start Batch AI Auto-Classification &amp; Staging</span>
        </button>
      </div>
    </template>
  </ClinicalDrawer>

  <!-- Sibling Inspection Modal for Individual Batch Item -->
  <ClinicalModal
    :model-value="Boolean(inspectedItem)"
    :teleport="false"
    size="md"
    :show-close="false"
    :show-footer="false"
    backdrop-class="item-inspection-overlay"
    dialog-class="item-inspection-card"
    aria-labelledby="item-inspection-title"
    @close="inspectedItem = null"
  >
    <template #header>
      <div class="inspection-header-row">
        <h4 id="item-inspection-title" class="inspection-title">
          AI Classification Preview: {{ inspectedItem?.file?.name }}
        </h4>
        <button
          type="button"
          class="btn btn-secondary btn-xs btn-close-inspection"
          aria-label="Close classification preview"
          @click="inspectedItem = null"
        >
          <X
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
        </button>
      </div>
    </template>

    <div v-if="inspectedItem?.report" class="inspection-details">
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
      >
        <strong>AI Classification Provenance</strong>
        <dl v-if="inspectedProvenanceRows.length > 0" class="provenance-dl">
          <template v-for="attr in inspectedProvenanceRows" :key="attr.label">
            <dt class="provenance-dt">{{ attr.label }}</dt>
            <dd class="provenance-dd">{{ attr.value }}</dd>
          </template>
        </dl>
        <SupportDetails
          summary-label="Technical provenance record"
          :detail="
            JSON.stringify(inspectedItem.report.ai_generation_manifest, null, 2)
          "
        />
      </div>
    </div>
  </ClinicalModal>
</template>

<script setup>
import { ref, computed } from "vue";
import {
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalDropzone,
  ClinicalIconButton,
  ClinicalInput,
  ClinicalModal,
  handoffFocus,
} from "ui";
import { AlertTriangle, FileSearch, UploadCloud, X } from "lucide-vue-next";
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

const emit = defineEmits(["close", "batch-completed"]);

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

const clinicalDrawerRef = ref(null);
const resolveDrawerEl = () => clinicalDrawerRef.value?.drawerRef || null;

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
  { key: "action", label: "Action", cellClass: "queue-actions-cell" },
];
const reasonForChange = ref(
  "Site regulatory documents batch ingestion for CRA Quality Control"
);
const batchError = ref("");
const isProcessing = ref(false);
const inspectedItem = ref(null);
const cancelRequested = ref(false);

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
        const buttons =
          resolveDrawerEl()?.querySelectorAll?.(".btn-remove-item");
        if (!buttons?.length) return null;
        const btnIndex = Math.min(priorPendingCount, buttons.length - 1);
        return buttons[btnIndex] || null;
      },
      ".btn-remove-item",
      ".clinical-dropzone-browse-btn",
    ],
    {
      container: resolveDrawerEl,
      includeDefaultFallbacks: false,
    }
  );
}

function clearQueue() {
  queue.value = [];
  batchError.value = "";
  void handoffFocus([".clinical-dropzone-browse-btn"], {
    container: resolveDrawerEl,
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
</script>

<style scoped src="../shared/drawerHeaderChrome.css"></style>

<style scoped>
.drawer-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
  font-variant-numeric: tabular-nums;
}

.drawer-subtitle {
  margin: 0;
  font-size: 0.8125rem;
  color: var(--semantic-color-text-secondary);
}

.error-banner {
  display: flex;
  align-items: flex-start;
  gap: 8px;
}

.inline-icon {
  flex-shrink: 0;
  margin-top: 2px;
}

.dropzone {
  border: 2px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 20px;
  text-align: center;
  background: var(--semantic-color-surface-muted);
  cursor: pointer;
  transition: all 0.2s ease;
}

.dropzone:hover,
.dropzone.is-dragging {
  border-color: var(--semantic-color-interactive-primary);
}

.reason-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

:deep(.reason-label) {
  font-size: 0.8125rem;
  font-weight: 600;
}

:deep(.batch-reason-control) {
  width: 100%;
  min-height: 44px;
  font-size: 1rem;
  box-sizing: border-box;
}

.progress-section {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  padding: 12px 14px;
  border-radius: 6px;
}

.progress-header-row {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
  font-size: 0.75rem;
  font-weight: 600;
  margin-bottom: 6px;
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
  gap: 8px;
  min-width: 0;
}

.queue-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.queue-title {
  margin: 0;
  font-size: 0.85rem;
  font-weight: 600;
}

.btn-clear-queue {
  min-height: 44px;
  padding: 6px 12px;
}

:deep(.queue-table-wrapper) {
  border: 1px solid var(--border);
  border-radius: 6px;
  overflow-x: auto;
  max-width: 100%;
}

:deep(.queue-table) {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.75rem;
}

:deep(.queue-table th),
:deep(.queue-table td) {
  padding: 8px 10px;
  text-align: left;
  border-bottom: 1px solid var(--border);
}

:deep(.queue-table th) {
  background: var(--semantic-color-surface-muted);
  font-weight: 600;
}

:deep(.filename-cell) {
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

:deep(.queue-actions-cell) {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.btn-inspect-item {
  min-height: 44px;
  padding: 6px 10px;
  font-size: 0.75rem;
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.drawer-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  width: 100%;
  flex-wrap: wrap;
}

.btn-footer-action {
  min-height: 44px;
  padding: 8px 16px;
  font-size: 0.8125rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

.inspection-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  width: 100%;
  min-width: 0;
}

.inspection-title {
  margin: 0;
  font-size: 0.95rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.btn-close-inspection {
  min-width: 44px;
  min-height: 44px;
  padding: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.provenance-summary-card {
  margin-top: 10px;
  background: var(--semantic-color-surface-muted);
  padding: 10px;
  border-radius: 6px;
  font-size: 0.75rem;
}

.provenance-dl {
  margin: 6px 0 8px 0;
  display: grid;
  grid-template-columns: max-content minmax(0, 1fr);
  gap: 4px 12px;
}

.provenance-dt {
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.provenance-dd {
  margin: 0;
  word-break: break-word;
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

@media (max-width: 640px) {
  .drawer-footer {
    flex-direction: column;
    align-items: stretch;
  }

  .btn-footer-action {
    width: 100%;
  }

  :deep(.queue-table),
  :deep(.queue-table tbody),
  :deep(.queue-table tr),
  :deep(.queue-table td) {
    display: block;
    width: 100%;
    box-sizing: border-box;
  }

  :deep(.queue-table thead) {
    display: none;
  }

  :deep(.queue-table tr) {
    padding: 10px;
    border-bottom: 1px solid var(--border);
  }

  :deep(.queue-table td) {
    border-bottom: none;
    padding: 4px 0;
  }

  :deep(.filename-cell) {
    max-width: 100%;
    white-space: normal;
    word-break: break-all;
  }

  :deep(.queue-table td::before) {
    content: attr(data-label) ": ";
    font-weight: 700;
    color: var(--semantic-color-text-secondary);
  }
}
</style>
