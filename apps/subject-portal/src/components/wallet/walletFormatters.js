import { humanizeExpenseReviewNotes } from "../../utils/participantCopy.js";

export const WALLET_ENUM_LABELS = Object.freeze({
  NOT_REQUIRED: "Not required",
  REQUIRED: "Required",
  SUBMITTED: "Submitted",
  VERIFIED: "Verified",
  PENDING_REVIEW: "Pending review",
  AUTO_APPROVED: "Approved",
  APPROVED: "Approved",
  PAID: "Paid",
  STIPEND_CREDIT: "Study stipend",
  EXPENSE_REIMBURSEMENT: "Expense reimbursement",
  BALANCE_DISBURSEMENT: "Balance payout",
});

export function formatEnumLabel(rawValue) {
  const key = String(rawValue || "").trim();
  if (!key) return "";
  if (WALLET_ENUM_LABELS[key]) return WALLET_ENUM_LABELS[key];
  const words = key
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[._-]+/g, " ")
    .trim()
    .toLowerCase();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : key;
}

export function formatPlainReviewNotes(rawNotes) {
  return humanizeExpenseReviewNotes(rawNotes);
}

export function claimDisplayTitle(claim) {
  return (
    claim?.merchantName ||
    claim?.merchant_name ||
    claim?.MerchantName ||
    "Expense Claim"
  );
}

export function txDisplayTitle(tx) {
  return (
    tx?.description ||
    tx?.Description ||
    formatEnumLabel(tx?.transactionType || tx?.TransactionType) ||
    "Wallet Activity"
  );
}

export function claimStatusLabel(claim) {
  const raw = String(
    claim.approvalDecision || claim.status || claim.Status || "PENDING_REVIEW"
  ).toUpperCase();
  if (raw === "AUTO_APPROVED" || raw === "APPROVED") return "Approved";
  if (raw === "REJECTED") return "Rejected";
  return "Pending Review";
}

export function claimStatusClass(claim) {
  const raw = String(
    claim.approvalDecision || claim.status || claim.Status || "PENDING_REVIEW"
  ).toUpperCase();
  if (raw === "AUTO_APPROVED" || raw === "APPROVED") return "completed";
  if (raw === "REJECTED") return "overdue";
  return "pending";
}
