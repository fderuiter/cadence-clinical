<template>
  <ClinicalModal
    id="portal-sign-dialog"
    overlay-id="portal-sign-modal"
    :is-open="ui.signatureModalOpen"
    keep-mounted
    :teleport="false"
    :show-header="false"
    :show-footer="false"
    :restore-focus="restoreSignatureFocus"
    aria-labelledby="portal-modal-title"
    backdrop-class="modal-overlay"
    dialog-class="modal"
    body-class="modal-content-passthrough"
    @close="closeSignatureModal"
  >
    <form id="sign-modal-form" @submit.prevent>
      <div id="portal-modal-title" class="modal-header">Confirm and Sign</div>
      <div class="modal-body">
        <p>
          Please confirm that these answers are your own by choosing a reason
          below and entering your participant ID and account password or
          security PIN.
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
          <label for="sign-password">Account Password or Security PIN</label>
          <input
            id="sign-password"
            v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
            placeholder="Enter your account password or security PIN..."
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
  </ClinicalModal>
</template>

<script setup>
import { computed, ref } from "vue";
import {
  ClinicalModal,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
  requiresCustomSignatureReason,
} from "ui";

import { closeSignatureModal } from "../features/signature.js";
import { state } from "../stores/state.js";
import { useUiStore } from "../stores/ui.js";

const selectedReason = ref("Initial Questionnaire Completion");
const ui = useUiStore();

const isOtherReason = computed(() =>
  requiresCustomSignatureReason(selectedReason.value)
);

function onReasonChange(event) {
  selectedReason.value = event?.target?.value || "";
}

function restoreSignatureFocus() {
  const target = state.initiatingButton || state.previouslyFocusedElement;
  state.initiatingButton = null;
  return target;
}
</script>
