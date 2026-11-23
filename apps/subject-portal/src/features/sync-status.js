// Offline sync status: retry bookkeeping, live countdowns, lifecycle events, and queue list.
import { createMilestoneCountdown } from "ui";

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

function updateDomSyncStatusText(statusMsg) {
  const statusTextEl =
    typeof document !== "undefined"
      ? document.getElementById("sync-queue-status-text")
      : null;
  if (statusTextEl && statusTextEl.textContent !== statusMsg) {
    statusTextEl.textContent = statusMsg;
  }
}

function updateDomRetryCountdownText() {
  const countdownEl =
    typeof document !== "undefined"
      ? document.getElementById("sync-queue-retry-countdown")
      : null;
  if (!countdownEl) return;
  const rem = state.syncRetryInfo?.remainingSeconds || 0;
  if (!state.syncRetryInfo?.isRetrying || rem <= 0) {
    countdownEl.textContent = "";
    return;
  }
  const nextText = state.syncUpdatesPaused
    ? `Automatic retry paused (${rem}s remaining)`
    : `Next automatic retry in ${rem}s`;
  if (countdownEl.textContent !== nextText) {
    countdownEl.textContent = nextText;
  }
}

function patchActiveSyncCountdownDescriptions() {
  if (typeof document === "undefined") return;
  const listEl = document.getElementById("sync-queue-list");
  if (!listEl) return;
  const seq = state.syncRetryInfo?.sequenceNumber;
  Array.from(listEl.querySelectorAll(".task-item[data-sync-item-key]")).forEach(
    (itemEl) => {
      const itemSeq = itemEl.getAttribute("data-sync-seq");
      const matchesTarget =
        seq === null ||
        seq === undefined ||
        (itemSeq !== null && String(itemSeq) === String(seq));
      if (!matchesTarget) return;
      const statusKind = itemEl.getAttribute("data-sync-status-kind") || "";
      if (statusKind !== "QUEUED") {
        return;
      }
      const descEl = itemEl.querySelector("[data-sync-status-desc]");
      if (descEl) {
        const syntheticItem = {
          sequence_number:
            itemSeq !== null && itemSeq !== "" ? Number(itemSeq) : undefined,
          status: statusKind || "PENDING",
        };
        const nextDesc = getSyncStatusExplanation(
          syntheticItem,
          describeQueuedSync(state.syncRetryInfo, syntheticItem)
        );
        if (descEl.textContent !== nextDesc) {
          descEl.textContent = nextDesc;
        }
      }
    }
  );
}

export const syncMilestoneCountdown = createMilestoneCountdown({
  tickIntervalMs: 1000,
  onMilestone: ({ message }) => {
    state.syncMilestoneText = message;
    updateDomSyncStatusText(message);
  },
  onTick: ({ remainingSeconds, attempt, maxRetries }) => {
    if (!state.syncRetryInfo?.isRetrying) return;
    state.syncRetryInfo.remainingSeconds = remainingSeconds;
    if (remainingSeconds > 0 && !state.syncUpdatesPaused) {
      state.syncStatusText = `Retrying... Attempt ${attempt} of ${maxRetries} (Next retry in ${remainingSeconds}s)`;
    }
    updateDomRetryCountdownText();
    patchActiveSyncCountdownDescriptions();
  },
});

function stopSyncCountdownTimer() {
  if (state.__syncCountdownTimer) {
    clearInterval(state.__syncCountdownTimer);
    state.__syncCountdownTimer = null;
  }
  syncMilestoneCountdown.stop();
}

export function setSyncMilestoneStatus(message) {
  const text = String(message ?? "");
  state.syncStatusText = text;
  state.syncMilestoneText = text;
  syncMilestoneCountdown.setMilestone("status", text);
  updateDomSyncStatusText(text);
}

export function pauseSyncUpdates() {
  if (state.syncUpdatesPaused) return true;
  const remMsFromTimestamp =
    state.syncRetryInfo?.isRetrying && state.syncRetryInfo?.nextRetryTimestamp
      ? Math.max(250, state.syncRetryInfo.nextRetryTimestamp - Date.now())
      : 0;

  if (state.__syncRetryTimer) {
    clearTimeout(state.__syncRetryTimer);
    state.__syncRetryTimer = null;
  }
  if (state.__pendingSyncRetry && remMsFromTimestamp > 0) {
    state.__pendingSyncRetry = {
      ...state.__pendingSyncRetry,
      remainingDelayMs: remMsFromTimestamp,
    };
  }

  state.syncUpdatesPaused = true;
  syncMilestoneCountdown.pause("Automatic sync updates paused.");
  if (state.syncRetryInfo?.isRetrying) {
    const frozenSec =
      syncMilestoneCountdown.remainingSeconds.value ||
      state.syncRetryInfo.remainingSeconds ||
      Math.max(1, Math.ceil(remMsFromTimestamp / 1000));
    state.syncRetryInfo.remainingSeconds = frozenSec;
    state.syncStatusText = `Automatic sync updates paused (${frozenSec}s remaining).`;
  }
  updateDomRetryCountdownText();
  patchActiveSyncCountdownDescriptions();
  return true;
}

export function resumeSyncUpdates() {
  if (!state.syncUpdatesPaused) return false;
  state.syncUpdatesPaused = false;

  const retryActive = Boolean(state.syncRetryInfo?.isRetrying);
  const remMs =
    syncMilestoneCountdown.remainingMs.value > 0
      ? syncMilestoneCountdown.remainingMs.value
      : state.__pendingSyncRetry?.remainingDelayMs || 0;

  const resumeMilestone = retryActive
    ? `Automatic sync updates resumed. Retrying... Attempt ${state.syncRetryInfo.attempt} of ${state.syncRetryInfo.maxRetries}`
    : "Automatic sync updates resumed.";

  if (retryActive && remMs > 0) {
    const nextTimestamp = Date.now() + remMs;
    state.syncRetryInfo.nextRetryTimestamp = nextTimestamp;
    state.syncRetryInfo.remainingSeconds = Math.max(1, Math.ceil(remMs / 1000));
    state.syncStatusText = `Retrying... Attempt ${state.syncRetryInfo.attempt} of ${state.syncRetryInfo.maxRetries} (Next retry in ${state.syncRetryInfo.remainingSeconds}s)`;
    syncMilestoneCountdown.resume(resumeMilestone);

    if (state.__pendingSyncRetry?.runRetry) {
      if (state.__syncRetryTimer) {
        clearTimeout(state.__syncRetryTimer);
      }
      const timer = setTimeout(() => {
        const pending = state.__pendingSyncRetry;
        state.__pendingSyncRetry = null;
        state.__syncRetryTimer = null;
        pending?.runRetry?.();
      }, remMs);
      if (timer && typeof timer.unref === "function") {
        timer.unref();
      }
      state.__syncRetryTimer = timer;
    }
  } else {
    syncMilestoneCountdown.resume(resumeMilestone);
  }

  updateDomRetryCountdownText();
  patchActiveSyncCountdownDescriptions();
  return false;
}

export function toggleSyncUpdatesPaused() {
  return state.syncUpdatesPaused ? resumeSyncUpdates() : pauseSyncUpdates();
}

export function resetSyncRetryInfo() {
  if (state.__syncRetryTimer) {
    clearTimeout(state.__syncRetryTimer);
    state.__syncRetryTimer = null;
  }
  state.__pendingSyncRetry = null;
  state.syncUpdatesPaused = false;
  syncMilestoneCountdown.reset();
  if (state.__syncCountdownTimer) {
    clearInterval(state.__syncCountdownTimer);
    state.__syncCountdownTimer = null;
  }
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
    nextRetryDelay: detail.nextRetryDelay ?? detail.delayMs ?? 0,
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

    const milestoneMsg = `Retrying... Attempt ${payload.attempt} of ${payload.maxRetries}`;
    state.syncStatusText = milestoneMsg;
    syncMilestoneCountdown.announceAttempt({
      attempt: payload.attempt,
      maxRetries: payload.maxRetries,
      message: milestoneMsg,
    });
    updateDomRetryCountdownText();
    patchActiveSyncCountdownDescriptions();
  } else if (eventType === "sync:retry-scheduled") {
    const resolvedDelayMs =
      payload.nextRetryDelay > 0
        ? payload.nextRetryDelay
        : payload.nextRetryTimestamp
          ? Math.max(0, payload.nextRetryTimestamp - Date.now())
          : 0;
    const delaySec = Math.max(1, Math.ceil(resolvedDelayMs / 1000));
    state.syncRetryInfo.attempt = payload.attempt;
    state.syncRetryInfo.maxRetries = payload.maxRetries;
    state.syncRetryInfo.nextRetryDelay = resolvedDelayMs;
    state.syncRetryInfo.nextRetryTimestamp =
      payload.nextRetryTimestamp ?? Date.now() + resolvedDelayMs;
    state.syncRetryInfo.remainingSeconds = delaySec;
    state.syncRetryInfo.sequenceNumber = payload.sequenceNumber;
    state.syncRetryInfo.isRetrying = true;

    state.syncStatusText = state.syncUpdatesPaused
      ? `Automatic sync updates paused (${delaySec}s remaining).`
      : `Retrying... Attempt ${payload.attempt} of ${payload.maxRetries} (Next retry in ${delaySec}s)`;

    const milestoneMsg = `Retrying... Attempt ${payload.attempt} of ${payload.maxRetries}`;
    if (state.syncUpdatesPaused && !syncMilestoneCountdown.isPaused.value) {
      syncMilestoneCountdown.pause("Automatic sync updates paused.");
    }
    syncMilestoneCountdown.start({
      durationMs: resolvedDelayMs,
      attempt: payload.attempt,
      maxRetries: payload.maxRetries,
      milestoneMessage: state.syncUpdatesPaused ? null : milestoneMsg,
    });
    updateDomRetryCountdownText();
    patchActiveSyncCountdownDescriptions();
  } else if (eventType === "sync:success") {
    const prevStatus = state.syncStatusText;
    if (state.__syncRetryTimer) {
      clearTimeout(state.__syncRetryTimer);
      state.__syncRetryTimer = null;
    }
    state.__pendingSyncRetry = null;
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
    if (prevStatus) {
      syncMilestoneCountdown.complete(prevStatus);
    }
    updateDomRetryCountdownText();
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

function getSyncQueueItemKey(item, fallbackIdx) {
  if (item?.sequence_number !== undefined && item?.sequence_number !== null) {
    return `seq:${item.sequence_number}`;
  }
  if (item?.id) return `id:${item.id}`;
  if (item?.assignment_id) {
    return `assign:${item.assignment_id}:${item.device_timestamp || fallbackIdx}`;
  }
  return `idx:${fallbackIdx}:${item?.device_timestamp || ""}`;
}

function buildRecoveryGuidanceData(item, contact) {
  if (isQuarantinedSubmission(item)) {
    return {
      signature: `quarantined|${contact.phoneHref}|${contact.phoneDisplay}|${contact.emailHref}`,
      html: `
          <div class="quarantine-guidance">
            <p>${escapeQueueHtml(COORDINATOR_QUARANTINE_STEPS)}</p>
            <div class="quarantine-contact-links">
              <a href="${escapeQueueHtml(contact.phoneHref)}" class="site-contact-inline-link">Call ${escapeQueueHtml(contact.phoneDisplay)}</a>
              <a href="${escapeQueueHtml(contact.emailHref)}" class="site-contact-inline-link">Email Coordinator</a>
            </div>
          </div>
        `,
    };
  }
  if (item.status === "DECRYPTION_ERROR" && item.unrecoverable) {
    return {
      signature: `decryption_unrecoverable|${contact.phoneHref}|${contact.phoneDisplay}|${contact.emailHref}`,
      html: `
          <div class="quarantine-guidance">
            <div class="quarantine-contact-links">
              <a href="${escapeQueueHtml(contact.phoneHref)}" class="site-contact-inline-link">Call ${escapeQueueHtml(contact.phoneDisplay)}</a>
              <a href="${escapeQueueHtml(contact.emailHref)}" class="site-contact-inline-link">Email Coordinator</a>
            </div>
          </div>
        `,
    };
  }
  return { signature: "none", html: "" };
}

function createOrPatchSyncQueueItem(doc, existingEl, item, key, contact) {
  const instName = getInstrumentDisplayName(item.diary_id);
  const submittedTime = new Date(item.device_timestamp).toLocaleString();
  const savedLabel = `Saved ${submittedTime}`;
  const badgeClass = `status-pill ${getSyncStatusBadgeClass(item)}`;
  const statusLabel = getSyncStatusLabel(item);
  const statusDesc = getSyncStatusExplanation(
    item,
    describeQueuedSync(state.syncRetryInfo, item)
  );
  const statusKind = isQuarantinedSubmission(item)
    ? "QUARANTINED"
    : String(item.status || "PENDING");
  const seqStr =
    item.sequence_number !== undefined && item.sequence_number !== null
      ? String(item.sequence_number)
      : "";
  const recovery = buildRecoveryGuidanceData(item, contact);

  const hideRawAnswers =
    item.status === "DECRYPTION_ERROR" || isQuarantinedSubmission(item);
  const recordedPairs = hideRawAnswers
    ? []
    : formatAnswerPairs(item.answers, item.diary_id);
  let answersDetails = renderAnswerPairsList("Your answers", recordedPairs);

  if (item.resolved_answers && item.status === "MERGED") {
    const mergedPairs = formatAnswerPairs(item.resolved_answers, item.diary_id);
    answersDetails += renderAnswerPairsList("Combined answers", mergedPairs);
  }

  if (existingEl) {
    existingEl.setAttribute("data-sync-seq", seqStr);
    existingEl.setAttribute("data-sync-status-kind", statusKind);

    const instEl = existingEl.querySelector("[data-sync-inst-name]");
    if (instEl && instEl.textContent !== instName) {
      instEl.textContent = instName;
    }
    const savedEl = existingEl.querySelector("[data-sync-saved-time]");
    if (savedEl && savedEl.textContent !== savedLabel) {
      savedEl.textContent = savedLabel;
    }
    const pillEl = existingEl.querySelector("[data-sync-status-pill]");
    if (pillEl) {
      if (pillEl.className !== badgeClass) pillEl.className = badgeClass;
      if (pillEl.textContent !== statusLabel) pillEl.textContent = statusLabel;
    }
    const descEl = existingEl.querySelector("[data-sync-status-desc]");
    if (descEl && descEl.textContent !== statusDesc) {
      descEl.textContent = statusDesc;
    }
    const recoveryEl = existingEl.querySelector("[data-sync-recovery]");
    if (
      recoveryEl &&
      recoveryEl.getAttribute("data-recovery-sig") !== recovery.signature
    ) {
      recoveryEl.setAttribute("data-recovery-sig", recovery.signature);
      recoveryEl.innerHTML = recovery.html;
    }
    const answersEl = existingEl.querySelector("[data-sync-answers]");
    if (
      answersEl &&
      answersEl.getAttribute("data-answers-sig") !== answersDetails
    ) {
      answersEl.setAttribute("data-answers-sig", answersDetails);
      answersEl.innerHTML = answersDetails;
    }
    return existingEl;
  }

  const itemEl = doc.createElement("div");
  itemEl.className = "task-item";
  itemEl.setAttribute("data-sync-item-key", key);
  itemEl.setAttribute("data-sync-seq", seqStr);
  itemEl.setAttribute("data-sync-status-kind", statusKind);
  itemEl.style.cssText =
    "flex-direction: column; align-items: stretch; gap: 8px; padding: 12px; border: 1px solid var(--semantic-color-border); border-radius: 8px; background: var(--semantic-color-surface);";
  itemEl.innerHTML = `
          <div style="display: flex; justify-content: space-between; align-items: center;">
            <div style="display: flex; flex-direction: column;">
              <span data-sync-inst-name style="font-weight: 700; font-size: 14px;">${escapeQueueHtml(instName)}</span>
              <span data-sync-saved-time style="font-size: 0.75rem; font-variant-numeric: tabular-nums; color: var(--text-muted);">${escapeQueueHtml(savedLabel)}</span>
            </div>
            <span data-sync-status-pill class="${badgeClass}">${escapeQueueHtml(statusLabel)}</span>
          </div>
          <div style="font-size: 12px; color: var(--text-muted); border-top: 1px dashed var(--semantic-color-border); padding-top: 6px; margin-top: 4px;">
            <p data-sync-status-desc style="margin: 0 0 4px 0;">${escapeQueueHtml(statusDesc)}</p>
            <div data-sync-recovery>${recovery.html}</div>
            <div data-sync-answers>${answersDetails}</div>
          </div>
        `;
  const recoveryEl = itemEl.querySelector("[data-sync-recovery]");
  if (recoveryEl) {
    recoveryEl.setAttribute("data-recovery-sig", recovery.signature);
  }
  const answersEl = itemEl.querySelector("[data-sync-answers]");
  if (answersEl) {
    answersEl.setAttribute("data-answers-sig", answersDetails);
  }
  return itemEl;
}

export async function renderSyncQueueList(submissionsSnapshot = null) {
  const listEl = document.getElementById("sync-queue-list");
  if (!listEl) return;

  const all = Array.isArray(submissionsSnapshot)
    ? submissionsSnapshot
    : await getAllSubmissions();
  // Re-verify listEl after asyncIndexedDB read in case the DOM unmounted
  const currentListEl = document.getElementById("sync-queue-list");
  if (!currentListEl) return;

  if (all.length === 0) {
    const hasOnlyEmptyPlaceholder =
      currentListEl.children.length === 1 &&
      currentListEl.firstElementChild?.getAttribute("data-sync-empty") ===
        "true";
    if (!hasOnlyEmptyPlaceholder) {
      currentListEl.innerHTML = `<div class="loading-state" data-sync-empty="true" style="padding: 8px 0; text-align: center; color: var(--text-muted);">No saved surveys waiting to send.</div>`;
    }
    return;
  }

  const doc = currentListEl.ownerDocument || document;
  const contact = getSiteSupportContact();
  const existingByKey = new Map();
  Array.from(
    currentListEl.querySelectorAll(".task-item[data-sync-item-key]")
  ).forEach((el) => {
    const key = el.getAttribute("data-sync-item-key");
    if (key && !existingByKey.has(key)) {
      existingByKey.set(key, el);
    }
  });

  Array.from(currentListEl.children).forEach((child) => {
    if (!child.hasAttribute("data-sync-item-key")) {
      child.remove();
    }
  });

  const activeKeys = new Set();
  all.forEach((item, idx) => {
    const key = getSyncQueueItemKey(item, idx);
    activeKeys.add(key);
    const existingEl = existingByKey.get(key) || null;
    const itemEl = createOrPatchSyncQueueItem(
      doc,
      existingEl,
      item,
      key,
      contact
    );
    const currentAtIdx = currentListEl.children[idx] || null;
    if (currentAtIdx !== itemEl) {
      currentListEl.insertBefore(itemEl, currentAtIdx);
    }
  });

  existingByKey.forEach((el, key) => {
    if (!activeKeys.has(key)) {
      el.remove();
    }
  });
}

export async function refreshSubmissionsState(submissionsSnapshot = null) {
  try {
    const all = Array.isArray(submissionsSnapshot)
      ? submissionsSnapshot.slice()
      : await getAllSubmissions();
    state.submissions = all;
    return all;
  } catch (err) {
    console.warn("Failed to refresh submissions state:", err);
    return state.submissions || [];
  }
}

export async function refreshSyncViews(submissionsSnapshot = null) {
  const all = await refreshSubmissionsState(submissionsSnapshot);
  await renderSyncQueueList(all);
  return all;
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
