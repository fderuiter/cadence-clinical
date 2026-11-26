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
      box-shadow: var(--shadow-md);
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
      <ClinicalIcon name="alert-circle" :size="20" />
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
        <p id="reconsent-banner-desc" style="margin: 0; font-size: 14px">
          Your study consent form has been updated. Please review the updated
          clauses and sign the changes before submitting your next survey.
        </p>
      </div>
    </div>
    <div class="reconsent-banner-actions">
      <a
        id="link-review-consent-clauses"
        href="/consent#consent-clauses-container"
        class="btn btn-secondary reconsent-review-link"
        aria-describedby="reconsent-banner-desc"
        @click.prevent="handleReviewUpdatedClauses"
      >
        Read Updated Consent Form
      </a>
      <button
        id="btn-open-reconsent-modal"
        type="button"
        class="btn btn-primary"
        aria-describedby="reconsent-banner-desc"
        style="
          white-space: normal;
          text-align: center;
          max-width: 100%;
          background-color: var(--semantic-color-warning-action);
          border: 1px solid var(--semantic-color-warning-action-border);
          color: var(--semantic-color-text-on-solid);
        "
        @click="openReconsentModal"
      >
        Review &amp; Sign Now
      </button>
    </div>
  </div>
</template>

<script setup>
import { nextTick } from "vue";
import { ClinicalIcon } from "ui";

import { loadConsentDetails } from "../features/consent.js";
import { showView } from "../features/navigation.js";
import { openReconsentModal } from "../features/reconsent.js";
import { useConsentStore } from "../stores/consent.js";

const consent = useConsentStore();

async function handleReviewUpdatedClauses() {
  showView("view-consent");
  await loadConsentDetails();
  await nextTick();
  const clausesEl = document.getElementById("consent-clauses-container");
  clausesEl?.scrollIntoView?.({ behavior: "smooth", block: "start" });
  clausesEl?.focus?.();
}
</script>
