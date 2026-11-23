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
        <span class="sync-icon" aria-hidden="true">
          <AlertTriangle
            v-if="quarantinedSubmissionsCount > 0"
            :size="16"
            aria-hidden="true"
          />
          <RefreshCw v-else :size="16" aria-hidden="true" />
        </span>
        <span
          v-if="queuedSubmissionsCount > 0"
          id="header-sync-badge"
          class="sync-badge"
          :class="{ 'sync-badge-quarantined': quarantinedSubmissionsCount > 0 }"
          aria-live="polite"
          aria-atomic="true"
        >
          {{ queuedSubmissionsCount }}
        </span>
        <span class="sync-btn-label">{{ headerSyncLabel }}</span>
      </button>

      <button
        id="btn-portal-theme-toggle"
        type="button"
        class="btn-theme-toggle"
        :aria-label="
          currentTheme === 'dark'
            ? 'Switch to light mode'
            : 'Switch to dark mode'
        "
        :title="
          currentTheme === 'dark'
            ? 'Switch to light mode'
            : 'Switch to dark mode'
        "
        @click="handleToggleTheme"
      >
        <Sun v-if="currentTheme === 'dark'" :size="16" aria-hidden="true" />
        <Moon v-else :size="16" aria-hidden="true" />
      </button>
    </div>
  </header>
</template>

<script setup>
import { AlertTriangle, Moon, RefreshCw, Sun } from "lucide-vue-next";
import { storeToRefs } from "pinia";
import {
  SURFACE_THEME_EVENT,
  isEffectiveDarkTheme,
  resolveSurfaceTheme,
  toggleSurfaceTheme,
} from "ui";
import { computed, onBeforeUnmount, onMounted, ref } from "vue";

import { useSyncStore } from "../stores/sync.js";

const sync = useSyncStore();
const { queuedSubmissionsCount, quarantinedSubmissionsCount, isSyncFailed } =
  storeToRefs(sync);
const { toggleSyncDrawer } = sync;
const currentTheme = ref(
  isEffectiveDarkTheme(resolveSurfaceTheme()) ? "dark" : "light"
);

function refreshThemeState() {
  currentTheme.value = isEffectiveDarkTheme(resolveSurfaceTheme())
    ? "dark"
    : "light";
}

function handleToggleTheme() {
  const result = toggleSurfaceTheme({ density: "participant" });
  currentTheme.value = result.isDark ? "dark" : "light";
}

onMounted(() => {
  refreshThemeState();
  if (typeof window !== "undefined") {
    window.addEventListener(SURFACE_THEME_EVENT, refreshThemeState);
    window.addEventListener("storage", refreshThemeState);
  }
});

onBeforeUnmount(() => {
  if (typeof window !== "undefined") {
    window.removeEventListener(SURFACE_THEME_EVENT, refreshThemeState);
    window.removeEventListener("storage", refreshThemeState);
  }
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
