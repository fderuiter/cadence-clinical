async function safeReadJson(request) {
  try {
    return await request.json();
  } catch {
    return {};
  }
}

/**
 * Decodes base64-encoded SVG/PNG/UTF-8 receipt content without filename sniffing.
 * Returns "" when bytes are unreadable or binary non-text without PNG tEXt chunks.
 */
export function decodeReceiptBase64ToText(imageBase64) {
  if (!imageBase64 || typeof imageBase64 !== "string") return "";
  try {
    const cleaned = imageBase64.includes(",")
      ? imageBase64.split(",")[1]
      : imageBase64;
    const bin = atob(cleaned.replace(/\s+/g, ""));
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i += 1) {
      bytes[i] = bin.charCodeAt(i);
    }

    if (
      bytes.length >= 8 &&
      bytes[0] === 0x89 &&
      bytes[1] === 0x50 &&
      bytes[2] === 0x4e &&
      bytes[3] === 0x47
    ) {
      const chunks = [];
      let pos = 8;
      const decoder = new TextDecoder("utf-8", { fatal: false });
      while (pos + 8 <= bytes.length) {
        const len =
          ((bytes[pos] << 24) >>> 0) |
          (bytes[pos + 1] << 16) |
          (bytes[pos + 2] << 8) |
          bytes[pos + 3];
        const type = String.fromCharCode(
          bytes[pos + 4],
          bytes[pos + 5],
          bytes[pos + 6],
          bytes[pos + 7]
        );
        pos += 8;
        if (pos + len + 4 > bytes.length) break;
        if (type === "tEXt") {
          const slice = bytes.subarray(pos, pos + len);
          const nul = slice.indexOf(0);
          if (nul >= 0 && nul + 1 < slice.length) {
            chunks.push(decoder.decode(slice.subarray(nul + 1)));
          }
        }
        pos += len + 4;
      }
      return chunks.join("\n").trim();
    }

    const strictDecoder = new TextDecoder("utf-8", { fatal: true });
    const decoded = strictDecoder.decode(bytes);
    let printable = 0;
    for (let i = 0; i < decoded.length; i += 1) {
      const code = decoded.charCodeAt(i);
      if (
        code === 9 ||
        code === 10 ||
        code === 13 ||
        (code >= 32 && code !== 127)
      ) {
        printable += 1;
      }
    }
    if (decoded.length === 0 || printable / decoded.length < 0.9) {
      return "";
    }
    if (decoded.includes("<svg") || decoded.includes("<text")) {
      return decoded
        .replace(/<[^>]+>/g, "\n")
        .replace(/\n+/g, "\n")
        .trim();
    }
    return decoded.trim();
  } catch {
    return "";
  }
}

/**
 * Parses receipt text or base64 payload into dual camelCase + snake_case OCR extraction fields.
 */
export function parseDemoReceiptContent(body = {}, options = {}) {
  const {
    fallbackOnPlainText = false,
    emptyWarning = "Unable to extract readable receipt text from the uploaded image. Please verify image clarity or enter details manually.",
  } = options;

  const explicitText =
    typeof body?.rawText === "string" && body.rawText.trim()
      ? body.rawText.trim()
      : typeof body?.raw_text === "string" && body.raw_text.trim()
        ? body.raw_text.trim()
        : "";
  const rawInput =
    explicitText ||
    decodeReceiptBase64ToText(body?.imageBase64 || body?.image_base64 || "");

  if (!rawInput || rawInput.replace(/[^a-zA-Z0-9]/g, "").length < 3) {
    const emptyCategory = fallbackOnPlainText ? "OTHER" : "";
    return {
      ocrReadable: false,
      ocr_readable: false,
      merchantName: "",
      merchant_name: "",
      expenseDate: "",
      expense_date: "",
      grossAmount: 0,
      gross_amount: 0,
      taxDeduction: 0,
      tax_deduction: 0,
      netAmount: 0,
      net_amount: 0,
      category: emptyCategory,
      confidenceScore: 0,
      confidence_score: 0,
      rawText: "",
      raw_text: "",
      warning: emptyWarning,
    };
  }

  const lines = rawInput
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);

  const dateMatch = rawInput.match(/\b(20\d{2}-\d{2}-\d{2})\b/);
  const expenseDate = dateMatch ? dateMatch[1] : "";

  const amountMatches = [...rawInput.matchAll(/\$?\s*(\d{1,5}\.\d{2})\b/g)];
  const grossAmount =
    amountMatches.length > 0
      ? Number(amountMatches[amountMatches.length - 1][1])
      : 0;

  let merchantName = "";
  for (const line of lines) {
    if (
      /^(date|total|subtotal|amount|fare|meal charge|fuel total|tax|receipt|route)\b/i.test(
        line
      )
    ) {
      continue;
    }
    if (/^\$?\d+\.\d{2}$/.test(line)) continue;
    if ((line.match(/[a-zA-Z]/g) || []).length >= 2) {
      merchantName = line.replace(/^merchant:\s*/i, "").trim();
      break;
    }
  }

  const lower = `${merchantName}\n${rawInput}`.toLowerCase();
  let category = body?.categoryHint || body?.category_hint || "OTHER";
  if (/mileage|odometer/.test(lower)) category = "MILEAGE";
  else if (
    /shell|chevron|exxon|mobil|fuel|gasoline|unleaded|gallon|pump/.test(lower)
  ) {
    category = "FUEL";
  } else if (/parking|garage|valet/.test(lower)) {
    category = "PARKING";
  } else if (
    /bistro|cafe|coffee|restaurant|diner|grill|meal|lunch|dinner/.test(lower)
  ) {
    category = "MEAL";
  } else if (/hotel|marriott|hilton|inn|lodging/.test(lower)) {
    category = "LODGING";
  } else if (
    /transit|metro|subway|rail|train|bus|fare|uber|lyft|cab|taxi/.test(lower)
  ) {
    category = "TRANSIT";
  }

  const hasMerchant = Boolean(merchantName);
  const hasAmount = grossAmount > 0;
  const hasDate = Boolean(expenseDate);

  if (fallbackOnPlainText && explicitText && (!hasMerchant || !hasAmount)) {
    const fallbackMerchant = merchantName || "Demo Transit Authority";
    const fallbackDate = expenseDate || "2026-08-01"; // deid-ignore
    const fallbackAmount = hasAmount ? grossAmount : 24.5;
    const fallbackCategory = category === "OTHER" ? "TRANSIT" : category;
    return {
      ocrReadable: true,
      ocr_readable: true,
      merchantName: fallbackMerchant,
      merchant_name: fallbackMerchant,
      expenseDate: fallbackDate,
      expense_date: fallbackDate,
      grossAmount: fallbackAmount,
      gross_amount: fallbackAmount,
      taxDeduction: 0,
      tax_deduction: 0,
      netAmount: fallbackAmount,
      net_amount: fallbackAmount,
      category: fallbackCategory,
      confidenceScore: 0.95,
      confidence_score: 0.95,
      rawText: rawInput,
      raw_text: rawInput,
      warning: null,
    };
  }

  const ocrReadable = hasMerchant && hasAmount;
  if (!hasMerchant && !hasAmount) {
    return {
      ocrReadable: false,
      ocr_readable: false,
      merchantName: "",
      merchant_name: "",
      expenseDate,
      expense_date: expenseDate,
      grossAmount: 0,
      gross_amount: 0,
      taxDeduction: 0,
      tax_deduction: 0,
      netAmount: 0,
      net_amount: 0,
      category,
      confidenceScore: 0,
      confidence_score: 0,
      rawText: rawInput,
      raw_text: rawInput,
      warning:
        "No recognizable merchant or payment amount was found on the uploaded receipt.",
    };
  }

  let completeness = 0;
  if (hasMerchant) completeness += 0.36;
  if (hasAmount) completeness += 0.46;
  if (hasDate) completeness += 0.16;
  const confidenceScore = ocrReadable
    ? Number(Math.min(0.98, completeness).toFixed(2))
    : Number(Math.min(0.45, completeness).toFixed(2));

  return {
    ocrReadable,
    ocr_readable: ocrReadable,
    merchantName,
    merchant_name: merchantName,
    expenseDate,
    expense_date: expenseDate,
    grossAmount,
    gross_amount: grossAmount,
    taxDeduction: 0,
    tax_deduction: 0,
    netAmount: grossAmount,
    net_amount: grossAmount,
    category,
    confidenceScore,
    confidence_score: confidenceScore,
    rawText: rawInput,
    raw_text: rawInput,
    warning:
      confidenceScore < 0.9
        ? "Receipt scan confidence is below 90%; please review extracted fields before submitting."
        : null,
  };
}

/**
 * Evaluates a participant expense submission against the <=$100 auto-approval cap,
 * meal per-diem cap, OCR confidence >=0.90 rule, and optional W-9 $600 threshold guard.
 */
export function evaluateDemoExpenseSubmission(body = {}, options = {}) {
  const {
    wallet = null,
    nextTimestampMs = () => Date.now(),
    nextTimestampIso = () => new Date().toISOString(),
    idPrefix = "claim_",
    defaultSubjectId = "SUBJ-101-001",
    defaultMerchantName = "",
    defaultExpenseDate = "",
  } = options;

  const grossAmount = Number(body?.grossAmount ?? body?.gross_amount ?? 0);
  const confidence = Number(body?.ocrConfidence ?? body?.ocr_confidence ?? 0);
  const category = String(body?.category || "TRANSIT").toUpperCase();
  const merchantName =
    String(body?.merchantName ?? body?.merchant_name ?? "").trim() ||
    defaultMerchantName;
  const expenseDate =
    String(body?.expenseDate ?? body?.expense_date ?? "").trim() ||
    defaultExpenseDate;
  const subjectId = body?.subject_id || body?.subjectId || defaultSubjectId;
  const receiptImageName =
    body?.receipt_image_name || body?.receiptImageName || "receipt.png";
  const isMeal = Boolean(
    body?.isMealAllowance ?? body?.is_meal_allowance ?? category === "MEAL"
  );
  const perDiemCap = Number(body?.perDiemCap ?? body?.per_diem_cap ?? 75.0);
  const isW9Blocked = Boolean(
    wallet &&
    (wallet.is1099ThresholdExceeded || Number(wallet.taxableYtd || 0) >= 600) &&
    String(wallet.w9Status || "").toUpperCase() !== "VERIFIED"
  );

  const isAmountUnderCap = grossAmount > 0 && grossAmount <= 100.0;
  const isConfidenceHigh = confidence >= 0.9;
  const isMealCompliant = !isMeal || grossAmount <= perDiemCap;

  if (isAmountUnderCap && isConfidenceHigh && isMealCompliant && !isW9Blocked) {
    const timestamp = nextTimestampMs();
    const disbRef = `DISB-ACH-${timestamp}`;
    const createdIso = nextTimestampIso();
    const id = `${idPrefix}${timestamp}`;
    return {
      id,
      claim_id: id,
      subject_id: subjectId,
      category,
      merchantName,
      merchant_name: merchantName,
      expenseDate,
      expense_date: expenseDate,
      status: "APPROVED",
      approvalDecision: "AUTO_APPROVED",
      autoApproved: true,
      auto_approved: true,
      disbursementReference: disbRef,
      disbursement_reference: disbRef,
      grossAmount,
      gross_amount: grossAmount,
      tax_deduction: 0,
      netAmount: grossAmount,
      net_amount: grossAmount,
      receipt_image_name: receiptImageName,
      disbursement_method: "DIRECT_ACH",
      dispatched_at: createdIso,
      createdAt: createdIso,
    };
  }

  const reasons = [];
  if (!isAmountUnderCap) {
    reasons.push(
      `Gross amount $${grossAmount.toFixed(2)} exceeds $100.00 auto-approval threshold.`
    );
  }
  if (!isMealCompliant) {
    reasons.push(
      `Meal expense $${grossAmount.toFixed(2)} exceeds per-diem cap ($${perDiemCap.toFixed(2)}).`
    );
  }
  if (isW9Blocked) {
    reasons.push(
      "IRS $600.00 annual taxable threshold reached without verified Form W-9 certification."
    );
  }
  if (!isConfidenceHigh) {
    reasons.push(
      `OCR confidence score ${confidence.toFixed(2)} is below 0.90 threshold.`
    );
  }
  const reviewNotes = `${reasons.join(" ")} Routed to site coordinator review queue.`;
  const id = `${idPrefix}${nextTimestampMs()}`;
  return {
    id,
    claim_id: id,
    subject_id: subjectId,
    category,
    merchantName,
    merchant_name: merchantName,
    expenseDate,
    expense_date: expenseDate,
    status: "PENDING_REVIEW",
    approvalDecision: "PENDING_REVIEW",
    autoApproved: false,
    auto_approved: false,
    disbursementReference: null,
    disbursement_reference: null,
    reviewNotes,
    review_notes: reviewNotes,
    grossAmount,
    gross_amount: grossAmount,
    tax_deduction: 0,
    netAmount: grossAmount,
    net_amount: grossAmount,
    receipt_image_name: receiptImageName,
    createdAt: nextTimestampIso(),
  };
}

/**
 * Shared `GET *\/health` MSW route factory.
 */
export function createDemoHealthRoute(
  { http, HttpResponse },
  payload = { status: "healthy", mode: "demo" }
) {
  return http.get("*/health", () =>
    HttpResponse.json(typeof payload === "function" ? payload() : payload)
  );
}

/**
 * Shared `POST *\/api/v1/auth/signature-verification` MSW route factory.
 */
export function createDemoSignatureVerificationRoute(
  { http, HttpResponse },
  options = {}
) {
  const {
    validatePayload = null,
    createToken = () => `demo_sig_tok_${Date.now()}`,
    defaultAction = "portal.sign",
  } = options;

  return http.post(
    "*/api/v1/auth/signature-verification",
    async ({ request }) => {
      const body = await safeReadJson(request);
      if (typeof validatePayload === "function" && !validatePayload(body)) {
        return HttpResponse.json(
          {
            title: "Invalid Step-Up Signature Payload",
            status: 400,
            code: "INVALID_STEP_UP_PAYLOAD",
            detail:
              "Both '/api/v1/...' action and governed snake_case semantic_action are required.",
          },
          { status: 400 }
        );
      }

      const payload = {
        sig_token: createToken(body),
        action: body?.action || defaultAction,
        expires_in: 300,
      };
      if (body?.semantic_action) {
        payload.semantic_action = body.semantic_action;
      }
      return HttpResponse.json(payload);
    }
  );
}

/**
 * Shared `POST *\/api/v1/execution/amendments/reconsent` MSW route factory.
 */
export function createDemoAmendmentReconsentRoute(
  { http, HttpResponse },
  options = {}
) {
  const { buildResponse = () => ({ status: "COMPLETED" }) } = options;
  return http.post(
    "*/api/v1/execution/amendments/reconsent",
    async ({ request }) => {
      const body = await safeReadJson(request);
      return HttpResponse.json(await buildResponse(body, request));
    }
  );
}

/**
 * Shared `POST *\/api/v1/payments/participant/expenses/ocr-extract` MSW route factory.
 */
export function createDemoParticipantExpenseOcrRoute(
  { http, HttpResponse },
  options = {}
) {
  const {
    path = "*/api/v1/payments/participant/expenses/ocr-extract",
    fallbackOnPlainText = false,
    emptyWarning,
  } = options;

  return http.post(path, async ({ request }) => {
    const body = await safeReadJson(request);
    return HttpResponse.json(
      parseDemoReceiptContent(body, { fallbackOnPlainText, emptyWarning })
    );
  });
}

/**
 * Shared `POST *\/api/v1/payments/participant/expenses` MSW route factory.
 */
export function createDemoParticipantExpenseSubmitRoute(
  { http, HttpResponse },
  options = {}
) {
  const {
    path = "*/api/v1/payments/participant/expenses",
    status = 200,
    resolveWallet = null,
    getIdempotencyHit = null,
    onEvaluated = null,
    nextTimestampMs,
    nextTimestampIso,
    idPrefix = "claim_",
    defaultSubjectId = "SUBJ-101-001",
    defaultMerchantName = "",
    defaultExpenseDate = "",
  } = options;

  return http.post(path, async ({ request }) => {
    const body = await safeReadJson(request);
    const wallet =
      typeof resolveWallet === "function" ? resolveWallet(body, request) : null;
    if (typeof getIdempotencyHit === "function") {
      const cached = getIdempotencyHit({ body, request, wallet });
      if (cached) {
        return HttpResponse.json(cached, { status });
      }
    }

    const evaluated = evaluateDemoExpenseSubmission(body, {
      wallet,
      nextTimestampMs,
      nextTimestampIso,
      idPrefix,
      defaultSubjectId,
      defaultMerchantName,
      defaultExpenseDate,
    });

    if (typeof onEvaluated === "function") {
      onEvaluated(evaluated, { body, request, wallet });
    }

    return HttpResponse.json(evaluated, { status });
  });
}
