// My Wallet: live wallet projection, receipt OCR extraction, expense claims, and balance payout.
import { DEFAULT_DEMO_SITE, DEMO_STUDY } from "demo-fixtures";
import { handoffFocus } from "ui";
import { dispatchApi, isAuthenticatedSession } from "../api/client.js";
import { state } from "../stores/state.js";
import {
  demoNowIso,
  demoRandomToken,
  isDemoModeActive,
} from "../utils/demoMode.js";
import { logAuditRecord } from "./audit.js";

const EXPENSE_IDEM_STORAGE_KEY = "cadence.wallet.expenseIdempotency";
const DISBURSE_IDEM_STORAGE_KEY = "cadence.wallet.disburseIdempotency";

function generateIdempotencyKey(prefix = "idem") {
  if (isDemoModeActive()) {
    return `${prefix}_${demoRandomToken(16, 36, `wallet-${prefix}`)}`;
  }
  const randomPart =
    typeof globalThis.crypto?.randomUUID === "function"
      ? globalThis.crypto.randomUUID()
      : `${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
  return `${prefix}_${randomPart}`;
}

function readStoredIdempotencyRecord(storageKey) {
  try {
    const raw = globalThis.sessionStorage?.getItem(storageKey);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed.key === "string" && parsed.key.trim()) {
      return parsed;
    }
  } catch {
    // Ignore storage read errors in restricted contexts
  }
  return null;
}

function writeStoredIdempotencyRecord(storageKey, fingerprint, key) {
  try {
    globalThis.sessionStorage?.setItem(
      storageKey,
      JSON.stringify({ fingerprint, key })
    );
  } catch {
    // Ignore storage write errors in restricted contexts
  }
}

function clearStoredIdempotencyRecord(storageKey) {
  try {
    globalThis.sessionStorage?.removeItem(storageKey);
  } catch {
    // Ignore storage removal errors
  }
}

function resolveIdempotencyKey({
  storageKey,
  prefix,
  fingerprint,
  explicitKey,
}) {
  if (typeof explicitKey === "string" && explicitKey.trim()) {
    return explicitKey.trim();
  }
  const stored = readStoredIdempotencyRecord(storageKey);
  if (stored && stored.fingerprint === fingerprint) {
    return stored.key;
  }
  const nextKey = generateIdempotencyKey(prefix);
  writeStoredIdempotencyRecord(storageKey, fingerprint, nextKey);
  return nextKey;
}

export function isWalletActionAllowed() {
  return Boolean(
    isDemoModeActive() || (isAuthenticatedSession() && state.session?.userId)
  );
}

export function isWalletW9HoldActive() {
  const thresholdExceeded =
    Boolean(state.is1099ThresholdExceeded) ||
    Number(state.taxableYtd || 0) >= 600;
  const w9Verified =
    String(state.w9Status || "")
      .trim()
      .toUpperCase() === "VERIFIED";
  return thresholdExceeded && !w9Verified;
}

function bytesToBase64(uint8) {
  let binary = "";
  const chunkSize = 0x8000;
  for (let i = 0; i < uint8.length; i += chunkSize) {
    const sub = uint8.subarray(i, i + chunkSize);
    binary += String.fromCharCode(...sub);
  }
  return btoa(binary);
}

async function tryRasterizeLossyImageToPngBase64(blob) {
  if (
    typeof document === "undefined" ||
    typeof createImageBitmap !== "function"
  ) {
    return null;
  }
  try {
    const bitmap = await createImageBitmap(blob);
    const canvas = document.createElement("canvas");
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    const ctx = canvas.getContext("2d");
    if (!ctx) return null;
    ctx.drawImage(bitmap, 0, 0);
    if (typeof bitmap.close === "function") bitmap.close();
    const dataUrl = canvas.toDataURL("image/png");
    return dataUrl && dataUrl.startsWith("data:image/png;base64,")
      ? dataUrl
      : null;
  } catch {
    return null;
  }
}

async function readReceiptPayload(fileOrText) {
  if (typeof fileOrText === "string") {
    return {
      rawText: fileOrText,
      imageBase64: null,
      fileName: "",
    };
  }

  if (!fileOrText || typeof fileOrText !== "object") {
    return { rawText: "", imageBase64: null, fileName: "" };
  }

  const fileName = typeof fileOrText.name === "string" ? fileOrText.name : "";

  if (typeof fileOrText.rawText === "string" && fileOrText.rawText.trim()) {
    return {
      rawText: fileOrText.rawText,
      imageBase64: fileOrText.imageBase64 || null,
      fileName,
    };
  }

  if (
    typeof fileOrText.imageBase64 === "string" &&
    fileOrText.imageBase64.trim()
  ) {
    return {
      rawText: "",
      imageBase64: fileOrText.imageBase64,
      fileName,
    };
  }

  const mimeType = String(fileOrText.type || "").toLowerCase();
  if (
    (mimeType === "image/jpeg" || mimeType === "image/webp") &&
    typeof Blob !== "undefined" &&
    fileOrText instanceof Blob
  ) {
    const convertedPng = await tryRasterizeLossyImageToPngBase64(fileOrText);
    if (convertedPng) {
      return { rawText: "", imageBase64: convertedPng, fileName };
    }
  }

  if (typeof fileOrText.arrayBuffer === "function") {
    try {
      const buf = await fileOrText.arrayBuffer();
      const bytes = new Uint8Array(buf);
      if (bytes.length > 0) {
        return {
          rawText: "",
          imageBase64: bytesToBase64(bytes),
          fileName,
        };
      }
    } catch {
      // Fall through to text fallback if arrayBuffer is unavailable in mock object
    }
  }

  if (typeof fileOrText.text === "function") {
    try {
      const txt = await fileOrText.text();
      return { rawText: txt || "", imageBase64: null, fileName };
    } catch {
      return { rawText: "", imageBase64: null, fileName };
    }
  }

  return { rawText: "", imageBase64: null, fileName };
}

function applyWalletProjection(summaryOrWallet) {
  if (!summaryOrWallet || typeof summaryOrWallet !== "object") return;
  const w = summaryOrWallet.wallet || summaryOrWallet.Wallet || summaryOrWallet;
  if (!w || typeof w !== "object") return;

  state.walletId = w.id ?? w.Id ?? state.walletId;
  state.walletStudyId =
    w.studyId ??
    w.study_id ??
    state.walletStudyId ??
    (isDemoModeActive() ? DEMO_STUDY?.code || null : null);
  state.walletSiteId =
    w.siteId ??
    w.site_id ??
    state.walletSiteId ??
    (isDemoModeActive() ? DEFAULT_DEMO_SITE?.code || null : null);

  if (!state.session.studyId && state.walletStudyId) {
    state.session.studyId = state.walletStudyId;
  }
  if (!state.session.siteId && state.walletSiteId) {
    state.session.siteId = state.walletSiteId;
  }

  const currentBal = w.currentBalance ?? w.current_balance;
  if (typeof currentBal === "number") {
    state.walletBalance = currentBal;
  }

  const stipends = w.lifetimeStipends ?? w.lifetime_stipends;
  if (typeof stipends === "number") {
    state.lifetimeStipends = stipends;
  }

  const reimbursements = w.lifetimeReimbursements ?? w.lifetime_reimbursements;
  if (typeof reimbursements === "number") {
    state.lifetimeReimbursements = reimbursements;
  }

  const disbursements = w.lifetimeDisbursements ?? w.lifetime_disbursements;
  if (typeof disbursements === "number") {
    state.lifetimeDisbursements = disbursements;
  }

  const taxableYtd = w.taxableYtd ?? w.taxable_ytd;
  if (typeof taxableYtd === "number") {
    state.taxableYtd = taxableYtd;
  }

  state.is1099ThresholdExceeded = Boolean(
    w.is1099ThresholdExceeded ?? w.is_1099_threshold_exceeded
  );
  state.w9Status = w.w9Status ?? w.w9_status ?? "NOT_REQUIRED";
  state.currency = w.currency ?? w.Currency ?? "USD";
  state.virtualCardMask =
    w.virtualCardMask ?? w.virtual_card_mask ?? state.virtualCardMask;
  state.virtualCardExpiry =
    w.virtualCardExpiry ?? w.virtual_card_expiry ?? state.virtualCardExpiry;
  state.achRoutingMask =
    w.achRoutingMask ??
    w.ach_routing_mask ??
    w.achRoutingNumber ??
    w.ach_routing_number ??
    state.achRoutingMask;
  state.achAccountMask =
    w.achAccountMask ?? w.ach_account_mask ?? state.achAccountMask;
  state.achAccountHolder =
    w.achAccountHolder ?? w.ach_account_holder ?? state.achAccountHolder;

  const txs =
    summaryOrWallet.recentTransactions ?? summaryOrWallet.recent_transactions;
  if (Array.isArray(txs)) {
    state.recentTransactions = txs;
  }

  const claims =
    summaryOrWallet.expenseClaims ?? summaryOrWallet.expense_claims;
  if (Array.isArray(claims)) {
    state.expenseClaims = claims;
  }

  if (state.expenseForm) {
    state.expenseForm.studyId = state.walletStudyId;
    state.expenseForm.siteId = state.walletSiteId;
  }
  state.walletLoaded = true;
}

export function resetExpenseClaimForm() {
  clearStoredIdempotencyRecord(EXPENSE_IDEM_STORAGE_KEY);
  state.expenseManualEntry = false;
  state.expenseOcr = {
    loading: false,
    error: null,
    warning: null,
    data: null,
    previewUrl: null,
  };
  const demo = isDemoModeActive();
  const defaultStudyId = demo ? DEMO_STUDY?.code || null : null;
  const defaultSiteId = demo ? DEFAULT_DEMO_SITE?.code || null : null;
  state.expenseForm = {
    subjectId: state.session?.userId || null,
    studyId: state.walletStudyId ?? defaultStudyId,
    siteId: state.walletSiteId ?? defaultSiteId,
    category: "TRANSIT",
    merchantName: "",
    grossAmount: 0.0,
    isMealAllowance: false,
    perDiemCap: 75.0,
    expenseDate: "",
    ocrConfidence: 0.0,
    receiptImageName: "",
    idempotencyKey: null,
  };
  if (typeof document !== "undefined") {
    const fileInput = document.getElementById("receipt-file-input");
    if (fileInput && "value" in fileInput) {
      try {
        fileInput.value = "";
      } catch {
        // Ignore read-only mock file inputs
      }
    }
  }
}

export function openManualExpenseEntry() {
  state.expenseError = null;
  state.expenseResult = null;
  state.expenseManualEntry = true;
  if (state.expenseForm) {
    state.expenseForm.ocrConfidence = 0.0;
    state.expenseForm.idempotencyKey = null;
  }
}

export function openDisburseModal(initiatingEl = null) {
  if (typeof document !== "undefined") {
    state.initiatingButton = initiatingEl || document.activeElement || null;
  }
  const available =
    typeof state.walletBalance === "number" && state.walletBalance > 0
      ? Number(state.walletBalance).toFixed(2)
      : "";
  const defaultMethod = state.achAccountMask
    ? "DIRECT_ACH"
    : state.virtualCardMask
      ? "VIRTUAL_CARD"
      : "DIRECT_ACH";
  state.disburseForm = {
    disbursementType: defaultMethod,
    amount: available,
    error: null,
    idempotencyKey: null,
  };
  state.disburseModalOpen = true;
}

export function closeDisburseModal() {
  state.disburseModalOpen = false;
  if (state.disburseForm) {
    state.disburseForm.error = null;
  }
}

export async function loadParticipantWallet() {
  if (!isWalletActionAllowed()) {
    return null;
  }

  state.walletLoading = true;
  state.expenseError = null;
  try {
    const res = await dispatchApi("api/v1/payments/participant/me/wallet", {
      method: "GET",
    });
    applyWalletProjection(res);
    return res;
  } catch (err) {
    const msg =
      err?.message || "Unable to load participant wallet details right now.";
    state.expenseError = msg;
    throw err;
  } finally {
    state.walletLoading = false;
  }
}

export async function extractReceiptOcr(fileOrText) {
  state.expenseError = null;
  state.expenseResult = null;
  state.expenseOcr.error = null;
  state.expenseOcr.warning = null;

  if (!isWalletActionAllowed()) {
    const msg =
      "Participant receipt OCR extraction and travel reimbursements are not available in this environment without an authenticated participant session.";
    state.expenseOcr.loading = false;
    state.expenseOcr.error = msg;
    state.expenseError = msg;
    throw new Error(msg);
  }

  state.expenseOcr.loading = true;
  clearStoredIdempotencyRecord(EXPENSE_IDEM_STORAGE_KEY);
  if (state.expenseForm) {
    state.expenseForm.idempotencyKey = null;
  }

  const { rawText, imageBase64, fileName } =
    await readReceiptPayload(fileOrText);
  if (fileName) {
    state.expenseForm.receiptImageName = fileName;
  }

  try {
    const rawRes = await dispatchApi(
      "api/v1/payments/participant/me/expenses/ocr-extract",
      {
        method: "POST",
        body: JSON.stringify({
          rawText: rawText || null,
          imageBase64: imageBase64 || null,
          filename: fileName || null,
        }),
        change_reason: "Mobile receipt OCR extraction",
      }
    );

    const ocrReadable =
      rawRes.ocrReadable ??
      rawRes.ocr_readable ??
      Boolean(
        (rawRes.merchantName || rawRes.merchant_name) &&
        (rawRes.grossAmount || rawRes.gross_amount)
      );
    const merchantName = rawRes.merchantName ?? rawRes.merchant_name ?? "";
    const expenseDate = rawRes.expenseDate ?? rawRes.expense_date ?? "";
    const grossAmount = Number(rawRes.grossAmount ?? rawRes.gross_amount ?? 0);
    const taxDeduction = Number(
      rawRes.taxDeduction ?? rawRes.tax_deduction ?? 0
    );
    const netAmount = Number(
      rawRes.netAmount ?? rawRes.net_amount ?? grossAmount
    );
    const category = rawRes.category || "OTHER";
    const confidenceScore = Number(
      rawRes.confidenceScore ?? rawRes.confidence_score ?? 0
    );
    const warning = rawRes.warning ?? null;

    const normalized = {
      ...rawRes,
      ocrReadable,
      ocr_readable: ocrReadable,
      merchantName,
      merchant_name: merchantName,
      expenseDate,
      expense_date: expenseDate,
      grossAmount,
      gross_amount: grossAmount,
      taxDeduction,
      tax_deduction: taxDeduction,
      netAmount,
      net_amount: netAmount,
      category,
      confidenceScore,
      confidence_score: confidenceScore,
      warning,
    };

    state.expenseOcr.data = normalized;
    state.expenseOcr.warning =
      warning ||
      (!ocrReadable
        ? "We could not read legible receipt text from this image. Please upload a clearer photo or enter the receipt details manually below."
        : null);

    state.expenseManualEntry = !ocrReadable;
    state.expenseForm.merchantName = merchantName;
    state.expenseForm.expenseDate = expenseDate;
    state.expenseForm.grossAmount = ocrReadable ? grossAmount : 0;
    state.expenseForm.category = category || "TRANSIT";
    state.expenseForm.ocrConfidence = ocrReadable ? confidenceScore : 0;
    state.expenseForm.isMealAllowance = state.expenseForm.category === "MEAL";

    await logAuditRecord(
      "OCR_RECEIPT_EXTRACTED",
      {
        ocr_readable: ocrReadable,
        merchant_name: merchantName,
        expense_date: expenseDate,
        gross_amount: grossAmount,
        category: state.expenseForm.category,
        confidence_score: confidenceScore,
      },
      "Mobile receipt OCR initial extraction"
    );
    state.expenseOcr.loading = false;
    return normalized;
  } catch (err) {
    const msg = err?.message || String(err);
    state.expenseOcr.error = msg;
    state.expenseOcr.warning =
      "Automatic receipt scan was unavailable. You can enter your receipt details manually below or retry uploading the receipt image.";
    state.expenseError = msg;
    state.expenseManualEntry = true;
    state.expenseForm.ocrConfidence = 0;
    state.expenseOcr.loading = false;
    throw err;
  }
}

export async function submitExpenseClaim(customFormData = null) {
  state.expenseError = null;

  if (!isWalletActionAllowed()) {
    const msg =
      "Participant travel expense submission is not available in this environment without an authenticated participant session.";
    state.expenseError = msg;
    throw new Error(msg);
  }

  const form = customFormData || state.expenseForm;
  const grossAmount = Number(form.grossAmount) || 0;
  const category = form.category || "TRANSIT";
  const merchantName = (form.merchantName || "").trim();
  const expenseDate = (form.expenseDate || "").trim();
  const receiptImageName = form.receiptImageName || "";
  const isMealAllowance = Boolean(form.isMealAllowance || category === "MEAL");
  const perDiemCap = form.perDiemCap ? Number(form.perDiemCap) : 75.0;
  const ocrConfidence =
    typeof form.ocrConfidence === "number" ? Number(form.ocrConfidence) : 0;

  const fingerprint = [
    state.session?.userId || "",
    category,
    merchantName,
    grossAmount.toFixed(2),
    expenseDate,
    receiptImageName,
  ].join("|");

  const idempotencyKey = resolveIdempotencyKey({
    storageKey: EXPENSE_IDEM_STORAGE_KEY,
    prefix: "exp",
    fingerprint,
    explicitKey: form.idempotencyKey,
  });

  if (!customFormData && state.expenseForm) {
    state.expenseForm.idempotencyKey = idempotencyKey;
  }

  const payload = {
    category,
    merchantName,
    grossAmount,
    isMealAllowance,
    perDiemCap,
    expenseDate,
    receiptImageName,
    ocrConfidence,
    receiptOcrData: state.expenseOcr.data || null,
    idempotencyKey,
    studyId: form.studyId || state.walletStudyId || undefined,
    siteId: form.siteId || state.walletSiteId || undefined,
    walletId: state.walletId || undefined,
  };

  state.expenseSubmitting = true;
  try {
    const rawRes = await dispatchApi(
      "api/v1/payments/participant/me/expenses",
      {
        method: "POST",
        headers: {
          "Idempotency-Key": idempotencyKey,
        },
        body: JSON.stringify(payload),
        change_reason: "Submit participant expense claim",
      }
    );

    const autoApproved = Boolean(
      rawRes.autoApproved ??
      rawRes.auto_approved ??
      (rawRes.approvalDecision === "AUTO_APPROVED" ||
        rawRes.status === "AUTO_APPROVED")
    );
    const disbursementReference =
      rawRes.disbursementReference ?? rawRes.disbursement_reference ?? null;
    const reviewNotes = rawRes.reviewNotes ?? rawRes.review_notes ?? null;
    const claimId = rawRes.id ?? rawRes.claim_id ?? `claim_${idempotencyKey}`;
    const netAmount = Number(
      rawRes.netAmount ?? rawRes.net_amount ?? grossAmount
    );

    const normalized = {
      ...rawRes,
      id: claimId,
      claim_id: claimId,
      category: rawRes.category || category,
      merchantName: rawRes.merchantName ?? rawRes.merchant_name ?? merchantName,
      expenseDate: rawRes.expenseDate ?? rawRes.expense_date ?? expenseDate,
      status: autoApproved
        ? "AUTO_APPROVED"
        : rawRes.status || "PENDING_REVIEW",
      ledgerStatus:
        rawRes.status || (autoApproved ? "APPROVED" : "PENDING_REVIEW"),
      autoApproved,
      auto_approved: autoApproved,
      disbursementReference,
      disbursement_reference: disbursementReference,
      reviewNotes,
      review_notes: reviewNotes,
      grossAmount,
      gross_amount: grossAmount,
      netAmount,
      net_amount: netAmount,
      createdAt: rawRes.createdAt ?? rawRes.created_at ?? demoNowIso(),
    };

    state.expenseResult = normalized;

    const existingClaims = Array.isArray(state.expenseClaims)
      ? [...state.expenseClaims]
      : [];
    const existingIdx = existingClaims.findIndex(
      (c) => (c.id || c.Id || c.claim_id) === claimId
    );
    if (existingIdx >= 0) {
      existingClaims[existingIdx] = {
        ...existingClaims[existingIdx],
        ...normalized,
      };
      state.expenseClaims = existingClaims;
    } else {
      state.expenseClaims = [normalized, ...existingClaims];
    }

    if (typeof rawRes.lifetimeReimbursements === "number") {
      state.lifetimeReimbursements = rawRes.lifetimeReimbursements;
    } else if (autoApproved && existingIdx < 0) {
      state.lifetimeReimbursements =
        (Number(state.lifetimeReimbursements) || 0) + netAmount;
    }

    if (typeof rawRes.lifetimeDisbursements === "number") {
      state.lifetimeDisbursements = rawRes.lifetimeDisbursements;
    } else if (autoApproved && existingIdx < 0) {
      state.lifetimeDisbursements =
        (Number(state.lifetimeDisbursements) || 0) + netAmount;
    }

    if (typeof rawRes.walletCurrentBalance === "number") {
      state.walletBalance = rawRes.walletCurrentBalance;
    }

    if (!customFormData) {
      resetExpenseClaimForm();
    } else {
      clearStoredIdempotencyRecord(EXPENSE_IDEM_STORAGE_KEY);
    }

    if (
      typeof document !== "undefined" &&
      state.currentView === "view-wallet"
    ) {
      void handoffFocus([
        "#expense-auto-approved-heading",
        "#expense-pending-review-heading",
        "#btn-manual-expense-entry",
        "#wallet-view-heading",
        "#main-content",
      ]);
    }

    if (autoApproved) {
      await logAuditRecord(
        "EXPENSE_CLAIM_AUTO_APPROVED",
        {
          claim_id: claimId,
          gross_amount: grossAmount,
          net_amount: netAmount,
        },
        "Policy-backed auto-approval executed"
      );

      await logAuditRecord(
        "DIGITAL_DISBURSEMENT_DISPATCHED",
        {
          disbursement_reference: disbursementReference,
          amount: netAmount,
          method: "DIRECT_ACH",
        },
        "Automated direct ACH reimbursement dispatched"
      );
    } else {
      await logAuditRecord(
        "EXPENSE_CLAIM_HELD_FOR_REVIEW",
        { claim_id: claimId, review_notes: reviewNotes },
        reviewNotes || "Expense claim placed on hold for manual review"
      );
    }

    return normalized;
  } catch (err) {
    const msg = err?.message || String(err);
    state.expenseError = msg;
    throw err;
  } finally {
    state.expenseSubmitting = false;
  }
}

export async function disburseAvailableBalance(
  disbursementType = "DIRECT_ACH",
  options = {}
) {
  if (state.disbursingBalance) {
    return null;
  }

  state.expenseError = null;
  state.disbursementConfirmation = null;
  if (state.disburseForm) {
    state.disburseForm.error = null;
  }

  if (!isWalletActionAllowed()) {
    const msg =
      "Participant wallet payouts are not available in this environment without an authenticated participant session.";
    state.expenseError = msg;
    throw new Error(msg);
  }

  if (isWalletW9HoldActive()) {
    const holdMsg =
      "Annual taxable payments have reached $600.00 without a verified Form W-9. Automated payouts are halted until your Form W-9 is verified by site staff.";
    state.expenseError = holdMsg;
    if (state.disburseForm) {
      state.disburseForm.error = holdMsg;
    }
    throw new Error(holdMsg);
  }

  const normalizedType =
    String(disbursementType || "DIRECT_ACH").toUpperCase() === "VIRTUAL_CARD"
      ? "VIRTUAL_CARD"
      : "DIRECT_ACH";

  const hasExplicitAmount =
    options &&
    options.amount !== undefined &&
    options.amount !== null &&
    options.amount !== "";

  let requestedAmount = null;
  if (hasExplicitAmount) {
    const parsedAmount = Number(options.amount);
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      const msg = "Please enter a payout amount greater than zero.";
      state.expenseError = msg;
      if (state.disburseForm) {
        state.disburseForm.error = msg;
      }
      throw new Error(msg);
    }
    const rounded = Math.round(parsedAmount * 100) / 100;
    if (
      typeof state.walletBalance === "number" &&
      rounded > state.walletBalance + 0.0001
    ) {
      const msg = "Requested payout amount exceeds available wallet balance.";
      state.expenseError = msg;
      if (state.disburseForm) {
        state.disburseForm.error = msg;
      }
      throw new Error(msg);
    }
    requestedAmount = rounded;
  }

  const amountFingerprint =
    requestedAmount !== null
      ? requestedAmount.toFixed(2)
      : typeof state.walletBalance === "number"
        ? Number(state.walletBalance).toFixed(2)
        : "FULL";

  const fingerprint = [
    state.session?.userId || "",
    state.walletId || "",
    normalizedType,
    amountFingerprint,
  ].join("|");

  const idempotencyKey = resolveIdempotencyKey({
    storageKey: DISBURSE_IDEM_STORAGE_KEY,
    prefix: "disb",
    fingerprint,
    explicitKey: options?.idempotencyKey || state.disburseForm?.idempotencyKey,
  });

  if (state.disburseForm) {
    state.disburseForm.idempotencyKey = idempotencyKey;
  }

  state.disbursingBalance = true;
  try {
    const bodyPayload = {
      disbursementType: normalizedType,
      walletId: state.walletId || undefined,
      idempotencyKey,
    };
    if (requestedAmount !== null) {
      bodyPayload.amount = requestedAmount;
    }

    const res = await dispatchApi("api/v1/payments/participant/me/disburse", {
      method: "POST",
      headers: {
        "Idempotency-Key": idempotencyKey,
      },
      body: JSON.stringify(bodyPayload),
      change_reason: "Participant on-demand available balance payout",
    });

    const amount = Number(res.amount ?? res.Amount ?? requestedAmount ?? 0);
    const remaining = Number(
      res.currentBalance ?? res.current_balance ?? res.new_balance ?? 0
    );
    const refNum =
      res.referenceNumber ??
      res.reference_number ??
      (normalizedType === "VIRTUAL_CARD"
        ? "DISB-VCARD-CONFIRMED"
        : "DISB-ACH-CONFIRMED");
    const fallbackMask =
      normalizedType === "VIRTUAL_CARD"
        ? state.virtualCardMask || "Reloadable Study Card"
        : state.achAccountMask || "Linked Bank Account";
    const destination =
      res.destinationMask ?? res.destination_mask ?? fallbackMask;

    state.walletBalance = remaining;
    if (typeof res.lifetimeDisbursements === "number") {
      state.lifetimeDisbursements = res.lifetimeDisbursements;
    } else {
      state.lifetimeDisbursements =
        (Number(state.lifetimeDisbursements) || 0) + amount;
    }
    if (res.currency || res.Currency) {
      state.currency = res.currency || res.Currency;
    }

    state.disbursementConfirmation = {
      id: res.id ?? res.Id ?? null,
      amount,
      remainingBalance: remaining,
      referenceNumber: refNum,
      destinationMask: destination,
      disbursementType: res.disbursementType ?? normalizedType,
      currency: res.currency || state.currency || "USD",
    };

    clearStoredIdempotencyRecord(DISBURSE_IDEM_STORAGE_KEY);
    if (state.disburseForm) {
      state.disburseForm.idempotencyKey = null;
      state.disburseForm.error = null;
    }
    state.disburseModalOpen = false;

    await logAuditRecord(
      "DIGITAL_DISBURSEMENT_DISPATCHED",
      {
        disbursement_reference: refNum,
        amount,
        remaining_balance: remaining,
        method: normalizedType,
      },
      "On-demand participant wallet balance disbursed"
    );

    return state.disbursementConfirmation;
  } catch (err) {
    const msg =
      err?.message || "Unable to disburse available wallet balance right now.";
    state.expenseError = msg;
    if (state.disburseForm) {
      state.disburseForm.error = msg;
    }
    throw err;
  } finally {
    state.disbursingBalance = false;
  }
}
