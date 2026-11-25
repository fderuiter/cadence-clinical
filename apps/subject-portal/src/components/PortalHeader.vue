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
          class="sync-badge tabular-nums"
          :class="{ 'sync-badge-quarantined': quarantinedSubmissionsCount > 0 }"
          aria-live="polite"
          aria-atomic="true"
        >
          {{ queuedSubmissionsCount }}
        </span>
        <span class="sync-btn-label tabular-nums">{{ headerSyncLabel }}</span>
      </button>
      <div
        v-if="!isTasksViewMounted"
        id="sync-queue-status-text"
        class="sr-only"
        role="status"
        aria-live="polite"
      >
        {{ sync.syncStatusText }}
      </div>
    </div>
  </header>
</template>

<script setup>
import { computed } from "vue";
import { storeToRefs } from "pinia";

import { useSyncStore } from "../stores/sync.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoBuild } from "../utils/demoMode.js";

const sync = useSyncStore();
const ui = useUiStore();
const { queuedSubmissionsCount, quarantinedSubmissionsCount, isSyncFailed } =
  storeToRefs(sync);
const { toggleSyncDrawer } = sync;

const isTasksViewMounted = computed(() => {
  if (!isDemoBuild() && import.meta.env?.MODE !== "test") {
    return false;
  }
  return (
    ui.currentView === "view-tasks" || ui.mountedViewIds.includes("view-tasks")
  );
});

const brandName = computed(() => import.meta.env.VITE_BRAND_NAME || "Cadence");

const headerSyncLabel = computed(() => {
  if (sync.syncRetryInfo?.isRetrying) {
    const base = `Retrying (${sync.syncRetryInfo.attempt}/${sync.syncRetryInfo.maxRetries})`;
    const rem = sync.syncRetryInfo.remainingSeconds;
    return typeof rem === "number" && rem > 0 ? `${base} · ${rem}s` : base;
  }
  if (quarantinedSubmissionsCount.value > 0) {
    return "Sync Status · Review Needed";
  }
  return "Sync Status";
});
</script>
