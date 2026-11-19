// Event bindings for the controls the feature modules own. Tab ARIA and
// keyboard navigation live declaratively in TabBar.vue + router/routes.js.
import { retryCompliance } from "../features/compliance.js";
import {
  loadConsentDetails,
  submitConsentAnswers,
  verifyBiometricLiveness,
} from "../features/consent.js";
import { retryInbox } from "../features/inbox.js";
import {
  leaveQuestionnaire,
  requestQuestionnaireSignature,
} from "../features/questionnaire.js";
import {
  closeSignatureModal,
  openSignatureModal,
  verifyAndSubmitSignature,
} from "../features/signature.js";
import { syncOfflineQueue } from "../features/sync.js";
import { retryTasks } from "../features/tasks.js";

export function bindPortalControls() {
  // Attach "Sync Now" button listener
  const btnSyncNow = document.getElementById("btn-sync-now");
  if (btnSyncNow) {
    btnSyncNow.addEventListener("click", () => {
      syncOfflineQueue(true, { autoDisburse: true });
    });
  }

  const btnBack = document.getElementById("btn-back-to-tasks");
  if (btnBack) btnBack.addEventListener("click", leaveQuestionnaire);

  const btnCancel = document.getElementById("btn-cancel-questionnaire");
  if (btnCancel) btnCancel.addEventListener("click", leaveQuestionnaire);

  const btnSubmit = document.getElementById("btn-submit-questionnaire");
  if (btnSubmit)
    btnSubmit.addEventListener("click", requestQuestionnaireSignature);

  // Modal actions
  const btnModalCancel = document.getElementById("btn-modal-cancel");
  if (btnModalCancel)
    btnModalCancel.addEventListener("click", closeSignatureModal);

  const btnModalSign = document.getElementById("btn-modal-sign");
  if (btnModalSign)
    btnModalSign.addEventListener("click", verifyAndSubmitSignature);

  // eConsent visual element binding
  const langSelector = document.getElementById("consent-lang-selector");
  if (langSelector) {
    langSelector.addEventListener("change", () => {
      loadConsentDetails();
    });
  }

  const btnRetryConsent = document.getElementById("btn-retry-consent");
  if (btnRetryConsent) {
    btnRetryConsent.addEventListener("click", () => {
      loadConsentDetails();
    });
  }

  const btnSubmitAnswers = document.getElementById(
    "btn-submit-consent-answers"
  );
  if (btnSubmitAnswers) {
    btnSubmitAnswers.addEventListener("click", () => {
      submitConsentAnswers();
    });
  }

  const btnVerifyLiveness = document.getElementById("btn-verify-liveness");
  if (btnVerifyLiveness) {
    btnVerifyLiveness.addEventListener("click", () => {
      verifyBiometricLiveness(false);
    });
  }

  const btnLivenessFallback = document.getElementById("btn-liveness-fallback");
  if (btnLivenessFallback) {
    btnLivenessFallback.addEventListener("click", () => {
      verifyBiometricLiveness(true);
    });
  }

  const btnTriggerSign = document.getElementById("btn-trigger-consent-sign");
  if (btnTriggerSign) {
    btnTriggerSign.addEventListener("click", () => {
      openSignatureModal("consent");
    });
  }

  // Retry Handlers
  const btnRetryTasks = document.getElementById("btn-retry-tasks");
  if (btnRetryTasks) {
    btnRetryTasks.addEventListener("click", retryTasks);
  }

  const btnRetryInbox = document.getElementById("btn-retry-inbox");
  if (btnRetryInbox) {
    btnRetryInbox.addEventListener("click", retryInbox);
  }

  const btnRetryCompliance = document.getElementById("btn-retry-compliance");
  if (btnRetryCompliance) {
    btnRetryCompliance.addEventListener("click", retryCompliance);
  }
}
