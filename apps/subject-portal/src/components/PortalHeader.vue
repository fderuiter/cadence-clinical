<template>
  <header class="portal-header">
    <div class="header-branding">
      <h1>
        My <span>{{ brandName }}</span>
      </h1>
      <p class="role-badge">Participant Companion Portal</p>
    </div>
    <div class="compliance-badges">
      <span class="badge badge-epro">Study Companion</span>
      <span class="badge badge-gxp">Private &amp; Encrypted</span>
      <span class="badge badge-gamp">Offline Ready</span>

      <!-- Persistent dynamic sync status button -->
      <button
        id="btn-global-sync-drawer"
        type="button"
        class="btn-sync-status"
        :class="{
          'has-queued': queuedSubmissionsCount > 0,
          'has-quarantined': quarantinedSubmissionsCount > 0,
          'sync-failed': isSyncFailed,
          'sync-retrying': sync.syncRetryInfo?.isRetrying,
        }"
        title="Toggle sync status drawer"
        @click="toggleSyncDrawer"
      >
        <span class="sync-icon" aria-hidden="true">{{
          quarantinedSubmissionsCount > 0 ? "⚠️" : "🔄"
        }}</span>
        <span
          v-if="queuedSubmissionsCount > 0"
          id="header-sync-badge"
          class="sync-badge"
          :class="{ 'sync-badge-quarantined': quarantinedSubmissionsCount > 0 }"
        >
          {{ queuedSubmissionsCount }}
        </span>
        <span class="sync-btn-label"
          ><span>{{ headerSyncBaseLabel }}</span
          ><span
            v-if="headerSyncCountdownSuffix"
            class="sync-btn-countdown"
            aria-hidden="true"
            style="font-variant-numeric: tabular-nums"
            >{{ headerSyncCountdownSuffix }}</span
          ></span
        >
      </button>

      <button
        v-if="sync.syncRetryInfo?.isRetrying || sync.syncUpdatesPaused"
        id="btn-header-pause-sync"
        type="button"
        class="btn-sync-status"
        :aria-pressed="sync.syncUpdatesPaused ? 'true' : 'false'"
        @click="toggleSyncUpdatesPaused"
      >
        {{ sync.syncUpdatesPaused ? "Resume updates" : "Pause updates" }}
      </button>
    </div>
  </header>
</template>

<script setup>
import { computed } from "vue";
import { storeToRefs } from "pinia";

import { toggleSyncUpdatesPaused } from "../features/sync-status.js";
import { useSyncStore } from "../stores/sync.js";

const sync = useSyncStore();
const { queuedSubmissionsCount, quarantinedSubmissionsCount, isSyncFailed } =
  storeToRefs(sync);
const { toggleSyncDrawer } = sync;

const brandName = computed(() => import.meta.env.VITE_BRAND_NAME || "Cadence");

const headerSyncBaseLabel = computed(() => {
  if (sync.syncRetryInfo?.isRetrying) {
    if (sync.syncUpdatesPaused) {
      return `Retry Paused (${sync.syncRetryInfo.attempt}/${sync.syncRetryInfo.maxRetries})`;
    }
    return `Retrying (${sync.syncRetryInfo.attempt}/${sync.syncRetryInfo.maxRetries})`;
  }
  if (quarantinedSubmissionsCount.value > 0) {
    return "Sync Status · Review Needed";
  }
  return "Sync Status";
});

const headerSyncCountdownSuffix = computed(() => {
  if (!sync.syncRetryInfo?.isRetrying) return "";
  const rem = sync.syncRetryInfo.remainingSeconds;
  return typeof rem === "number" && rem > 0 ? ` · ${rem}s` : "";
});
</script>
