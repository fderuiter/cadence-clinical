<template>
  <div
    v-if="consent.pendingReconsent"
    id="reconsent-urgent-banner"
    class="card urgent-reconsent-banner"
    style="
      border: 2px solid var(--semantic-color-warning-border-strong);
      background-color: var(--semantic-color-warning-bg-light);
      color: var(--semantic-color-warning-text);
      padding: 16px 20px;
      margin-bottom: 24px;
      border-radius: 8px;
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
    "
    role="alert"
  >
    <div
      style="
        display: flex;
        align-items: center;
        gap: 12px;
        flex: 1 1 180px;
        min-width: 0;
      "
    >
      <AlertTriangle
        :size="22"
        class="reconsent-banner-icon"
        aria-hidden="true"
      />
      <div style="min-width: 0">
        <h3
          style="
            margin: 0 0 4px 0;
            font-size: 16px;
            font-weight: 700;
            color: var(--semantic-color-warning-text-dark);
          "
        >
          Action Needed: Updated Study Consent
        </h3>
        <p style="margin: 0; font-size: 14px">
          Your study consent form has been updated. Please review and sign the
          changes before submitting your next survey.
        </p>
      </div>
    </div>
    <button
      id="btn-open-reconsent-modal"
      type="button"
      class="btn btn-primary"
      style="
        white-space: normal;
        text-align: center;
        max-width: 100%;
        background-color: var(--semantic-color-warning-action);
        border: 1px solid var(--semantic-color-warning-action-border);
        color: var(--semantic-color-surface);
      "
      @click="openReconsentModal"
    >
      Review & Sign Now
    </button>
  </div>
</template>

<script setup>
import { AlertTriangle } from "lucide-vue-next";

import { openReconsentModal } from "../features/reconsent.js";
import { useConsentStore } from "../stores/consent.js";

const consent = useConsentStore();
</script>
