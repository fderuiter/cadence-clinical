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
import { DEFAULT_DEMO_PARTICIPANT } from "demo-fixtures";
import { storeToRefs } from "pinia";
import { ClinicalIcon } from "ui";
import { computed } from "vue";

import { maskParticipantIdentifier } from "../features/site-support.js";
import { useSessionStore } from "../stores/session.js";
import { useSyncStore } from "../stores/sync.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const sync = useSyncStore();
const sessionStore = useSessionStore();
const ui = useUiStore();

const { queuedSubmissionsCount, quarantinedSubmissionsCount, isSyncFailed } =
  storeToRefs(sync);
const { toggleSyncDrawer } = sync;

const brandName = computed(() => import.meta.env.VITE_BRAND_NAME || "Cadence");

const isDemo = computed(() => {
  void sessionStore.session?.isDemoMode;
  return isDemoModeActive();
});

const hasSessionAlert = computed(
  () =>
    Boolean(sessionStore.authError) ||
    (!sessionStore.session?.token &&
      !isDemo.value &&
      Boolean(sessionStore.session?.sessionExpired))
);

const maskedSubjectId = computed(() => {
  const raw = isDemo.value
    ? sessionStore.session?.userId || DEFAULT_DEMO_PARTICIPANT?.subjectCode
    : sessionStore.session?.userId;
  return maskParticipantIdentifier(raw) || "";
});

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
