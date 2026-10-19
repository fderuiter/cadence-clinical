<template>
  <div
    v-if="sync.autoSyncSuspended"
    class="card error-state"
    style="
      border: 1px solid var(--danger);
      background-color: var(--semantic-color-error-bg);
      color: var(--semantic-color-error);
      padding: 16px;
      margin-bottom: 24px;
      text-align: center;
    "
  >
    <p style="font-weight: bold; font-size: 16px; margin: 0 0 8px 0">
      ⚠️ Outdated Form Structure Detected
    </p>
    <p style="font-size: 14px; margin: 0 0 12px 0">
      The questionnaire version index does not match the active server
      structure. Automatic sync is suspended. Please perform a manual refresh.
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

const sync = useSyncStore();

function triggerManualSync() {
  syncOfflineQueue();
}
</script>
