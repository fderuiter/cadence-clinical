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
      <span
        id="session-auth-status"
        class="info-val"
        :class="statusClass"
        style="font-weight: 600"
      >
        {{ statusLabel }}
      </span>
    </div>
    <div v-if="isDemo" class="info-row">
      <span class="info-lbl">Demo Wallet:</span>
      <span
        id="session-wallet-balance"
        class="info-val"
        style="font-weight: bold"
      >
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
      role="alert"
      style="
        margin-top: 8px;
        font-size: 12px;
        color: var(--semantic-color-error);
        font-weight: 600;
      "
    >
      {{ sessionStore.authError }}
    </div>
    <div class="info-row" style="margin-top: 12px">
      <button
        v-if="isSignedInOrDemo"
        id="btn-logout"
        type="button"
        class="btn btn-secondary"
        style="width: 100%; padding: 4px 8px; font-size: 12px"
        @click="handleLogout"
      >
        {{ isDemo ? "Reset Demo Session" : "Logout" }}
      </button>
      <button
        v-else
        id="btn-login"
        type="button"
        class="btn btn-primary"
        style="width: 100%; padding: 6px 8px; font-size: 12px"
        @click="handleLogin"
      >
        Sign In
      </button>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";

import { login, logout } from "../features/auth.js";
import { useSessionStore } from "../stores/session.js";
import { useWalletStore } from "../stores/wallet.js";
import { isDemoModeActive } from "../utils/demoMode.js";

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
  if (isDemo.value) {
    return sessionStore.session.userId || "subject_001";
  }
  return sessionStore.session.userId || "Not signed in";
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
