<template>
  <header class="portal-header">
    <div class="header-branding">
      <h1>
        My <span>{{ brandName }}</span>
      </h1>
      <p class="role-badge">Participant Companion Portal</p>
    </div>
    <div class="compliance-badges">
      <span class="badge badge-epro">ePRO Companion</span>
      <span class="badge badge-gxp">21 CFR Part 11</span>
      <span class="badge badge-gamp">GAMP 5</span>

      <!-- Persistent dynamic sync status button -->
      <button
        id="btn-global-sync-drawer"
        type="button"
        class="btn-sync-status"
        :class="{
          'has-queued': queuedSubmissionsCount > 0,
          'sync-failed': isSyncFailed,
          'sync-retrying': sync.syncRetryInfo?.isRetrying,
        }"
        aria-label="Toggle sync status drawer"
        @click="toggleSyncDrawer"
      >
        <span class="sync-icon">🔄</span>
        <span
          v-if="queuedSubmissionsCount > 0"
          class="sync-badge"
          aria-live="polite"
          aria-atomic="true"
        >
          {{ queuedSubmissionsCount }}
        </span>
        <span class="sync-btn-label">
          {{
            sync.syncRetryInfo?.isRetrying
              ? `Retrying (${sync.syncRetryInfo.attempt}/${sync.syncRetryInfo.maxRetries})`
              : "Sync Status"
          }}
        </span>
      </button>
    </div>
  </header>
</template>

<script setup>
import { computed } from "vue";
import { storeToRefs } from "pinia";

import { useSyncStore } from "../stores/sync.js";

const sync = useSyncStore();
const { queuedSubmissionsCount, isSyncFailed } = storeToRefs(sync);
const { toggleSyncDrawer } = sync;

const brandName = computed(() => import.meta.env.VITE_BRAND_NAME || "Cadence");
</script>
