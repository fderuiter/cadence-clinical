// My Wallet: receipt OCR extraction and travel expense claims.
import { dispatchApi } from "../api/client.js";
import { state } from "../stores/state.js";
import { allowUnbackedAction } from "../utils/unbackedActions.js";
import { logAuditRecord } from "./audit.js";

export async function extractReceiptOcr(fileOrText) {
  state.expenseError = null;
  state.expenseOcr.error = null;

  if (!allowUnbackedAction("Mobile receipt OCR extraction")) {
    const msg =
      "Participant receipt OCR extraction and travel reimbursements are not available in this environment.";
    state.expenseOcr.loading = false;
    state.expenseOcr.error = msg;
    state.expenseError = msg;
    throw new Error(msg);
  }

  state.expenseOcr.loading = true;

  let rawText = "";
  let fileName = "";

  if (typeof fileOrText === "string") {
    rawText = fileOrText;
  } else if (fileOrText && typeof fileOrText === "object") {
    if (fileOrText.name) {
      fileName = fileOrText.name;
      state.expenseForm.receiptImageName = fileOrText.name;
    }
    if (typeof fileOrText.text === "function") {
      try {
        rawText = await fileOrText.text();
      } catch {
        rawText = "";
      }
    }
  }

  try {
    const ocrRes = await dispatchApi(
      "api/v1/payments/participant/expenses/ocr-extract",
      {
        method: "POST",
        body: JSON.stringify({
          raw_text: rawText,
          file_name: fileName,
        }),
        change_reason: "Mobile receipt OCR extraction",
      }
    );

    state.expenseOcr.data = ocrRes;
    state.expenseForm.merchantName = ocrRes.merchant_name || "";
    state.expenseForm.expenseDate = ocrRes.expense_date || "";
    state.expenseForm.grossAmount =
      typeof ocrRes.gross_amount === "number" ? ocrRes.gross_amount : 0;
    state.expenseForm.category = ocrRes.category || "TRANSIT";
    state.expenseForm.ocrConfidence =
      typeof ocrRes.confidence_score === "number" ? ocrRes.confidence_score : 0;
    state.expenseForm.isMealAllowance = ocrRes.category === "MEAL";

    await logAuditRecord(
      "OCR_RECEIPT_EXTRACTED",
      ocrRes,
      "Mobile receipt OCR initial extraction"
    );
    state.expenseOcr.loading = false;
    return ocrRes;
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

  if (!allowUnbackedAction("Participant travel expense claim")) {
    const msg =
      "Participant travel expense submission is not available in this environment.";
    state.expenseError = msg;
    throw new Error(msg);
  }

  const form = customFormData || state.expenseForm;

  const payload = {
    subject_id: form.subjectId || state.session.userId || "",
    study_id: form.studyId || "",
    site_id: form.siteId || "",
    category: form.category || "TRANSIT",
    merchant_name: form.merchantName || "",
    gross_amount: Number(form.grossAmount) || 0,
    is_meal_allowance: Boolean(form.isMealAllowance),
    per_diem_cap: form.perDiemCap ? Number(form.perDiemCap) : 75.0,
    expense_date: form.expenseDate || "",
    receipt_image_name: form.receiptImageName || "",
    ocr_confidence:
      typeof form.ocrConfidence === "number" ? Number(form.ocrConfidence) : 0,
    receipt_ocr_data: state.expenseOcr.data || null,
  };

  try {
    const res = await dispatchApi("api/v1/payments/participant/expenses", {
      method: "POST",
      body: JSON.stringify(payload),
      change_reason: "Submit participant expense claim",
    });

    state.expenseResult = res;

    if (res.status === "AUTO_APPROVED" || res.auto_approved) {
      const net = res.net_amount || payload.gross_amount;
      state.walletBalance = (state.walletBalance || 0) + net;

      await logAuditRecord(
        "EXPENSE_CLAIM_AUTO_APPROVED",
        {
          claim_id: res.claim_id,
          gross_amount: payload.gross_amount,
          net_amount: net,
        },
        "Policy-backed auto-approval executed"
      );

      await logAuditRecord(
        "DIGITAL_DISBURSEMENT_DISPATCHED",
        {
          disbursement_reference: res.disbursement_reference,
          amount: net,
          method: "DIRECT_ACH",
        },
        "Automated direct ACH reimbursement dispatched"
      );
    } else {
      await logAuditRecord(
        "EXPENSE_CLAIM_HELD_FOR_REVIEW",
        { claim_id: res.claim_id, review_notes: res.review_notes },
        res.review_notes || "Expense claim placed on hold for manual review"
      );
    }

    return res;
  } catch (err) {
    const msg = err?.message || String(err);
    state.expenseError = msg;
    throw err;
  }
}
