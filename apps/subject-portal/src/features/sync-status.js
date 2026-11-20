// Offline sync status: retry bookkeeping, live countdowns, lifecycle events, and queue list.
import { state } from "../stores/state.js";
import { getAllSubmissions } from "../sync-queue.js";
import {
  COORDINATOR_QUARANTINE_STEPS,
  formatAnswerPairs,
  getInstrumentDisplayName,
  getSiteSupportContact,
  getSyncStatusBadgeClass,
  getSyncStatusExplanation,
  getSyncStatusLabel,
  isQuarantinedSubmission,
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

function escapeQueueHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function renderAnswerPairsList(heading, pairs) {
  if (!pairs || pairs.length === 0) return "";
  const items = pairs
    .map(
      ({ label, value }) =>
        `<li><strong>${escapeQueueHtml(label)}:</strong> ${escapeQueueHtml(value)}</li>`
    )
    .join("");
  return `
    <div class="sync-answers-block" style="margin-top: 6px;">
      <div style="font-weight: 600; color: var(--semantic-color-text); margin-bottom: 2px;">${escapeQueueHtml(heading)}</div>
      <ul class="answer-summary-list" style="margin: 0; padding-left: 18px;">${items}</ul>
    </div>
  `;
}

export async function renderSyncQueueList() {
  const listEl = document.getElementById("sync-queue-list");
  if (!listEl) return;

  const all = await getAllSubmissions();
  if (all.length === 0) {
    listEl.innerHTML = `<div class="loading-state" style="padding: 8px 0; text-align: center; color: var(--text-muted);">No saved surveys waiting to send.</div>`;
    return;
  }

  const contact = getSiteSupportContact();

  listEl.innerHTML = all
    .map((item) => {
      const instName = getInstrumentDisplayName(item.diary_id);
      const submittedTime = new Date(item.device_timestamp).toLocaleString();
      const badgeClass = getSyncStatusBadgeClass(item);
      const statusLabel = getSyncStatusLabel(item);
      const statusDesc = getSyncStatusExplanation(
        item,
        describeQueuedSync(state.syncRetryInfo, item)
      );
      let recoveryGuidanceHtml = "";

      if (isQuarantinedSubmission(item)) {
        recoveryGuidanceHtml = `
          <div class="quarantine-guidance">
            <p>${escapeQueueHtml(COORDINATOR_QUARANTINE_STEPS)}</p>
            <div class="quarantine-contact-links">
              <a href="${escapeQueueHtml(contact.phoneHref)}" class="site-contact-inline-link">Call ${escapeQueueHtml(contact.phoneDisplay)}</a>
              <a href="${escapeQueueHtml(contact.emailHref)}" class="site-contact-inline-link">Email Coordinator</a>
            </div>
          </div>
        `;
      } else if (item.status === "DECRYPTION_ERROR" && item.unrecoverable) {
        recoveryGuidanceHtml = `
          <div class="quarantine-guidance">
            <div class="quarantine-contact-links">
              <a href="${escapeQueueHtml(contact.phoneHref)}" class="site-contact-inline-link">Call ${escapeQueueHtml(contact.phoneDisplay)}</a>
              <a href="${escapeQueueHtml(contact.emailHref)}" class="site-contact-inline-link">Email Coordinator</a>
            </div>
          </div>
        `;
      }

      const hideRawAnswers =
        item.status === "DECRYPTION_ERROR" || isQuarantinedSubmission(item);
      const recordedPairs = hideRawAnswers
        ? []
        : formatAnswerPairs(item.answers, item.diary_id);
      let answersDetails = renderAnswerPairsList("Your answers", recordedPairs);

      if (item.resolved_answers && item.status === "MERGED") {
        const mergedPairs = formatAnswerPairs(
          item.resolved_answers,
          item.diary_id
        );
        answersDetails += renderAnswerPairsList(
          "Combined answers",
          mergedPairs
        );
      }

      return `
        <div class="task-item" style="flex-direction: column; align-items: stretch; gap: 8px; padding: 12px; border: 1px solid var(--semantic-color-border); border-radius: 8px; background: rgba(255,255,255,0.02);">
          <div style="display: flex; justify-content: space-between; align-items: center;">
            <div style="display: flex; flex-direction: column;">
              <span style="font-weight: 700; font-size: 14px;">${escapeQueueHtml(instName)}</span>
              <span style="font-size: 11px; color: var(--text-muted);">Saved ${escapeQueueHtml(submittedTime)}</span>
            </div>
            <span class="status-pill ${badgeClass}">${escapeQueueHtml(statusLabel)}</span>
          </div>
          <div style="font-size: 12px; color: var(--text-muted); border-top: 1px dashed var(--semantic-color-border); padding-top: 6px; margin-top: 4px;">
            <p style="margin: 0 0 4px 0;">${escapeQueueHtml(statusDesc)}</p>
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
