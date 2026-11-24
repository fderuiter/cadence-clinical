// Offline queue reconciliation with the gateway (epro/sync + isolated epro/submit
// fallback), automatic exponential retry, instrument refresh, and auto-disbursement.
import { validateEproPayload } from "usdm-schemas/epro";

import {
  dispatchApi,
  fetchAssignedInstruments,
  fetchInstrument,
  isAuthenticatedSession,
} from "../api/client.js";
import { MOCK_INSTRUMENTS } from "../data/mock-data.js";
import { state } from "../stores/state.js";
import {
  bulkUpdateSubmissionStatuses,
  getQueuedSubmissions,
  saveAssignmentsToDB,
  saveInstrumentsToDB,
  updateSubmissionStatus,
} from "../sync-queue.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import { logAuditRecord } from "./audit.js";
import { renderCompliance } from "./compliance.js";
import { humanizeValidationError } from "./site-support.js";
import {
  checkOnline,
  emitSyncLifecycleEvent,
  refreshSyncViews,
  resetSyncRetryInfo,
  setSyncMilestoneStatus,
} from "./sync-status.js";
import { renderTasks } from "./tasks.js";

const CONFIRMED_SYNC_STATUSES = new Set([
  "CREATED",
  "UPDATED_CLIENT_WINS",
  "MERGED",
  "SYNCED",
]);

function isValidationFailure(err) {
  if (!err) return false;
  if (err.isValidationError || err.status === 400 || err.status === 422) {
    return true;
  }
  const msg = String(err.message || "");
  return /validation|unprocessable|schema|invalid|out of range|http error (400|422)/i.test(
    msg
  );
}

function buildSingleSubmissionPayload(item) {
  return {
    subject_id: item.subject_id,
    diary_id: item.diary_id,
    version_index:
      typeof item.version_index === "number" ? item.version_index : 1,
    device_timestamp: item.device_timestamp,
    answers: item.answers,
    offline_sync_markers: {
      sequence_number: item.sequence_number,
      client_id: item.client_id,
      conflict_strategy: "CLIENT_WINS",
    },
  };
}

async function refreshActiveSurveyDefinitions(queued = []) {
  let refreshed = false;
  const updatedList = [];

  if (isAuthenticatedSession() && state.session.userId) {
    try {
      const assigned = await fetchAssignedInstruments(state.session.userId);
      if (Array.isArray(assigned) && assigned.length > 0) {
        if (!state.instruments) state.instruments = {};
        for (const inst of assigned) {
          if (inst && inst.id) {
            state.instruments[inst.id] = inst;
            updatedList.push(inst);
          }
        }
        refreshed = updatedList.length > 0;
      }
    } catch (err) {
      console.warn(
        "Could not reload assigned instruments during manual sync:",
        err
      );
    }
  }

  if (!refreshed) {
    const candidateIds = new Set();
    for (const assign of state.assignments || []) {
      if (assign?.instrument_id) candidateIds.add(assign.instrument_id);
    }
    for (const q of queued || []) {
      if (q?.diary_id) candidateIds.add(q.diary_id);
    }
    for (const sub of state.submissions || []) {
      if (
        sub?.diary_id &&
        (sub.status === "QUARANTINED" ||
          sub.status === "LOCAL_QUARANTINED" ||
          sub.status === "QUEUED")
      ) {
        candidateIds.add(sub.diary_id);
      }
    }

    for (const instId of candidateIds) {
      try {
        const fetched = await fetchInstrument(instId);
        if (fetched && fetched.id) {
          if (!state.instruments) state.instruments = {};
          state.instruments[fetched.id] = fetched;
          updatedList.push(fetched);
          refreshed = true;
        }
      } catch (err) {
        console.warn(`Could not reload instrument ${instId}:`, err);
      }
    }
  }

  if (!refreshed && isDemoModeActive()) {
    if (!state.instruments) state.instruments = {};
    for (const [id, mockInst] of Object.entries(MOCK_INSTRUMENTS)) {
      if (!state.instruments[id]) {
        state.instruments[id] = JSON.parse(JSON.stringify(mockInst));
      }
    }
    refreshed = true;
  }

  if (updatedList.length > 0) {
    try {
      await saveInstrumentsToDB(updatedList);
    } catch (dbErr) {
      console.warn(
        "Could not persist refreshed instruments to IndexedDB:",
        dbErr
      );
    }
  }

  if (refreshed) {
    renderTasks();
  }

  return { refreshed };
}

async function isolateSubmissionsIndividually(validSubmissions) {
  const updates = [];
  let hasQuarantined = false;
  let localQuarantinedCount = 0;
  let transientError = null;

  for (const item of validSubmissions) {
    const singlePayload = buildSingleSubmissionPayload(item);
    try {
      const singleRes = await dispatchApi("epro/submit", {
        method: "POST",
        body: JSON.stringify(singlePayload),
        change_reason:
          item.change_reason || "Reconcile isolated offline submission",
      });

      const rawStatus = singleRes?.status;
      const isServerQuarantined = rawStatus === "QUARANTINED";
      if (isServerQuarantined) {
        hasQuarantined = true;
      }

      const resolvedStatus =
        rawStatus &&
        (CONFIRMED_SYNC_STATUSES.has(rawStatus) ||
          rawStatus === "QUARANTINED" ||
          rawStatus === "IGNORED_SERVER_WINS")
          ? rawStatus
          : "CREATED";

      const syncVerificationToken =
        singleRes?.syncVerificationToken ??
        singleRes?.sync_verification_token ??
        null;

      updates.push({
        sequence_number: item.sequence_number,
        status: resolvedStatus,
        ...(syncVerificationToken ? { syncVerificationToken } : {}),
        additionalFields: {
          resolved_answers: singleRes?.answers ?? item.answers,
          resolved_at: new Date().toISOString(),
          ...(syncVerificationToken
            ? { sync_verification_token: syncVerificationToken }
            : {}),
          error: isServerQuarantined
            ? "Survey form version changed on the study server. Tap Sync Now to refresh active forms or contact your study coordinator."
            : null,
        },
      });
    } catch (itemErr) {
      if (isValidationFailure(itemErr)) {
        localQuarantinedCount += 1;
        updates.push({
          sequence_number: item.sequence_number,
          status: "LOCAL_QUARANTINED",
          additionalFields: {
            resolved_at: new Date().toISOString(),
            error: humanizeValidationError(itemErr.message),
          },
        });
      } else {
        transientError = itemErr;
      }
    }
  }

  return { updates, hasQuarantined, localQuarantinedCount, transientError };
}

function scheduleExponentialRetry({
  isManual,
  options,
  currentAttempt,
  maxRetries,
  baseDelay,
  seqNumber,
  queuedCount,
  err,
  statusTextEl,
}) {
  const nextDelay = baseDelay * Math.pow(2, currentAttempt - 1);
  const nextTimestamp = Date.now() + nextDelay;

  if (currentAttempt < maxRetries && !options.noRetry) {
    emitSyncLifecycleEvent("sync:retry-scheduled", {
      attempt: currentAttempt,
      maxRetries,
      nextRetryDelay: nextDelay,
      nextRetryTimestamp: nextTimestamp,
      sequenceNumber: seqNumber,
      queuedCount,
      error: humanizeValidationError(err?.message || "Network connection lost"),
    });

    const shouldAutoRetry = options.autoRetry !== false && !options.noRetry;
    if (shouldAutoRetry) {
      if (state.__syncRetryTimer) {
        clearTimeout(state.__syncRetryTimer);
        state.__syncRetryTimer = null;
      }
      const runRetry = () => {
        state.__pendingSyncRetry = null;
        state.__syncRetryTimer = null;
        syncOfflineQueue(isManual, {
          ...options,
          attempt: currentAttempt + 1,
        });
      };
      state.__pendingSyncRetry = {
        remainingDelayMs: nextDelay,
        runRetry,
      };
      if (!state.syncUpdatesPaused) {
        const timer = setTimeout(runRetry, nextDelay);
        if (timer && typeof timer.unref === "function") {
          timer.unref();
        }
        state.__syncRetryTimer = timer;
      }
    }
  } else {
    resetSyncRetryInfo();
    const txtFailed = `Sync failed. ${queuedCount} submission(s) still queued.`;
    setSyncMilestoneStatus(txtFailed);
    if (statusTextEl && statusTextEl.textContent !== txtFailed) {
      statusTextEl.textContent = txtFailed;
    }
  }
}

export async function syncOfflineQueue(isManual = false, options = {}) {
  const statusTextEl =
    typeof document !== "undefined"
      ? document.getElementById("sync-queue-status-text")
      : null;

  if (state.autoSyncSuspended && !isManual) {
    console.log(
      "[App] Auto-sync suspended due to version mismatch. Manual refresh required."
    );
    return;
  }

  const queued = await getQueuedSubmissions();
  const online = checkOnline();

  if (!online) {
    const txt = `Offline Mode. ${queued.length} submission(s) queued locally.`;
    setSyncMilestoneStatus(txt);
    await refreshSyncViews(
      Array.isArray(options.initialSubmissions)
        ? options.initialSubmissions
        : null
    );
    return;
  }

  let manualRefreshResult = { refreshed: false };
  if (isManual) {
    if (state.__syncRetryTimer) {
      clearTimeout(state.__syncRetryTimer);
      state.__syncRetryTimer = null;
    }
    state.__pendingSyncRetry = null;
    manualRefreshResult = await refreshActiveSurveyDefinitions(queued);
  }

  if (queued.length === 0) {
    if (isManual && manualRefreshResult.refreshed) {
      state.autoSyncSuspended = false;
    }
    const txt = "Online. All submissions synchronized.";
    setSyncMilestoneStatus(txt);
    emitSyncLifecycleEvent("sync:success", { syncedCount: 0, queuedCount: 0 });
    await refreshSyncViews(
      !isManual && Array.isArray(options.initialSubmissions)
        ? options.initialSubmissions
        : null
    );
    return;
  }

  const lockedItems = queued.filter(
    (item) =>
      (item.status === "DECRYPTION_ERROR" || Boolean(item.locked)) &&
      !item.unrecoverable
  );
  const lockedCount = lockedItems.length;
  const decryptable = queued.filter(
    (item) => item.status !== "DECRYPTION_ERROR"
  );

  // Pre-transmission schema gate: isolate invalid items locally while allowing valid siblings through
  const validSubmissions = [];
  const quarantinedBySeq = new Map();
  let preCheckQuarantinedCount = 0;
  for (const item of decryptable) {
    const activeInstrument = state.instruments?.[item.diary_id];
    if (
      isManual &&
      activeInstrument &&
      typeof activeInstrument.version_index === "number" &&
      item.version_index !== activeInstrument.version_index
    ) {
      item.version_index = activeInstrument.version_index;
    }

    const validation = validateEproPayload(item.answers || {});
    if (!validation.valid) {
      preCheckQuarantinedCount += 1;
      const errMsg = humanizeValidationError(validation.errors.join("; "));
      const updatedQuarantined = await updateSubmissionStatus(
        item.sequence_number,
        "LOCAL_QUARANTINED",
        {
          error: errMsg,
        }
      );
      if (updatedQuarantined) {
        quarantinedBySeq.set(item.sequence_number, updatedQuarantined);
      }
    } else {
      validSubmissions.push(item);
    }
  }

  let candidateSnapshot =
    !isManual && Array.isArray(options.initialSubmissions)
      ? options.initialSubmissions.map((existing) =>
          quarantinedBySeq.has(existing.sequence_number)
            ? quarantinedBySeq.get(existing.sequence_number)
            : existing
        )
      : null;

  if (validSubmissions.length === 0) {
    if (isManual && manualRefreshResult.refreshed) {
      state.autoSyncSuspended = false;
    }
    if (lockedCount > 0) {
      const lockedBase = `Locked — enter your security PIN to sync ${lockedCount} queued survey${lockedCount === 1 ? "" : "s"}.`;
      const txtLocked =
        preCheckQuarantinedCount > 0
          ? `${lockedBase} ${preCheckQuarantinedCount} survey(s) quarantined for coordinator review.`
          : lockedBase;
      setSyncMilestoneStatus(txtLocked);
      emitSyncLifecycleEvent("sync:locked", {
        syncedCount: 0,
        lockedCount,
        queuedCount: lockedCount,
      });
      await refreshSyncViews(candidateSnapshot);
      return;
    }
    const txtComplete =
      preCheckQuarantinedCount > 0
        ? `Action needed: ${preCheckQuarantinedCount} survey(s) quarantined for coordinator review.`
        : "Online. Sync complete.";
    setSyncMilestoneStatus(txtComplete);
    emitSyncLifecycleEvent("sync:success", { syncedCount: 0, queuedCount: 0 });
    await refreshSyncViews(candidateSnapshot);
    return;
  }

  const seqNumber = validSubmissions[0]?.sequence_number ?? null;
  const maxRetries = options.maxRetries || 5;
  const currentAttempt =
    options.attempt ||
    (state.syncRetryInfo.isRetrying ? state.syncRetryInfo.attempt + 1 : 1);
  const baseDelay = options.retryDelay || 1000;

  emitSyncLifecycleEvent("sync:attempt", {
    attempt: currentAttempt,
    maxRetries,
    sequenceNumber: seqNumber,
    queuedCount: validSubmissions.length,
  });

  let snapshot = await refreshSyncViews(candidateSnapshot);

  const payload = {
    submissions: validSubmissions.map(buildSingleSubmissionPayload),
  };

  try {
    let updates = [];
    let hasQuarantined = false;
    let isolatedQuarantinedCount = 0;
    let transientFallbackError = null;

    try {
      const response = await dispatchApi("epro/sync", {
        method: "POST",
        body: JSON.stringify(payload),
        change_reason: "Reconcile offline submissions",
      });

      if (response && response.results) {
        const hasKeys = response.results.some((res) => {
          return (
            (res.offline_sync_markers?.sequence_number !== undefined &&
              res.offline_sync_markers?.client_id !== undefined) ||
            (res.sequence_number !== undefined && res.client_id !== undefined)
          );
        });

        if (hasKeys) {
          for (const res of response.results) {
            const resSeq =
              res.offline_sync_markers?.sequence_number ?? res.sequence_number;
            const resClientId =
              res.offline_sync_markers?.client_id ?? res.client_id;
            const item = validSubmissions.find(
              (q) => q.sequence_number === resSeq && q.client_id === resClientId
            );
            if (item) {
              const isQuar = res.status === "QUARANTINED";
              if (isQuar) {
                hasQuarantined = true;
              }
              const syncVerificationToken =
                res.syncVerificationToken ??
                res.sync_verification_token ??
                null;
              updates.push({
                sequence_number: item.sequence_number,
                status: res.status,
                ...(syncVerificationToken ? { syncVerificationToken } : {}),
                additionalFields: {
                  resolved_answers: res.answers,
                  resolved_at: new Date().toISOString(),
                  ...(syncVerificationToken
                    ? { sync_verification_token: syncVerificationToken }
                    : {}),
                  error: isQuar
                    ? "Survey form version changed on the study server. Tap Sync Now to refresh active forms or contact your study coordinator."
                    : null,
                },
              });
            }
          }
        } else {
          // Graceful fallback to index matching
          for (let i = 0; i < validSubmissions.length; i++) {
            const item = validSubmissions[i];
            const res = response.results[i];
            if (res) {
              const isQuar = res.status === "QUARANTINED";
              if (isQuar) {
                hasQuarantined = true;
              }
              const syncVerificationToken =
                res.syncVerificationToken ??
                res.sync_verification_token ??
                null;
              updates.push({
                sequence_number: item.sequence_number,
                status: res.status,
                ...(syncVerificationToken ? { syncVerificationToken } : {}),
                additionalFields: {
                  resolved_answers: res.answers,
                  resolved_at: new Date().toISOString(),
                  ...(syncVerificationToken
                    ? { sync_verification_token: syncVerificationToken }
                    : {}),
                  error: isQuar
                    ? "Survey form version changed on the study server. Tap Sync Now to refresh active forms or contact your study coordinator."
                    : null,
                },
              });
            }
          }
        }
      }
    } catch (batchErr) {
      if (!isValidationFailure(batchErr)) {
        throw batchErr;
      }
      // Batch validation failed: isolate surveys individually via POST epro/submit
      const isolated = await isolateSubmissionsIndividually(validSubmissions);
      updates = isolated.updates;
      hasQuarantined = isolated.hasQuarantined;
      isolatedQuarantinedCount = isolated.localQuarantinedCount;
      transientFallbackError = isolated.transientError;
    }

    if (updates.length > 0) {
      const bulkUpdatedRecords = await bulkUpdateSubmissionStatuses(updates);
      if (Array.isArray(snapshot) && bulkUpdatedRecords.length > 0) {
        const updatedBySeq = new Map(
          bulkUpdatedRecords.map((rec) => [rec.sequence_number, rec])
        );
        snapshot = snapshot.map((existing) =>
          updatedBySeq.has(existing.sequence_number)
            ? updatedBySeq.get(existing.sequence_number)
            : existing
        );
      }
    }

    const confirmedSubs = [];
    let assignmentsPromoted = false;
    for (const upd of updates) {
      if (CONFIRMED_SYNC_STATUSES.has(upd.status)) {
        const syncedSub = validSubmissions.find(
          (s) => s.sequence_number === upd.sequence_number
        );
        if (syncedSub) {
          const resolvedSyncToken =
            upd.syncVerificationToken ??
            upd.additionalFields?.sync_verification_token ??
            syncedSub.syncVerificationToken ??
            syncedSub.sync_verification_token ??
            undefined;
          confirmedSubs.push(
            resolvedSyncToken
              ? { ...syncedSub, syncVerificationToken: resolvedSyncToken }
              : syncedSub
          );
          if (syncedSub.assignment_id) {
            const assign = state.assignments.find(
              (a) => a.id === syncedSub.assignment_id
            );
            if (assign && assign.status !== "COMPLETED") {
              assign.status = "COMPLETED";
              assign.has_draft = false;
              assign.submitted_at =
                syncedSub.device_timestamp || new Date().toISOString();
              assignmentsPromoted = true;
            }
          }
        }
      }
    }

    if (assignmentsPromoted) {
      try {
        await saveAssignmentsToDB(state.assignments);
      } catch (dbErr) {
        console.warn(
          "Could not persist synced assignment status to IndexedDB:",
          dbErr
        );
      }
      renderTasks();
      renderCompliance();
    }

    // Automatic Stipend Wallet Crediting upon confirmed ePRO sync (strictly server-rule driven; zero client amount)
    if (
      confirmedSubs.length > 0 &&
      typeof options.autoDisburse === "boolean" &&
      options.autoDisburse
    ) {
      for (const sub of confirmedSubs) {
        try {
          const targetSubjectId = sub.subject_id || state.session.userId;
          const idempotencyKey = `seq_${sub.sequence_number}_${targetSubjectId}`;
          const syncVerificationToken =
            sub.syncVerificationToken ??
            sub.sync_verification_token ??
            undefined;
          const stipendPayload = {
            studyId: sub.study_id || state.walletStudyId || undefined,
            siteId: sub.site_id || state.walletSiteId || undefined,
            sequenceNumber: sub.sequence_number,
            assignmentId: sub.assignment_id,
            diaryId: sub.diary_id,
            idempotencyKey,
            ...(syncVerificationToken ? { syncVerificationToken } : {}),
          };

          const disburseRes = await dispatchApi(
            "payments/participant/me/diary-stipend",
            {
              method: "POST",
              body: JSON.stringify(stipendPayload),
              change_reason:
                "Credit protocol stipend for synchronized ePRO diary",
            }
          );

          const updatedBalance =
            disburseRes?.currentBalance ??
            disburseRes?.current_balance ??
            disburseRes?.new_balance ??
            disburseRes?.NewBalance;
          if (typeof updatedBalance === "number") {
            state.walletBalance = updatedBalance;
          }

          const updatedLifetime =
            disburseRes?.lifetimeStipends ?? disburseRes?.lifetime_stipends;
          if (typeof updatedLifetime === "number") {
            state.lifetimeStipends = updatedLifetime;
          }

          await logAuditRecord(
            "AUTOMATIC_STIPEND_DISBURSED",
            {
              subject_id: targetSubjectId,
              sequence_number: sub.sequence_number,
              stipend_amount:
                disburseRes?.creditedAmount ??
                disburseRes?.credited_amount ??
                disburseRes?.amount ??
                0,
              new_balance: state.walletBalance,
            },
            "Server-rule ePRO diary synchronization stipend evaluation"
          );
        } catch (disburseErr) {
          console.warn("Auto-disbursement payment call warning:", disburseErr);
        }
      }
    }

    if (transientFallbackError) {
      const updatedSeqs = new Set(updates.map((u) => u.sequence_number));
      const remainingQueued = validSubmissions.filter(
        (s) => !updatedSeqs.has(s.sequence_number)
      );
      scheduleExponentialRetry({
        isManual,
        options,
        currentAttempt,
        maxRetries,
        baseDelay,
        seqNumber: remainingQueued[0]?.sequence_number ?? seqNumber,
        queuedCount: remainingQueued.length,
        err: transientFallbackError,
        statusTextEl,
      });
    } else if (hasQuarantined) {
      state.autoSyncSuspended = true;
      resetSyncRetryInfo();
      const msg =
        "Outdated form structure detected. Tap Manual Sync / Refresh to reload active survey definitions.";
      setSyncMilestoneStatus(msg);
    } else if (lockedCount > 0) {
      if (isManual) {
        state.autoSyncSuspended = false;
      }
      const totalQuarantined =
        preCheckQuarantinedCount + isolatedQuarantinedCount;
      const quarantineSuffix =
        totalQuarantined > 0 ? ` ${totalQuarantined} quarantined.` : "";
      const txtLockedRemaining = `Locked — ${confirmedSubs.length} synced; enter your security PIN to sync ${lockedCount} remaining queued survey${lockedCount === 1 ? "" : "s"}.${quarantineSuffix}`;
      setSyncMilestoneStatus(txtLockedRemaining);
      emitSyncLifecycleEvent("sync:locked", {
        syncedCount: confirmedSubs.length,
        lockedCount,
        sequenceNumber: seqNumber,
        queuedCount: lockedCount,
      });
    } else {
      if (isManual) {
        state.autoSyncSuspended = false;
      }
      const totalQuarantined =
        preCheckQuarantinedCount + isolatedQuarantinedCount;
      const txtComplete =
        totalQuarantined > 0
          ? `Online. ${confirmedSubs.length} synced; ${totalQuarantined} quarantined for coordinator review.`
          : "Online. Sync complete.";
      setSyncMilestoneStatus(txtComplete);

      emitSyncLifecycleEvent("sync:success", {
        syncedCount: confirmedSubs.length,
        sequenceNumber: seqNumber,
        queuedCount: 0,
      });
    }
  } catch (err) {
    console.error("Sync failed:", err);
    scheduleExponentialRetry({
      isManual,
      options,
      currentAttempt,
      maxRetries,
      baseDelay,
      seqNumber,
      queuedCount: validSubmissions.length,
      err,
      statusTextEl,
    });
  }

  await refreshSyncViews(snapshot);
}
