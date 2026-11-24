<template>
  <div class="participant-info-card">
    <div class="info-title">Participant Session</div>
    <div class="info-row">
      <span class="info-lbl">ID:</span>
      <span id="session-subject-id" class="info-val">{{
        displaySubjectId
      }}</span>
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
      v-else-if="isSessionExpired"
      id="session-expired-banner"
      class="participant-auth-error"
      role="alert"
    >
      Your session has expired. Sign in again to continue — your local drafts
      are preserved on this device.
    </div>
    <div v-if="!isSignedInOrDemo" class="info-row participant-session-actions">
      <button
        id="btn-login"
        type="button"
        class="btn btn-primary participant-action-btn"
        :data-testid="isSessionExpired ? 'reauthenticate-btn' : 'signin-btn'"
        @click="handleLogin"
      >
        {{ isSessionExpired ? "Sign In to Continue" : "Sign In" }}
      </button>
    </div>
    <div
      id="participant-secondary-details"
      class="participant-secondary-details"
      :class="{ 'is-mobile-expanded': mobileExpanded }"
    >
      <div class="info-row">
        <span class="info-lbl">Status:</span>
        <span id="session-auth-status" class="info-val" :class="statusClass">
          {{ statusLabel }}
        </span>
      </div>
      <div v-if="isAuthenticated || isDemo" class="info-row">
        <span class="info-lbl">{{ isDemo ? "Demo Wallet:" : "Wallet:" }}</span>
        <span
          id="session-wallet-balance"
          class="info-val"
          :aria-busy="
            wallet.walletLoading && wallet.walletBalance === null
              ? 'true'
              : undefined
          "
        >
          {{ formattedSessionWalletBalance }}
        </span>
      </div>
      <div v-if="isSignedInOrDemo" class="info-row participant-session-actions">
        <button
          id="btn-logout"
          type="button"
          class="btn btn-secondary participant-action-btn"
          @click="handleLogout"
        >
          {{ isDemo ? "Reset Demo Session" : "Logout" }}
        </button>
      </div>
      <SiteContactLinks
        id="participant-site-contacts"
        compact
        heading="Site Support"
        region-label="Participant card study site coordinator contacts"
      />
    </div>
    <SignOutConfirmModal
      id="portal-signout-confirm-modal"
      :open="isSignOutConfirmOpen"
      title="Log Out With Unsaved or Unsent Survey Entries?"
      description="You have unsaved questionnaire drafts or unsent offline survey submissions on this device. Logging out now will close your session before pending entries are sent."
      stay-label="Stay & Keep Entries"
      confirm-label="Log Out Anyway"
      @confirm="confirmSignOut"
      @cancel="cancelSignOut"
    />
  </div>
</template>

<script setup>
import { DEFAULT_DEMO_PARTICIPANT } from "demo-fixtures";
import { SignOutConfirmModal, useSignOutGuard } from "ui";
import { computed } from "vue";

import {
  hasUnsavedOrUnsyncedLocalData,
  login,
  logout,
  prepareSignOutGuardState,
  resolveSessionSignerUsername,
} from "../features/auth.js";
import { maskParticipantIdentifier } from "../features/site-support.js";
import { useSessionStore } from "../stores/session.js";
import { useWalletStore } from "../stores/wallet.js";
import { formatWalletCurrency } from "../utils/currency.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import SiteContactLinks from "./SiteContactLinks.vue";

const sessionStore = useSessionStore();
const wallet = useWalletStore();

defineProps({
  mobileExpanded: {
    type: Boolean,
    default: false,
  },
});

const formattedSessionWalletBalance = computed(() => {
  if (wallet.walletLoading && wallet.walletBalance === null) {
    return "Loading...";
  }
  const amount =
    typeof wallet.walletBalance === "number" ? wallet.walletBalance : 0;
  return formatWalletCurrency(amount, wallet.currency);
});

const {
  isConfirmOpen: isSignOutConfirmOpen,
  requestSignOut,
  confirmSignOut,
  cancelSignOut,
} = useSignOutGuard({
  flushExternalDrafts: prepareSignOutGuardState,
  hasExternalDrafts: hasUnsavedOrUnsyncedLocalData,
  onConfirmSignOut: () => logout({ confirmed: true }),
});

const isDemo = computed(() => {
  void sessionStore.session?.isDemoMode;
  return isDemoModeActive();
});

const isAuthenticated = computed(
  () => Boolean(sessionStore.session.token) && !isDemo.value
);

const isSessionExpired = computed(
  () =>
    !isAuthenticated.value &&
    !isDemo.value &&
    Boolean(sessionStore.session.sessionExpired)
);

const isOfflineCached = computed(
  () =>
    !isAuthenticated.value &&
    !isDemo.value &&
    !isSessionExpired.value &&
    Boolean(sessionStore.session.isOfflineMode) &&
    Boolean(sessionStore.session.userId)
);

const isSignedInOrDemo = computed(
  () => isAuthenticated.value || isOfflineCached.value || isDemo.value
);

const displaySubjectId = computed(() => {
  void sessionStore.session.username;
  const signerOrUser = resolveSessionSignerUsername();
  const raw = isDemo.value
    ? signerOrUser || DEFAULT_DEMO_PARTICIPANT?.subjectCode
    : signerOrUser;
  return maskParticipantIdentifier(raw) || "Not signed in";
});

const statusLabel = computed(() => {
  if (isDemo.value) return "Demo Mode";
  if (isAuthenticated.value) {
    return sessionStore.session.isOfflineMode ? "Offline" : "Authenticated";
  }
  if (isSessionExpired.value) return "Session Expired";
  if (isOfflineCached.value) return "Offline (Locked)";
  return "Unauthenticated";
});

const statusClass = computed(() => {
  if (isAuthenticated.value && !sessionStore.session.isOfflineMode) {
    return "text-success";
  }
  if (isSessionExpired.value) {
    return "participant-status-expired";
  }
  return "text-muted";
});

function handleLogin() {
  login();
}

function handleLogout() {
  return requestSignOut();
}
</script>

<style scoped>
.participant-status-expired {
  color: var(--semantic-color-error-text-dark);
  font-weight: 700;
}
</style>
