<template>
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <div v-if="isOpen" class="activity-feed-overlay" @click.self="$emit('close')">
    <div
      ref="drawerRef"
      class="activity-feed-drawer"
      role="dialog"
      aria-modal="true"
      aria-label="Document Activity Feed"
    >
      <!-- Header -->
      <div class="drawer-header">
        <div class="header-info">
          <span class="activity-header-icon">📜</span>
          <div>
            <h3 class="drawer-title">Document Activity Feed</h3>
            <p class="drawer-subtitle" :title="file?.filename || 'Document'">
              {{ file?.filename || "Document" }}
              <span v-if="file?.version_index" class="version-tag">
                v{{ file.version_index }}
              </span>
            </p>
          </div>
        </div>
        <button
          class="close-drawer-btn"
          aria-label="Close activity feed"
          @click="$emit('close')"
        >
          ✕
        </button>
      </div>

      <!-- Filter Ribbon -->
      <div class="filter-ribbon">
        <div class="filter-group">
          <label for="filter-event-type" class="filter-label">
            Event Type:
          </label>
          <select
            id="filter-event-type"
            v-model="selectedEventType"
            class="filter-select"
            @change="applyFilters"
          >
            <option value="">All Event Types ({{ total }})</option>
            <option
              v-for="opt in eventTypeOptions"
              :key="opt.value"
              :value="opt.value"
            >
              {{ opt.label }}
            </option>
          </select>
        </div>

        <div class="filter-group">
          <label for="filter-from-date" class="filter-label"> From: </label>
          <input
            id="filter-from-date"
            v-model="fromDate"
            type="date"
            class="filter-date"
            @change="applyFilters"
          />
        </div>

        <div class="filter-group">
          <label for="filter-to-date" class="filter-label"> To: </label>
          <input
            id="filter-to-date"
            v-model="toDate"
            type="date"
            class="filter-date"
            @change="applyFilters"
          />
        </div>

        <button
          v-if="hasActiveFilters"
          class="reset-filters-btn"
          title="Reset filters"
          @click="resetFilters"
        >
          Reset
        </button>
      </div>

      <!-- Compliance Notice Banner -->
      <div class="gxp-badge-strip">
        <span class="gxp-icon">🛡️</span>
        <span class="gxp-text">
          ICH E6(R2) & 21 CFR Part 11 Inspection-Ready Audit Trail.
          Chronological UTC timeline.
        </span>
      </div>

      <!-- Drawer Body: Timeline -->
      <div class="drawer-body">
        <!-- Loading State -->
        <div v-if="loading && entries.length === 0" class="loading-state">
          <div class="spinner" />
          <p>Retrieving immutable audit events...</p>
        </div>

        <!-- Error State -->
        <div v-else-if="error" class="error-state">
          <span class="error-icon">⚠️</span>
          <p>{{ error }}</p>
          <button class="retry-btn" @click="loadFeed(1)">Retry</button>
        </div>

        <!-- Empty State -->
        <div v-else-if="entries.length === 0" class="empty-state">
          <span class="empty-icon">📭</span>
          <h4>No Activity Events Found</h4>
          <p v-if="hasActiveFilters">
            Try adjusting your filters to see historical events.
          </p>
          <p v-else>No audit log records exist for this document yet.</p>
        </div>

        <!-- Timeline List -->
        <div v-else class="timeline-container">
          <div
            v-for="(entry, index) in entries"
            :key="`${entry.event_type}-${entry.timestamp}-${index}`"
            class="timeline-item"
            :class="`event-${entry.event_type.toLowerCase()}`"
          >
            <!-- Timeline Track Icon -->
            <div class="timeline-marker">
              <span class="marker-icon" :title="entry.event_type">
                {{ getEventIcon(entry.event_type) }}
              </span>
              <div
                v-if="index < entries.length - 1"
                class="timeline-connector"
              />
            </div>

            <!-- Timeline Content Card -->
            <div class="timeline-card">
              <div class="card-header">
                <div class="event-title-group">
                  <span
                    class="event-badge"
                    :class="getEventBadgeClass(entry.event_type)"
                  >
                    {{ formatEventTitle(entry.event_type) }}
                  </span>
                  <span class="event-actor">
                    by
                    <strong>{{
                      entry.actor_display_name || entry.actor_user_id
                    }}</strong>
                  </span>
                </div>
                <time
                  class="event-time"
                  :datetime="entry.timestamp"
                  :title="formatFullDate(entry.timestamp)"
                >
                  {{ formatRelativeTime(entry.timestamp) }}
                </time>
              </div>

              <!-- Detail preview or toggle -->
              <div v-if="hasDetail(entry.detail)" class="card-detail-section">
                <button
                  class="detail-toggle-btn"
                  :aria-expanded="isExpanded(index)"
                  @click="toggleExpand(index)"
                >
                  <span>{{
                    isExpanded(index) ? "▼ Hide Details" : "▶ Show Details"
                  }}</span>
                </button>

                <div v-if="isExpanded(index)" class="detail-content">
                  <!-- Formatted detail fields -->
                  <div v-if="entry.detail.hold_reason" class="detail-row">
                    <span class="detail-label">Hold Reason:</span>
                    <span class="detail-value text-danger">{{
                      entry.detail.hold_reason
                    }}</span>
                  </div>

                  <div v-if="entry.detail.permission_level" class="detail-row">
                    <span class="detail-label">Permission:</span>
                    <span class="detail-value">{{
                      entry.detail.permission_level
                    }}</span>
                  </div>

                  <div
                    v-if="entry.detail.granted_to_user_id"
                    class="detail-row"
                  >
                    <span class="detail-label">Granted To:</span>
                    <span class="detail-value font-mono">{{
                      entry.detail.granted_to_user_id
                    }}</span>
                  </div>

                  <div v-if="entry.detail.scope" class="detail-row">
                    <span class="detail-label">Scope:</span>
                    <span class="detail-value">{{ entry.detail.scope }}</span>
                  </div>

                  <div v-if="entry.detail.expires_at" class="detail-row">
                    <span class="detail-label">Expires:</span>
                    <span class="detail-value">{{
                      formatFullDate(entry.detail.expires_at)
                    }}</span>
                  </div>

                  <div v-if="entry.detail.scan_engine" class="detail-row">
                    <span class="detail-label">Engine:</span>
                    <span class="detail-value"
                      >{{ entry.detail.scan_engine }} ({{
                        entry.detail.status || "CLEAN"
                      }})</span
                    >
                  </div>

                  <div
                    v-if="entry.detail.threat_name"
                    class="detail-row text-danger"
                  >
                    <span class="detail-label">Threat:</span>
                    <span class="detail-value font-bold">{{
                      entry.detail.threat_name
                    }}</span>
                  </div>

                  <div
                    v-if="entry.detail.signature_manifestation"
                    class="detail-row signature-box"
                  >
                    <span class="detail-label">Part 11 Signature:</span>
                    <div class="sig-details">
                      <div>
                        Signer:
                        {{
                          entry.detail.signature_manifestation.printed_name ||
                          entry.detail.user_id
                        }}
                      </div>
                      <div>
                        Reason:
                        {{
                          entry.detail.signature_manifestation.meaning ||
                          "Approval"
                        }}
                      </div>
                      <div class="font-mono text-xs">
                        Sig:
                        {{
                          entry.detail.signature_manifestation.signature?.substring(
                            0,
                            32
                          )
                        }}...
                      </div>
                    </div>
                  </div>

                  <!-- Fallback raw key/values for any remaining fields -->
                  <div
                    v-if="getRawDetailKeys(entry.detail).length > 0"
                    class="raw-detail-grid"
                  >
                    <div
                      v-for="k in getRawDetailKeys(entry.detail)"
                      :key="k"
                      class="detail-row"
                    >
                      <span class="detail-label">{{ k }}:</span>
                      <span class="detail-value">{{
                        typeof entry.detail[k] === "object"
                          ? JSON.stringify(entry.detail[k])
                          : entry.detail[k]
                      }}</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Pagination / Load More -->
          <div v-if="hasMore" class="load-more-section">
            <button
              class="load-more-btn"
              :disabled="loadingMore"
              @click="loadMore"
            >
              <span v-if="loadingMore" class="btn-spinner" />
              {{
                loadingMore
                  ? "Loading..."
                  : `Load More Activities (${entries.length} of ${total})`
              }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import { useFileshareStore } from "../../stores/fileshare";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  file: {
    type: Object,
    default: null,
  },
});

const emit = defineEmits(["close"]);

const fileshareStore = useFileshareStore();

const loading = ref(false);
const loadingMore = ref(false);
const error = ref(null);
const entries = ref([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(25);

const selectedEventType = ref("");
const fromDate = ref("");
const toDate = ref("");
const expandedIndices = ref(new Set());

const eventTypeOptions = [
  { value: "FILE_UPLOADED", label: "File Uploaded" },
  { value: "VERSION_CREATED", label: "Revision / Version Created" },
  { value: "GRANT_CREATED", label: "Share Grant Delegated" },
  { value: "GRANT_REVOKED", label: "Share Grant Revoked" },
  { value: "GUEST_LINK_CREATED", label: "Guest Link Created" },
  { value: "GUEST_LINK_ACCESSED", label: "Guest Link Accessed" },
  { value: "GUEST_LINK_REVOKED", label: "Guest Link Revoked" },
  { value: "SCAN_COMPLETE", label: "Virus Scan Clean" },
  { value: "SCAN_QUARANTINED", label: "Virus Quarantined" },
  { value: "CONVERSION_COMPLETE", label: "Format Conversion Complete" },
  { value: "HOLD_SET", label: "Retention Hold Placed" },
  { value: "HOLD_CLEARED", label: "Retention Hold Removed" },
  { value: "DOCUMENT_APPROVED", label: "Document Approved (Part 11)" },
  { value: "DOCUMENT_REJECTED", label: "Document Rejected" },
  { value: "APPROVAL_RESCINDED", label: "Approval Rescinded" },
  { value: "VIEW_OPEN", label: "Document Viewed / Downloaded" },
  { value: "PLAY_START", label: "Video Playback Started" },
  { value: "PLAY_END", label: "Video Playback Ended" },
];

const hasActiveFilters = computed(() => {
  return Boolean(selectedEventType.value || fromDate.value || toDate.value);
});

const hasMore = computed(() => {
  return entries.value.length < total.value;
});

const handleKeyDown = (e) => {
  if (e.key === "Escape" && props.isOpen) {
    emit("close");
  }
};

onMounted(() => {
  window.addEventListener("keydown", handleKeyDown);
  if (props.isOpen && props.file?.id) {
    loadFeed(1);
  }
});

onUnmounted(() => {
  window.removeEventListener("keydown", handleKeyDown);
});

watch(
  () => props.isOpen,
  (open) => {
    if (open && props.file?.id) {
      expandedIndices.value.clear();
      loadFeed(1);
    }
  }
);

watch(
  () => props.file?.id,
  (newId) => {
    if (props.isOpen && newId) {
      expandedIndices.value.clear();
      loadFeed(1);
    }
  }
);

async function loadFeed(pageNum = 1) {
  if (!props.file?.id) return;
  if (pageNum === 1) {
    loading.value = true;
  } else {
    loadingMore.value = true;
  }
  error.value = null;

  try {
    const params = {
      page: pageNum,
      page_size: pageSize.value,
    };
    if (selectedEventType.value) {
      params.event_type = selectedEventType.value;
    }
    if (fromDate.value) {
      params.from_date = new Date(fromDate.value).toISOString();
    }
    if (toDate.value) {
      const endOfDay = new Date(toDate.value);
      endOfDay.setHours(23, 59, 59, 999);
      params.to_date = endOfDay.toISOString();
    }

    const data = await fileshareStore.fetchActivityFeed(props.file.id, params);
    if (pageNum === 1) {
      entries.value = data.entries || [];
    } else {
      entries.value = [...entries.value, ...(data.entries || [])];
    }
    total.value = data.total || entries.value.length;
    page.value = pageNum;
  } catch (err) {
    console.error("Failed to load activity feed:", err);
    error.value = err.message || "Failed to load activity feed.";
  } finally {
    loading.value = false;
    loadingMore.value = false;
  }
}

function loadMore() {
  if (!hasMore.value || loadingMore.value) return;
  loadFeed(page.value + 1);
}

function applyFilters() {
  expandedIndices.value.clear();
  loadFeed(1);
}

function resetFilters() {
  selectedEventType.value = "";
  fromDate.value = "";
  toDate.value = "";
  expandedIndices.value.clear();
  loadFeed(1);
}

function isExpanded(index) {
  return expandedIndices.value.has(index);
}

function toggleExpand(index) {
  if (expandedIndices.value.has(index)) {
    expandedIndices.value.delete(index);
  } else {
    expandedIndices.value.add(index);
  }
}

function hasDetail(detail) {
  if (!detail || typeof detail !== "object") return false;
  return Object.keys(detail).length > 0;
}

const HANDLED_DETAIL_KEYS = [
  "hold_reason",
  "permission_level",
  "granted_to_user_id",
  "scope",
  "expires_at",
  "scan_engine",
  "threat_name",
  "signature_manifestation",
];

function getRawDetailKeys(detail) {
  if (!detail) return [];
  return Object.keys(detail).filter((k) => !HANDLED_DETAIL_KEYS.includes(k));
}

function getEventIcon(eventType) {
  const norm = (eventType || "").toUpperCase();
  switch (norm) {
    case "FILE_UPLOADED":
      return "📄";
    case "VERSION_CREATED":
    case "REVISION_UPLOADED":
      return "🔄";
    case "GRANT_CREATED":
      return "👥";
    case "GRANT_REVOKED":
      return "🚫";
    case "GUEST_LINK_CREATED":
      return "🌐";
    case "GUEST_LINK_ACCESSED":
      return "👁️";
    case "GUEST_LINK_REVOKED":
      return "🔒";
    case "SCAN_COMPLETE":
      return "🛡️";
    case "SCAN_QUARANTINED":
      return "⚠️";
    case "CONVERSION_COMPLETE":
      return "📑";
    case "HOLD_SET":
      return "🛑";
    case "HOLD_CLEARED":
      return "🔓";
    case "DOCUMENT_APPROVED":
    case "FILE_APPROVED":
      return "✅";
    case "DOCUMENT_REJECTED":
    case "FILE_REJECTED":
      return "❌";
    case "APPROVAL_RESCINDED":
      return "↩️";
    case "VIEW_OPEN":
      return "👁️";
    case "PLAY_START":
      return "▶️";
    case "PLAY_END":
      return "⏹️";
    default:
      return "📜";
  }
}

function formatEventTitle(eventType) {
  const norm = (eventType || "").toUpperCase();
  const option = eventTypeOptions.find((o) => o.value === norm);
  if (option) return option.label;
  return norm
    .replace(/_/g, " ")
    .toLowerCase()
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

function getEventBadgeClass(eventType) {
  const norm = (eventType || "").toUpperCase();
  if (norm.includes("APPROVED") || norm === "SCAN_COMPLETE")
    return "badge-success";
  if (
    norm.includes("QUARANTINED") ||
    norm.includes("REJECTED") ||
    norm === "HOLD_SET"
  )
    return "badge-danger";
  if (norm.includes("REVOKED") || norm === "HOLD_CLEARED")
    return "badge-warning";
  if (norm.includes("GRANT") || norm.includes("GUEST")) return "badge-info";
  return "badge-neutral";
}

function formatFullDate(isoString) {
  if (!isoString) return "";
  try {
    const d = new Date(isoString);
    return `${d.toUTCString()} (${d.toLocaleString()})`;
  } catch {
    return isoString;
  }
}

function formatRelativeTime(isoString) {
  if (!isoString) return "";
  try {
    const d = new Date(isoString);
    const now = new Date();
    const diffSec = Math.floor((now - d) / 1000);

    if (diffSec < 60) return "Just now";
    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) return `${diffMin}m ago`;
    const diffHours = Math.floor(diffMin / 60);
    if (diffHours < 24) return `${diffHours}h ago`;
    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 30) return `${diffDays}d ago`;

    return d.toLocaleDateString(undefined, {
      month: "short",
      day: "numeric",
      year: "numeric",
    });
  } catch {
    return isoString;
  }
}
</script>

<style scoped>
.activity-feed-overlay {
  position: fixed;
  inset: 0;
  background-color: rgba(15, 23, 42, 0.65);
  backdrop-filter: blur(4px);
  z-index: 1000;
  display: flex;
  justify-content: flex-end;
  animation: fadeIn 0.2s ease-out;
}

.activity-feed-drawer {
  width: 100%;
  max-width: 580px;
  height: 100%;
  background: var(--semantic-color-surface);
  box-shadow: -8px 0 32px rgba(0, 0, 0, 0.2);
  display: flex;
  flex-direction: column;
  animation: slideInRight 0.25s cubic-bezier(0.16, 1, 0.3, 1);
  overflow: hidden;
}

@keyframes fadeIn {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

@keyframes slideInRight {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

/* Header */
.drawer-header {
  padding: 1.25rem 1.5rem;
  background: var(--semantic-color-surface-header);
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.header-info {
  display: flex;
  align-items: center;
  gap: 0.875rem;
  overflow: hidden;
}

.activity-header-icon {
  font-size: 1.75rem;
  background: var(--semantic-color-surface-header);
  width: 44px;
  height: 44px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--radius-md, 8px);
  border: 1px solid var(--border);
  flex-shrink: 0;
}

.drawer-title {
  margin: 0;
  font-size: 1.15rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.drawer-subtitle {
  margin: 0.2rem 0 0 0;
  font-size: 0.825rem;
  color: var(--semantic-color-text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 380px;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.version-tag {
  display: inline-block;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-size: 0.725rem;
  font-weight: 700;
  padding: 0.1rem 0.4rem;
  border-radius: 4px;
}

.close-drawer-btn {
  background: transparent;
  border: 1px solid transparent;
  color: var(--semantic-color-text-secondary);
  font-size: 1.1rem;
  cursor: pointer;
  padding: 0.4rem 0.6rem;
  border-radius: var(--radius-sm, 6px);
  transition: all 0.15s ease;
}

.close-drawer-btn:hover {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

/* Filter Ribbon */
.filter-ribbon {
  padding: 0.75rem 1.5rem;
  background: var(--semantic-color-surface);
  border-bottom: 1px solid var(--border);
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: center;
}

.filter-group {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.filter-label {
  font-size: 0.775rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.filter-select,
.filter-date {
  padding: 0.35rem 0.6rem;
  font-size: 0.8rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  outline: none;
}

.filter-select:focus,
.filter-date:focus {
  border-color: var(--primary);
  box-shadow: 0 0 0 2px rgba(37, 99, 235, 0.15);
}

.reset-filters-btn {
  padding: 0.35rem 0.65rem;
  font-size: 0.775rem;
  font-weight: 600;
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  transition: all 0.15s ease;
}

.reset-filters-btn:hover {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

/* Compliance Badge */
.gxp-badge-strip {
  padding: 0.5rem 1.5rem;
  background: var(--semantic-color-success-bg-soft);
  border-bottom: 1px solid var(--semantic-color-success-border-soft);
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.gxp-icon {
  font-size: 0.95rem;
}

.gxp-text {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-success-dark);
}

/* Drawer Body */
.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 1.5rem;
  background: var(--semantic-color-surface-muted);
}

/* Loading, Error, Empty */
.loading-state,
.error-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 3rem 1.5rem;
  text-align: center;
  color: var(--semantic-color-text-secondary);
}

.spinner {
  width: 32px;
  height: 32px;
  border: 3px solid var(--semantic-color-border);
  border-top-color: var(--primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
  margin-bottom: 1rem;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.error-icon,
.empty-icon {
  font-size: 2.5rem;
  margin-bottom: 0.75rem;
}

.retry-btn {
  margin-top: 1rem;
  padding: 0.45rem 1rem;
  background: var(--primary);
  color: var(--semantic-color-surface);
  border: none;
  border-radius: var(--radius-sm, 6px);
  cursor: pointer;
}

/* Timeline */
.timeline-container {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.timeline-item {
  display: flex;
  gap: 1rem;
  position: relative;
}

.timeline-marker {
  display: flex;
  flex-direction: column;
  align-items: center;
  width: 36px;
  flex-shrink: 0;
}

.marker-icon {
  width: 32px;
  height: 32px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1rem;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.04);
  z-index: 2;
}

.timeline-connector {
  width: 2px;
  flex: 1;
  background: var(--border);
  margin-top: 4px;
  margin-bottom: -12px;
}

.timeline-card {
  flex: 1;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  padding: 0.875rem 1rem;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 0.5rem;
}

.event-title-group {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.event-badge {
  font-size: 0.775rem;
  font-weight: 700;
  padding: 0.15rem 0.5rem;
  border-radius: 4px;
  display: inline-block;
  align-self: flex-start;
}

.badge-success {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.badge-danger {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.badge-warning {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}

.badge-info {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.badge-neutral {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
}

.event-actor {
  font-size: 0.825rem;
  color: var(--semantic-color-text-secondary);
}

.event-actor strong {
  color: var(--semantic-color-text);
}

.event-time {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  white-space: nowrap;
}

/* Detail Section */
.card-detail-section {
  margin-top: 0.75rem;
  padding-top: 0.5rem;
  border-top: 1px dashed var(--border);
}

.detail-toggle-btn {
  background: transparent;
  border: none;
  font-size: 0.775rem;
  color: var(--primary);
  cursor: pointer;
  padding: 0;
  font-weight: 600;
}

.detail-toggle-btn:hover {
  text-decoration: underline;
}

.detail-content {
  margin-top: 0.5rem;
  padding: 0.6rem 0.75rem;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  font-size: 0.8rem;
}

.detail-row {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.2rem 0;
}

.detail-label {
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.detail-value {
  color: var(--semantic-color-text);
  text-align: right;
  word-break: break-word;
}

.signature-box {
  flex-direction: column;
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
  border-radius: 4px;
  padding: 0.5rem;
  margin-top: 0.25rem;
}

.sig-details {
  font-size: 0.75rem;
  color: var(--semantic-color-success-dark);
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.font-mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}

.text-danger {
  color: var(--semantic-color-error);
}

.text-xs {
  font-size: 0.7rem;
}

.load-more-section {
  display: flex;
  justify-content: center;
  margin-top: 1rem;
}

.load-more-btn {
  padding: 0.5rem 1.25rem;
  font-size: 0.825rem;
  font-weight: 600;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  color: var(--primary);
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  transition: all 0.15s ease;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
}

.load-more-btn:hover:not(:disabled) {
  background: var(--semantic-color-surface-muted);
  border-color: var(--primary);
}

.load-more-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-spinner {
  width: 14px;
  height: 14px;
  border: 2px solid var(--semantic-color-border-control);
  border-top-color: var(--primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
</style>
