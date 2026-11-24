<template>
  <ClinicalModal
    id="portal-reconsent-modal"
    overlay-id="portal-reconsent-modal-backdrop"
    :is-open="consent.reconsentModalOpen"
    :teleport="false"
    :restore-focus="false"
    :show-close="false"
    initial-focus="#reconsent-sign-password"
    size="md"
    dialog-class="modal reconsent-modal-dialog"
    aria-labelledby="portal-reconsent-modal-title"
    @close="closeReconsentModal"
  >
    <template #header>
      <h2 id="portal-reconsent-modal-title" class="modal-title">
        Updated Study Consent Review
      </h2>
    </template>

    <form id="reconsent-modal-form" @submit.prevent="submitReconsentSignature">
      <p class="reconsent-modal-intro">
        Your study consent form has been updated. Please review what changed
        below and sign to continue with your study surveys.
      </p>

      <ModalErrorAlert
        id="reconsent-modal-error-banner"
        :error="consent.reconsentModalError"
        :auto-focus="false"
        retry-label="Re-enter & Retry"
        dismiss-label="Dismiss"
        style="display: block"
        @retry="focusReconsentPassword"
        @dismiss="dismissReconsentError"
      >
        <span>{{ consent.reconsentModalError }}</span>
        <span
          v-if="consent.reconsentNeedsPasswordReentry"
          class="reconsent-retry-guidance"
        >
          Re-enter your account password below and select Sign and Confirm
          Re-Consent to retry. Your comprehension answers and signing reason
          have been kept.
        </span>
      </ModalErrorAlert>

      <div id="reconsent-change-summary" class="card reconsent-summary-card">
        <h3 class="reconsent-section-heading">
          What Changed in Your Study Consent:
        </h3>
        <p class="reconsent-summary-body">
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
        class="reconsent-redlines-section"
      >
        <h3 class="reconsent-section-heading">Highlighted Clause Redlines</h3>
        <div
          v-for="clause in clauseDiffs"
          :key="clause.id"
          class="reconsent-clause-diff-item"
          :data-change-type="clause.change_type"
        >
          <div class="reconsent-clause-header">
            <strong class="reconsent-clause-title">{{
              clause.clause_title
            }}</strong>
            <span class="reconsent-diff-badge">
              {{ formatChangeTypeLabel(clause.change_type) }}
            </span>
          </div>
          <p v-if="clause.previous_text" class="reconsent-clause-removed">
            <span class="sr-only">Previous wording: </span>
            <del>{{ clause.previous_text }}</del>
          </p>
          <p v-if="clause.updated_text" class="reconsent-clause-added">
            <span class="sr-only">Updated wording: </span>
            <ins class="reconsent-ins">{{ clause.updated_text }}</ins>
          </p>
        </div>
      </section>

      <section
        id="reconsent-comprehension-section"
        aria-label="Updated consent comprehension check"
        class="reconsent-comprehension-card"
      >
        <h3 class="reconsent-section-heading">Quick Understanding Check</h3>
        <fieldset
          v-for="question in comprehensionQuestions"
          :key="question.id"
          class="reconsent-question-group"
        >
          <legend class="reconsent-question-legend">
            {{ question.text }}
          </legend>
          <label
            v-for="(option, optIndex) in question.options"
            :key="`${question.id}_${optIndex}`"
            :for="`${question.id}_opt_${optIndex}`"
            class="reconsent-option-label"
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
        <div class="reconsent-password-label-row">
          <label
            id="reconsent-sign-password-label"
            for="reconsent-sign-password"
            >Account Password</label
          >
          <span
            v-if="consent.reconsentNeedsPasswordReentry"
            id="reconsent-password-reentry-hint"
            class="reconsent-password-reentry-cue"
          >
            Cleared after last attempt — please re-enter
          </span>
        </div>
        <input
          id="reconsent-sign-password"
          v-model="consent.reconsentForm.password"
          v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
          :aria-describedby="
            consent.reconsentNeedsPasswordReentry
              ? 'reconsent-password-reentry-hint'
              : undefined
          "
          placeholder="Enter your account password..."
          required
          @input="onPasswordInput"
        />
      </div>
    </form>

    <template #footer>
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
        form="reconsent-modal-form"
        class="btn btn-primary"
      >
        Sign and Confirm Re-Consent
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { computed } from "vue";
import {
  ClinicalModal,
  ModalErrorAlert,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
  requiresCustomSignatureReason,
} from "ui";

import {
  closeReconsentModal,
  resolveReconsentClauseDiffs,
  resolveReconsentComprehensionQuestions,
  submitReconsentSignature,
} from "../features/reconsent.js";
import { useConsentStore } from "../stores/consent.js";

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

function onPasswordInput(event) {
  if (event?.target?.value) {
    consent.reconsentNeedsPasswordReentry = false;
  }
}

function focusReconsentPassword() {
  const pwdEl =
    typeof document !== "undefined"
      ? document.getElementById("reconsent-sign-password")
      : null;
  if (pwdEl && typeof pwdEl.focus === "function") {
    pwdEl.focus();
  }
}

function dismissReconsentError() {
  consent.reconsentModalError = "";
  focusReconsentPassword();
}
</script>

<style scoped>
:deep(.reconsent-modal-dialog) {
  max-width: 600px;
}

.reconsent-modal-intro {
  margin: 0 0 12px 0;
  font-size: 0.875rem;
  color: var(--semantic-color-text);
}

.reconsent-retry-guidance {
  display: block;
  margin-top: 4px;
  font-size: 0.8125rem;
  font-weight: 500;
  color: var(--semantic-color-error-text-deep);
}

.reconsent-summary-card {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  padding: 12px 16px;
  margin-bottom: 14px;
  border-radius: var(--radius-sm);
}

.reconsent-section-heading {
  margin: 0 0 8px 0;
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.reconsent-summary-body {
  margin: 0;
  font-size: 0.8125rem;
  color: var(--semantic-color-text-muted);
}

.reconsent-redlines-section {
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-sm);
  padding: 12px 16px;
  margin-bottom: 14px;
  background-color: var(--semantic-color-surface);
}

.reconsent-clause-diff-item {
  padding: 10px 12px;
  border-radius: var(--radius-xs);
  border: 1px solid var(--semantic-color-border);
  margin-bottom: 8px;
}

.reconsent-clause-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 6px;
}

.reconsent-clause-title {
  font-size: 0.8125rem;
}

.reconsent-diff-badge {
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  padding: 2px 6px;
  border-radius: var(--radius-xs);
  background-color: var(--semantic-color-surface-muted);
}

.reconsent-clause-removed {
  margin: 0 0 6px 0;
  font-size: 0.8125rem;
  padding: 6px 8px;
  border-radius: var(--radius-xs);
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.reconsent-clause-added {
  margin: 0;
  font-size: 0.8125rem;
  padding: 6px 8px;
  border-radius: var(--radius-xs);
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}

.reconsent-ins {
  text-decoration: none;
  font-weight: 600;
}

.reconsent-comprehension-card {
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-sm);
  padding: 12px 16px;
  margin-bottom: 16px;
  background-color: var(--semantic-color-surface-muted);
}

.reconsent-question-group {
  border: none;
  padding: 0;
  margin: 0 0 12px 0;
}

.reconsent-question-legend {
  font-size: 0.8125rem;
  font-weight: 600;
  margin-bottom: 6px;
  color: var(--semantic-color-text-dark);
}

.reconsent-option-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.875rem;
  min-height: 44px;
  padding: 6px 8px;
  cursor: pointer;
}

.reconsent-password-label-row {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 6px;
}

.reconsent-password-reentry-cue {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-error-text-dark);
}
</style>
