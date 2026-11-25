<template>
  <div
    v-if="consent.pendingReconsent"
    id="reconsent-urgent-banner"
    class="card urgent-reconsent-banner"
    :class="{ 'is-expanded': detailsExpanded }"
    role="alert"
  >
    <div class="reconsent-banner-bar">
      <div class="reconsent-banner-summary">
        <ClinicalIcon
          name="alert-circle"
          :size="20"
          class="reconsent-banner-icon"
        />
        <div class="reconsent-banner-text">
          <h3 class="reconsent-banner-heading">
            <span class="reconsent-heading-prefix">Action Needed: </span
            ><span>Updated Study Consent</span>
          </h3>
          <p
            id="reconsent-banner-desc"
            class="reconsent-banner-desc"
            :class="{ 'is-expanded': detailsExpanded }"
          >
            Your study consent form has been updated. Please review the updated
            clauses and sign the changes before submitting your next survey.
          </p>
        </div>
      </div>

      <div class="reconsent-banner-actions">
        <button
          id="btn-reconsent-banner-details"
          type="button"
          class="reconsent-details-toggle"
          :aria-expanded="detailsExpanded ? 'true' : 'false'"
          aria-controls="reconsent-banner-extra"
          @click="detailsExpanded = !detailsExpanded"
        >
          {{ detailsExpanded ? "Hide" : "Details" }}
        </button>

        <a
          id="link-review-consent-clauses"
          href="/consent#consent-clauses-container"
          class="btn btn-secondary reconsent-review-link reconsent-desktop-link"
          aria-describedby="reconsent-banner-desc"
          @click.prevent="handleReviewUpdatedClauses"
        >
          Read Updated Consent Form
        </a>

        <button
          id="btn-open-reconsent-modal"
          type="button"
          class="btn btn-primary reconsent-sign-cta"
          aria-describedby="reconsent-banner-desc"
          @click="openReconsentModal"
        >
          <span class="reconsent-cta-full">Review &amp; Sign Now</span>
          <span class="reconsent-cta-short" aria-hidden="true">Sign Now</span>
        </button>
      </div>
    </div>

    <div
      id="reconsent-banner-extra"
      class="reconsent-banner-extra"
      :class="{ 'is-expanded': detailsExpanded }"
    >
      <p class="reconsent-banner-extra-copy">
        Your study consent form has been updated. Please review the updated
        clauses and sign the changes before submitting your next survey.
      </p>
      <a
        href="/consent#consent-clauses-container"
        class="btn btn-secondary reconsent-review-link"
        aria-describedby="reconsent-banner-desc"
        @click.prevent="handleReviewUpdatedClauses"
      >
        Read Updated Consent Form
      </a>
    </div>
  </div>
</template>

<script setup>
import { ClinicalIcon } from "ui";
import { nextTick, ref } from "vue";

import { loadConsentDetails } from "../features/consent.js";
import { showView } from "../features/navigation.js";
import { openReconsentModal } from "../features/reconsent.js";
import { useConsentStore } from "../stores/consent.js";

const consent = useConsentStore();
const detailsExpanded = ref(false);

async function handleReviewUpdatedClauses() {
  showView("view-consent");
  await loadConsentDetails();
  await nextTick();
  const clausesEl = document.getElementById("consent-clauses-container");
  clausesEl?.scrollIntoView?.({ behavior: "smooth", block: "start" });
  clausesEl?.focus?.();
}
</script>

<style scoped>
.urgent-reconsent-banner {
  border: 2px solid var(--semantic-color-warning-border-strong);
  background-color: var(--semantic-color-warning-bg-light);
  color: var(--semantic-color-warning-text);
  padding: 16px 20px;
  margin-bottom: 20px;
  border-radius: 8px;
  box-shadow: var(--shadow-md);
}

.reconsent-banner-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px 16px;
}

.reconsent-banner-summary {
  display: flex;
  align-items: center;
  gap: 12px;
  flex: 1 1 220px;
  min-width: 0;
}

.reconsent-banner-icon {
  flex-shrink: 0;
  color: var(--semantic-color-warning-text-dark);
}

.reconsent-banner-text {
  min-width: 0;
  flex: 1 1 auto;
}

.reconsent-banner-heading {
  margin: 0 0 4px 0;
  font-size: 16px;
  font-weight: 700;
  color: var(--semantic-color-warning-text-dark);
}

.reconsent-banner-desc {
  margin: 0;
  font-size: 14px;
  color: var(--semantic-color-warning-text);
}

.reconsent-details-toggle {
  display: none;
}

.reconsent-banner-extra {
  display: none;
}

.reconsent-sign-cta {
  white-space: nowrap;
  text-align: center;
  max-width: 100%;
  background-color: var(--semantic-color-warning-action);
  border: 1px solid var(--semantic-color-warning-action-border);
  color: var(--semantic-color-text-on-solid);
}

.reconsent-cta-short {
  display: none;
}

@media (max-width: 1023px), (max-height: 480px) {
  .urgent-reconsent-banner {
    padding: 8px 10px;
    margin-bottom: 12px;
  }

  .reconsent-banner-bar {
    flex-wrap: nowrap;
    gap: 6px;
  }

  .reconsent-banner-summary {
    flex: 1 1 auto;
    gap: 6px;
    min-width: 0;
  }

  .reconsent-banner-heading {
    margin: 0;
    font-size: 0.8125rem;
    line-height: 1.25;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .reconsent-banner-desc {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
  }

  .reconsent-desktop-link {
    display: none;
  }

  .reconsent-banner-actions {
    flex-wrap: nowrap;
    flex-shrink: 0;
    gap: 6px;
  }

  .reconsent-details-toggle {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-height: var(--semantic-control-target-min, 24px);
    min-width: var(--semantic-control-target-min, 24px);
    padding: 4px 8px;
    border-radius: var(--semantic-radius-sm, 6px);
    border: 1px solid var(--semantic-color-warning-border-strong);
    background-color: var(--semantic-color-surface);
    color: var(--semantic-color-warning-text-dark);
    font-family: inherit;
    font-size: 0.75rem;
    font-weight: 700;
    line-height: 1.2;
    cursor: pointer;
    flex-shrink: 0;
  }

  .reconsent-sign-cta {
    min-height: 36px;
    padding: 6px 10px;
    font-size: 0.75rem;
  }

  .reconsent-banner-extra.is-expanded {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 8px;
    margin-top: 8px;
    padding-top: 8px;
    border-top: 1px solid var(--semantic-color-warning-border-strong);
  }

  .reconsent-banner-extra-copy {
    margin: 0;
    font-size: 0.8125rem;
    line-height: 1.4;
    color: var(--semantic-color-warning-text);
  }
}

@media (max-width: 639px) {
  .reconsent-heading-prefix {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
  }

  .reconsent-cta-full {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
  }

  .reconsent-cta-short {
    display: inline;
  }
}
</style>
