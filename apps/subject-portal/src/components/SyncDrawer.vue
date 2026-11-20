<template>
  <!-- Global Floating Sync Drawer -->
  <div
    class="global-drawer-overlay"
    :class="{ active: sync.isSyncDrawerOpen }"
    role="presentation"
    aria-hidden="true"
    :inert="!sync.isSyncDrawerOpen || undefined"
    @click="toggleSyncDrawer"
  />

  <div
    id="global-sync-drawer"
    ref="drawerRef"
    class="global-drawer"
    :class="{ active: sync.isSyncDrawerOpen }"
    role="dialog"
    :aria-modal="sync.isSyncDrawerOpen ? 'true' : undefined"
    :aria-hidden="!sync.isSyncDrawerOpen ? 'true' : undefined"
    :inert="!sync.isSyncDrawerOpen || undefined"
    aria-labelledby="drawer-title"
    tabindex="-1"
  >
    <div class="drawer-header">
      <h3 id="drawer-title">🔄 Submission Sync Status</h3>
      <button
        id="btn-close-sync-drawer"
        type="button"
        class="btn-close-drawer touch-target-interactive"
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
        <p
          v-if="
            sync.syncRetryInfo?.isRetrying &&
            sync.syncRetryInfo.remainingSeconds > 0
          "
          id="sync-drawer-retry-countdown"
          class="retry-countdown-banner"
          aria-live="polite"
        >
          Next automatic retry in
          <strong>{{ sync.syncRetryInfo.remainingSeconds }}s</strong> (Attempt
          {{ sync.syncRetryInfo.attempt }} of
          {{ sync.syncRetryInfo.maxRetries }})
        </p>
        <button
          id="btn-drawer-sync-now"
          type="button"
          class="btn btn-primary btn-sync-trigger drawer-sync-btn"
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
              <div
                v-if="isQuarantinedItem(item)"
                class="quarantine-guidance"
                role="note"
              >
                <p class="quarantine-next-steps">
                  {{ COORDINATOR_QUARANTINE_STEPS }}
                </p>
              </div>
              <div v-if="shouldShowAnswersSummary(item)" class="item-data">
                <strong>Recorded Values:</strong>
                <code class="data-code">{{
                  formatAnswersSummary(item.answers)
                }}</code>
              </div>
              <div
                v-if="
                  item.resolved_answers &&
                  item.status === 'MERGED' &&
                  formatAnswersSummary(item.resolved_answers)
                "
                class="item-data merged-data"
              >
                <strong>Merged Result:</strong>
                <code class="data-code">{{
                  formatAnswersSummary(item.resolved_answers)
                }}</code>
              </div>
            </div>
          </div>
        </div>
      </div>

      <SiteContactLinks
        id="sync-drawer-site-contacts"
        heading="Study Site Support"
        region-label="Sync drawer study site coordinator contacts"
      />
    </div>
  </div>
</template>

<script setup>
import { ref } from "vue";
import { useFocusTrap } from "ui";

import {
  COORDINATOR_QUARANTINE_STEPS,
  formatAnswersSummary,
  humanizeValidationError,
} from "../features/site-support.js";
import { syncOfflineQueue } from "../features/sync.js";
import { describeQueuedSync } from "../features/sync-status.js";
import { useSyncStore } from "../stores/sync.js";
import { useTasksStore } from "../stores/tasks.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import SiteContactLinks from "./SiteContactLinks.vue";

const drawerRef = ref(null);
const sync = useSyncStore();
const tasks = useTasksStore();
const { toggleSyncDrawer } = sync;

useFocusTrap(drawerRef, {
  isOpen: () => sync.isSyncDrawerOpen,
  onEscape: () => {
    if (sync.isSyncDrawerOpen) {
      toggleSyncDrawer();
    }
  },
});

function triggerManualSync() {
  syncOfflineQueue(true, { autoDisburse: isDemoModeActive() });
}

function isQuarantinedItem(item) {
  return item.status === "QUARANTINED" || item.status === "LOCAL_QUARANTINED";
}

function shouldShowAnswersSummary(item) {
  if (
    item.status === "DECRYPTION_ERROR" ||
    item.status === "QUARANTINED" ||
    item.status === "LOCAL_QUARANTINED"
  ) {
    return false;
  }
  return Boolean(formatAnswersSummary(item.answers));
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
    "submission-quarantined": isQuarantinedItem(item),
    "submission-error":
      item.status === "DECRYPTION_ERROR" || isQuarantinedItem(item),
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
    isQuarantinedItem(item)
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
  if (isQuarantinedItem(item)) return "QUARANTINED";
  if (item.status === "DECRYPTION_ERROR" && item.unrecoverable)
    return "LOCKED (RESET)";
  return item.status;
}

function getStatusDescription(item) {
  if (item.status === "QUEUED") {
    return describeQueuedSync(sync.syncRetryInfo, item);
  }
  if (item.status === "CREATED" || item.status === "UPDATED_CLIENT_WINS") {
    return "Successfully synchronized with clinical database.";
  }
  if (item.status === "MERGED") {
    return "Conflict resolved: Local and server entries were combined.";
  }
  if (item.status === "IGNORED_SERVER_WINS") {
    return "Conflict resolved: Server data was preserved; local entry archived.";
  }
  if (isQuarantinedItem(item)) {
    return humanizeValidationError(item.error);
  }
  if (item.status === "DECRYPTION_ERROR") {
    if (item.unrecoverable) {
      return humanizeValidationError(item.error);
    }
    return "Locked — enter your security PIN to decrypt queued answers.";
  }
  return "";
}
</script>
