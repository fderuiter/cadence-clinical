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
        <h4 class="submissions-heading">Saved &amp; Sent Surveys</h4>
        <div
          v-if="!sync.submissions || sync.submissions.length === 0"
          class="empty-state"
        >
          No saved surveys waiting to send.
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
                getInstrumentDisplayName(item.diary_id)
              }}</span>
              <span class="status-pill" :class="getSyncStatusBadgeClass(item)">
                {{ getSyncStatusLabel(item) }}
              </span>
            </div>
            <div class="item-time">
              Saved {{ formatTime(item.device_timestamp) }}
            </div>
            <div class="item-details">
              <p class="item-desc">
                {{ getStatusDescription(item) }}
              </p>
              <div
                v-if="isQuarantinedSubmission(item)"
                class="quarantine-guidance"
                role="note"
              >
                <p class="quarantine-next-steps">
                  {{ COORDINATOR_QUARANTINE_STEPS }}
                </p>
              </div>
              <div v-if="recordedPairs(item).length > 0" class="item-data">
                <strong>Your answers:</strong>
                <ul class="answer-summary-list">
                  <li
                    v-for="pair in recordedPairs(item)"
                    :key="pair.key"
                    class="answer-summary-row"
                  >
                    <span class="answer-question">{{ pair.label }}:</span>
                    <span class="answer-value">{{ pair.value }}</span>
                  </li>
                </ul>
              </div>
              <div
                v-if="mergedPairs(item).length > 0"
                class="item-data merged-data"
              >
                <strong>Combined answers:</strong>
                <ul class="answer-summary-list">
                  <li
                    v-for="pair in mergedPairs(item)"
                    :key="pair.key"
                    class="answer-summary-row"
                  >
                    <span class="answer-question">{{ pair.label }}:</span>
                    <span class="answer-value">{{ pair.value }}</span>
                  </li>
                </ul>
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
import { ref, watch } from "vue";
import { getFocusableElements, useFocusTrap } from "ui";

import {
  COORDINATOR_QUARANTINE_STEPS,
  formatAnswerPairs,
  getInstrumentDisplayName,
  getSyncStatusBadgeClass,
  getSyncStatusExplanation,
  getSyncStatusLabel,
  isQuarantinedSubmission,
} from "../features/site-support.js";
import { syncOfflineQueue } from "../features/sync.js";
import { describeQueuedSync } from "../features/sync-status.js";
import { useSyncStore } from "../stores/sync.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import SiteContactLinks from "./SiteContactLinks.vue";

const drawerRef = ref(null);
const sync = useSyncStore();
const { toggleSyncDrawer } = sync;

useFocusTrap(drawerRef, {
  isOpen: () => sync.isSyncDrawerOpen,
  onEscape: () => {
    if (sync.isSyncDrawerOpen) {
      toggleSyncDrawer();
    }
  },
});

watch(
  () => sync.isSyncDrawerOpen,
  (open) => {
    if (!open || !drawerRef.value || typeof document === "undefined") return;
    if (!drawerRef.value.contains(document.activeElement)) {
      const target =
        getFocusableElements(drawerRef.value)[0] || drawerRef.value;
      target.focus();
    }
  },
  { flush: "post" }
);

function triggerManualSync() {
  syncOfflineQueue(true, { autoDisburse: isDemoModeActive() });
}

function recordedPairs(item) {
  if (item.status === "DECRYPTION_ERROR" || isQuarantinedSubmission(item)) {
    return [];
  }
  return formatAnswerPairs(item.answers, item.diary_id);
}

function mergedPairs(item) {
  if (!item.resolved_answers || item.status !== "MERGED") {
    return [];
  }
  return formatAnswerPairs(item.resolved_answers, item.diary_id);
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
    "submission-quarantined": isQuarantinedSubmission(item),
    "submission-error":
      item.status === "DECRYPTION_ERROR" || isQuarantinedSubmission(item),
  };
}

function getStatusDescription(item) {
  return getSyncStatusExplanation(
    item,
    describeQueuedSync(sync.syncRetryInfo, item)
  );
}
</script>

<style scoped>
#sync-drawer-retry-countdown,
.status-text,
.item-time,
.answer-value {
  font-variant-numeric: tabular-nums;
}

.answer-value {
  font-family: var(--semantic-font-mono);
}
</style>
