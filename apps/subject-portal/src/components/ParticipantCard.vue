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
    <div
      v-if="isAuthenticated"
      id="participant-activation-panel"
      class="participant-activation-panel"
    >
      <label for="input-activation-code" class="participant-activation-label">
        Activation Code
      </label>
      <input
        id="input-activation-code"
        v-model="activationCodeInput"
        type="text"
        class="participant-activation-input"
        autocomplete="off"
        placeholder="CAD-XXXX-XXXX-XXXX"
      />
      <button
        id="btn-redeem-activation"
        type="button"
        class="btn btn-secondary participant-action-btn"
        :disabled="redeeming || !activationCodeInput.trim()"
        @click="handleRedeemActivation"
      >
        {{ redeeming ? "Linking…" : "Redeem Code" }}
      </button>
      <div
        v-if="activationMessage"
        id="activation-redeem-status"
        class="participant-activation-note"
        role="status"
      >
        {{ activationMessage }}
      </div>
      <div
        v-if="activationError"
        id="activation-redeem-error"
        class="participant-auth-error"
        role="alert"
      >
        {{ activationError }}
      </div>
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
import { computed, ref } from "vue";

import { login, logout, redeemActivationCode } from "../features/auth.js";
import { maskParticipantIdentifier } from "../features/site-support.js";
import { useSessionStore } from "../stores/session.js";
import { useWalletStore } from "../stores/wallet.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import SiteContactLinks from "./SiteContactLinks.vue";

const sessionStore = useSessionStore();
const wallet = useWalletStore();
const activationCodeInput = ref("");
const redeeming = ref(false);
const activationMessage = ref("");
const activationError = ref("");

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

async function handleRedeemActivation() {
  const code = activationCodeInput.value.trim();
  if (!code || redeeming.value) return;
  redeeming.value = true;
  activationMessage.value = "";
  activationError.value = "";
  try {
    const res = await redeemActivationCode(code);
    activationCodeInput.value = "";
    activationMessage.value = `Linked to ${res.subject_id}`;
  } catch (err) {
    activationError.value =
      err instanceof Error ? err.message : "Failed to redeem activation code.";
  } finally {
    redeeming.value = false;
  }
}
</script>

<style scoped>
.participant-activation-panel {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px solid var(--border);
}

.participant-activation-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-muted);
}

.participant-activation-input {
  width: 100%;
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background-color: var(--bg-card);
  color: var(--text-main);
  font-size: 13px;
}

.participant-activation-note {
  font-size: 12px;
  color: var(--success);
}
</style>
