<!-- apps/web/src/components/etmf/ExportJobDrawer.vue -->
<template>
  <ClinicalDrawer
    :model-value="isOpen"
    :teleport="false"
    size="md"
    overlay-class="export-drawer-overlay"
    panel-class="export-drawer"
    aria-label="Study binder export progress"
    @close="handleClose"
  >
    <template #header>
      <div class="drawer-header">
        <div class="header-title-group">
          <Archive :size="18" class="header-icon" aria-hidden="true" />
          <h3 class="drawer-title">eTMF Study Binder Export</h3>
        </div>
        <button
          type="button"
          class="btn-close-drawer"
          aria-label="Close export drawer"
          @click="handleClose"
        >
          <X :size="18" aria-hidden="true" />
        </button>
      </div>
    </template>

    <div class="drawer-body">
      <p class="drawer-subtitle">
        Prepare and download complete eTMF study binder archives
      </p>

      <div
        v-if="downloadError"
        id="export-download-error"
        class="error-box"
        role="alert"
      >
        <AlertCircle :size="15" class="inline-icon" aria-hidden="true" />
        <span>{{ downloadError }}</span>
      </div>
      <p
        v-if="!canExport"
        id="export-drawer-role-note"
        class="expiration-notice"
        role="note"
      >
        {{ ETMF_EXPORT_ROLE_REASON }}
      </p>

      <!-- Active Job Section -->
      <div v-if="activeJob" class="active-job-card">
        <div class="card-header">
          <div class="card-header-meta">
            <span class="job-id-tag">Study Binder Export</span>
            <span class="study-id-text">Study: {{ activeJob.study_id }}</span>
          </div>
          <span
            :class="getStatusBadgeClass(activeJob.status)"
            class="status-badge"
          >
            {{ formatClinicalStatus(activeJob.status) }}
          </span>
        </div>

        <div class="progress-container">
          <div class="progress-info">
            <span class="progress-label">Export Progress</span>
            <span class="progress-value"
              >{{ Math.round(activeJob.progress_percentage) }}%</span
            >
          </div>
          <div
            class="progress-bar-track"
            role="progressbar"
            :aria-valuenow="Math.round(activeJob.progress_percentage)"
            aria-valuemin="0"
            aria-valuemax="100"
            :aria-label="`Export progress: ${Math.round(activeJob.progress_percentage)} percent`"
          >
            <div
              class="progress-bar-fill"
              :class="{ 'is-animated': activeJob.status === 'PROCESSING' }"
              :style="{ width: `${activeJob.progress_percentage}%` }"
            />
          </div>
          <div class="progress-details">
            <span>
              Processed
              <strong>{{ activeJob.processed_documents }}</strong> of
              <strong>{{ activeJob.total_documents }}</strong> documents
            </span>
            <span v-if="activeJob.created_at" class="created-at">
              Requested {{ formatDate(activeJob.created_at) }}
            </span>
          </div>
          <SupportDetails
            v-if="activeJob.job_id"
            summary-label="Export reference"
            :reference="activeJob.job_id"
          />
        </div>

        <!-- Download Action Area -->
        <div v-if="activeJob.status === 'COMPLETED'" class="completion-box">
          <div class="completion-message">
            <CheckCircle2 :size="15" class="inline-icon" aria-hidden="true" />
            <span>
              Study binder archive is ready for download with complete document
              index and verification inventory.
            </span>
          </div>
          <button
            v-if="activeJob.download_url || activeJob.job_id"
            type="button"
            class="btn-download"
            :disabled="!canExport || downloadingJobId === activeJob.job_id"
            :title="!canExport ? ETMF_EXPORT_ROLE_REASON : undefined"
            @click="handleDownload(activeJob)"
          >
            <span v-if="downloadingJobId === activeJob.job_id">
              Downloading Export Package...
            </span>
            <template v-else>
              <Download :size="15" aria-hidden="true" />
              <span>Download Export Package (.zip)</span>
            </template>
          </button>
          <p v-if="activeJob.expires_at" class="expiration-notice">
            <Clock :size="13" class="inline-icon" aria-hidden="true" />
            <span>
              Temporary download link expires at
              {{ formatDate(activeJob.expires_at) }} (24h validity).
            </span>
          </p>
        </div>

        <!-- Failure Alert -->
        <div v-else-if="activeJob.status === 'FAILED'" class="error-box">
          <AlertCircle :size="15" class="inline-icon" aria-hidden="true" />
          <span>
            Background export job failed. Please verify document storage
            connectivity or retry export.
          </span>
        </div>
      </div>

      <!-- No Active Job State -->
      <div v-else class="empty-active-job">
        <p>No export job currently in progress.</p>
      </div>

      <!-- Export Job History Section -->
      <div class="history-section">
        <h4 class="history-title">Export Job History</h4>
        <div v-if="exportJobs && exportJobs.length > 0" class="history-list">
          <div
            v-for="job in exportJobs"
            :key="job.job_id"
            class="history-item"
            :class="{
              'is-active': activeJob && activeJob.job_id === job.job_id,
            }"
          >
            <div class="history-item-header">
              <span class="history-job-id"
                >Binder archive &middot;
                {{ job.total_documents }} documents</span
              >
              <span
                :class="getStatusBadgeClass(job.status)"
                class="status-badge-sm"
              >
                {{ formatClinicalStatus(job.status) }}
              </span>
            </div>
            <div class="history-item-meta">
              <span>Requested {{ formatDate(job.created_at) }}</span>
            </div>
            <SupportDetails
              v-if="job.job_id"
              summary-label="Export reference"
              :reference="job.job_id"
            />
            <div
              v-if="
                job.status === 'COMPLETED' && (job.download_url || job.job_id)
              "
              class="history-item-action"
            >
              <button
                type="button"
                class="history-download-link"
                :disabled="!canExport || downloadingJobId === job.job_id"
                :title="!canExport ? ETMF_EXPORT_ROLE_REASON : undefined"
                @click="handleDownload(job)"
              >
                <Download :size="13" aria-hidden="true" />
                <span>{{
                  downloadingJobId === job.job_id
                    ? "Downloading..."
                    : "Download .zip"
                }}</span>
              </button>
            </div>
          </div>
        </div>
        <p v-else class="empty-history-text">
          No previous export history found.
        </p>
      </div>
    </div>
  </ClinicalDrawer>
</template>

<script setup>
import { computed, ref } from "vue";
import { ClinicalDrawer } from "ui";
import {
  AlertCircle,
  Archive,
  CheckCircle2,
  Clock,
  Download,
  X,
} from "lucide-vue-next";
import SupportDetails from "../canvas/SupportDetails.vue";
import { useEtmfStore } from "../../stores/etmf.js";
import { useAuthStore } from "../../stores/auth.js";
import { useToastStore } from "../../stores/toasts.js";
import {
  canExportEtmf,
  ETMF_EXPORT_ROLE_REASON,
} from "../../canvas/authority.ts";
import { formatClinicalStatus } from "../../utils/clinicalLabels";

const store = useEtmfStore();
const authStore = useAuthStore();
const toastStore = useToastStore();
const downloadingJobId = ref("");
const downloadError = ref("");

const hasActiveAuth = computed(() =>
  Boolean(
    authStore.isAuthenticated ||
    (Array.isArray(authStore.rawRoles) && authStore.rawRoles.length > 0)
  )
);
const canExport = computed(
  () => !hasActiveAuth.value || canExportEtmf(authStore)
);

const isOpen = computed(() => store.isExportDrawerOpen);
const activeJob = computed(() => store.activeExportJob);
const exportJobs = computed(() => store.exportJobs);

async function handleDownload(job) {
  if (!job?.job_id) return;
  if (!canExport.value) {
    downloadError.value = ETMF_EXPORT_ROLE_REASON;
    return;
  }
  downloadingJobId.value = job.job_id;
  downloadError.value = "";
  try {
    await store.downloadExportJobArchive(job);
  } catch (err) {
    downloadError.value =
      err?.message ||
      "Failed to download export ZIP package. The job archive may have expired.";
    toastStore.apiError("Export ZIP download failed", err);
  } finally {
    downloadingJobId.value = "";
  }
}

function handleClose() {
  downloadError.value = "";
  store.toggleExportDrawer(false);
}

function getStatusBadgeClass(status) {
  switch (status) {
    case "COMPLETED":
      return "badge-completed";
    case "PROCESSING":
      return "badge-processing";
    case "QUEUED":
      return "badge-queued";
    case "FAILED":
      return "badge-failed";
    default:
      return "badge-default";
  }
}

function formatDate(dt) {
  if (!dt) return "";
  const d = new Date(dt);
  return d.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}
</script>

<style scoped src="../shared/drawerHeaderChrome.css"></style>

<style scoped>
.drawer-subtitle {
  margin: 0;
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-secondary);
}

.btn-close-drawer:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.drawer-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
  font-variant-numeric: tabular-nums;
}

.active-job-card {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 14px;
  min-width: 0;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
}

.card-header-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  min-width: 0;
}

.job-id-tag {
  font-weight: 700;
  font-size: var(--font-size-xs);
  background: var(--semantic-color-surface-header);
  padding: 2px 6px;
  border-radius: 4px;
  color: var(--semantic-color-text);
}

.study-id-text {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
}

.status-badge {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 12px;
  letter-spacing: 0.2px;
}

.status-badge-sm {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 8px;
}

.badge-completed {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.badge-processing {
  background: var(--semantic-color-info-light);
  color: var(--semantic-color-interactive-primary);
}

.badge-queued {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}

.badge-failed {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.badge-default {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.progress-container {
  margin-top: 14px;
}

.progress-info {
  display: flex;
  justify-content: space-between;
  font-size: var(--font-size-2xs);
  font-weight: 600;
  margin-bottom: 6px;
  color: var(--semantic-color-text-body);
}

.progress-bar-track {
  height: 10px;
  background: var(--semantic-color-surface-header);
  border-radius: 5px;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  background: var(--semantic-color-interactive-primary);
  transition: width 0.3s ease;
}

.progress-bar-fill.is-animated {
  animation: progressPulse 1.5s infinite linear;
}

@keyframes progressPulse {
  0% {
    opacity: 1;
  }
  50% {
    opacity: 0.7;
  }
  100% {
    opacity: 1;
  }
}

.progress-details {
  display: flex;
  justify-content: space-between;
  gap: 6px;
  flex-wrap: wrap;
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
  margin-top: 6px;
}

.completion-box {
  margin-top: 16px;
  padding-top: 14px;
  border-top: 1px dashed var(--semantic-color-border-control);
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.completion-message {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-success-dark);
  background: var(--semantic-color-success-bg-soft);
  padding: 8px 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-success-border-soft);
}

.inline-icon {
  flex-shrink: 0;
  margin-top: 2px;
}

.btn-download {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-height: 44px;
  background: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
  font-weight: 600;
  font-size: var(--font-size-xs);
  padding: 10px 16px;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  text-decoration: none;
  transition: background-color 0.2s;
}

.btn-download:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-download:hover:not(:disabled) {
  background: var(--semantic-color-success-accent);
}

.expiration-notice {
  margin: 0;
  display: flex;
  align-items: flex-start;
  gap: 6px;
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
  font-style: italic;
}

.error-box {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 10px 12px;
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
  font-size: var(--font-size-2xs);
  border-radius: 6px;
}

.empty-active-job {
  padding: 20px;
  text-align: center;
  background: var(--semantic-color-surface-muted);
  border: 1px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-secondary);
}

.history-section {
  border-top: 1px solid var(--semantic-color-border);
  padding-top: 16px;
}

.history-title {
  margin: 0 0 12px 0;
  font-size: var(--font-size-sm);
  font-weight: 700;
  color: var(--semantic-color-text);
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.history-item {
  padding: 10px 12px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  font-size: var(--font-size-2xs);
  min-width: 0;
}

.history-item.is-active {
  border-color: var(--semantic-color-interactive-primary);
  background: var(--semantic-color-info-bg);
}

.history-item-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 4px;
}

.history-job-id {
  font-family: var(--semantic-font-mono);
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.history-item-meta {
  display: flex;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 4px;
  color: var(--semantic-color-text-secondary);
  font-size: var(--font-size-2xs);
}

.history-item-action {
  margin-top: 8px;
  display: flex;
  justify-content: flex-end;
}

.history-download-link {
  background: transparent;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  min-height: 44px;
  padding: 6px 12px;
  cursor: pointer;
  color: var(--semantic-color-interactive-primary);
  font-weight: 600;
  text-decoration: none;
  font-size: var(--font-size-2xs);
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.history-download-link:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.history-download-link:hover:not(:disabled) {
  background: var(--semantic-color-surface-muted);
}

.empty-history-text {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
  font-style: italic;
}
</style>
