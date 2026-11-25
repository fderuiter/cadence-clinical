<template>
  <div
    v-if="sync.autoSyncSuspended"
    id="sync-suspended-banner"
    class="card error-state sync-suspended-card"
    role="alert"
  >
    <p
      class="sync-suspended-title"
      style="display: flex; align-items: center; gap: 8px"
    >
      <ClinicalIcon name="alert-triangle" :size="16" />
      <span>Survey Updated by Your Study Team</span>
    </p>
    <p class="sync-suspended-desc">
      One of your study surveys has a newer version available. Refresh your
      surveys so your saved answers can finish sending.
    </p>
    <button
      id="btn-alert-sync-now"
      type="button"
      class="btn btn-primary"
      @click="triggerManualSync"
    >
      Refresh Surveys
    </button>
  </div>
</template>

<script setup>
import { ClinicalIcon } from "ui";
import { syncOfflineQueue } from "../features/sync.js";
import { useSyncStore } from "../stores/sync.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const sync = useSyncStore();

function triggerManualSync() {
  syncOfflineQueue(true, { autoDisburse: isDemoModeActive() });
}
</script>
