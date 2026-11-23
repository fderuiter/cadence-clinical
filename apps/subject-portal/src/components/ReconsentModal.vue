<template>
  <div
    v-if="consent.reconsentModalOpen"
    id="portal-reconsent-modal"
    ref="modalRef"
    class="modal-overlay"
    role="dialog"
    aria-modal="true"
    aria-labelledby="portal-reconsent-modal-title"
    tabindex="-1"
  >
    <div class="modal" style="max-width: 600px">
      <form
        id="reconsent-modal-form"
        @submit.prevent="submitReconsentSignature"
      >
        <div id="portal-reconsent-modal-title" class="modal-header">
          Updated Study Consent Review
        </div>
        <div class="modal-body">
          <p style="font-size: var(--font-size-sm); margin-bottom: 12px">
            Your study consent form has been updated. Please review what changed
            below and sign to continue with your study surveys.
          </p>

          <div
            v-if="consent.reconsentModalError"
            id="reconsent-modal-error-banner"
            style="
              padding: 8px 12px;
              border-radius: 4px;
              background-color: var(--semantic-color-error-bg);
              color: var(--semantic-color-error);
              margin-bottom: 12px;
              font-size: var(--font-size-xs);
              font-weight: 600;
            "
            role="status"
            aria-live="polite"
          >
            {{ consent.reconsentModalError }}
          </div>

          <div
            id="reconsent-change-summary"
            class="card"
            style="
              background-color: var(--semantic-color-surface-muted);
              border: 1px solid var(--semantic-color-border);
              padding: 12px 16px;
              margin-bottom: 16px;
              border-radius: 6px;
            "
          >
            <h4
              style="
                margin: 0 0 6px 0;
                font-size: var(--font-size-sm);
                color: var(--semantic-color-text-dark);
              "
            >
              What Changed in Your Study Consent:
            </h4>
            <p
              style="
                margin: 0;
                font-size: var(--font-size-xs);
                color: var(--semantic-color-text-muted);
              "
            >
              {{
                consent.pendingReconsent?.change_summary ||
                consent.pendingReconsent?.summary_of_changes ||
                "Updated study consent document terms."
              }}
            </p>
          </div>

          <div class="form-group mb-12">
            <label for="reconsent-sign-reason"
              >Reason for Action / Declaration</label
            >
            <select
              id="reconsent-sign-reason"
              v-model="consent.reconsentForm.reason"
            >
              <option value="Protocol Amendment Re-Consent Acknowledgment">
                Protocol Amendment Re-Consent Acknowledgment
              </option>
              <option value="Acknowledge updated clinical study consent">
                Acknowledge updated clinical study consent
              </option>
              <option value="Other">Other (specify below)</option>
            </select>
          </div>
          <div class="form-group mb-12">
            <label
              id="reconsent-sign-reason-custom-label"
              for="reconsent-sign-reason-custom"
            >
              {{
                isOtherReason
                  ? "Custom Reason Detail (Required)"
                  : "Custom Reason Detail (Optional)"
              }}
            </label>
            <textarea
              id="reconsent-sign-reason-custom"
              v-model="consent.reconsentForm.customReason"
              :required="isOtherReason || undefined"
              :aria-required="isOtherReason ? 'true' : undefined"
              placeholder="Provide extra detail if 'Other' selected..."
            />
          </div>
          <div class="form-group mb-12">
            <label for="reconsent-sign-username">User ID / Username</label>
            <input
              id="reconsent-sign-username"
              v-model="consent.reconsentForm.username"
              type="text"
              autocomplete="username"
              placeholder="Enter your participant identifier..."
              required
            />
          </div>
          <div class="form-group">
            <label for="reconsent-sign-password"
              >Account Password / Security PIN</label
            >
            <input
              id="reconsent-sign-password"
              v-model="consent.reconsentForm.password"
              v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
              placeholder="Enter your password or verification credential..."
              required
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            id="btn-reconsent-cancel"
            type="button"
            class="btn btn-secondary"
            @click="closeReconsentModal"
          >
            Cancel
          </button>
          <button
            id="btn-reconsent-submit"
            type="submit"
            class="btn btn-primary"
          >
            Sign and Confirm Re-Consent
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

import {
  closeReconsentModal,
  submitReconsentSignature,
} from "../features/reconsent.js";
import { useConsentStore } from "../stores/consent.js";
import { state } from "../stores/state.js";

const modalRef = ref(null);
const consent = useConsentStore();

const isOtherReason = computed(() =>
  requiresCustomSignatureReason(consent.reconsentForm.reason)
);

useFocusTrap(modalRef, {
  isOpen: () => consent.reconsentModalOpen,
  onEscape: closeReconsentModal,
  initialFocus: "#reconsent-sign-password",
  restoreFocus: () => {
    const prev = state.reconsentPreviouslyFocusedElement;
    state.reconsentPreviouslyFocusedElement = null;
    return prev;
  },
});
</script>
