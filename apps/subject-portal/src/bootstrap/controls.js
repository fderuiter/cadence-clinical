// Event bindings for the controls the feature modules own. Tab ARIA and
// keyboard navigation live declaratively in TabBar.vue + router/routes.js.
import { isAuthenticatedSession } from "../api/client.js";
import { retryCompliance } from "../features/compliance.js";
import {
  ensureConsentLivenessFresh,
  isConsentLivenessExpired,
  loadConsentDetails,
  submitConsentAnswers,
  verifyBiometricLiveness,
} from "../features/consent.js";
import { retryInbox } from "../features/inbox.js";
import {
  requestCancelQuestionnaire,
  requestQuestionnaireSignature,
  saveAndLeaveQuestionnaire,
} from "../features/questionnaire.js";
import {
  closeSignatureModal,
  openSignatureModal,
  verifyAndSubmitSignature,
} from "../features/signature.js";
import { syncOfflineQueue } from "../features/sync.js";
import { retryTasks } from "../features/tasks.js";
import { isDemoModeActive } from "../utils/demoMode.js";

export function bindPortalControls() {
  // Attach "Sync Now" button listener
  const btnSyncNow = document.getElementById("btn-sync-now");
  if (btnSyncNow && btnSyncNow.dataset.vueBound !== "true") {
    btnSyncNow.addEventListener("click", () => {
      syncOfflineQueue(true, {
        autoDisburse: isAuthenticatedSession() || isDemoModeActive(),
      });
    });
  }

  const btnBack = document.getElementById("btn-back-to-tasks");
  if (btnBack && btnBack.dataset.vueBound !== "true") {
    btnBack.addEventListener("click", saveAndLeaveQuestionnaire);
  }

  const btnCancel = document.getElementById("btn-cancel-questionnaire");
  if (btnCancel && btnCancel.dataset.vueBound !== "true") {
    btnCancel.addEventListener("click", requestCancelQuestionnaire);
  }

  const btnSubmit = document.getElementById("btn-submit-questionnaire");
  if (btnSubmit && btnSubmit.dataset.vueBound !== "true") {
    btnSubmit.addEventListener("click", requestQuestionnaireSignature);
  }

  // Modal actions
  const btnModalCancel = document.getElementById("btn-modal-cancel");
  if (btnModalCancel && btnModalCancel.dataset.vueBound !== "true") {
    btnModalCancel.addEventListener("click", closeSignatureModal);
  }

  const btnModalSign = document.getElementById("btn-modal-sign");
  if (btnModalSign && btnModalSign.dataset.vueBound !== "true") {
    btnModalSign.addEventListener("click", verifyAndSubmitSignature);
  }

  // eConsent visual element binding
  const langSelector = document.getElementById("consent-lang-selector");
  if (langSelector && langSelector.dataset.vueBound !== "true") {
    langSelector.addEventListener("change", () => {
      void loadConsentDetails();
    });
  }

  const btnRetryConsent = document.getElementById("btn-retry-consent");
  if (btnRetryConsent && btnRetryConsent.dataset.vueBound !== "true") {
    btnRetryConsent.addEventListener("click", () => {
      loadConsentDetails();
    });
  }

  const btnSubmitAnswers = document.getElementById(
    "btn-submit-consent-answers"
  );
  if (btnSubmitAnswers && btnSubmitAnswers.dataset.vueBound !== "true") {
    btnSubmitAnswers.addEventListener("click", () => {
      submitConsentAnswers();
    });
  }

  const btnVerifyLiveness = document.getElementById("btn-verify-liveness");
  if (btnVerifyLiveness && btnVerifyLiveness.dataset.vueBound !== "true") {
    btnVerifyLiveness.addEventListener("click", () => {
      verifyBiometricLiveness(false);
    });
  }

  const btnLivenessFallback = document.getElementById("btn-liveness-fallback");
  if (btnLivenessFallback && btnLivenessFallback.dataset.vueBound !== "true") {
    btnLivenessFallback.addEventListener("click", () => {
      verifyBiometricLiveness(true);
    });
  }

  const btnTriggerSign = document.getElementById("btn-trigger-consent-sign");
  if (btnTriggerSign && btnTriggerSign.dataset.vueBound !== "true") {
    btnTriggerSign.addEventListener("click", () => {
      if (isConsentLivenessExpired()) {
        ensureConsentLivenessFresh();
        return;
      }
      openSignatureModal("consent");
    });
  }

  // Retry Handlers
  const btnRetryTasks = document.getElementById("btn-retry-tasks");
  if (btnRetryTasks && btnRetryTasks.dataset.vueBound !== "true") {
    btnRetryTasks.addEventListener("click", retryTasks);
  }

  const btnRetryInbox = document.getElementById("btn-retry-inbox");
  if (btnRetryInbox && btnRetryInbox.dataset.vueBound !== "true") {
    btnRetryInbox.addEventListener("click", retryInbox);
  }

  const btnRetryCompliance = document.getElementById("btn-retry-compliance");
  if (btnRetryCompliance && btnRetryCompliance.dataset.vueBound !== "true") {
    btnRetryCompliance.addEventListener("click", retryCompliance);
  }
}
