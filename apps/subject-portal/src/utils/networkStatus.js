// Lightweight network status and sync retry state reset helpers shared by
// cold-boot auth/bootstrap modules without pulling in sync-queue.js.
import { state } from "../stores/state.js";

export function stopSyncCountdownTimer() {
  if (state.__syncCountdownTimer) {
    clearInterval(state.__syncCountdownTimer);
    state.__syncCountdownTimer = null;
  }
}

export function resetSyncRetryInfo() {
  if (state.__syncRetryTimer) {
    clearTimeout(state.__syncRetryTimer);
    state.__syncRetryTimer = null;
  }
  stopSyncCountdownTimer();
  state.syncRetryInfo = {
    attempt: 0,
    maxRetries: 5,
    nextRetryDelay: 0,
    nextRetryTimestamp: null,
    remainingSeconds: 0,
    sequenceNumber: null,
    isRetrying: false,
  };
}

export function checkOnline() {
  if (typeof window !== "undefined" && window.__MOCK_TEST_ENV__) {
    return !state.session.isOfflineMode;
  }
  return typeof navigator !== "undefined" && navigator.onLine;
}
