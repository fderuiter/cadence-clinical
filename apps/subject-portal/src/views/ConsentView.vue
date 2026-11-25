<template>
  <section
    id="view-consent"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-consent' }"
    :hidden="ui.currentView !== 'view-consent'"
    :inert="ui.currentView !== 'view-consent' || undefined"
    :role="hasConsentTab ? 'tabpanel' : 'region'"
    :aria-labelledby="hasConsentTab ? 'btn-tab-btn-consent' : undefined"
    :aria-label="hasConsentTab ? undefined : 'My Informed Consent Review'"
    tabindex="0"
  >
    <div class="view-header">
      <h2 id="consent-view-heading" tabindex="-1">
        My Informed Consent Review
      </h2>
      <p>
        Please read the study details below, complete the comprehension check,
        and submit your digital signature.
      </p>
    </div>

    <!-- Language selector for approved content -->
    <div
      class="card"
      style="
        margin-bottom: 16px;
        padding: 12px 16px;
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 16px;
      "
    >
      <div>
        <label
          for="consent-lang-selector"
          style="font-weight: bold; margin-right: 8px"
          >Select Language:</label
        >
        <select
          id="consent-lang-selector"
          :value="consent.consentLanguage"
          style="
            min-height: var(--touch-target-min);
            padding: var(--spacing-xs) var(--spacing-sm);
            border-radius: 6px;
            border: 1px solid var(--semantic-color-border-control);
            background: var(--semantic-color-surface);
            color: var(--semantic-color-text);
            font-size: var(--font-size-md);
            cursor: pointer;
          "
        >
          <option value="en">English (en)</option>
          <option value="es">Español (es)</option>
          <option value="nl">Nederlands (nl)</option>
          <option value="fr">Français (fr)</option>
        </select>
      </div>
      <span id="consent-status-badge" class="status-pill pending"
        >Pending Check</span
      >
    </div>

    <!-- Loading indicator -->
    <div id="consent-loading" class="loading-state" style="display: none">
      Loading informed consent details...
    </div>

    <!-- Failure container -->
    <div
      id="consent-failure"
      class="card error-state"
      style="
        display: none;
        text-align: center;
        padding: 32px;
        border: 1px solid var(--danger);
        margin-bottom: 16px;
      "
    >
      <p
        style="
          color: var(--danger);
          font-weight: bold;
          font-size: var(--font-size-md);
        "
      >
        Failed to load consent form.
      </p>
      <p
        id="consent-error-msg"
        style="font-size: var(--font-size-sm); margin-bottom: 12px"
      >
        Unknown error
      </p>
      <button id="btn-retry-consent" type="button" class="btn btn-primary">
        Retry
      </button>
    </div>

    <!-- Consent Document Content & Visual Renderer -->
    <div id="consent-content-wrapper" style="display: none">
      <!-- Metadata and Clauses -->
      <div class="card" style="margin-bottom: 24px">
        <h3
          id="consent-template-title"
          style="margin-top: 0; color: var(--semantic-color-text)"
        >
          Informed Consent Form
        </h3>
        <div
          id="consent-metadata-display"
          style="
            font-size: var(--font-size-2xs);
            color: var(--text-muted);
            border-bottom: 1px solid var(--semantic-color-border);
            padding-bottom: 12px;
            margin-bottom: 16px;
          "
        >
          <!-- Dynamically populated template metadata -->
        </div>
        <div
          id="consent-clauses-container"
          tabindex="-1"
          style="
            line-height: 1.6;
            display: flex;
            flex-direction: column;
            gap: 16px;
          "
        >
          <!-- Dynamically populated clauses -->
        </div>
      </div>

      <!-- Comprehension Check Form Section -->
      <div
        id="consent-comprehension-card"
        class="card"
        tabindex="-1"
        style="margin-bottom: 24px"
      >
        <h3 style="margin-top: 0; display: flex; align-items: center; gap: 8px">
          <ClinicalIcon name="book-open" :size="20" />
          <span>Step 1: Quick Understanding Check</span>
        </h3>
        <p
          style="
            font-size: var(--font-size-sm);
            color: var(--text-muted);
            margin-bottom: 16px;
          "
        >
          Please answer these short questions to confirm the key study details,
          benefits, and risks.
        </p>

        <!-- Status Feedback region -->
        <div
          id="comprehension-status-banner"
          style="
            display: none;
            padding: 12px;
            border-radius: 6px;
            margin-bottom: 16px;
            font-size: var(--font-size-sm);
            font-weight: 600;
          "
          role="status"
          aria-live="polite"
        >
          <!-- Success / Failure live statuses -->
        </div>

        <div id="consent-questions-container">
          <!-- Dynamically rendered questions using radio grids -->
        </div>

        <div class="form-actions" style="margin-top: 20px">
          <button
            id="btn-submit-consent-answers"
            type="button"
            class="btn btn-primary"
          >
            Verify and Submit Answers
          </button>
        </div>
      </div>

      <!-- Signature Section -->
      <!-- Camera Identity Check Section -->
      <div
        id="consent-liveness-card"
        class="card"
        tabindex="-1"
        style="margin-bottom: 24px"
      >
        <h3 style="margin-top: 0; display: flex; align-items: center; gap: 8px">
          <ClinicalIcon name="video" :size="20" />
          <span>Step 2: Quick Camera Check</span>
        </h3>
        <p
          style="
            font-size: var(--font-size-sm);
            color: var(--text-muted);
            margin-bottom: 16px;
          "
        >
          Please complete a quick camera check so we can confirm it is really
          you signing. Your camera preview stays in temporary memory and no
          photos or videos are saved.
        </p>

        <!-- Status Feedback region -->
        <div
          id="liveness-status-banner"
          style="
            display: none;
            padding: 12px;
            border-radius: 6px;
            margin-bottom: 16px;
            font-size: var(--font-size-sm);
            font-weight: 600;
          "
          role="status"
          aria-live="polite"
        >
          <!-- Success / Failure live statuses -->
        </div>

        <div
          id="liveness-camera-simulator"
          style="
            border: 2px dashed var(--semantic-color-border-control);
            border-radius: 8px;
            padding: 24px;
            text-align: center;
            background: var(--semantic-color-surface-muted);
            margin-bottom: 16px;
          "
        >
          <div style="margin-bottom: 8px">
            <ClinicalIcon name="camera" :size="32" />
          </div>
          <p
            style="margin: 0; font-size: var(--font-size-sm); font-weight: 500"
          >
            Camera Ready for Quick Check
          </p>
          <p
            style="
              margin: 4px 0 0 0;
              font-size: var(--font-size-2xs);
              color: var(--text-muted);
            "
          >
            Center your face in view, then select Complete Camera Check.
          </p>
        </div>

        <div
          class="form-actions"
          style="display: flex; gap: 12px; flex-wrap: wrap"
        >
          <button
            id="btn-verify-liveness"
            type="button"
            class="btn btn-primary"
          >
            Complete Camera Check
          </button>

          <button
            id="btn-liveness-fallback"
            type="button"
            class="btn btn-secondary"
            style="
              border: 1px solid var(--semantic-color-border-control);
              background: var(--semantic-color-surface);
            "
          >
            Verify with Study Coordinator Instead
          </button>
        </div>
      </div>

      <!-- Signature Section -->
      <div id="consent-signature-card" class="card" style="margin-bottom: 24px">
        <h3 style="margin-top: 0; display: flex; align-items: center; gap: 8px">
          <ClinicalIcon name="file-signature" :size="20" />
          <span>Step 3: Electronic Signature</span>
        </h3>
        <p
          style="
            font-size: var(--font-size-sm);
            color: var(--text-muted);
            margin-bottom: 12px;
          "
        >
          By signing this document, you confirm that you have read this informed
          consent, have passed the verification, and agree to participate.
        </p>
        <div
          id="consent-sign-readiness-hint"
          class="consent-readiness-hint"
          role="status"
          aria-live="polite"
        >
          Before you can sign, please finish
          <a
            id="link-jump-comprehension"
            href="#consent-comprehension-card"
            class="consent-step-link"
            >Step 1: Quick Understanding Check</a
          >
          and
          <a
            id="link-jump-liveness"
            href="#consent-liveness-card"
            class="consent-step-link"
            >Step 2: Quick Camera Check</a
          >.
        </div>
        <div class="form-actions">
          <button
            id="btn-trigger-consent-sign"
            type="button"
            class="btn btn-primary"
            aria-describedby="consent-sign-readiness-hint"
            disabled
          >
            Sign Consent Form
          </button>
        </div>
      </div>

      <!-- Signed Confirmation Card -->
      <div
        id="consent-signed-card"
        class="card"
        style="
          display: none;
          margin-bottom: 24px;
          background: var(--semantic-color-success-bg);
          border: 1px solid var(--semantic-color-success);
        "
      >
        <h3
          id="consent-signed-heading"
          tabindex="-1"
          style="
            margin-top: 0;
            color: var(--semantic-color-success);
            display: flex;
            align-items: center;
            gap: 8px;
          "
        >
          <ClinicalIcon name="check" :size="18" />
          <span>Informed Consent Signed &amp; Archived</span>
        </h3>
        <p
          style="
            margin: 0;
            font-size: var(--font-size-sm);
            color: var(--semantic-color-text);
          "
        >
          Your electronic signature has been verified and recorded for this
          study consent version.
        </p>
      </div>
    </div>
  </section>
</template>

<script setup>
import { computed } from "vue";
import { ClinicalIcon } from "ui";

import { isScreenAvailable } from "../router/availability.js";
import { useConsentStore } from "../stores/consent.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const consent = useConsentStore();
const session = useSessionStore();

const hasConsentTab = computed(() => {
  void session.session?.isDemoMode;
  void session.session?.token;
  void session.session?.userId;
  void session.session?.isOfflineMode;
  return isDemoModeActive() || isScreenAvailable("consent");
});
</script>
