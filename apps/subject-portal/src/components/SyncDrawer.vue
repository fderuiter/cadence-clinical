<template>
  <!-- Global Floating Sync Drawer -->
  <div
    class="global-drawer-overlay"
    :class="{ active: sync.isSyncDrawerOpen }"
    role="button"
    tabindex="0"
    aria-label="Close sync drawer overlay"
    @click="toggleSyncDrawer"
    @keydown.enter.prevent="toggleSyncDrawer"
    @keydown.space.prevent="toggleSyncDrawer"
  />

  <div
    class="global-drawer"
    :class="{ active: sync.isSyncDrawerOpen }"
    role="dialog"
    aria-modal="true"
    aria-labelledby="drawer-title"
  >
    <div class="drawer-header">
      <h3 id="drawer-title">🔄 Submission Sync Status</h3>
      <button
        id="btn-close-sync-drawer"
        type="button"
        class="btn-close-drawer"
        aria-label="Close drawer"
        @click="toggleSyncDrawer"
      >
        ×
      </button>
    </div>

    <div class="drawer-body">
      <div class="sync-status-section">
        <p class="status-text" role="status" aria-live="polite">
          {{ sync.syncStatusText || "Checking sync status..." }}
        </p>
        <button
          id="btn-drawer-sync-now"
          type="button"
          class="btn btn-primary btn-sync-trigger"
          style="width: 100%; margin-top: 8px"
          @click="triggerManualSync"
        >
          Sync Now
        </button>
      </div>

      <div class="submissions-list-wrapper">
        <h4 class="submissions-heading">Submissions Status</h4>
        <div
          v-if="!sync.submissions || sync.submissions.length === 0"
          class="empty-state"
        >
          No submission history in queue.
        </div>
        <div v-else class="submissions-items-list">
          <div
            v-for="item in sync.submissions"
            :key="item.sequence_number"
            class="drawer-submission-item"
            :class="getSubmissionClass(item)"
          >
            <div class="item-header-row">
              <span class="item-name">{{
                getInstrumentName(item.diary_id)
              }}</span>
              <span class="status-pill" :class="getBadgeClass(item)">
                {{ getStatusLabel(item) }}
              </span>
            </div>
            <div class="item-time">
              Seq: #{{ item.sequence_number }} | Device Time:
              {{ formatTime(item.device_timestamp) }}
            </div>
            <div class="item-details">
              <p class="item-desc">
                {{ getStatusDescription(item) }}
              </p>
              <div class="item-data">
                <strong>Local Answers:</strong>
                <code class="data-code">{{
                  JSON.stringify(item.answers)
                }}</code>
              </div>
              <div
                v-if="item.resolved_answers && item.status === 'MERGED'"
                class="item-data merged-data"
              >
                <strong>Merged Result:</strong>
                <code class="data-code">{{
                  JSON.stringify(item.resolved_answers)
                }}</code>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { syncOfflineQueue } from "../features/sync.js";
import { describeQueuedSync } from "../features/sync-status.js";
import { useSyncStore } from "../stores/sync.js";
import { useTasksStore } from "../stores/tasks.js";

const sync = useSyncStore();
const tasks = useTasksStore();
const { toggleSyncDrawer } = sync;

function triggerManualSync() {
  syncOfflineQueue();
}

function getInstrumentName(diaryId) {
  const fallback = {
    inst_daily_diary: "Daily Health & Vital Diary",
    inst_weekly_symptoms: "Weekly Symptoms & eCOA Checklist",
  };
  return (
    (tasks.instruments && tasks.instruments[diaryId]?.name) ||
    fallback[diaryId] ||
    diaryId
  );
}

function formatTime(isoString) {
  if (!isoString) return "";
  return new Date(isoString).toLocaleString();
}

function getSubmissionClass(item) {
  return {
    "submission-queued": item.status === "QUEUED",
    "submission-synced":
      item.status === "CREATED" || item.status === "UPDATED_CLIENT_WINS",
    "submission-merged": item.status === "MERGED",
    "submission-ignored": item.status === "IGNORED_SERVER_WINS",
    "submission-quarantined": item.status === "QUARANTINED",
    "submission-error":
      item.status === "DECRYPTION_ERROR" || item.status === "QUARANTINED",
  };
}

function getBadgeClass(item) {
  if (item.status === "QUEUED") return "pending";
  if (
    item.status === "CREATED" ||
    item.status === "UPDATED_CLIENT_WINS" ||
    item.status === "MERGED"
  )
    return "completed";
  if (
    item.status === "IGNORED_SERVER_WINS" ||
    item.status === "DECRYPTION_ERROR" ||
    item.status === "QUARANTINED"
  )
    return "overdue";
  return "pending";
}

function getStatusLabel(item) {
  if (item.status === "QUEUED") return "QUEUED";
  if (item.status === "CREATED" || item.status === "UPDATED_CLIENT_WINS")
    return "SYNCED";
  if (item.status === "MERGED") return "MERGED";
  if (item.status === "IGNORED_SERVER_WINS") return "CONFLICT (Ignored)";
  if (item.status === "QUARANTINED") return "QUARANTINED";
  return item.status;
}

function getStatusDescription(item) {
  if (item.status === "QUEUED") {
    return describeQueuedSync(sync.syncRetryInfo, item);
  } else if (
    item.status === "CREATED" ||
    item.status === "UPDATED_CLIENT_WINS"
  ) {
    return "Successfully synchronized with clinical database.";
  } else if (item.status === "MERGED") {
    return "Conflict resolved: Local and server entries were combined.";
  } else if (item.status === "IGNORED_SERVER_WINS") {
    return "Conflict resolved: Server data was preserved; local entry archived.";
  } else if (item.status === "QUARANTINED") {
    return "Quarantined: Under review by clinical trial managers due to validation/version mismatch errors.";
  } else if (item.status === "DECRYPTION_ERROR") {
    return item.error || "Decryption failed: Secure key cleared.";
  }
  return "";
}
</script>
