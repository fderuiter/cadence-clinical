// Offline sync status: retry bookkeeping, live countdowns, lifecycle events, and queue list.
import { MOCK_INSTRUMENTS } from "../data/mock-data.js";
import { state } from "../stores/state.js";
import { getAllSubmissions } from "../sync-queue.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import {
  COORDINATOR_QUARANTINE_STEPS,
  formatAnswersSummary,
  getSiteSupportContact,
  humanizeValidationError,
} from "./site-support.js";

function stopSyncCountdownTimer() {
  if (state.__syncCountdownTimer) {
    clearInterval(state.__syncCountdownTimer);
    state.__syncCountdownTimer = null;
  }
}

function updateDomSyncStatusText(statusMsg) {
  const statusTextEl =
    typeof document !== "undefined"
      ? document.getElementById("sync-queue-status-text")
      : null;
  if (statusTextEl) {
    statusTextEl.textContent = statusMsg;
  }
}

function startSyncCountdownTimer() {
  stopSyncCountdownTimer();
  const interval = setInterval(() => {
    if (
      !state.syncRetryInfo?.isRetrying ||
      !state.syncRetryInfo?.nextRetryTimestamp
    ) {
      stopSyncCountdownTimer();
      return;
    }

    const remainingMs = state.syncRetryInfo.nextRetryTimestamp - Date.now();
    const remainingSec = Math.max(0, Math.ceil(remainingMs / 1000));
    state.syncRetryInfo.remainingSeconds = remainingSec;

    if (remainingSec > 0) {
      const statusMsg = `Retrying... Attempt ${state.syncRetryInfo.attempt} of ${state.syncRetryInfo.maxRetries} (Next retry in ${remainingSec}s)`;
      state.syncStatusText = statusMsg;
      updateDomSyncStatusText(statusMsg);
      renderSyncQueueList();
    } else {
      stopSyncCountdownTimer();
    }
  }, 1000);

  if (interval && typeof interval.unref === "function") {
    interval.unref();
  }
  state.__syncCountdownTimer = interval;
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

export function emitSyncLifecycleEvent(eventType, detail = {}) {
  const payload = {
    attempt: detail.attempt ?? state.syncRetryInfo.attempt,
    maxRetries: detail.maxRetries ?? state.syncRetryInfo.maxRetries ?? 5,
    nextRetryDelay: detail.nextRetryDelay ?? 0,
    nextRetryTimestamp: detail.nextRetryTimestamp ?? null,
    sequenceNumber: detail.sequenceNumber ?? null,
    queuedCount: detail.queuedCount ?? 0,
    syncedCount: detail.syncedCount ?? 0,
    error: detail.error ?? null,
    timestamp: new Date().toISOString(),
    ...detail,
  };

  if (eventType === "sync:attempt") {
    stopSyncCountdownTimer();
    state.syncRetryInfo.attempt = payload.attempt;
    state.syncRetryInfo.maxRetries = payload.maxRetries;
    state.syncRetryInfo.nextRetryDelay = payload.nextRetryDelay;
    state.syncRetryInfo.nextRetryTimestamp = payload.nextRetryTimestamp;
    state.syncRetryInfo.remainingSeconds = 0;
    state.syncRetryInfo.sequenceNumber = payload.sequenceNumber;
    state.syncRetryInfo.isRetrying = true;

    const statusMsg = `Retrying... Attempt ${payload.attempt} of ${payload.maxRetries}`;
    state.syncStatusText = statusMsg;
    updateDomSyncStatusText(statusMsg);
  } else if (eventType === "sync:retry-scheduled") {
    const delaySec = Math.max(1, Math.ceil(payload.nextRetryDelay / 1000));
    state.syncRetryInfo.attempt = payload.attempt;
    state.syncRetryInfo.maxRetries = payload.maxRetries;
    state.syncRetryInfo.nextRetryDelay = payload.nextRetryDelay;
    state.syncRetryInfo.nextRetryTimestamp = payload.nextRetryTimestamp;
    state.syncRetryInfo.remainingSeconds = delaySec;
    state.syncRetryInfo.sequenceNumber = payload.sequenceNumber;
    state.syncRetryInfo.isRetrying = true;

    const statusMsg = `Retrying... Attempt ${payload.attempt} of ${payload.maxRetries} (Next retry in ${delaySec}s)`;
    state.syncStatusText = statusMsg;
    updateDomSyncStatusText(statusMsg);
    startSyncCountdownTimer();
  } else if (eventType === "sync:success") {
    resetSyncRetryInfo();
  }

  if (
    typeof window !== "undefined" &&
    typeof window.dispatchEvent === "function"
  ) {
    try {
      window.dispatchEvent(new CustomEvent(eventType, { detail: payload }));
    } catch (e) {
      console.warn(`Failed to dispatch ${eventType} event:`, e);
    }
  }

  return payload;
}

export function checkOnline() {
  if (typeof window !== "undefined" && window.__MOCK_TEST_ENV__) {
    return !state.session.isOfflineMode;
  }
  return typeof navigator !== "undefined" && navigator.onLine;
}

export async function renderSyncQueueList() {
  const listEl = document.getElementById("sync-queue-list");
  if (!listEl) return;

  const all = await getAllSubmissions();
  if (all.length === 0) {
    listEl.innerHTML = `<div class="loading-state" style="padding: 8px 0; text-align: center; color: var(--text-muted);">No submission history in queue.</div>`;
    return;
  }

  const contact = getSiteSupportContact();

  listEl.innerHTML = all
    .map((item) => {
      const instName =
        (state.instruments && state.instruments[item.diary_id]?.name) ||
        (isDemoModeActive() && MOCK_INSTRUMENTS[item.diary_id]?.name) ||
        item.diary_id;
      const submittedTime = new Date(item.device_timestamp).toLocaleString();

      let badgeClass = "pending";
      let statusLabel = item.status;
      let statusDesc = "";
      let recoveryGuidanceHtml = "";

      if (item.status === "QUEUED") {
        badgeClass = "pending";
        statusLabel = "QUEUED";
        statusDesc = describeQueuedSync(state.syncRetryInfo, item);
      } else if (
        item.status === "CREATED" ||
        item.status === "UPDATED_CLIENT_WINS"
      ) {
        badgeClass = "completed";
        statusLabel = "SYNCED";
        statusDesc = "Successfully synchronized with clinical database.";
      } else if (item.status === "MERGED") {
        badgeClass = "completed";
        statusLabel = "MERGED";
        statusDesc =
          "Conflict resolved: Local and server entries were combined.";
      } else if (item.status === "IGNORED_SERVER_WINS") {
        badgeClass = "overdue";
        statusLabel = "CONFLICT (Ignored)";
        statusDesc =
          "Conflict resolved: Server data was preserved; local entry archived.";
      } else if (
        item.status === "QUARANTINED" ||
        item.status === "LOCAL_QUARANTINED"
      ) {
        badgeClass = "overdue";
        statusLabel = "QUARANTINED";
        statusDesc = humanizeValidationError(item.error);
        recoveryGuidanceHtml = `
          <div class="quarantine-guidance">
            <p>${COORDINATOR_QUARANTINE_STEPS}</p>
            <div class="quarantine-contact-links">
              <a href="${contact.phoneHref}" class="site-contact-inline-link">Call ${contact.phoneDisplay}</a>
              <a href="${contact.emailHref}" class="site-contact-inline-link">Email Coordinator</a>
            </div>
          </div>
        `;
      } else if (item.status === "DECRYPTION_ERROR") {
        badgeClass = "overdue";
        statusLabel = item.unrecoverable
          ? "LOCKED (RESET)"
          : "DECRYPTION_ERROR";
        statusDesc = item.unrecoverable
          ? humanizeValidationError(item.error)
          : "Locked — enter your security PIN to decrypt queued answers.";
        if (item.unrecoverable) {
          recoveryGuidanceHtml = `
            <div class="quarantine-guidance">
              <div class="quarantine-contact-links">
                <a href="${contact.phoneHref}" class="site-contact-inline-link">Call ${contact.phoneDisplay}</a>
                <a href="${contact.emailHref}" class="site-contact-inline-link">Email Coordinator</a>
              </div>
            </div>
          `;
        }
      }

      const hideRawAnswers =
        item.status === "DECRYPTION_ERROR" ||
        item.status === "QUARANTINED" ||
        item.status === "LOCAL_QUARANTINED";
      const safeAnswers = hideRawAnswers
        ? ""
        : formatAnswersSummary(item.answers);
      let answersDetails = safeAnswers
        ? `<strong>Recorded Values:</strong> <code class="data-code">${safeAnswers}</code>`
        : "";

      if (item.resolved_answers && item.status === "MERGED") {
        const mergedSummary = formatAnswersSummary(item.resolved_answers);
        if (mergedSummary) {
          answersDetails += `<br/><strong>Merged Result:</strong> <code class="data-code">${mergedSummary}</code>`;
        }
      }

      return `
        <div class="task-item" style="flex-direction: column; align-items: stretch; gap: 8px; padding: 12px; border: 1px solid var(--border-color); border-radius: 8px; background: rgba(255,255,255,0.02);">
          <div style="display: flex; justify-content: space-between; align-items: center;">
            <div style="display: flex; flex-direction: column;">
              <span style="font-weight: 700; font-size: 14px;">${instName}</span>
              <span style="font-size: 11px; color: var(--text-muted);">Seq: #${item.sequence_number} | Device Time: ${submittedTime}</span>
            </div>
            <span class="status-pill ${badgeClass}">${statusLabel}</span>
          </div>
          <div style="font-size: 12px; color: var(--text-muted); border-top: 1px dashed var(--border-color); padding-top: 6px; margin-top: 4px;">
            <p style="margin: 0 0 4px 0;">${statusDesc}</p>
            ${recoveryGuidanceHtml}
            ${answersDetails}
          </div>
        </div>
      `;
    })
    .join("");
}

export async function refreshSubmissionsState() {
  try {
    const all = await getAllSubmissions();
    state.submissions = all;
  } catch (err) {
    console.warn("Failed to refresh submissions state:", err);
  }
}

/**
 * Describes a queued submission's sync state: the retry attempt (and the live
 * countdown before the next one) when the sync worker is retrying this item,
 * otherwise that it is waiting for the network.
 */
export function describeQueuedSync(syncRetryInfo, item) {
  if (
    syncRetryInfo &&
    syncRetryInfo.isRetrying &&
    (syncRetryInfo.sequenceNumber === null ||
      syncRetryInfo.sequenceNumber === undefined ||
      syncRetryInfo.sequenceNumber === item.sequence_number)
  ) {
    if (syncRetryInfo.nextRetryDelay > 0 && syncRetryInfo.nextRetryTimestamp) {
      const computedSec = Math.max(
        1,
        Math.ceil((syncRetryInfo.nextRetryTimestamp - Date.now()) / 1000)
      );
      const delaySec =
        typeof syncRetryInfo.remainingSeconds === "number" &&
        syncRetryInfo.remainingSeconds > 0
          ? syncRetryInfo.remainingSeconds
          : computedSec;
      return `Retrying... Attempt ${syncRetryInfo.attempt} of ${syncRetryInfo.maxRetries} (Next retry in ${delaySec}s)`;
    }
    return `Retrying... Attempt ${syncRetryInfo.attempt} of ${syncRetryInfo.maxRetries}`;
  }
  return "Waiting for network connection...";
}
