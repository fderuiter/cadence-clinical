// Local audit ledger: hash-chained blocks rendered into the footer timeline.
import { buildLedgerBlock } from "ui";

import { state } from "../stores/state.js";
import { demoNowIso } from "../utils/demoMode.js";
import { getInstrumentDisplayName } from "./site-support.js";

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function humanizeActionCode(action) {
  const words = String(action || "ACTIVITY")
    .replace(/[._-]+/g, " ")
    .trim()
    .toLowerCase();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : "Activity";
}

function describeLedgerBlock(block) {
  const action = String(block?.action || "");
  const details =
    block?.details && typeof block.details === "object" ? block.details : {};

  switch (action) {
    case "GENESIS":
      return {
        title: "Study portal opened",
        summary: "Your companion session is ready on this device.",
      };
    case "EPRO_SUBMIT": {
      const surveyTitle = details.diary_id
        ? getInstrumentDisplayName(details.diary_id)
        : "Study survey";
      return {
        title: "Survey answers signed",
        summary: `${surveyTitle} was signed and saved for your study team.`,
      };
    }
    case "SIGN_CONSENT":
      return {
        title: "Study consent signed",
        summary: "Your signed informed consent form has been recorded.",
      };
    case "RECONSENT_EXECUTED":
      return {
        title: "Updated study consent signed",
        summary: "You reviewed and signed the updated study consent.",
      };
    case "ACKNOWLEDGE_NOTIFICATION":
      return {
        title: "Reminder marked as read",
        summary: "You confirmed a study reminder.",
      };
    case "PIN_WRAPPER_RESET":
      return {
        title: "Security PIN reset",
        summary:
          "You verified your identity and set up a new security PIN on this device.",
      };
    case "OCR_RECEIPT_EXTRACTED":
      return {
        title: "Receipt scanned",
        summary: details.merchant_name
          ? `Read receipt details from ${details.merchant_name}.`
          : "Receipt details were read from your uploaded image.",
      };
    case "EXPENSE_CLAIM_AUTO_APPROVED":
      return {
        title: "Travel expense approved",
        summary:
          typeof details.net_amount === "number"
            ? `Approved $${details.net_amount.toFixed(2)} for reimbursement.`
            : "Your travel expense claim was approved.",
      };
    case "EXPENSE_CLAIM_HELD_FOR_REVIEW":
      return {
        title: "Travel expense sent for review",
        summary:
          details.review_notes ||
          "Your study coordinator will review this receipt.",
      };
    case "DIGITAL_DISBURSEMENT_DISPATCHED":
      return {
        title: "Payout sent",
        summary:
          typeof details.amount === "number"
            ? `Sent $${details.amount.toFixed(2)} to your linked payout method.`
            : "Your balance payout has been sent.",
      };
    case "AUTOMATIC_STIPEND_DISBURSED":
      return {
        title: "Study stipend added",
        summary:
          typeof details.stipend_amount === "number"
            ? `Added $${details.stipend_amount.toFixed(2)} to your wallet.`
            : "Your study stipend was added to your wallet.",
      };
    default:
      return {
        title: humanizeActionCode(action),
        summary: block?.reason || "Recorded in your activity history.",
      };
  }
}

function formatLedgerTime(isoTimestamp) {
  if (!isoTimestamp) return "";
  const parsed = new Date(isoTimestamp);
  return Number.isNaN(parsed.getTime())
    ? String(isoTimestamp)
    : parsed.toLocaleString();
}

export async function logAuditRecord(
  action,
  details,
  reason = "Patient action verified"
) {
  const index = state.ledgerBlocks.length;
  const timestamp = demoNowIso(index);
  const prevHash =
    index === 0
      ? "0000000000000000000000000000000000000000000000000000000000000000" // deid-ignore
      : state.ledgerBlocks[index - 1].hash;

  const block = await buildLedgerBlock(
    index,
    timestamp,
    action,
    details,
    reason,
    prevHash
  );

  state.ledgerBlocks.push(block);
  renderLedger();
  return block;
}

export function renderLedger() {
  const container = document.getElementById("portal-ledger-timeline");
  if (!container) return;

  if (state.ledgerBlocks.length === 0) {
    container.innerHTML = `<div class="loading-state">No recent activity yet.</div>`;
    return;
  }

  container.innerHTML = state.ledgerBlocks
    .slice()
    .reverse()
    .map((block) => {
      const { title, summary } = describeLedgerBlock(block);
      const formattedTime = formatLedgerTime(block.timestamp);
      return `
      <div class="ledger-item">
        <div class="ledger-item-header">
          <span><strong>${escapeHtml(title)}</strong></span>
          <span>${escapeHtml(formattedTime)}</span>
        </div>
        <div class="ledger-item-details">
          <span>${escapeHtml(summary)}</span>
        </div>
        <details class="ledger-verification-details">
          <summary>Verification status</summary>
          <div class="ledger-hash">
            Saved securely on this device · Entry #${escapeHtml((block.index ?? 0) + 1)}
          </div>
        </details>
      </div>
    `;
    })
    .join("");
}
