// Subject portal entry point (loaded by index.html). The portal lives in
// focused modules; this file starts it on DOMContentLoaded and re-exports its
// public API, which the tests import from here.
//
//   api/        gateway dispatch and fetch helpers
//   bootstrap/  initializeApp, mounting, control bindings
//   data/       sandbox mock data
//   features/   one module per portal feature (tasks, consent, sync, ...)
//   router/     routes (one per view) and URL <-> view synchronisation
//   stores/     Pinia stores and the `state` facade over them
//   widgets/    HTML-string form widgets, field errors, toast
//   views/, components/, App.vue   the Vue shell and its views
import { initializeApp } from "./bootstrap/initialize.js";
import { unmountApp } from "./bootstrap/mount.js";
import {
  dispatchApi,
  fetchAssignedInstruments,
  fetchAssignments,
  fetchCompliance,
  fetchInstrument,
  fetchNotifications,
  isAuthenticatedSession,
} from "./api/client.js";
import {
  ensurePortalSessionToken,
  expirePortalSession,
  login,
  logout,
  portalSessionCoordinator,
  redeemActivationCode,
  refreshPortalSessionOnUnauthorized,
} from "./features/auth.js";
import { renderCompliance } from "./features/compliance.js";
import {
  loadConsentDetails,
  renderConsentUI,
  submitConsentAnswers,
  verifyBiometricLiveness,
} from "./features/consent.js";
import { acknowledgeNotification, renderInbox } from "./features/inbox.js";
import { showView } from "./features/navigation.js";
import {
  MAX_PIN_UNLOCK_ATTEMPTS,
  PENDING_OIDC_PIN_RESET_KEY,
  cancelPINReset,
  checkPINWrapper,
  confirmPINReset,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
  reauthenticateForPINReset,
  requestPINReset,
} from "./features/pin.js";
import {
  confirmCancelQuestionnaire,
  dismissCancelQuestionnaire,
  flushActiveQuestionnaireDraft,
  hasUnsavedQuestionnaireDrafts,
  leaveQuestionnaire,
  requestCancelQuestionnaire,
  saveActiveDraftProgress,
  saveAndLeaveQuestionnaire,
  startQuestionnaire,
  validateActiveQuestionnaire,
} from "./features/questionnaire.js";
import {
  checkPendingReconsent,
  closeReconsentModal,
  openReconsentModal,
  submitReconsentSignature,
} from "./features/reconsent.js";
import {
  COORDINATOR_QUARANTINE_STEPS,
  DEFAULT_SITE_CONTACT,
  SITE_CONTACT_STORAGE_KEY,
  formatAnswerPairs,
  formatAnswersSummary,
  getInstrumentDisplayName,
  getSiteSupportContact,
  getSyncStatusLabel,
  hydrateSiteSupportContact,
  humanizeValidationError,
  maskParticipantIdentifier,
  resolveQuestionLabel,
} from "./features/site-support.js";
import {
  closeSignatureModal,
  openSignatureModal,
  verifyAndSubmitSignature,
} from "./features/signature.js";
import { syncOfflineQueue } from "./features/sync.js";
import {
  checkOnline,
  describeQueuedSync,
  emitSyncLifecycleEvent,
  refreshSubmissionsState,
  renderSyncQueueList,
  resetSyncRetryInfo,
} from "./features/sync-status.js";
import { hydrateDraftPresence, renderTasks } from "./features/tasks.js";
import {
  closeDisburseModal,
  disburseAvailableBalance,
  extractReceiptOcr,
  loadParticipantWallet,
  openDisburseModal,
  openManualExpenseEntry,
  resetExpenseClaimForm,
  submitExpenseClaim,
} from "./features/wallet.js";
import { isScreenAvailable } from "./router/availability.js";
import { state } from "./stores/state.js";
import {
  clearAllSubmissions,
  flagLockedEntriesForPinReset,
  getDraftAssignmentIds,
} from "./sync-queue.js";
import {
  formatSignedWalletCurrency,
  formatWalletCurrency,
  normalizeCurrencyCode,
} from "./utils/currency.js";
import { isDemoBuild, isDemoModeActive } from "./utils/demoMode.js";
import { markFieldInvalid } from "./widgets/field-errors.js";

// Auto-run on load in DOM environments
if (typeof document !== "undefined") {
  if (
    import.meta.env?.MODE === "test" ||
    import.meta.env?.VITEST ||
    window.__MOCK_TEST_ENV__ ||
    document.readyState === "loading"
  ) {
    document.addEventListener("DOMContentLoaded", initializeApp);
  } else if (
    (typeof window === "undefined" || !window.__MOCK_TEST_ENV__) &&
    document.getElementById("app")
  ) {
    void initializeApp();
  }
}

export {
  state,
  showView,
  startQuestionnaire,
  validateActiveQuestionnaire,
  saveActiveDraftProgress,
  flushActiveQuestionnaireDraft,
  hasUnsavedQuestionnaireDrafts,
  saveAndLeaveQuestionnaire,
  leaveQuestionnaire,
  requestCancelQuestionnaire,
  dismissCancelQuestionnaire,
  confirmCancelQuestionnaire,
  verifyAndSubmitSignature,
  acknowledgeNotification,
  initializeApp,
  checkOnline,
  renderSyncQueueList,
  syncOfflineQueue,
  clearAllSubmissions,
  flagLockedEntriesForPinReset,
  getDraftAssignmentIds,
  hydrateDraftPresence,
  dispatchApi,
  fetchAssignments,
  fetchAssignedInstruments,
  fetchInstrument,
  fetchCompliance,
  fetchNotifications,
  isAuthenticatedSession,
  renderTasks,
  renderCompliance,
  renderInbox,
  loadConsentDetails,
  renderConsentUI,
  submitConsentAnswers,
  verifyBiometricLiveness,
  openSignatureModal,
  closeSignatureModal,
  markFieldInvalid,
  login,
  logout,
  redeemActivationCode,
  expirePortalSession,
  portalSessionCoordinator,
  ensurePortalSessionToken,
  refreshPortalSessionOnUnauthorized,
  refreshSubmissionsState,
  checkPINWrapper,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
  requestPINReset,
  cancelPINReset,
  confirmPINReset,
  reauthenticateForPINReset,
  MAX_PIN_UNLOCK_ATTEMPTS,
  PENDING_OIDC_PIN_RESET_KEY,
  openReconsentModal,
  closeReconsentModal,
  checkPendingReconsent,
  submitReconsentSignature,
  emitSyncLifecycleEvent,
  resetSyncRetryInfo,
  unmountApp,
  describeQueuedSync,
  extractReceiptOcr,
  openManualExpenseEntry,
  resetExpenseClaimForm,
  submitExpenseClaim,
  loadParticipantWallet,
  openDisburseModal,
  closeDisburseModal,
  disburseAvailableBalance,
  normalizeCurrencyCode,
  formatWalletCurrency,
  formatSignedWalletCurrency,
  isDemoBuild,
  isDemoModeActive,
  isScreenAvailable,
  SITE_CONTACT_STORAGE_KEY,
  DEFAULT_SITE_CONTACT,
  COORDINATOR_QUARANTINE_STEPS,
  hydrateSiteSupportContact,
  getSiteSupportContact,
  maskParticipantIdentifier,
  formatAnswerPairs,
  formatAnswersSummary,
  resolveQuestionLabel,
  getInstrumentDisplayName,
  getSyncStatusLabel,
  humanizeValidationError,
};
