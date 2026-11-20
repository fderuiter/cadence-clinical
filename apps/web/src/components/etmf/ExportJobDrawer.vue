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
            <a
              v-if="activeJob.download_url"
              :href="activeJob.download_url"
              download
              class="btn-download"
            >
              ⬇️ Download Export Package (.zip)
            </a>
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
                v-if="job.status === 'COMPLETED' &amp;&amp; job.download_url"
                class="history-item-action"
              >
                <a
                  :href="job.download_url"
                  download
                  class="history-download-link"
                >
                  Download .zip
                </a>
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

const store = useEtmfStore();
const drawerRef = ref(null);

const isOpen = computed(() => store.isExportDrawerOpen);
const activeJob = computed(() => store.activeExportJob);
const exportJobs = computed(() => store.exportJobs);

function handleClose() {
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
  background: #ffffff;
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.drawer-header {
  padding: 16px 20px;
  background: var(--bg-surface, #f8fafc);
  border-bottom: 1px solid var(--border-color, #e2e8f0);
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
  font-size: 24px;
}

.drawer-title {
  margin: 0;
  font-size: 16px;
  font-weight: 700;
  color: #0f172a;
}

.drawer-subtitle {
  margin: 2px 0 0 0;
  font-size: 11px;
  color: #64748b;
}

.btn-close-drawer {
  background: transparent;
  border: none;
  font-size: 18px;
  color: #64748b;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}

.btn-close-drawer:hover {
  background: #e2e8f0;
  color: #0f172a;
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
  background: #f8fafc;
  border: 1px solid #cbd5e1;
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
  font-size: 13px;
  background: #e2e8f0;
  padding: 2px 6px;
  border-radius: 4px;
  color: #0f172a;
}

.study-id-text {
  font-size: 12px;
  color: #64748b;
  margin-left: 8px;
}

.status-badge {
  font-size: 11px;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 12px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.status-badge-sm {
  font-size: 10px;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 8px;
  text-transform: uppercase;
}

.badge-completed {
  background: #dcfce7;
  color: #15803d;
}

.badge-processing {
  background: #dbeafe;
  color: #1d4ed8;
}

.badge-queued {
  background: #fef3c7;
  color: #b45309;
}

.badge-failed {
  background: #fee2e2;
  color: #b91c1c;
}

.badge-default {
  background: #f1f5f9;
  color: #475569;
}

.progress-info {
  display: flex;
  justify-content: space-between;
  font-size: 12px;
  font-weight: 600;
  margin-bottom: 6px;
  color: #334155;
}

.progress-bar-track {
  height: 10px;
  background: #e2e8f0;
  border-radius: 5px;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  background: #2563eb;
  transition: width 0.3s ease;
}

.progress-bar-fill.is-animated {
  background: linear-gradient(90deg, #2563eb, #3b82f6, #2563eb);
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
  font-size: 11px;
  color: #64748b;
  margin-top: 6px;
}

.completion-box {
  margin-top: 16px;
  padding-top: 14px;
  border-top: 1px dashed #cbd5e1;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.completion-message {
  font-size: 12px;
  color: #166534;
  background: #f0fdf4;
  padding: 8px 12px;
  border-radius: 6px;
  border: 1px solid #bbf7d0;
}

.btn-download {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: #16a34a;
  color: #ffffff;
  font-weight: 600;
  font-size: 13px;
  padding: 10px 16px;
  border-radius: 6px;
  text-decoration: none;
  transition: background-color 0.2s;
}

.btn-download:hover {
  background: #15803d;
}

.expiration-notice {
  margin: 0;
  font-size: 11px;
  color: #64748b;
  font-style: italic;
}

.error-box {
  margin-top: 14px;
  padding: 10px 12px;
  background: #fef2f2;
  border: 1px solid #fecaca;
  color: #991b1b;
  font-size: 12px;
  border-radius: 6px;
}

.empty-active-job {
  padding: 20px;
  text-align: center;
  background: #f8fafc;
  border: 1px dashed #cbd5e1;
  border-radius: 8px;
  font-size: 13px;
  color: #64748b;
}

.history-section {
  border-top: 1px solid #e2e8f0;
  padding-top: 16px;
}

.history-title {
  margin: 0 0 12px 0;
  font-size: 14px;
  font-weight: 700;
  color: #0f172a;
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.history-item {
  padding: 10px 12px;
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  font-size: 12px;
}

.history-item.is-active {
  border-color: #2563eb;
  background: #eff6ff;
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
  color: #1e293b;
}

.history-item-meta {
  display: flex;
  justify-content: space-between;
  color: #64748b;
  font-size: 11px;
}

.history-item-action {
  margin-top: 8px;
  text-align: right;
}

.history-download-link {
  color: #2563eb;
  font-weight: 600;
  text-decoration: none;
  font-size: 11px;
}

.history-download-link:hover {
  text-decoration: underline;
}

.empty-history-text {
  font-size: 12px;
  color: #94a3b8;
  font-style: italic;
}
</style>
