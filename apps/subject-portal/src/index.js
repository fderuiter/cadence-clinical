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
import { logout } from "./features/auth.js";
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
  checkPINWrapper,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
} from "./features/pin.js";
import {
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
import { renderTasks } from "./features/tasks.js";
import { extractReceiptOcr, submitExpenseClaim } from "./features/wallet.js";
import { state } from "./stores/state.js";
import { clearAllSubmissions } from "./sync-queue.js";
import { markFieldInvalid } from "./widgets/field-errors.js";

// Auto-run on load in DOM environments
if (typeof document !== "undefined") {
  document.addEventListener("DOMContentLoaded", initializeApp);
}

export {
  state,
  showView,
  startQuestionnaire,
  validateActiveQuestionnaire,
  verifyAndSubmitSignature,
  acknowledgeNotification,
  initializeApp,
  checkOnline,
  renderSyncQueueList,
  syncOfflineQueue,
  clearAllSubmissions,
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
  logout,
  refreshSubmissionsState,
  checkPINWrapper,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
  openReconsentModal,
  closeReconsentModal,
  checkPendingReconsent,
  submitReconsentSignature,
  emitSyncLifecycleEvent,
  resetSyncRetryInfo,
  unmountApp,
  describeQueuedSync,
  extractReceiptOcr,
  submitExpenseClaim,
};
