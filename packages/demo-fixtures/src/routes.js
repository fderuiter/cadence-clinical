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
 * Computes a deterministic non-cryptographic digest for receipt payload deduplication in demo sandboxes.
 */
export function computeDemoReceiptDigest(rawBytesText) {
  if (!rawBytesText || typeof rawBytesText !== "string") return null;
  let h1 = 0x811c9dc5;
  let h2 = 0x01000193;
  for (let i = 0; i < rawBytesText.length; i += 1) {
    const ch = rawBytesText.charCodeAt(i);
    h1 = Math.imul(h1 ^ ch, 0x01000193) >>> 0;
    h2 = Math.imul(h2 ^ (ch + i), 0x85ebca6b) >>> 0;
  }
  return `${h1.toString(16).padStart(8, "0")}${h2.toString(16).padStart(8, "0")}${rawBytesText.length.toString(16).padStart(8, "0")}`;
}

/**
 * Parses receipt text or base64 payload into dual camelCase + snake_case OCR extraction fields.
 */
export function parseDemoReceiptContent(body = {}, options = {}) {
  const {
    fallbackOnPlainText = false,
    emptyWarning = "Unable to extract readable receipt text from the uploaded image. Please verify image clarity or enter details manually.",
    issueToken = true,
    tokenStore = null,
    tokenPrefix = "mock_ocr_token_",
    nextTimestampMs = () => Date.now(),
  } = options;

  const imagePayload = body?.imageBase64 || body?.image_base64 || "";
  const receiptSha256 = imagePayload
    ? computeDemoReceiptDigest(imagePayload)
    : null;

  const explicitText =
    typeof body?.rawText === "string" && body.rawText.trim()
      ? body.rawText.trim()
      : typeof body?.raw_text === "string" && body.raw_text.trim()
        ? body.raw_text.trim()
        : "";
  const rawInput = imagePayload
    ? decodeReceiptBase64ToText(imagePayload)
    : explicitText;

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
    const fallbackTaxDeduction =
      fallbackCategory === "MEAL" && fallbackAmount > 75.0
        ? Number(((fallbackAmount - 75.0) * 0.15).toFixed(2))
        : 0;
    const fallbackNetAmount = Number(
      (fallbackAmount - fallbackTaxDeduction).toFixed(2)
    );
    const fallbackToken = issueToken
      ? `${tokenPrefix}${nextTimestampMs()}`
      : null;
    return {
      ocrReadable: true,
      ocr_readable: true,
      merchantName: fallbackMerchant,
      merchant_name: fallbackMerchant,
      expenseDate: fallbackDate,
      expense_date: fallbackDate,
      grossAmount: fallbackAmount,
      gross_amount: fallbackAmount,
      taxDeduction: fallbackTaxDeduction,
      tax_deduction: fallbackTaxDeduction,
      netAmount: fallbackNetAmount,
      net_amount: fallbackNetAmount,
      category: fallbackCategory,
      confidenceScore: 0.95,
      confidence_score: 0.95,
      rawText: rawInput,
      raw_text: rawInput,
      ocrToken: fallbackToken,
      ocr_token: fallbackToken,
      receiptSha256,
      receipt_sha256: receiptSha256,
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
      ocrToken: null,
      ocr_token: null,
      receiptSha256,
      receipt_sha256: receiptSha256,
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
  const taxDeduction =
    category === "MEAL" && grossAmount > 75.0
      ? Number(((grossAmount - 75.0) * 0.15).toFixed(2))
      : 0;
  const netAmount = Number((grossAmount - taxDeduction).toFixed(2));

  let ocrToken = null;
  if (
    issueToken &&
    ocrReadable &&
    ((receiptSha256 && !explicitText) || fallbackOnPlainText)
  ) {
    ocrToken = `${tokenPrefix}${nextTimestampMs()}`;
    if (receiptSha256 && tokenStore?.ocrTokens) {
      tokenStore.ocrTokens.set(ocrToken, {
        ocrReadable,
        merchantName,
        expenseDate,
        grossAmount,
        category,
        confidenceScore,
        receiptSha256,
      });
    }
  }

  const warning =
    confidenceScore < 0.9
      ? "We could not read every detail on the receipt photo clearly. Please check the merchant, date, and amount before submitting."
      : null;

  const camelFields = {
    ocrReadable,
    merchantName,
    expenseDate,
    grossAmount,
    taxDeduction,
    netAmount,
    category,
    confidenceScore,
    rawText: rawInput,
    ocrToken,
    receiptSha256,
    warning,
  };

  return {
    ...camelFields,
    ocr_readable: ocrReadable,
    merchant_name: merchantName,
    expense_date: expenseDate,
    gross_amount: grossAmount,
    tax_deduction: taxDeduction,
    net_amount: netAmount,
    confidence_score: confidenceScore,
    raw_text: rawInput,
    ocr_token: ocrToken,
    receipt_sha256: receiptSha256,
  };
}

/**
 * Evaluates a participant expense submission against the <=$100 auto-approval cap,
 * server-enforced $75 meal per-diem cap, server-verified OCR scan rule, and optional W-9 $600 threshold guard.
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
    tokenStore = null,
    requireServerScan = true,
  } = options;

  const grossAmount = Number(body?.grossAmount ?? body?.gross_amount ?? 0);
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
  const perDiemCap = 75.0;
  const taxDeduction =
    isMeal && grossAmount > perDiemCap
      ? Number(((grossAmount - perDiemCap) * 0.15).toFixed(2))
      : 0;
  const netAmount = Number((grossAmount - taxDeduction).toFixed(2));
  const isW9Blocked = Boolean(
    wallet &&
    (wallet.is1099ThresholdExceeded || Number(wallet.taxableYtd || 0) >= 600) &&
    String(wallet.w9Status || "").toUpperCase() !== "VERIFIED"
  );

  const candidateToken =
    body?.ocrToken ??
    body?.ocr_token ??
    body?.receiptOcrData?.ocrToken ??
    body?.receiptOcrData?.ocr_token ??
    null;
  const receiptImageBase64 =
    body?.receiptImageBase64 ?? body?.receipt_image_base64 ?? "";

  let confidence = 0;
  const holdReasons = [];

  if (tokenStore) {
    let scanned = null;
    let receiptSha256 = null;

    if (candidateToken) {
      if (tokenStore.consumedOcrTokens?.has(candidateToken)) {
        holdReasons.push("Receipt OCR token was already consumed.");
      } else if (!tokenStore.ocrTokens?.has(candidateToken)) {
        holdReasons.push("Receipt OCR token could not be verified.");
      } else {
        scanned = tokenStore.ocrTokens.get(candidateToken);
        tokenStore.consumedOcrTokens?.add(candidateToken);
        receiptSha256 = scanned.receiptSha256;
      }
    }

    if (receiptImageBase64) {
      const parsedFromBytes = parseDemoReceiptContent(
        { imageBase64: receiptImageBase64, categoryHint: category },
        { issueToken: false }
      );
      receiptSha256 = parsedFromBytes.receiptSha256;
      scanned = parsedFromBytes;
    }

    if (receiptSha256 && tokenStore.recordedReceiptDigests) {
      if (tokenStore.recordedReceiptDigests.has(receiptSha256)) {
        holdReasons.push(
          "Duplicate receipt digest detected across expense claims."
        );
        scanned = null;
      } else {
        tokenStore.recordedReceiptDigests.add(receiptSha256);
      }
    }

    if (scanned && scanned.ocrReadable && holdReasons.length === 0) {
      const amountMatches =
        Number(grossAmount.toFixed(2)) ===
        Number(Number(scanned.grossAmount || 0).toFixed(2));
      const merchantMatches =
        merchantName.toLowerCase() ===
        String(scanned.merchantName || "")
          .trim()
          .toLowerCase();
      const categoryMatches =
        category === String(scanned.category || "").toUpperCase();
      const dateMatches =
        !scanned.expenseDate ||
        !expenseDate ||
        expenseDate === String(scanned.expenseDate).trim();
      if (amountMatches && merchantMatches && categoryMatches && dateMatches) {
        confidence = Number(scanned.confidenceScore || 0);
      } else {
        holdReasons.push(
          "Submitted claim fields differ from server-extracted receipt scan."
        );
      }
    }
  } else if (requireServerScan) {
    confidence = candidateToken || receiptImageBase64 ? 0.95 : 0;
  } else {
    confidence = Number(body?.ocrConfidence ?? body?.ocr_confidence ?? 0);
  }

  const isAmountUnderCap = grossAmount > 0 && grossAmount <= 100.0;
  const isConfidenceHigh = confidence >= 0.9;
  const isMealCompliant = !isMeal || grossAmount <= perDiemCap;

  if (
    isAmountUnderCap &&
    isConfidenceHigh &&
    isMealCompliant &&
    !isW9Blocked &&
    holdReasons.length === 0
  ) {
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
      taxDeduction,
      tax_deduction: taxDeduction,
      netAmount,
      net_amount: netAmount,
      ocrConfidence: confidence,
      receipt_image_name: receiptImageName,
      disbursement_method: "DIRECT_ACH",
      dispatched_at: createdIso,
      createdAt: createdIso,
    };
  }

  const reasons = [...holdReasons];
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
      "We could not read every detail on the receipt photo clearly, so your study coordinator will check it for you."
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
    taxDeduction,
    tax_deduction: taxDeduction,
    netAmount,
    net_amount: netAmount,
    ocrConfidence: confidence,
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
    tokenStore = null,
    tokenPrefix = "mock_ocr_token_",
    nextTimestampMs = () => Date.now(),
  } = options;

  return http.post(path, async ({ request }) => {
    const body = await safeReadJson(request);
    return HttpResponse.json(
      parseDemoReceiptContent(body, {
        fallbackOnPlainText,
        emptyWarning,
        tokenStore,
        tokenPrefix,
        nextTimestampMs,
      })
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
    tokenStore = null,
    requireServerScan = true,
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
      tokenStore,
      requireServerScan,
    });

    if (typeof onEvaluated === "function") {
      onEvaluated(evaluated, { body, request, wallet });
    }

    return HttpResponse.json(evaluated, { status });
  });
}
