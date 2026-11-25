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

      <!-- Session sheet trigger (< 1280px) -->
      <button
        id="btn-mobile-session-toggle"
        type="button"
        class="btn btn-secondary mobile-session-toggle"
        :class="{ 'has-session-alert': hasSessionAlert }"
        :aria-expanded="ui.sessionDrawerOpen ? 'true' : 'false'"
        aria-controls="participant-session-drawer"
        title="Participant session details"
        @click="toggleSessionDrawer"
      >
        <ClinicalIcon
          :name="hasSessionAlert ? 'alert-circle' : 'user'"
          :size="16"
        />
        <span class="session-toggle-label">
          <span>Session</span>
          <span
            v-if="maskedSubjectId"
            class="session-toggle-subject tabular-nums"
          >
            &middot; {{ maskedSubjectId }}
          </span>
        </span>
      </button>

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
        <span class="sync-icon" aria-hidden="true">
          <ClinicalIcon
            :name="quarantinedSubmissionsCount > 0 ? 'alert-circle' : 'refresh'"
            :size="16"
          />
        </span>
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
    </div>
  </header>
</template>

<script setup>
import { storeToRefs } from "pinia";
import { ClinicalIcon } from "ui";
import { computed } from "vue";

import { usePortalSessionChrome } from "../composables/usePortalSessionChrome.js";
import { useSyncStore } from "../stores/sync.js";

const sync = useSyncStore();
const { ui, hasSessionAlert, maskedSubjectId } = usePortalSessionChrome();

const { queuedSubmissionsCount, quarantinedSubmissionsCount, isSyncFailed } =
  storeToRefs(sync);
const { toggleSyncDrawer } = sync;

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

function toggleSessionDrawer() {
  ui.sessionDrawerOpen = !ui.sessionDrawerOpen;
}
</script>

<style scoped>
.mobile-session-toggle,
.btn.mobile-session-toggle {
  display: none;
}

.mobile-session-toggle.has-session-alert,
.btn.mobile-session-toggle.has-session-alert {
  background-color: var(--danger-bg);
  border-color: var(--danger);
  color: var(--semantic-color-error-text-dark);
}

@media (max-width: 1279px), (max-height: 480px) {
  .portal-header {
    padding: 1px 12px;
    border-bottom: 1px solid var(--border);
    flex-wrap: nowrap;
    gap: 8px;
    min-height: 50px;
    max-height: 52px;
    box-sizing: border-box;
  }

  .header-branding {
    min-width: 0;
    flex: 1 1 auto;
  }

  .header-branding h1 {
    font-size: 18px;
    line-height: 1.2;
    margin: 0;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .header-branding p.role-badge,
  .compliance-badges .badge {
    display: none;
  }

  .compliance-badges {
    flex-wrap: nowrap;
    gap: 6px;
    flex-shrink: 0;
  }

  .btn-sync-status {
    height: var(--touch-target-min, 48px);
    min-height: var(--touch-target-min, 48px);
    padding: 0 10px;
    gap: 6px;
    font-size: 12px;
    white-space: nowrap;
    box-sizing: border-box;
  }
}

@media (max-width: 1279px) {
  .mobile-session-toggle,
  .btn.mobile-session-toggle {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    width: auto;
    height: var(--touch-target-min, 48px);
    min-height: var(--touch-target-min, 48px);
    padding: 0 10px;
    font-size: 12px;
    white-space: nowrap;
    box-sizing: border-box;
  }

  .session-toggle-label {
    display: inline-flex;
    align-items: center;
    gap: 4px;
  }
}

@media (max-width: 639px) {
  .portal-header {
    padding: 1px 10px;
    gap: 6px;
  }

  .header-branding h1 {
    font-size: 16px;
  }

  .session-toggle-subject {
    display: none;
  }

  .mobile-session-toggle,
  .btn.mobile-session-toggle,
  .btn-sync-status {
    padding: 0 8px;
    gap: 4px;
  }

  .sync-btn-label {
    max-width: 108px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
}
</style>
