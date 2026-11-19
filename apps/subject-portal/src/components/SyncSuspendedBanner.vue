<template>
  <div
    v-if="sync.autoSyncSuspended"
    id="sync-suspended-banner"
    class="card error-state sync-suspended-card"
    role="alert"
  >
    <p class="sync-suspended-title">⚠️ Outdated Form Structure Detected</p>
    <p class="sync-suspended-desc">
      A study survey definition was updated on the clinical server. Automatic
      background sync is paused until you refresh active survey structures.
    </p>
    <button
      id="btn-alert-sync-now"
      type="button"
      class="btn btn-primary"
      @click="triggerManualSync"
    >
      Manual Sync / Refresh
    </button>
  </div>
</template>

<script setup>
import { syncOfflineQueue } from "../features/sync.js";
import { useSyncStore } from "../stores/sync.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const sync = useSyncStore();

function triggerManualSync() {
  syncOfflineQueue(true, { autoDisburse: isDemoModeActive() });
}
</script>
