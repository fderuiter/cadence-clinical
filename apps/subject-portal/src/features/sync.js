// Offline queue reconciliation with the gateway (epro/sync), retry with
// backoff and the automatic stipend disbursement after a successful sync.
import { validateEproPayload } from "usdm-schemas";

import { dispatchApi } from "../api/client.js";
import { state } from "../stores/state.js";
import {
  bulkUpdateSubmissionStatuses,
  getQueuedSubmissions,
  saveAssignmentsToDB,
  updateSubmissionStatus,
} from "../sync-queue.js";
import { logAuditRecord } from "./audit.js";
import { renderCompliance } from "./compliance.js";
import {
  checkOnline,
  emitSyncLifecycleEvent,
  refreshSubmissionsState,
  renderSyncQueueList,
} from "./sync-status.js";
import { renderTasks } from "./tasks.js";

const CONFIRMED_SYNC_STATUSES = new Set([
  "CREATED",
  "UPDATED_CLIENT_WINS",
  "MERGED",
  "SYNCED",
]);

export async function syncOfflineQueue(isManual = false, options = {}) {
  const statusTextEl = document.getElementById("sync-queue-status-text");
  const queued = await getQueuedSubmissions();
  const online = checkOnline();

  if (state.autoSyncSuspended && !isManual) {
    console.log(
      "[App] Auto-sync suspended due to version mismatch. Manual refresh required."
    );
    return;
  }

  if (!online) {
    const txt = `Offline Mode. ${queued.length} submission(s) queued locally.`;
    if (statusTextEl) {
      statusTextEl.textContent = txt;
    }
    state.syncStatusText = txt;
    await renderSyncQueueList();
    await refreshSubmissionsState();
    return;
  }

  if (queued.length === 0) {
    const txt = "Online. All submissions synchronized.";
    if (statusTextEl) {
      statusTextEl.textContent = txt;
    }
    state.syncStatusText = txt;
    emitSyncLifecycleEvent("sync:success", { syncedCount: 0, queuedCount: 0 });
    await renderSyncQueueList();
    await refreshSubmissionsState();
    return;
  }

  const decryptable = queued.filter(
    (item) => item.status !== "DECRYPTION_ERROR"
  );

  // Pre-transmission schema gate: validate queued submissions against USDM Zod schema
  const validSubmissions = [];
  for (const item of decryptable) {
    const validation = validateEproPayload(item.answers || {});
    if (!validation.valid) {
      const errMsg = `Local schema quarantine: ${validation.errors.join("; ")}`;
      await updateSubmissionStatus(item.sequence_number, "LOCAL_QUARANTINED", {
        error: errMsg,
      });
    } else {
      validSubmissions.push(item);
    }
  }

  if (validSubmissions.length === 0) {
    const txtComplete = "Online. Sync complete.";
    if (statusTextEl) {
      statusTextEl.textContent = txtComplete;
    }
    state.syncStatusText = txtComplete;
    emitSyncLifecycleEvent("sync:success", { syncedCount: 0, queuedCount: 0 });
    await renderSyncQueueList();
    await refreshSubmissionsState();
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

  await renderSyncQueueList();
  await refreshSubmissionsState();

  const payload = {
    submissions: validSubmissions.map((item) => ({
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
    })),
  };

  try {
    const response = await dispatchApi("epro/sync", {
      method: "POST",
      body: JSON.stringify(payload),
      change_reason: "Reconcile offline submissions",
    });

    if (response && response.results) {
      const updates = [];
      let hasQuarantined = false;
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
            updates.push({
              sequence_number: item.sequence_number,
              status: res.status,
              additionalFields: {
                resolved_answers: res.answers,
                resolved_at: new Date().toISOString(),
                error: isQuar
                  ? "Version mismatch: client version index does not match the active instrument version."
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
            updates.push({
              sequence_number: item.sequence_number,
              status: res.status,
              additionalFields: {
                resolved_answers: res.answers,
                resolved_at: new Date().toISOString(),
                error: isQuar
                  ? "Version mismatch: client version index does not match the active instrument version."
                  : null,
              },
            });
          }
        }
      }

      if (updates.length > 0) {
        await bulkUpdateSubmissionStatuses(updates);
      }

      // Promote matching assignments from PENDING_SYNC to COMPLETED only after server confirmation
      let assignmentsPromoted = false;
      for (const upd of updates) {
        if (CONFIRMED_SYNC_STATUSES.has(upd.status)) {
          const syncedSub = validSubmissions.find(
            (s) => s.sequence_number === upd.sequence_number
          );
          if (syncedSub && syncedSub.assignment_id) {
            const assign = state.assignments.find(
              (a) => a.id === syncedSub.assignment_id
            );
            if (assign && assign.status !== "COMPLETED") {
              assign.status = "COMPLETED";
              assign.submitted_at =
                syncedSub.device_timestamp || new Date().toISOString();
              assignmentsPromoted = true;
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

      // Automatic Stipend Wallet Disbursement upon successful ePRO sync
      if (
        validSubmissions.length > 0 &&
        typeof options.autoDisburse === "boolean" &&
        options.autoDisburse
      ) {
        for (const sub of validSubmissions) {
          try {
            const targetSubjectId = sub.subject_id || state.session.userId;
            const disbursePayload = {
              subject_id: targetSubjectId,
              study_id: "study_01",
              site_id: "site_01",
              sequence_number: sub.sequence_number,
              assignment_id: sub.assignment_id,
              diary_id: sub.diary_id,
              amount: 25.0,
              idempotency_key: `seq_${sub.sequence_number}_${targetSubjectId}`,
              disbursement_type: "STIPEND",
              answers: sub.answers,
            };

            const disburseRes = await dispatchApi("payments/disburse", {
              method: "POST",
              body: JSON.stringify(disbursePayload),
            });

            if (disburseRes && typeof disburseRes.new_balance === "number") {
              state.walletBalance = disburseRes.new_balance;
            } else if (
              disburseRes &&
              typeof disburseRes.NewBalance === "number"
            ) {
              state.walletBalance = disburseRes.NewBalance;
            }

            await logAuditRecord(
              "AUTOMATIC_STIPEND_DISBURSED",
              {
                subject_id: targetSubjectId,
                sequence_number: sub.sequence_number,
                stipend_amount:
                  disburseRes?.amount ?? disburseRes?.Amount ?? 25.0,
                new_balance: state.walletBalance,
              },
              "Instant ePRO survey auto-disbursement"
            );
          } catch (disburseErr) {
            console.warn(
              "Auto-disbursement payment call warning:",
              disburseErr
            );
          }
        }
      }

      if (hasQuarantined) {
        state.autoSyncSuspended = true;
        const msg =
          "ALERT: Outdated Form Structure! Please perform manual refresh to reload active clinical structures.";
        state.syncStatusText = msg;
        if (statusTextEl) {
          statusTextEl.textContent = msg;
        }
        alert(msg);
      } else if (isManual) {
        state.autoSyncSuspended = false;
      }
    }

    const txtComplete = "Online. Sync complete.";
    if (statusTextEl) {
      statusTextEl.textContent = txtComplete;
    }
    state.syncStatusText = txtComplete;

    emitSyncLifecycleEvent("sync:success", {
      syncedCount: validSubmissions.length,
      sequenceNumber: seqNumber,
      queuedCount: 0,
    });
  } catch (err) {
    console.error("Sync failed:", err);
    const nextDelay = baseDelay * Math.pow(2, currentAttempt - 1);
    const nextTimestamp = Date.now() + nextDelay;

    if (currentAttempt < maxRetries && !options.noRetry) {
      emitSyncLifecycleEvent("sync:retry-scheduled", {
        attempt: currentAttempt,
        maxRetries,
        nextRetryDelay: nextDelay,
        nextRetryTimestamp: nextTimestamp,
        sequenceNumber: seqNumber,
        queuedCount: validSubmissions.length,
        error: err.message,
      });

      if (options.autoRetry) {
        if (state.__syncRetryTimer) {
          clearTimeout(state.__syncRetryTimer);
        }
        state.__syncRetryTimer = setTimeout(() => {
          syncOfflineQueue(isManual, {
            ...options,
            attempt: currentAttempt + 1,
          });
        }, nextDelay);
      }
    } else {
      state.syncRetryInfo.isRetrying = false;
      const txtFailed = `Sync failed. ${queued.length} submission(s) still queued.`;
      if (statusTextEl) {
        statusTextEl.textContent = txtFailed;
      }
      state.syncStatusText = txtFailed;
    }
  }

  await renderSyncQueueList();
  await refreshSubmissionsState();
}
