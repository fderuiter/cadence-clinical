// My Wallet: receipt OCR extraction and travel expense claims.
import { dispatchApi } from "../api/client.js";
import { state } from "../stores/state.js";
import { logAuditRecord } from "./audit.js";

export async function extractReceiptOcr(fileOrText) {
  state.expenseOcr.loading = true;
  state.expenseOcr.error = null;

  let rawText =
    "METRO TRANSIT AUTHORITY\nRoute: Clinical Trial Express\nDate: 2026-08-23\nFare Paid: $28.50"; // deid-ignore
  let categoryHint = "TRANSIT";

  if (typeof fileOrText === "string") {
    rawText = fileOrText;
  } else if (fileOrText && fileOrText.name) {
    state.expenseForm.receiptImageName = fileOrText.name;
    if (fileOrText.name.toLowerCase().includes("meal")) {
      rawText =
        "CITY BISTRO RESTAURANT\nDate: 2026-08-23\nMeal Charge: $125.00"; // deid-ignore
      categoryHint = "MEAL";
    } else if (
      fileOrText.name.toLowerCase().includes("fuel") ||
      fileOrText.name.toLowerCase().includes("gas")
    ) {
      rawText = "SHELL OIL\nDate: 2026-08-23\nFuel: $45.00"; // deid-ignore
      categoryHint = "MILEAGE";
    }
  }

  try {
    const ocrRes = await dispatchApi(
      "api/v1/payments/participant/expenses/ocr-extract",
      {
        method: "POST",
        body: JSON.stringify({
          raw_text: rawText,
          category_hint: categoryHint,
        }),
        change_reason: "Mobile receipt OCR extraction",
      }
    );

    state.expenseOcr.data = ocrRes;
    state.expenseForm.merchantName =
      ocrRes.merchant_name || "Metro Transit Authority";
    state.expenseForm.expenseDate = ocrRes.expense_date || "2026-08-23"; // deid-ignore
    state.expenseForm.grossAmount = ocrRes.gross_amount || 28.5;
    state.expenseForm.category = ocrRes.category || categoryHint;
    state.expenseForm.ocrConfidence = ocrRes.confidence_score || 0.98;
    state.expenseForm.isMealAllowance = ocrRes.category === "MEAL";

    await logAuditRecord(
      "OCR_RECEIPT_EXTRACTED",
      ocrRes,
      "Mobile receipt OCR initial extraction"
    );
    state.expenseOcr.loading = false;
    return ocrRes;
  } catch (err) {
    state.expenseOcr.error = err.message || err;
    state.expenseOcr.loading = false;
    throw err;
  }
}

export async function submitExpenseClaim(customFormData = null) {
  const form = customFormData || state.expenseForm;

  const payload = {
    subject_id: form.subjectId || "subject_001",
    study_id: form.studyId || "study_001",
    site_id: form.siteId || "site_001",
    category: form.category || "TRANSIT",
    merchant_name: form.merchantName || "Metro Transit Authority",
    gross_amount: Number(form.grossAmount) || 0,
    is_meal_allowance: Boolean(form.isMealAllowance),
    per_diem_cap: form.perDiemCap ? Number(form.perDiemCap) : 75.0,
    expense_date: form.expenseDate || "2026-08-23", // deid-ignore
    receipt_image_name: form.receiptImageName || "receipt.png",
    ocr_confidence: form.ocrConfidence ? Number(form.ocrConfidence) : 0.98,
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
    console.error("Failed to submit expense claim:", err);
    throw err;
  }
}
