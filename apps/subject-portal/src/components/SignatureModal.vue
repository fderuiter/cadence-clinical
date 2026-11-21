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
        <div id="portal-modal-title" class="modal-header">
          Electronic Signature Required
        </div>
        <div class="modal-body">
          <p>
            To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>,
            you must provide your electronic signature credentials and declare
            your change reason or completion affirmation.
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
            <label for="sign-reason">Reason for Action / Declaration</label>
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
                  ? "Custom Reason Detail (Required)"
                  : "Custom Reason Detail (Optional)"
              }}
            </label>
            <textarea
              id="sign-reason-custom"
              :required="isOtherReason || undefined"
              :aria-required="isOtherReason ? 'true' : undefined"
              placeholder="Provide extra detail if 'Other' selected..."
            />
          </div>
          <div class="form-group mb-12">
            <label for="sign-username">User ID / Username</label>
            <input
              id="sign-username"
              type="text"
              autocomplete="username"
              placeholder="Enter your participant identifier..."
              required
            />
          </div>
          <div class="form-group">
            <label for="sign-password">Account Password / Security PIN</label>
            <input
              id="sign-password"
              v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
              placeholder="Enter your password or verification credential..."
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

import { closeSignatureModal } from "../features/signature.js";
import { state } from "../stores/state.js";
import { useUiStore } from "../stores/ui.js";

const modalRef = ref(null);
const selectedReason = ref("Initial Questionnaire Completion");
const ui = useUiStore();

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
