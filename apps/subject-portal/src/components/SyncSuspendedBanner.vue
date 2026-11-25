<template>
  <div
    v-if="sync.autoSyncSuspended"
    id="sync-suspended-banner"
    class="card error-state sync-suspended-card"
    role="alert"
  >
    <p class="sync-suspended-title">⚠️ Survey Updated by Your Study Team</p>
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
import { isAuthenticatedSession } from "../api/client.js";
import { useSyncStore } from "../stores/sync.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const sync = useSyncStore();

async function triggerManualSync() {
  const { syncOfflineQueue } = await import("../features/sync.js");
  await syncOfflineQueue(true, {
    autoDisburse: isAuthenticatedSession() || isDemoModeActive(),
  });
}
</script>
