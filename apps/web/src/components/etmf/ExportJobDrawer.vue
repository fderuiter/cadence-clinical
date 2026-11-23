<!-- apps/web/src/components/etmf/ExportJobDrawer.vue -->
<template>
  <div
    v-if="isOpen"
    class="export-drawer-overlay"
    role="presentation"
    @click.self="handleClose"
  >
    <div
      ref="drawerRef"
      class="export-drawer"
      role="dialog"
      aria-modal="true"
      aria-label="Study binder export progress"
      tabindex="-1"
    >
      <div class="drawer-header">
        <div class="header-title-group">
          <span class="header-icon" aria-hidden="true">📦</span>
          <div>
            <h3 class="drawer-title">eTMF Study Binder Export</h3>
            <p class="drawer-subtitle">
              Dual-mode synchronous streaming &amp; background async export jobs
            </p>
          </div>
        </div>
        <button
          class="btn-close-drawer"
          aria-label="Close export drawer"
          @click="handleClose"
        >
          ✕
        </button>
      </div>

      <div class="drawer-body">
        <div
          v-if="downloadError"
          id="export-download-error"
          class="error-box"
          role="alert"
        >
          ❌ {{ downloadError }}
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
            <div>
              <span class="job-id-tag">{{ activeJob.job_id }}</span>
              <span class="study-id-text">Study: {{ activeJob.study_id }}</span>
            </div>
            <span
              :class="getStatusBadgeClass(activeJob.status)"
              class="status-badge"
            >
              {{ activeJob.status }}
            </span>
          </div>

          <div class="progress-container" style="margin-top: 14px">
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
          </div>

          <!-- Download Action Area -->
          <div v-if="activeJob.status === 'COMPLETED'" class="completion-box">
            <div class="completion-message">
              <span
                >✅ ZIP package successfully generated with root
                <code>manifest.json</code> &amp;
                <code>manifest.csv</code>.</span
              >
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
              <span v-else>⬇️ Download Export Package (.zip)</span>
            </button>
            <p v-if="activeJob.expires_at" class="expiration-notice">
              ⏱️ Temporary download link expires at
              {{ formatDate(activeJob.expires_at) }} (24h validity).
            </p>
          </div>

          <!-- Failure Alert -->
          <div v-else-if="activeJob.status === 'FAILED'" class="error-box">
            ❌ Background export job failed. Please verify document storage
            connectivity or retry export.
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
              :class="{ 'is-active': activeJob &amp;&amp; activeJob.job_id === job.job_id }"
            >
              <div class="history-item-header">
                <span class="history-job-id">{{ job.job_id }}</span>
                <span
                  :class="getStatusBadgeClass(job.status)"
                  class="status-badge-sm"
                >
                  {{ job.status }}
                </span>
              </div>
              <div class="history-item-meta">
                <span>{{ job.total_documents }} documents</span>
                <span>{{ formatDate(job.created_at) }}</span>
              </div>
              <div
                v-if="
                  job.status === 'COMPLETED' &amp;&amp;
                  (job.download_url || job.job_id)
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
                  {{
                    downloadingJobId === job.job_id
                      ? "Downloading..."
                      : "Download .zip"
                  }}
                </button>
              </div>
            </div>
          </div>
          <p v-else class="empty-history-text">
            No previous export history found.
          </p>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from "vue";
import { useFocusTrap } from "ui";
import { useEtmfStore } from "../../stores/etmf.js";
import { useAuthStore } from "../../stores/auth.js";
import { useToastStore } from "../../stores/toasts.js";
import {
  canExportEtmf,
  ETMF_EXPORT_ROLE_REASON,
} from "../../canvas/authority.ts";

const store = useEtmfStore();
const authStore = useAuthStore();
const toastStore = useToastStore();
const drawerRef = ref(null);
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

useFocusTrap(drawerRef, {
  isOpen,
  onEscape: handleClose,
});

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

<style scoped>
.export-drawer-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.5);
  backdrop-filter: blur(2px);
  z-index: 1050;
  display: flex;
  justify-content: flex-end;
}

.export-drawer {
  width: 480px;
  max-width: 90vw;
  height: 100%;
  background: var(--semantic-color-surface);
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.drawer-header {
  padding: 16px 20px;
  background: var(--semantic-color-surface);
  border-bottom: 1px solid var(--semantic-color-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.header-title-group {
  display: flex;
  align-items: center;
  gap: 12px;
}

.header-icon {
  font-size: var(--font-size-2xl, 1.5rem);
}

.drawer-title {
  margin: 0;
  font-size: var(--font-size-md, 1rem);
  font-weight: 700;
  color: var(--semantic-color-text);
}

.drawer-subtitle {
  margin: 2px 0 0 0;
  font-size: var(--font-size-2xs, 0.6875rem);
  color: var(--semantic-color-text-secondary);
}

.btn-close-drawer {
  background: transparent;
  border: none;
  font-size: var(--font-size-lg, 1.125rem);
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}

.btn-close-drawer:hover {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.active-job-card {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 16px;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.job-id-tag {
  font-family: monospace;
  font-weight: 700;
  font-size: var(--font-size-xs, 0.8125rem);
  background: var(--semantic-color-surface-header);
  padding: 2px 6px;
  border-radius: 4px;
  color: var(--semantic-color-text);
}

.study-id-text {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  margin-left: 8px;
}

.status-badge {
  font-size: var(--font-size-2xs, 0.6875rem);
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 12px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.status-badge-sm {
  font-size: 0.625rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 8px;
  text-transform: uppercase;
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

.progress-info {
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
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
  background: linear-gradient(
    90deg,
    var(--semantic-color-interactive-primary),
    var(--semantic-color-interactive-primary),
    var(--semantic-color-interactive-primary)
  );
  background-size: 200% 100%;
  animation: progressPulse 1.5s infinite linear;
}

@keyframes progressPulse {
  0% {
    background-position: 200% 0;
  }
  100% {
    background-position: -200% 0;
  }
}

.progress-details {
  display: flex;
  justify-content: space-between;
  font-size: var(--font-size-2xs, 0.6875rem);
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
  font-size: 0.75rem;
  color: var(--semantic-color-success-dark);
  background: var(--semantic-color-success-bg-soft);
  padding: 8px 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-success-border-soft);
}

.btn-download {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
  font-weight: 600;
  font-size: var(--font-size-xs, 0.8125rem);
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

.btn-download:hover {
  background: var(--semantic-color-success);
}

.expiration-notice {
  margin: 0;
  font-size: var(--font-size-2xs, 0.6875rem);
  color: var(--semantic-color-text-secondary);
  font-style: italic;
}

.error-box {
  margin-top: 14px;
  padding: 10px 12px;
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
  font-size: 0.75rem;
  border-radius: 6px;
}

.empty-active-job {
  padding: 20px;
  text-align: center;
  background: var(--semantic-color-surface-muted);
  border: 1px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  font-size: var(--font-size-xs, 0.8125rem);
  color: var(--semantic-color-text-secondary);
}

.history-section {
  border-top: 1px solid var(--semantic-color-border);
  padding-top: 16px;
}

.history-title {
  margin: 0 0 12px 0;
  font-size: var(--font-size-sm, 0.875rem);
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
  font-size: 0.75rem;
}

.history-item.is-active {
  border-color: var(--semantic-color-interactive-primary);
  background: var(--semantic-color-info-bg);
}

.history-item-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 4px;
}

.history-job-id {
  font-family: monospace;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.history-item-meta {
  display: flex;
  justify-content: space-between;
  color: var(--semantic-color-text-secondary);
  font-size: var(--font-size-2xs, 0.6875rem);
}

.history-item-action {
  margin-top: 8px;
  text-align: right;
}

.history-download-link {
  background: transparent;
  border: none;
  padding: 0;
  cursor: pointer;
  color: var(--semantic-color-interactive-primary);
  font-weight: 600;
  text-decoration: none;
  font-size: var(--font-size-2xs, 0.6875rem);
}

.history-download-link:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.history-download-link:hover {
  text-decoration: underline;
}

.empty-history-text {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  font-style: italic;
}
</style>
