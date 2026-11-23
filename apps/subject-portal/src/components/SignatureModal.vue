<template>
  <div
    id="portal-sign-modal"
    ref="modalRef"
    class="modal-overlay"
    style="display: none"
    role="dialog"
    :aria-modal="ui.signatureModalOpen ? 'true' : undefined"
    :aria-hidden="!ui.signatureModalOpen ? 'true' : undefined"
    :inert="!ui.signatureModalOpen || undefined"
    aria-labelledby="portal-modal-title"
    tabindex="-1"
  >
    <div class="modal">
      <form id="sign-modal-form" @submit.prevent>
        <div id="portal-modal-title" class="modal-header">Confirm and Sign</div>
        <div class="modal-body">
          <p id="sign-modal-instructions">
            {{ modalInstructionText }}
          </p>
          <!-- Accessible modal live error region -->
          <div
            id="modal-error-banner"
            style="
              display: none;
              padding: 8px;
              border-radius: 4px;
              background-color: var(--semantic-color-error-bg);
              color: var(--semantic-color-error);
              margin-bottom: 12px;
              font-size: 13px;
              font-weight: 600;
            "
            role="status"
            aria-live="polite"
          />

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
            <label id="sign-password-label" for="sign-password">{{
              credentialLabel
            }}</label>
            <input
              id="sign-password"
              v-bind="credentialInputAttrs"
              :placeholder="credentialPlaceholder"
              required
            />
          </div>
        </div>
        <div class="modal-footer">
          <button id="btn-modal-cancel" type="button" class="btn btn-secondary">
            Cancel
          </button>
          <button id="btn-modal-sign" type="submit" class="btn btn-primary">
            Sign and Confirm
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from "vue";
import {
  SIGNATURE_PASSWORD_INPUT_ATTRS,
  requiresCustomSignatureReason,
  useFocusTrap,
} from "ui";

import { isAuthenticatedSession } from "../api/client.js";
import { closeSignatureModal } from "../features/signature.js";
import { useSessionStore } from "../stores/session.js";
import { state } from "../stores/state.js";
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

const modalRef = ref(null);
const selectedReason = ref("Initial Questionnaire Completion");
const sessionStore = useSessionStore();
const ui = useUiStore();

const isOfflinePinMode = computed(() => {
  void sessionStore.session?.token;
  void sessionStore.session?.isOfflineMode;
  void sessionStore.session?.isDemoMode;
  return ui.pendingSignableAction === "epro" && !isAuthenticatedSession();
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

useFocusTrap(modalRef, {
  isOpen: () => ui.signatureModalOpen,
  onEscape: closeSignatureModal,
  restoreFocus: () => {
    const target = state.initiatingButton || state.previouslyFocusedElement;
    state.initiatingButton = null;
    return target;
  },
});
</script>
