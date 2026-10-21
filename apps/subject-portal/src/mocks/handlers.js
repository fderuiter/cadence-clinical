import { http, HttpResponse, passthrough } from "msw";
import { isGatewayApiCall, validateGatewayRequest } from "ui/testing";

export const gatewayHandlers = [
  http.post("*/api/v1/ai/biometrics/verify-liveness", async ({ request }) => {
    const body = await request.json().catch(() => ({}));
    const subject = body.subject_pseudonym || "subj-001";
    const isFallback = Boolean(body.fallback_mode);
    const frames = body.video_frames || [];
    const isLive = isFallback || frames.length > 0;
    const score = isFallback ? 0.92 : isLive ? 0.98 : 0.15;
    const expiresAt = new Date(Date.now() + 15 * 60 * 1000).toISOString();
    const token = `mock_liveness_token_${subject}_${isFallback ? "fallback" : "camera"}_${Date.now()}`;

    if (!isLive) {
      return HttpResponse.json(
        {
          liveness_token: token,
          is_live: false,
          anti_spoof_score: score,
          expires_at: expiresAt,
          status: "REJECTED",
        },
        { status: 400 }
      );
    }

    return HttpResponse.json({
      liveness_token: token,
      is_live: true,
      anti_spoof_score: score,
      expires_at: expiresAt,
      status: "VERIFIED",
    });
  }),

  http.post(
    "*/api/v1/payments/participant/expenses/ocr-extract",
    async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const text =
        body.raw_text ||
        body.rawText ||
        "METRO TRANSIT AUTHORITY\nRoute: Clinical Trial Express\nDate: 2026-08-23\nFare Paid: $28.50"; // deid-ignore
      const category = body.category_hint || body.categoryHint || "TRANSIT";

      let grossAmount = 28.5;
      if (text.includes("125.00") || text.includes("125")) grossAmount = 125.0;
      else if (text.includes("85.00") || text.includes("85"))
        grossAmount = 85.0;
      else if (text.includes("28.50")) grossAmount = 28.5;

      return HttpResponse.json({
        merchant_name: "METRO TRANSIT AUTHORITY",
        expense_date: "2026-08-23", // deid-ignore
        gross_amount: grossAmount,
        tax_deduction: 0.0,
        net_amount: grossAmount,
        category: category,
        confidence_score: 0.98,
        raw_text: text,
      });
    }
  ),

  http.post("*/api/v1/payments/participant/expenses", async ({ request }) => {
    const body = await request.json().catch(() => ({}));
    const grossAmount =
      typeof body.gross_amount === "number"
        ? body.gross_amount
        : typeof body.grossAmount === "number"
          ? body.grossAmount
          : 28.5;
    const confidence =
      typeof body.ocr_confidence === "number"
        ? body.ocr_confidence
        : typeof body.ocrConfidence === "number"
          ? body.ocrConfidence
          : 0.98;
    const category = body.category || "TRANSIT";
    const isMeal =
      body.is_meal_allowance || body.isMealAllowance || category === "MEAL";
    const perDiemCap =
      typeof body.per_diem_cap === "number" ? body.per_diem_cap : 75.0;

    const isAmountUnderCap = grossAmount < 100.0;
    const isConfidenceHigh = confidence >= 0.9;
    const isMealCompliant = !isMeal || grossAmount <= perDiemCap;

    if (isAmountUnderCap && isConfidenceHigh && isMealCompliant) {
      const timestamp = Date.now();
      const disbRef = `DISB-ACH-${timestamp}`;
      return HttpResponse.json({
        claim_id: "claim_" + timestamp,
        status: "AUTO_APPROVED",
        auto_approved: true,
        disbursement_reference: disbRef,
        gross_amount: grossAmount,
        net_amount: grossAmount,
        wallet_credit: grossAmount,
        disbursement_method: "DIRECT_ACH",
        dispatched_at: new Date().toISOString(),
      });
    } else {
      let reason = "Claim flagged for manual review.";
      if (!isAmountUnderCap)
        reason = `Gross amount $${grossAmount.toFixed(2)} exceeds $100.00 auto-approval threshold. Placed on hold for sponsor manual review.`;
      else if (!isMealCompliant)
        reason = `Meal expense $${grossAmount.toFixed(2)} exceeds per-diem allowance $${perDiemCap.toFixed(2)}. Placed on hold for manual review.`;
      else if (!isConfidenceHigh)
        reason = `OCR confidence score ${confidence} below required 0.90 threshold. Placed on hold for manual review.`;

      return HttpResponse.json({
        claim_id: "claim_" + Date.now(),
        status: "PENDING_REVIEW",
        auto_approved: false,
        disbursement_reference: null,
        review_notes: reason,
        gross_amount: grossAmount,
        net_amount: grossAmount,
      });
    }
  }),

  http.post("*/api/v1/payments/disburse", async ({ request }) => {
    const body = await request.json().catch(() => ({}));
    const subjectId = body.subject_id || "subject_001";
    const amount = typeof body.amount === "number" ? body.amount : 25.0;
    const seq = body.sequence_number || 1;
    return HttpResponse.json({
      success: true,
      id: "disb_" + Date.now(),
      wallet_id: "wallet_" + subjectId,
      subject_id: subjectId,
      amount: amount,
      new_balance: amount * seq,
      currency: "USD",
      status: "COMPLETED",
      reference_number: "DISB-MOCK-" + seq,
      idempotency_key: body.idempotency_key || `seq_${seq}_${subjectId}`,
      dispatched_at: new Date().toISOString(),
    });
  }),

  http.post(
    "*/api/v1/payments/participant/wallets/disburse",
    async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const subjectId = body.subject_id || "subject_001";
      const amount = typeof body.amount === "number" ? body.amount : 25.0;
      return HttpResponse.json({
        success: true,
        id: "disb_" + Date.now(),
        wallet_id: "wallet_" + subjectId,
        subject_id: subjectId,
        amount: amount,
        new_balance: amount,
        currency: "USD",
        status: "COMPLETED",
        reference_number: "DISB-MOCK",
        idempotency_key: body.idempotency_key || `disburse_${subjectId}`,
        dispatched_at: new Date().toISOString(),
      });
    }
  ),

  // Intercept all HTTP methods matching API gateway host or API paths
  http.all("*", async ({ request }) => {
    const url = new URL(request.url);

    // Only intercept API / service calls targeting gateway or non-asset paths
    const isApiCall = isGatewayApiCall(url);

    if (!isApiCall) {
      return passthrough();
    }

    const validation = await validateGatewayRequest(request);

    if (!validation.valid) {
      return HttpResponse.json(validation.body, {
        status: validation.status,
        headers: { "Content-Type": "application/json" },
      });
    }

    // Default passthrough for valid gateway requests in live dev/mock mode
    return passthrough();
  }),
];
