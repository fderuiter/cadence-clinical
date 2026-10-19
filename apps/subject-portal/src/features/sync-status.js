// Offline sync status: retry bookkeeping, lifecycle events and the queue list.
import { isAuthenticatedSession } from "../api/client.js";
import { MOCK_INSTRUMENTS } from "../data/mock-data.js";
import { state } from "../stores/state.js";
import { getAllSubmissions } from "../sync-queue.js";

export function resetSyncRetryInfo() {
  if (state.__syncRetryTimer) {
    clearTimeout(state.__syncRetryTimer);
    state.__syncRetryTimer = null;
  }
  state.syncRetryInfo = {
    attempt: 0,
    maxRetries: 5,
    nextRetryDelay: 0,
    nextRetryTimestamp: null,
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
    state.syncRetryInfo.attempt = payload.attempt;
    state.syncRetryInfo.maxRetries = payload.maxRetries;
    state.syncRetryInfo.nextRetryDelay = payload.nextRetryDelay;
    state.syncRetryInfo.nextRetryTimestamp = payload.nextRetryTimestamp;
    state.syncRetryInfo.sequenceNumber = payload.sequenceNumber;
    state.syncRetryInfo.isRetrying = true;

    const statusMsg = `Retrying... Attempt ${payload.attempt} of ${payload.maxRetries}`;
    state.syncStatusText = statusMsg;
    const statusTextEl =
      typeof document !== "undefined"
        ? document.getElementById("sync-queue-status-text")
        : null;
    if (statusTextEl) {
      statusTextEl.textContent = statusMsg;
    }
  } else if (eventType === "sync:retry-scheduled") {
    state.syncRetryInfo.attempt = payload.attempt;
    state.syncRetryInfo.maxRetries = payload.maxRetries;
    state.syncRetryInfo.nextRetryDelay = payload.nextRetryDelay;
    state.syncRetryInfo.nextRetryTimestamp = payload.nextRetryTimestamp;
    state.syncRetryInfo.sequenceNumber = payload.sequenceNumber;
    state.syncRetryInfo.isRetrying = true;

    const delaySec = Math.max(1, Math.ceil(payload.nextRetryDelay / 1000));
    const statusMsg = `Retrying... Attempt ${payload.attempt} of ${payload.maxRetries} (Next retry in ${delaySec}s)`;
    state.syncStatusText = statusMsg;
    const statusTextEl =
      typeof document !== "undefined"
        ? document.getElementById("sync-queue-status-text")
        : null;
    if (statusTextEl) {
      statusTextEl.textContent = statusMsg;
    }
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

  listEl.innerHTML = all
    .map((item) => {
      const instName =
        (state.instruments && state.instruments[item.diary_id]?.name) ||
        (!isAuthenticatedSession() && MOCK_INSTRUMENTS[item.diary_id]?.name) ||
        item.diary_id;
      const submittedTime = new Date(item.device_timestamp).toLocaleString();

      let badgeClass = "pending";
      let statusLabel = item.status;
      let statusDesc = "";

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
        badgeClass = "pending";
        statusLabel = "QUARANTINED";
        statusDesc =
          item.error ||
          "Quarantined: Under review by clinical trial managers due to validation errors.";
      } else if (item.status === "DECRYPTION_ERROR") {
        badgeClass = "pending";
        statusLabel = "DECRYPTION_ERROR";
        statusDesc = item.error || "Decryption failed: Secure key cleared.";
      }

      let answersDetails = `<strong>Local Answers:</strong> <code style="background: rgba(0,0,0,0.2); padding: 2px 4px; border-radius: 4px;">${JSON.stringify(item.answers)}</code>`;
      if (item.resolved_answers && item.status === "MERGED") {
        answersDetails += `<br/><strong>Merged Result:</strong> <code style="background: rgba(0,0,0,0.2); padding: 2px 4px; border-radius: 4px;">${JSON.stringify(item.resolved_answers)}</code>`;
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
 * Describes a queued submission's sync state: the retry attempt (and the wait
 * before the next one) when the sync worker is retrying this item, otherwise
 * that it is waiting for the network.
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
      const delaySec = Math.max(
        1,
        Math.ceil((syncRetryInfo.nextRetryTimestamp - Date.now()) / 1000)
      );
      return `Retrying... Attempt ${syncRetryInfo.attempt} of ${syncRetryInfo.maxRetries} (Next retry in ${delaySec}s)`;
    }
    return `Retrying... Attempt ${syncRetryInfo.attempt} of ${syncRetryInfo.maxRetries}`;
  }
  return "Waiting for network connection...";
}
