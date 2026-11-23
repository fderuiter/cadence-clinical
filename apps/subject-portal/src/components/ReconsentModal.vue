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
          <p style="font-size: 14px; margin-bottom: 12px">
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
              font-size: 13px;
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
              margin-bottom: 14px;
              border-radius: 6px;
            "
          >
            <h4
              style="
                margin: 0 0 6px 0;
                font-size: 0.875rem;
                color: var(--semantic-color-text-dark);
              "
            >
              What Changed in Your Study Consent:
            </h4>
            <p
              style="
                margin: 0;
                font-size: 13px;
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

          <section
            id="reconsent-clause-redlines"
            aria-label="Amended consent clause changes"
            style="
              border: 1px solid var(--semantic-color-border);
              border-radius: 6px;
              padding: 12px 16px;
              margin-bottom: 14px;
              background-color: var(--semantic-color-surface);
            "
          >
            <h4
              style="
                margin: 0 0 10px 0;
                font-size: 14px;
                color: var(--semantic-color-text-dark);
              "
            >
              Highlighted Clause Redlines
            </h4>
            <div
              v-for="clause in clauseDiffs"
              :key="clause.id"
              class="reconsent-clause-diff-item"
              :data-change-type="clause.change_type"
              style="
                padding: 10px 12px;
                border-radius: 4px;
                border: 1px solid var(--semantic-color-border);
                margin-bottom: 8px;
              "
            >
              <div
                style="
                  display: flex;
                  align-items: center;
                  justify-content: space-between;
                  gap: 8px;
                  margin-bottom: 6px;
                "
              >
                <strong style="font-size: 13px">{{
                  clause.clause_title
                }}</strong>
                <span
                  class="reconsent-diff-badge"
                  style="
                    font-size: 0.75rem;
                    font-weight: 700;
                    text-transform: uppercase;
                    padding: 2px 6px;
                    border-radius: 4px;
                    background-color: var(--semantic-color-surface-muted);
                  "
                >
                  {{ formatChangeTypeLabel(clause.change_type) }}
                </span>
              </div>
              <p
                v-if="clause.previous_text"
                class="reconsent-clause-removed"
                style="
                  margin: 0 0 6px 0;
                  font-size: 13px;
                  padding: 6px 8px;
                  border-radius: 4px;
                  background-color: var(--semantic-color-error-bg);
                  color: var(--semantic-color-error);
                "
              >
                <span class="sr-only">Previous wording: </span>
                <del>{{ clause.previous_text }}</del>
              </p>
              <p
                v-if="clause.updated_text"
                class="reconsent-clause-added"
                style="
                  margin: 0;
                  font-size: 13px;
                  padding: 6px 8px;
                  border-radius: 4px;
                  background-color: var(--semantic-color-success-bg-soft);
                  color: var(--semantic-color-success-dark);
                "
              >
                <span class="sr-only">Updated wording: </span>
                <ins style="text-decoration: none; font-weight: 600">{{
                  clause.updated_text
                }}</ins>
              </p>
            </div>
          </section>

          <section
            id="reconsent-comprehension-section"
            aria-label="Updated consent comprehension check"
            style="
              border: 1px solid var(--semantic-color-border);
              border-radius: 6px;
              padding: 12px 16px;
              margin-bottom: 16px;
              background-color: var(--semantic-color-surface-muted);
            "
          >
            <h4
              style="
                margin: 0 0 8px 0;
                font-size: 14px;
                color: var(--semantic-color-text-dark);
              "
            >
              Quick Understanding Check
            </h4>
            <fieldset
              v-for="question in comprehensionQuestions"
              :key="question.id"
              class="reconsent-question-group"
              style="border: none; padding: 0; margin: 0 0 12px 0"
            >
              <legend
                style="
                  font-size: 13px;
                  font-weight: 600;
                  margin-bottom: 6px;
                  color: var(--semantic-color-text-dark);
                "
              >
                {{ question.text }}
              </legend>
              <label
                v-for="(option, optIndex) in question.options"
                :key="`${question.id}_${optIndex}`"
                :for="`${question.id}_opt_${optIndex}`"
                style="
                  display: flex;
                  align-items: center;
                  gap: 8px;
                  font-size: 0.875rem;
                  min-height: 44px;
                  padding: 6px 8px;
                  cursor: pointer;
                "
              >
                <input
                  :id="`${question.id}_opt_${optIndex}`"
                  v-model="consent.reconsentComprehensionAnswers[question.id]"
                  type="radio"
                  :name="`reconsent_${question.id}`"
                  :value="option"
                />
                <span>{{ option }}</span>
              </label>
            </fieldset>
          </section>

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
            <label
              id="reconsent-sign-password-label"
              for="reconsent-sign-password"
              >Account Password</label
            >
            <input
              id="reconsent-sign-password"
              v-model="consent.reconsentForm.password"
              v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
              placeholder="Enter your account password..."
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
  resolveReconsentClauseDiffs,
  resolveReconsentComprehensionQuestions,
  submitReconsentSignature,
} from "../features/reconsent.js";
import { useConsentStore } from "../stores/consent.js";
import { state } from "../stores/state.js";

const modalRef = ref(null);
const consent = useConsentStore();

const clauseDiffs = computed(() =>
  resolveReconsentClauseDiffs(consent.pendingReconsent)
);

const comprehensionQuestions = computed(() =>
  resolveReconsentComprehensionQuestions(consent.pendingReconsent)
);

const isOtherReason = computed(() =>
  requiresCustomSignatureReason(consent.reconsentForm.reason)
);

function formatChangeTypeLabel(changeType) {
  if (changeType === "added") return "Added";
  if (changeType === "removed") return "Removed";
  return "Modified";
}

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
