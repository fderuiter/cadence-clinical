<template>
  <div class="participant-info-card">
    <div class="info-title">Participant Session</div>
    <div class="info-row">
      <span class="info-lbl">ID:</span>
      <span id="session-subject-id" class="info-val">{{
        displaySubjectId
      }}</span>
    </div>
    <div class="info-row">
      <span class="info-lbl">Status:</span>
      <span id="session-auth-status" class="info-val" :class="statusClass">
        {{ statusLabel }}
      </span>
    </div>
    <div v-if="isAuthenticated || isDemo" class="info-row">
      <span class="info-lbl">{{ isDemo ? "Demo Wallet:" : "Wallet:" }}</span>
      <span id="session-wallet-balance" class="info-val">
        ${{
          typeof wallet.walletBalance === "number"
            ? wallet.walletBalance.toFixed(2)
            : "0.00"
        }}
      </span>
    </div>
    <div
      v-if="sessionStore.authError"
      id="session-auth-error"
      class="participant-auth-error"
      role="alert"
    >
      {{ sessionStore.authError }}
    </div>
    <div class="info-row participant-session-actions">
      <button
        v-if="isSignedInOrDemo"
        id="btn-logout"
        type="button"
        class="btn btn-secondary participant-action-btn"
        @click="handleLogout"
      >
        {{ isDemo ? "Reset Demo Session" : "Logout" }}
      </button>
      <button
        v-else
        id="btn-login"
        type="button"
        class="btn btn-primary participant-action-btn"
        @click="handleLogin"
      >
        Sign In
      </button>
    </div>
    <SiteContactLinks
      id="participant-site-contacts"
      compact
      heading="Site Support"
      region-label="Participant card study site coordinator contacts"
    />
  </div>
</template>

<script setup>
import { computed } from "vue";

import { login, logout } from "../features/auth.js";
import { maskParticipantIdentifier } from "../features/site-support.js";
import { useSessionStore } from "../stores/session.js";
import { useWalletStore } from "../stores/wallet.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import SiteContactLinks from "./SiteContactLinks.vue";

const sessionStore = useSessionStore();
const wallet = useWalletStore();

const isDemo = computed(() => {
  void sessionStore.session?.isDemoMode;
  return isDemoModeActive();
});

const isAuthenticated = computed(
  () => Boolean(sessionStore.session.token) && !isDemo.value
);

const isOfflineCached = computed(
  () =>
    !isAuthenticated.value &&
    !isDemo.value &&
    Boolean(sessionStore.session.isOfflineMode) &&
    Boolean(sessionStore.session.userId)
);

const isSignedInOrDemo = computed(
  () => isAuthenticated.value || isOfflineCached.value || isDemo.value
);

const displaySubjectId = computed(() => {
  const raw = isDemo.value
    ? sessionStore.session.userId || "subject_001"
    : sessionStore.session.userId;
  return maskParticipantIdentifier(raw) || "Not signed in";
});

const statusLabel = computed(() => {
  if (isDemo.value) return "Demo Mode";
  if (isAuthenticated.value) {
    return sessionStore.session.isOfflineMode ? "Offline" : "Authenticated";
  }
  if (isOfflineCached.value) return "Offline (Locked)";
  return "Unauthenticated";
});

const statusClass = computed(() => {
  if (isAuthenticated.value && !sessionStore.session.isOfflineMode) {
    return "text-success";
  }
  return "text-muted";
});

function handleLogin() {
  login();
}

function handleLogout() {
  logout();
}
</script>
