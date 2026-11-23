<template>
  <div
    v-if="sync.autoSyncSuspended"
    id="sync-suspended-banner"
    class="card error-state sync-suspended-card"
    role="alert"
  >
    <p class="sync-suspended-title">
      <AlertTriangle :size="18" aria-hidden="true" />
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
import { AlertTriangle } from "lucide-vue-next";

import { syncOfflineQueue } from "../features/sync.js";
import { useSyncStore } from "../stores/sync.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const sync = useSyncStore();

function triggerManualSync() {
  syncOfflineQueue(true, { autoDisburse: isDemoModeActive() });
}
</script>
