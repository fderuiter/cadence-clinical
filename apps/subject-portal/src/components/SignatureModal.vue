<template>
  <ClinicalModal
    id="portal-sign-modal"
    overlay-id="portal-sign-modal-backdrop"
    :is-open="ui.signatureModalOpen"
    :keep-mounted="true"
    :teleport="false"
    :restore-focus="false"
    :show-close="false"
    initial-focus="#sign-reason"
    size="md"
    dialog-class="modal"
    aria-labelledby="portal-modal-title"
    :style="{ display: ui.signatureModalOpen ? 'flex' : 'none' }"
    @close="closeSignatureModal"
  >
    <template #header>
      <h2 id="portal-modal-title" class="modal-title">
        {{ modalTitle }}
      </h2>
    </template>

    <form id="sign-modal-form" @submit.prevent="onFormSubmit">
      <p id="sign-modal-instructions" class="signature-modal-instructions">
        {{ modalInstructionText }}
      </p>

      <div data-signature-error-host="true">
        <ModalErrorAlert
          id="modal-error-banner"
          :error="ui.modalError"
          :auto-focus="false"
          retry-label="Re-enter & Retry"
          dismiss-label="Dismiss"
          style="display: block"
          @retry="focusSignaturePassword"
          @dismiss="dismissSignatureError"
        >
          <span>{{ ui.modalError }}</span>
          <span
            v-if="ui.signatureNeedsPasswordReentry"
            class="signature-retry-guidance"
          >
            Re-enter your {{ credentialLabel.toLowerCase() }} below and select
            Sign and Confirm to retry. Your selected signing reason has been
            kept.
          </span>
        </ModalErrorAlert>
      </div>

      <div class="form-group mb-12">
        <label for="sign-reason">Reason for Signing</label>
        <select id="sign-reason" @change="onReasonChange">
          <option value="Initial Questionnaire Completion">
            Initial Questionnaire Completion
          </option>
          <option value="Correction of previous entry">
            Correction of previous entry
          </option>
          <option value="Requested update by study coordinator">
            Requested update by study coordinator
          </option>
          <option value="Acknowledge important reminder">
            Acknowledge important reminder
          </option>
          <option value="Other">Other (specify below)</option>
        </select>
      </div>
      <div class="form-group mb-12">
        <label id="sign-reason-custom-label" for="sign-reason-custom">
          {{
            isOtherReason
              ? "Additional Note (Required)"
              : "Additional Note (Optional)"
          }}
        </label>
        <textarea
          id="sign-reason-custom"
          :required="isOtherReason || undefined"
          :aria-required="isOtherReason ? 'true' : undefined"
          placeholder="Add a brief note if you selected 'Other'..."
        />
      </div>
      <div class="form-group mb-12">
        <label for="sign-username">Participant ID</label>
        <input
          id="sign-username"
          type="text"
          autocomplete="username"
          placeholder="Enter your participant ID..."
          required
        />
      </div>
      <div class="form-group">
        <div class="signature-password-label-row">
          <label id="sign-password-label" for="sign-password">{{
            credentialLabel
          }}</label>
          <span
            v-if="ui.signatureNeedsPasswordReentry"
            id="sign-password-reentry-hint"
            class="signature-password-reentry-cue"
          >
            Cleared after last attempt — please re-enter
          </span>
        </div>
        <input
          id="sign-password"
          v-bind="credentialInputAttrs"
          :aria-describedby="
            ui.signatureNeedsPasswordReentry
              ? 'sign-password-reentry-hint'
              : undefined
          "
          :placeholder="credentialPlaceholder"
          required
          @input="onPasswordInput"
        />
      </div>
    </form>

    <template #footer>
      <button
        id="btn-modal-cancel"
        type="button"
        class="btn btn-secondary"
        @click="onCancelClick"
      >
        Cancel
      </button>
      <button
        id="btn-modal-sign"
        type="submit"
        form="sign-modal-form"
        class="btn btn-primary"
        @click="onSignClick"
      >
        Sign and Confirm
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import {
  ClinicalModal,
  ModalErrorAlert,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
  requiresCustomSignatureReason,
} from "ui";

import { isAuthenticatedSession } from "../api/client.js";
import {
  closeSignatureModal,
  verifyAndSubmitSignature,
} from "../features/signature.js";
import { checkOnline } from "../features/sync-status.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";

const OFFLINE_SECURITY_PIN_INPUT_ATTRS = Object.freeze({
  type: "password",
  inputmode: "numeric",
  pattern: "[0-9]*",
  autocomplete: "off",
  autocapitalize: "off",
  autocorrect: "off",
  spellcheck: "false",
});

const selectedReason = ref("Initial Questionnaire Completion");
const isBrowserOnline = ref(checkOnline());
const sessionStore = useSessionStore();
const ui = useUiStore();

const modalTitle = computed(() =>
  ui.pendingSignableAction === "consent"
    ? "eConsent Electronic Signature Required"
    : "Electronic Signature Required"
);

function refreshOnlineState() {
  isBrowserOnline.value = checkOnline();
}

onMounted(() => {
  refreshOnlineState();
  if (typeof window !== "undefined") {
    window.addEventListener("online", refreshOnlineState);
    window.addEventListener("offline", refreshOnlineState);
  }
});

onBeforeUnmount(() => {
  if (typeof window !== "undefined") {
    window.removeEventListener("online", refreshOnlineState);
    window.removeEventListener("offline", refreshOnlineState);
  }
});

const isOfflinePinMode = computed(() => {
  void sessionStore.session?.token;
  void sessionStore.session?.isOfflineMode;
  void sessionStore.session?.isDemoMode;
  void ui.signatureModalOpen;
  const onlineNow = isBrowserOnline.value && checkOnline();
  return (
    ui.pendingSignableAction === "epro" &&
    (!isAuthenticatedSession() || !onlineNow)
  );
});

const credentialLabel = computed(() =>
  isOfflinePinMode.value ? "Security PIN" : "Account Password"
);

const credentialPlaceholder = computed(() =>
  isOfflinePinMode.value
    ? "Enter your numeric security PIN..."
    : "Enter your account password..."
);

const credentialInputAttrs = computed(() =>
  isOfflinePinMode.value
    ? OFFLINE_SECURITY_PIN_INPUT_ATTRS
    : SIGNATURE_PASSWORD_INPUT_ATTRS
);

const modalInstructionText = computed(() => {
  if (ui.pendingSignableAction === "consent") {
    return "Please confirm your electronic consent by selecting a signing reason below and entering your participant ID and account password.";
  }
  if (isOfflinePinMode.value) {
    return "Please confirm that these answers are your own by choosing a reason below and entering your participant ID and numeric security PIN.";
  }
  return "Please confirm that these answers are your own by choosing a reason below and entering your participant ID and account password.";
});

const isOtherReason = computed(() =>
  requiresCustomSignatureReason(selectedReason.value)
);

function onReasonChange(event) {
  selectedReason.value = event?.target?.value || "";
}

function onPasswordInput(event) {
  if (event?.target?.value) {
    ui.signatureNeedsPasswordReentry = false;
  }
}

function focusSignaturePassword() {
  const pwdEl =
    typeof document !== "undefined"
      ? document.getElementById("sign-password")
      : null;
  if (pwdEl && typeof pwdEl.focus === "function") {
    pwdEl.focus();
  }
}

function dismissSignatureError() {
  ui.modalError = "";
  focusSignaturePassword();
}

function onCancelClick(event) {
  if (event?.currentTarget?.dataset?.portalBound === "true") return;
  closeSignatureModal();
}

function onSignClick(event) {
  if (event?.currentTarget?.dataset?.portalBound === "true") return;
  event?.preventDefault?.();
  void verifyAndSubmitSignature();
}

function onFormSubmit() {
  const btn =
    typeof document !== "undefined"
      ? document.getElementById("btn-modal-sign")
      : null;
  if (btn?.dataset?.portalBound === "true") return;
  void verifyAndSubmitSignature();
}
</script>

<style scoped>
.signature-modal-instructions {
  margin: 0 0 12px 0;
  font-size: 0.875rem;
  color: var(--semantic-color-text);
}

.signature-retry-guidance {
  display: block;
  margin-top: 4px;
  font-size: 0.8125rem;
  font-weight: 500;
  color: var(--semantic-color-error-text-deep);
}

.signature-password-label-row {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 6px;
}

.signature-password-reentry-cue {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-error-text-dark);
}
</style>
