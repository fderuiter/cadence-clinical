import { nextTick } from "vue";

import { state } from "../stores/state.js";
import {
  formatSignedWalletCurrency,
  formatWalletCurrency,
  normalizeCurrencyCode,
} from "../utils/currency.js";
import { demoNowMs } from "../utils/demoMode.js";
import {
  humanizeExpenseReviewNotes,
  resolveFriendlyStudyTitle,
} from "../utils/participantCopy.js";
import {
  formatAnswerPairs,
  getInstrumentDisplayName,
  getSyncStatusExplanation,
  getSyncStatusLabel,
  humanizeValidationError,
  isQuarantinedSubmission,
} from "./site-support.js";

async function navigateToView(viewId) {
  const { showView } = await import("./navigation.js");
  showView(viewId);
}

export const PEEK_QUERY_KEYS = Object.freeze([
  "notifId",
  "claimId",
  "txId",
  "ledgerIndex",
  "complianceId",
  "syncSeq",
]);

let syncPeekQueryHandler = null;

export function registerPeekQuerySync(handler) {
  syncPeekQueryHandler = typeof handler === "function" ? handler : null;
}

function notifyPeekQueryChange(record) {
  if (typeof syncPeekQueryHandler === "function") {
    syncPeekQueryHandler(record);
  }
}

function formatReadableDateTime(isoValue) {
  if (!isoValue) return "Not scheduled";
  const parsed = new Date(isoValue);
  if (Number.isNaN(parsed.getTime())) {
    return String(isoValue);
  }
  return parsed.toLocaleString();
}

function formatReadableCategory(category) {
  switch (String(category || "").toUpperCase()) {
    case "TRANSIT":
      return "Transit & Ride Share";
    case "PARKING":
      return "Parking & Tolls";
    case "MEAL":
      return "Meals";
    case "LODGING":
      return "Hotel & Lodging";
    default:
      return "Travel Expense";
  }
}

function formatReadableTransactionType(txType) {
  switch (String(txType || "").toUpperCase()) {
    case "STIPEND_CREDIT":
      return "Study stipend";
    case "EXPENSE_REIMBURSEMENT":
      return "Travel reimbursement";
    case "DIRECT_ACH":
    case "DISBURSEMENT_DEBIT":
      return "Bank payout";
    case "VIRTUAL_CARD":
      return "Reloadable card payout";
    default:
      return "Wallet activity";
  }
}

export function openPeekRecord(
  record,
  triggerEl = null,
  { syncUrl = true } = {}
) {
  if (!record) return;
  if (triggerEl && typeof triggerEl.focus === "function") {
    state.peekTriggerElement = triggerEl;
  } else if (
    typeof document !== "undefined" &&
    document.activeElement &&
    document.activeElement !== document.body
  ) {
    state.peekTriggerElement = document.activeElement;
  }
  state.peekRecord = record;
  if (syncUrl) {
    notifyPeekQueryChange(record);
  }
}

export function closePeekRecord({ restoreFocus = true, syncUrl = true } = {}) {
  const previousTrigger = state.peekTriggerElement;
  state.peekRecord = null;
  state.peekTriggerElement = null;
  if (syncUrl) {
    notifyPeekQueryChange(null);
  }
  if (
    restoreFocus &&
    previousTrigger &&
    typeof previousTrigger.focus === "function"
  ) {
    if (previousTrigger.isConnected && !previousTrigger.closest?.("[inert]")) {
      previousTrigger.focus();
    }
    nextTick(() => {
      if (
        previousTrigger.isConnected &&
        !previousTrigger.closest?.("[inert]")
      ) {
        previousTrigger.focus();
      }
    });
  }
}

export async function openPeekFullView() {
  const active = state.peekRecord;
  if (!active) return;
  const action = active.fullViewAction;
  closePeekRecord({ restoreFocus: false, syncUrl: true });
  if (typeof action === "function") {
    await action();
  }
}

export function buildNotificationPeekRecord(notif) {
  if (!notif) return null;
  const isReconsent =
    notif.related_entity_type === "RECONSENT_REQUIRED" ||
    String(notif.message_content || notif.message || "")
      .toLowerCase()
      .includes("re-consent required");
  const rawMessage =
    notif.message_content ||
    notif.message ||
    "You have a scheduled study survey reminder.";
  const cleanMessage = rawMessage.replace(
    /\bfor study\s+[A-Z0-9_-]+\.?/gi,
    `for ${resolveFriendlyStudyTitle(state.session?.studyId)}.`
  );
  const linkedAssignment = notif.assignment_id
    ? (state.assignments || []).find((a) => a.id === notif.assignment_id)
    : null;
  const canOpenSurvey =
    linkedAssignment &&
    linkedAssignment.status !== "COMPLETED" &&
    linkedAssignment.status !== "PENDING_SYNC";

  return {
    kind: "notification",
    id: String(notif.id),
    queryKey: "notifId",
    queryValue: String(notif.id),
    title: isReconsent ? "Updated Study Consent Needed" : "Study Reminder",
    subtitle: notif.is_read ? "Marked as read" : "Needs your attention",
    statusLabel: notif.is_read
      ? "Read"
      : isReconsent
        ? "Action needed"
        : "Unread",
    statusTone: isReconsent ? "warning" : notif.is_read ? "success" : "info",
    explanation: isReconsent
      ? "Your study team updated the consent form. Please read what changed and sign before starting new surveys."
      : cleanMessage,
    details: [
      { label: "Message", value: cleanMessage },
      {
        label: "Scheduled time",
        value: formatReadableDateTime(notif.due_at),
        tabular: true,
      },
      {
        label: "Status",
        value: notif.is_read ? "Read" : "Waiting for your review",
      },
    ],
    showCoordinatorHelp: isReconsent,
    fullViewLabel: isReconsent
      ? "Open full view: Review & sign consent"
      : canOpenSurvey
        ? "Open full view: Start survey"
        : "Open full view: Reminders",
    fullViewAction: async () => {
      if (isReconsent) {
        await navigateToView("view-consent");
        const { openReconsentModal } = await import("./reconsent.js");
        openReconsentModal();
        return;
      }
      if (canOpenSurvey) {
        const { startQuestionnaire } = await import("./questionnaire.js");
        await startQuestionnaire(linkedAssignment.id);
        return;
      }
      await navigateToView("view-inbox");
      nextTick(() => {
        document.getElementById(`notif-card-${notif.id}`)?.focus?.();
      });
    },
  };
}

export function buildCompliancePeekRecord(item, index = 0) {
  if (!item) return null;
  const id = String(
    item.id || item.assignment_id || item.instrument_id || index
  );
  const instName =
    item.instrument_name ||
    (state.instruments && state.instruments[item.instrument_id]?.name) ||
    getInstrumentDisplayName(item.instrument_id) ||
    "Study Survey";
  const isCompleted = item.status === "COMPLETED";
  const isPendingSync = item.status === "PENDING_SYNC";
  const isOverdue =
    !isCompleted &&
    !isPendingSync &&
    (item.status === "OVERDUE" ||
      (item.due_at && new Date(item.due_at) < new Date(demoNowMs(0))));

  const statusLabel = isCompleted
    ? "Completed"
    : isPendingSync
      ? "Waiting to send"
      : isOverdue
        ? "Past due"
        : "Scheduled";
  const statusTone = isCompleted ? "success" : isOverdue ? "warning" : "info";

  const explanation = isCompleted
    ? "Thank you! Your study team has received your answers for this survey."
    : isPendingSync
      ? "Your answers are saved safely on this device and will send as soon as your internet connection is ready."
      : isOverdue
        ? "This survey window is past its target time. You can still open My Surveys or reach out to your study coordinator if you need help."
        : "This survey is ready whenever you are. Tap Open full view to go to My Surveys.";

  const matchingAssignment = (state.assignments || []).find(
    (a) =>
      (item.id && a.id === item.id) ||
      (item.instrument_id &&
        a.instrument_id === item.instrument_id &&
        a.status !== "COMPLETED")
  );

  return {
    kind: "compliance",
    id,
    queryKey: "complianceId",
    queryValue: id,
    title: instName,
    subtitle: "Survey progress details",
    statusLabel,
    statusTone,
    explanation,
    details: [
      { label: "Survey", value: instName },
      {
        label: "Due date",
        value: item.due_at
          ? new Date(item.due_at).toLocaleDateString()
          : "Flexible",
        tabular: true,
      },
      {
        label: "Completed at",
        value: item.submitted_at
          ? formatReadableDateTime(item.submitted_at)
          : item.queued_at
            ? `${formatReadableDateTime(item.queued_at)} (Saved on device)`
            : "Not yet submitted",
        tabular: true,
      },
    ],
    showCoordinatorHelp: isOverdue,
    fullViewLabel:
      matchingAssignment &&
      matchingAssignment.status !== "COMPLETED" &&
      matchingAssignment.status !== "PENDING_SYNC"
        ? `Open full view: Start ${instName}`
        : "Open full view: My Surveys",
    fullViewAction: async () => {
      if (
        matchingAssignment &&
        matchingAssignment.status !== "COMPLETED" &&
        matchingAssignment.status !== "PENDING_SYNC"
      ) {
        const { startQuestionnaire } = await import("./questionnaire.js");
        await startQuestionnaire(matchingAssignment.id);
        return;
      }
      await navigateToView("view-tasks");
    },
  };
}

export function buildExpenseClaimPeekRecord(claim) {
  if (!claim) return null;
  const id = String(claim.id || claim.claim_id || "");
  const currency = normalizeCurrencyCode(
    claim.currency || state.walletCurrency
  );
  const amountText = formatWalletCurrency(
    claim.netAmount ??
      claim.net_amount ??
      claim.grossAmount ??
      claim.gross_amount ??
      0,
    currency
  );
  const statusUpper = String(claim.status || "PENDING_REVIEW").toUpperCase();
  const isApproved =
    statusUpper === "APPROVED" ||
    statusUpper === "AUTO_APPROVED" ||
    Boolean(claim.autoApproved || claim.auto_approved);
  const rawNotes = claim.reviewNotes || claim.review_notes || "";
  const plainNotes = humanizeExpenseReviewNotes(rawNotes);

  const explanation = isApproved
    ? `Good news! Your receipt for ${amountText} was approved and sent to your payout method.`
    : plainNotes ||
      "Your study coordinator is checking this receipt for you. No extra action is needed unless your coordinator contacts you.";

  return {
    kind: "expense_claim",
    id,
    queryKey: "claimId",
    queryValue: id,
    title:
      claim.merchantName || claim.merchant_name || "Travel Expense Receipt",
    subtitle: `${formatReadableCategory(claim.category)} • ${amountText}`,
    statusLabel: isApproved ? "Approved" : "Coordinator review",
    statusTone: isApproved ? "success" : "warning",
    explanation,
    details: [
      {
        label: "Category",
        value: formatReadableCategory(claim.category),
      },
      {
        label: "Amount",
        value: amountText,
        tabular: true,
      },
      {
        label: "Receipt date",
        value: claim.expenseDate || claim.expense_date || "Recorded today",
        tabular: true,
      },
      ...(claim.disbursementReference || claim.disbursement_reference
        ? [
            {
              label: "Payment reference",
              value:
                claim.disbursementReference || claim.disbursement_reference,
              tabular: true,
            },
          ]
        : []),
    ],
    showCoordinatorHelp: !isApproved,
    fullViewLabel: "Open full view: Stipends & Expenses",
    fullViewAction: async () => {
      await navigateToView("view-wallet");
      nextTick(() => {
        const section = document.getElementById("wallet-expense-claims");
        section?.scrollIntoView?.({ behavior: "smooth", block: "nearest" });
      });
    },
  };
}

export function buildTransactionPeekRecord(tx) {
  if (!tx) return null;
  const id = String(tx.id || tx.referenceId || "");
  const currency = normalizeCurrencyCode(tx.currency || state.walletCurrency);
  const signedAmount = formatSignedWalletCurrency(tx.amount ?? 0, currency);
  const typeLabel = formatReadableTransactionType(
    tx.transactionType || tx.type
  );

  return {
    kind: "wallet_transaction",
    id,
    queryKey: "txId",
    queryValue: id,
    title: tx.description || typeLabel,
    subtitle: `${typeLabel} • ${signedAmount}`,
    statusLabel: "Posted",
    statusTone: "success",
    explanation:
      Number(tx.amount ?? 0) >= 0
        ? `This ${typeLabel.toLowerCase()} added ${formatWalletCurrency(Math.abs(Number(tx.amount ?? 0)), currency)} to your study wallet.`
        : `This payout sent ${formatWalletCurrency(Math.abs(Number(tx.amount ?? 0)), currency)} from your study wallet to your chosen payment method.`,
    details: [
      { label: "Activity type", value: typeLabel },
      { label: "Amount", value: signedAmount, tabular: true },
      ...(tx.createdAt
        ? [
            {
              label: "Recorded time",
              value: formatReadableDateTime(tx.createdAt),
              tabular: true,
            },
          ]
        : []),
    ],
    showCoordinatorHelp: false,
    fullViewLabel: "Open full view: Stipends & Expenses",
    fullViewAction: async () => {
      await navigateToView("view-wallet");
      nextTick(() => {
        const section = document.getElementById("wallet-recent-transactions");
        section?.scrollIntoView?.({ behavior: "smooth", block: "nearest" });
      });
    },
  };
}

export function buildLedgerPeekRecord(block, index) {
  if (!block) return null;
  const resolvedIndex = Number.isFinite(Number(index))
    ? Number(index)
    : Number(block.index ?? 0);
  const action = String(block.action || "");
  const details =
    block.details && typeof block.details === "object" ? block.details : {};
  let title = "Saved study activity";
  let explanation =
    "This step was saved in your private activity log so you have a clear record of what happened.";
  let targetView = "view-tasks";

  if (action === "EPRO_SUBMIT") {
    const surveyName = details.diary_id
      ? getInstrumentDisplayName(details.diary_id)
      : "Study survey";
    title = `Survey signed: ${surveyName}`;
    explanation = `Your answers for ${surveyName} were signed and saved safely.`;
    targetView = "view-compliance";
  } else if (action === "SIGN_CONSENT" || action === "RECONSENT_EXECUTED") {
    title = "Study consent signed";
    explanation =
      "Your signed consent form was saved. You can read your consent anytime in Study Consent.";
    targetView = "view-consent";
  } else if (
    action === "EXPENSE_CLAIM_HELD_FOR_REVIEW" ||
    action === "EXPENSE_CLAIM_AUTO_APPROVED" ||
    action === "OCR_RECEIPT_EXTRACTED" ||
    action === "DIGITAL_DISBURSEMENT_DISPATCHED" ||
    action === "AUTOMATIC_STIPEND_DISBURSED"
  ) {
    title =
      action === "EXPENSE_CLAIM_HELD_FOR_REVIEW"
        ? "Receipt sent to coordinator"
        : "Wallet & expense update";
    explanation = details.review_notes
      ? humanizeExpenseReviewNotes(details.review_notes)
      : "Your study wallet activity has been updated.";
    targetView = "view-wallet";
  } else if (action === "ACKNOWLEDGE_NOTIFICATION") {
    title = "Reminder marked as read";
    explanation = "You marked this study reminder as read.";
    targetView = "view-inbox";
  }

  return {
    kind: "ledger_entry",
    id: String(resolvedIndex),
    queryKey: "ledgerIndex",
    queryValue: String(resolvedIndex),
    title,
    subtitle: formatReadableDateTime(block.timestamp),
    statusLabel:
      action === "EXPENSE_CLAIM_HELD_FOR_REVIEW"
        ? "Coordinator review"
        : "Saved",
    statusTone:
      action === "EXPENSE_CLAIM_HELD_FOR_REVIEW" ? "warning" : "success",
    explanation,
    details: [
      {
        label: "Recorded time",
        value: formatReadableDateTime(block.timestamp),
        tabular: true,
      },
      {
        label: "Summary",
        value: humanizeExpenseReviewNotes(block.reason || explanation),
      },
    ],
    showCoordinatorHelp: action === "EXPENSE_CLAIM_HELD_FOR_REVIEW",
    fullViewLabel: "Open full view",
    fullViewAction: async () => {
      await navigateToView(targetView);
    },
  };
}

export function buildSyncSubmissionPeekRecord(sub) {
  if (!sub) return null;
  const seq = String(sub.sequence_number ?? "");
  const instName = getInstrumentDisplayName(sub.diary_id);
  const statusLabel = getSyncStatusLabel(sub);
  const isQuarantined = isQuarantinedSubmission(sub);
  const isSynced =
    sub.status === "CREATED" ||
    sub.status === "UPDATED_CLIENT_WINS" ||
    sub.status === "SYNCED" ||
    sub.status === "MERGED";
  const answerPairs =
    isQuarantined || sub.status === "DECRYPTION_ERROR"
      ? []
      : formatAnswerPairs(sub.answers, sub.diary_id);

  return {
    kind: "sync_submission",
    id: seq,
    queryKey: "syncSeq",
    queryValue: seq,
    title: instName,
    subtitle: statusLabel,
    statusLabel,
    statusTone: isQuarantined ? "warning" : isSynced ? "success" : "info",
    explanation: isQuarantined
      ? `${humanizeValidationError(sub.error || sub.quarantine_reason)} Your answers are still safe on this device. Contact your study coordinator for help.`
      : getSyncStatusExplanation(sub) ||
        "Your answers are saved safely on this device and will send automatically when your connection is ready.",
    details: [
      { label: "Survey", value: instName },
      { label: "Status", value: statusLabel },
      ...answerPairs.map((pair) => ({
        label: pair.label,
        value: String(pair.value ?? ""),
        tabular: true,
      })),
    ],
    showCoordinatorHelp: isQuarantined,
    fullViewLabel: "Open full view: My Progress",
    fullViewAction: async () => {
      state.isSyncDrawerOpen = false;
      await navigateToView("view-compliance");
    },
  };
}

export function openNotificationPeek(notif, triggerEl) {
  openPeekRecord(buildNotificationPeekRecord(notif), triggerEl);
}

export function openCompliancePeek(item, index, triggerEl) {
  openPeekRecord(buildCompliancePeekRecord(item, index), triggerEl);
}

export function openExpenseClaimPeek(claim, triggerEl) {
  openPeekRecord(buildExpenseClaimPeekRecord(claim), triggerEl);
}

export function openTransactionPeek(tx, triggerEl) {
  openPeekRecord(buildTransactionPeekRecord(tx), triggerEl);
}

export function openLedgerPeek(block, index, triggerEl) {
  openPeekRecord(buildLedgerPeekRecord(block, index), triggerEl);
}

export function openSyncSubmissionPeek(sub, triggerEl) {
  openPeekRecord(buildSyncSubmissionPeekRecord(sub), triggerEl);
}

export function syncPeekFromQuery(query = {}) {
  if (!query || typeof query !== "object") {
    if (state.peekRecord) {
      closePeekRecord({ restoreFocus: false, syncUrl: false });
    }
    return;
  }

  if (query.notifId) {
    const notif = (state.notifications || []).find(
      (n) => String(n.id) === String(query.notifId)
    );
    if (notif) {
      state.peekRecord = buildNotificationPeekRecord(notif);
      return;
    }
  }

  if (query.claimId) {
    const claim = (state.expenseClaims || []).find(
      (c) => String(c.id || c.claim_id) === String(query.claimId)
    );
    if (claim) {
      state.peekRecord = buildExpenseClaimPeekRecord(claim);
      return;
    }
  }

  if (query.txId) {
    const tx = (state.recentTransactions || []).find(
      (t) => String(t.id || t.referenceId) === String(query.txId)
    );
    if (tx) {
      state.peekRecord = buildTransactionPeekRecord(tx);
      return;
    }
  }

  if (
    query.ledgerIndex !== undefined &&
    query.ledgerIndex !== null &&
    query.ledgerIndex !== ""
  ) {
    const idx = Number(query.ledgerIndex);
    const block = Number.isFinite(idx) ? state.ledgerBlocks?.[idx] : null;
    if (block) {
      state.peekRecord = buildLedgerPeekRecord(block, idx);
      return;
    }
  }

  if (query.complianceId) {
    const sourceList =
      (state.compliance &&
      Array.isArray(state.compliance.assignments) &&
      state.compliance.assignments.length > 0
        ? state.compliance.assignments
        : state.assignments) || [];
    const idx = sourceList.findIndex(
      (item, i) =>
        String(item.id || item.assignment_id || item.instrument_id || i) ===
        String(query.complianceId)
    );
    if (idx !== -1) {
      state.peekRecord = buildCompliancePeekRecord(sourceList[idx], idx);
      return;
    }
  }

  if (query.syncSeq) {
    const targetSeq = String(query.syncSeq);
    const sub = (state.submissions || []).find(
      (s) => String(s.sequence_number) === targetSeq
    );
    if (sub) {
      state.peekRecord = buildSyncSubmissionPeekRecord(sub);
      return;
    }
    void import("./sync-status.js")
      .then(({ refreshSubmissionsState }) => refreshSubmissionsState())
      .then(() => {
        const refreshed = (state.submissions || []).find(
          (s) => String(s.sequence_number) === targetSeq
        );
        if (refreshed) {
          state.peekRecord = buildSyncSubmissionPeekRecord(refreshed);
        }
      })
      .catch(() => {});
    return;
  }

  if (state.peekRecord) {
    closePeekRecord({ restoreFocus: false, syncUrl: false });
  }
}
