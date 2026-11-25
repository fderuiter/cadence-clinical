<template>
  <div
    v-if="sessionStore.pinUnlock.isOpen"
    id="portal-pin-unlock-modal"
    ref="modalRef"
    class="modal-overlay"
    role="dialog"
    aria-modal="true"
    aria-labelledby="pin-unlock-title"
    tabindex="-1"
  >
    <div class="modal">
      <div
        v-if="sessionStore.pinUnlock.confirmingReset"
        id="pin-reset-confirmation"
      >
        <div id="pin-unlock-title" class="modal-header">
          Confirm Security PIN Reset
        </div>
        <div class="modal-body">
          <div id="pin-reset-warning" class="pin-warning-alert" role="alert">
            <strong>Warning:</strong> Resetting your security PIN sets up a new
            PIN on this device, will permanently clear unsent survey drafts, and
            marks older locked offline entries without showing your health
            information. Answers already sent to your study team are not
            affected.
          </div>
          <p v-if="requiresIdentitySignIn" class="pin-reset-prompt">
            Please sign in with your participant account to verify your identity
            before setting a new security PIN, or contact your study site for
            assistance.
          </p>
          <p v-else class="pin-reset-prompt">
            Do you want to verify your session, flag locked local entries, and
            choose a new security PIN?
          </p>
          <div
            v-if="sessionStore.pinUnlock.error"
            id="pin-reset-error"
            class="error-state pin-error-msg"
            role="status"
            aria-live="polite"
          >
            {{ sessionStore.pinUnlock.error }}
          </div>
          <SiteContactLinks
            id="pin-reset-site-contacts"
            compact
            heading="Site Support"
            region-label="PIN reset study site coordinator contacts"
          />
        </div>
        <div class="modal-footer modal-footer-wrap">
          <button
            v-if="requiresIdentitySignIn"
            id="btn-pin-reauth-oidc"
            type="button"
            class="btn btn-primary"
            @click="reauthenticateForPINReset"
          >
            Sign In to Reset PIN
          </button>
          <button
            v-else
            id="btn-pin-reset-confirm"
            type="button"
            class="btn btn-primary btn-danger-solid"
            @click="confirmPINReset"
          >
            Clear Local Entries &amp; Reset PIN
          </button>
          <button
            id="btn-pin-reset-cancel"
            type="button"
            class="btn btn-secondary"
            @click="cancelPINReset"
          >
            Cancel
          </button>
          <button
            id="btn-pin-reset-signout"
            type="button"
            class="btn btn-secondary"
            @click="handleSignOut"
          >
            Sign Out
          </button>
        </div>
      </div>

      <form v-else id="pin-unlock-form" @submit.prevent="handlePINUnlockSubmit">
        <div id="pin-unlock-title" class="modal-header">
          Device Locked - Enter Security PIN
        </div>
        <div class="modal-body">
          <p>
            Please enter your numeric security PIN to access the clinical portal
            and decrypt your offline data.
          </p>
          <div
            v-if="isLockedOut"
            id="pin-lockout-banner"
            class="pin-warning-alert"
            role="alert"
          >
            <strong>PIN Unlock Paused:</strong> Maximum failed PIN attempts (5
            of 5) reached. Sign in again to reset your PIN, sign out, or contact
            your study coordinator below.
          </div>
          <div
            v-if="sessionStore.pinUnlock.error"
            id="pin-unlock-error"
            class="error-state pin-error-msg"
            role="status"
            aria-live="polite"
          >
            {{ sessionStore.pinUnlock.error }}
          </div>
          <p
            v-if="failedAttempts > 0 && !isLockedOut"
            id="pin-attempts-remaining"
            class="pin-attempts-note"
            aria-live="polite"
          >
            Attempts remaining before unlock pause:
            <strong>{{ remainingAttempts }}</strong> of {{ maxAttempts }}
          </p>
          <div class="form-group">
            <label for="unlock-pin">Security PIN</label>
            <input
              id="unlock-pin"
              v-model="sessionStore.pinUnlock.pin"
              type="password"
              inputmode="numeric"
              pattern="[0-9]*"
              placeholder="••••"
              :disabled="isLockedOut"
              required
              @keydown.enter.prevent="handlePINUnlockSubmit"
            />
          </div>
        </div>
        <div class="modal-footer modal-footer-between">
          <div class="modal-recovery-actions">
            <button
              id="btn-pin-unlock-reset"
              type="button"
              class="btn btn-secondary"
              @click="requestPINReset"
            >
              Forgot / Reset PIN
            </button>
            <button
              id="btn-pin-unlock-signout"
              type="button"
              class="btn btn-secondary"
              @click="handleSignOut"
            >
              Sign Out
            </button>
          </div>
          <button
            id="btn-pin-unlock-confirm"
            type="submit"
            class="btn btn-primary"
            :disabled="isLockedOut"
          >
            Unlock
          </button>
        </div>
        <div class="modal-support-strip">
          <SiteContactLinks
            id="pin-unlock-site-contacts"
            compact
            heading="Study Site Coordinator"
            region-label="PIN dialog study site coordinator contacts"
          />
        </div>
      </form>
    </div>
    <SignOutConfirmModal
      id="pin-unlock-signout-confirm-modal"
      :open="isSignOutConfirmOpen"
      title="Log Out With Unsaved or Unsent Survey Entries?"
      description="You have unsaved questionnaire drafts or locked offline survey submissions waiting to sync on this device. Logging out now will leave those entries unsent until you sign back in and unlock your PIN."
      stay-label="Stay & Unlock PIN"
      confirm-label="Log Out Anyway"
      @confirm="confirmSignOut"
      @cancel="cancelSignOut"
    />
  </div>
</template>

<script setup>
import { computed, ref } from "vue";
import { SignOutConfirmModal, useFocusTrap } from "ui/portal-overlays";

import { usePortalSignOutGuard } from "../features/auth.js";
import {
  MAX_PIN_UNLOCK_ATTEMPTS,
  cancelPINReset,
  confirmPINReset,
  handlePINUnlockSubmit,
  hasVerifiedParticipantIdentity,
  reauthenticateForPINReset,
  requestPINReset,
} from "../features/pin.js";
import { useSessionStore } from "../stores/session.js";
import SiteContactLinks from "./SiteContactLinks.vue";

const modalRef = ref(null);
const sessionStore = useSessionStore();

const {
  isConfirmOpen: isSignOutConfirmOpen,
  requestSignOut: handleSignOut,
  confirmSignOut,
  cancelSignOut,
} = usePortalSignOutGuard();

useFocusTrap(modalRef, {
  isOpen: () => sessionStore.pinUnlock.isOpen && !isSignOutConfirmOpen.value,
  escapeDeactivates: false,
  initialFocus: "#unlock-pin",
});

const maxAttempts = computed(
  () => sessionStore.pinUnlock.maxAttempts || MAX_PIN_UNLOCK_ATTEMPTS
);

const failedAttempts = computed(
  () => sessionStore.pinUnlock.failedAttempts || 0
);

const isLockedOut = computed(
  () =>
    Boolean(sessionStore.pinUnlock.isLockedOut) ||
    failedAttempts.value >= maxAttempts.value
);

const remainingAttempts = computed(() =>
  Math.max(0, maxAttempts.value - failedAttempts.value)
);

const requiresIdentitySignIn = computed(() => {
  void sessionStore.session?.token;
  void sessionStore.session?.isOfflineMode;
  void sessionStore.session?.isDemoMode;
  return (
    Boolean(sessionStore.pinUnlock.requiresReauth) ||
    !hasVerifiedParticipantIdentity()
  );
});
</script>
