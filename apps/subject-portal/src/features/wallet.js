// My Wallet: live wallet projection, receipt OCR extraction, expense claims, and balance payout.
import { dispatchApi, isAuthenticatedSession } from "../api/client.js";
import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import { logAuditRecord } from "./audit.js";

export function isWalletActionAllowed() {
  return Boolean(
    isDemoModeActive() || (isAuthenticatedSession() && state.session?.userId)
  );
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
    w.studyId ?? w.study_id ?? state.walletStudyId ?? "study_001";
  state.walletSiteId =
    w.siteId ?? w.site_id ?? state.walletSiteId ?? "site_001";

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
    state.expenseError = msg;
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
  const isMealAllowance = Boolean(form.isMealAllowance || category === "MEAL");
  const perDiemCap = form.perDiemCap ? Number(form.perDiemCap) : 75.0;
  const ocrConfidence =
    typeof form.ocrConfidence === "number" ? Number(form.ocrConfidence) : 0;

  const payload = {
    category,
    merchantName: form.merchantName || "",
    grossAmount,
    isMealAllowance,
    perDiemCap,
    expenseDate: form.expenseDate || "",
    receiptImageName: form.receiptImageName || "",
    ocrConfidence,
    receiptOcrData: state.expenseOcr.data || null,
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
    const claimId = rawRes.id ?? rawRes.claim_id ?? null;
    const netAmount = Number(
      rawRes.netAmount ?? rawRes.net_amount ?? grossAmount
    );

    const normalized = {
      ...rawRes,
      claim_id: claimId,
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
      gross_amount: grossAmount,
      net_amount: netAmount,
    };

    state.expenseResult = normalized;

    if (typeof rawRes.lifetimeReimbursements === "number") {
      state.lifetimeReimbursements = rawRes.lifetimeReimbursements;
    } else if (autoApproved) {
      state.lifetimeReimbursements =
        (Number(state.lifetimeReimbursements) || 0) + netAmount;
    }

    if (typeof rawRes.lifetimeDisbursements === "number") {
      state.lifetimeDisbursements = rawRes.lifetimeDisbursements;
    } else if (autoApproved) {
      state.lifetimeDisbursements =
        (Number(state.lifetimeDisbursements) || 0) + netAmount;
    }

    if (typeof rawRes.walletCurrentBalance === "number") {
      state.walletBalance = rawRes.walletCurrentBalance;
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
  disbursementType = "DIRECT_ACH"
) {
  if (state.disbursingBalance) {
    return null;
  }

  state.expenseError = null;
  state.disbursementConfirmation = null;

  if (!isWalletActionAllowed()) {
    const msg =
      "Participant wallet payouts are not available in this environment without an authenticated participant session.";
    state.expenseError = msg;
    throw new Error(msg);
  }

  state.disbursingBalance = true;
  try {
    const res = await dispatchApi("api/v1/payments/participant/me/disburse", {
      method: "POST",
      body: JSON.stringify({
        disbursementType,
        walletId: state.walletId || undefined,
      }),
      change_reason: "Participant on-demand available balance payout",
    });

    const amount = Number(res.amount ?? res.Amount ?? 0);
    const remaining = Number(
      res.currentBalance ?? res.current_balance ?? res.new_balance ?? 0
    );
    const refNum =
      res.referenceNumber ?? res.reference_number ?? "DISB-ACH-CONFIRMED";
    const destination =
      res.destinationMask ??
      res.destination_mask ??
      state.achAccountMask ??
      "Linked Bank Account";

    state.walletBalance = remaining;
    if (typeof res.lifetimeDisbursements === "number") {
      state.lifetimeDisbursements = res.lifetimeDisbursements;
    } else {
      state.lifetimeDisbursements =
        (Number(state.lifetimeDisbursements) || 0) + amount;
    }

    state.disbursementConfirmation = {
      amount,
      remainingBalance: remaining,
      referenceNumber: refNum,
      destinationMask: destination,
      disbursementType: res.disbursementType ?? disbursementType,
    };

    await logAuditRecord(
      "DIGITAL_DISBURSEMENT_DISPATCHED",
      {
        disbursement_reference: refNum,
        amount,
        remaining_balance: remaining,
        method: disbursementType,
      },
      "On-demand participant wallet balance disbursed"
    );

    return state.disbursementConfirmation;
  } catch (err) {
    const msg =
      err?.message || "Unable to disburse available wallet balance right now.";
    state.expenseError = msg;
    throw err;
  } finally {
    state.disbursingBalance = false;
  }
}
