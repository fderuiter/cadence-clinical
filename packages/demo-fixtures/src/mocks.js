/**
 * Shared MSW-free demo payload builders for the five routes duplicated across
 * `apps/web` and `apps/subject-portal`:
 *   1. `GET /health`
 *   2. `POST /api/v1/auth/signature-verification`
 *   3. `POST /api/v1/execution/amendments/reconsent`
 *   4. `POST /api/v1/payments/participant/(me/)?expenses/ocr-extract`
 *   5. `POST /api/v1/payments/participant/(me/)?expenses`
 *
 * Intentionally imports neither `msw` nor `./index.js` so `packages/demo-fixtures`
 * stays free of runtime test-harness packages and non-demo production builds
 * can stub `demo-fixtures/mocks` cleanly via `scripts/vite-strip-demo-fixtures.mjs`.
 */

export function buildDemoHealthPayload({
  surface = "mode",
  value = "demo",
} = {}) {
  return {
    status: "healthy",
    [surface]: value,
  };
}

export function buildSignatureVerificationOutcome(
  body = {},
  {
    validateAction = null,
    validateSemanticAction = null,
    requireSemanticAction = false,
    tokenPrefix = "demo_sig_tok_",
    defaultAction = "portal.sign",
    timestampMs = Date.now(),
  } = {}
) {
  const actionValid =
    typeof validateAction === "function" ? validateAction(body?.action) : true;
  const semanticValid = requireSemanticAction
    ? typeof validateSemanticAction === "function"
      ? validateSemanticAction(body?.semantic_action)
      : Boolean(body?.semantic_action)
    : true;

  if (!actionValid || !semanticValid) {
    return {
      status: 400,
      body: {
        title: "Invalid Step-Up Signature Payload",
        status: 400,
        code: "INVALID_STEP_UP_PAYLOAD",
        detail:
          "Both '/api/v1/...' action and governed snake_case semantic_action are required.",
      },
    };
  }

  const payload = {
    sig_token: `${tokenPrefix}${timestampMs}`,
    action: body?.action || defaultAction,
    expires_in: 300,
  };
  if (body?.semantic_action !== undefined) {
    payload.semantic_action = body.semantic_action;
  }

  return {
    status: 200,
    body: payload,
  };
}

export function buildAmendmentReconsentPayload({
  status = "COMPLETED",
  recordedAt,
} = {}) {
  if (recordedAt !== undefined) {
    return {
      status,
      recorded_at: recordedAt,
    };
  }
  return { status };
}

export function computeMockReceiptDigest(rawBytesText) {
  if (!rawBytesText || typeof rawBytesText !== "string") return null;
  let h1 = 0x811c9dc5;
  let h2 = 0x01000193;
  for (let i = 0; i < rawBytesText.length; i++) {
    const ch = rawBytesText.charCodeAt(i);
    h1 = Math.imul(h1 ^ ch, 0x01000193) >>> 0;
    h2 = Math.imul(h2 ^ (ch + i), 0x85ebca6b) >>> 0;
  }
  return `${h1.toString(16).padStart(8, "0")}${h2.toString(16).padStart(8, "0")}${rawBytesText.length.toString(16).padStart(8, "0")}`;
}

export function decodeReceiptImageText(imageBase64) {
  if (!imageBase64 || typeof imageBase64 !== "string") return "";
  try {
    const cleaned = imageBase64.includes(",")
      ? imageBase64.split(",")[1]
      : imageBase64;
    const bin = atob(cleaned.replace(/\s+/g, ""));
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) {
      bytes[i] = bin.charCodeAt(i);
    }

    // PNG signature -> only extract embedded tEXt chunks if present; pure blank/corrupted bytes yield ""
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
    for (let i = 0; i < decoded.length; i++) {
      const code = decoded.charCodeAt(i);
      if (
        code === 9 ||
        code === 10 ||
        code === 13 ||
        (code >= 32 && code !== 127)
      ) {
        printable++;
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

export function extractDemoReceiptOcr(
  body = {},
  {
    issueToken = true,
    allowTextOnlyToken = false,
    fallbackExtracted = null,
    ocrTokens = null,
    tokenPrefix = "mock_ocr_token_",
    nextTimestampMs = () => Date.now(),
  } = {}
) {
  const imagePayload = body?.imageBase64 || body?.image_base64 || "";
  const receiptSha256 = imagePayload
    ? computeMockReceiptDigest(imagePayload)
    : null;
  const rawInput =
    typeof body?.rawText === "string" && body.rawText.trim()
      ? body.rawText.trim()
      : typeof body?.raw_text === "string" && body.raw_text.trim()
        ? body.raw_text.trim()
        : decodeReceiptImageText(imagePayload);

  if (!rawInput || rawInput.replace(/[^a-zA-Z0-9]/g, "").length < 3) {
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
      category: "",
      confidenceScore: 0,
      confidence_score: 0,
      rawText: "",
      raw_text: "",
      ocrToken: null,
      ocr_token: null,
      receiptSha256: null,
      receipt_sha256: null,
      warning:
        "Unable to extract readable receipt text from the uploaded image. Please verify image clarity or enter details manually.",
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
  )
    category = "FUEL";
  else if (/parking|garage|valet/.test(lower)) category = "PARKING";
  else if (
    /bistro|cafe|coffee|restaurant|diner|grill|meal|lunch|dinner/.test(lower)
  )
    category = "MEAL";
  else if (/hotel|marriott|hilton|inn|lodging/.test(lower))
    category = "LODGING";
  else if (
    /transit|metro|subway|rail|train|bus|fare|uber|lyft|cab|taxi/.test(lower)
  )
    category = "TRANSIT";

  const hasMerchant = Boolean(merchantName);
  const hasAmount = grossAmount > 0;
  const hasDate = Boolean(expenseDate);
  const ocrReadable = hasMerchant && hasAmount;

  if (!ocrReadable && fallbackExtracted && !imagePayload) {
    const fbGross = Number(fallbackExtracted.grossAmount || 0);
    const fbCat = String(fallbackExtracted.category || category || "TRANSIT");
    const fbConf = Number(fallbackExtracted.confidenceScore ?? 0.95);
    const fbTax =
      fbCat === "MEAL" && fbGross > 75.0
        ? Number(((fbGross - 75.0) * 0.15).toFixed(2))
        : 0;
    const fbNet = Number((fbGross - fbTax).toFixed(2));
    const fbToken =
      issueToken && (receiptSha256 || allowTextOnlyToken)
        ? `${tokenPrefix}${nextTimestampMs()}`
        : null;
    if (fbToken) {
      ocrTokens?.set(fbToken, {
        ocrReadable: true,
        merchantName: fallbackExtracted.merchantName,
        expenseDate: fallbackExtracted.expenseDate,
        grossAmount: fbGross,
        category: fbCat,
        confidenceScore: fbConf,
        receiptSha256,
      });
    }
    return {
      ocrReadable: true,
      ocr_readable: true,
      merchantName: fallbackExtracted.merchantName,
      merchant_name: fallbackExtracted.merchantName,
      expenseDate: fallbackExtracted.expenseDate,
      expense_date: fallbackExtracted.expenseDate,
      grossAmount: fbGross,
      gross_amount: fbGross,
      taxDeduction: fbTax,
      tax_deduction: fbTax,
      netAmount: fbNet,
      net_amount: fbNet,
      category: fbCat,
      confidenceScore: fbConf,
      confidence_score: fbConf,
      rawText: rawInput,
      raw_text: rawInput,
      ocrToken: fbToken,
      ocr_token: fbToken,
      receiptSha256,
      receipt_sha256: receiptSha256,
      warning: null,
    };
  }

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
  if (issueToken && (receiptSha256 || allowTextOnlyToken)) {
    ocrToken = `${tokenPrefix}${nextTimestampMs()}`;
    ocrTokens?.set(ocrToken, {
      ocrReadable,
      merchantName,
      expenseDate,
      grossAmount,
      category,
      confidenceScore,
      receiptSha256,
    });
  }

  const camelFields = {
    ocrReadable,
    merchantName,
    expenseDate,
    grossAmount,
    taxDeduction,
    netAmount,
    confidenceScore,
    rawText: rawInput,
    ocrToken,
    receiptSha256,
  };
  const snakeAliases = Object.fromEntries(
    Object.entries(camelFields).map(([key, value]) => [
      key.replace(/([a-z0-9])([A-Z])/g, "$1_$2").toLowerCase(),
      value,
    ])
  );

  return {
    ...camelFields,
    ...snakeAliases,
    category,
    warning:
      confidenceScore < 0.9
        ? "We could not read every detail on the receipt photo clearly. Please check the merchant, date, and amount before submitting."
        : null,
  };
}

export function evaluateDemoExpenseClaim(
  body = {},
  {
    wallet = null,
    ocrTokens = null,
    consumedOcrTokens = null,
    recordedReceiptDigests = null,
    claimPrefix = "claim_",
    defaultSubjectId = "SUBJ-101-001",
    defaultMerchantName = "",
    defaultExpenseDate = "",
    nextTimestampMs = () => Date.now(),
    nextTimestampIso = () => new Date().toISOString(),
  } = {}
) {
  const grossAmount = Number(body?.grossAmount ?? body?.gross_amount ?? 0);
  const category = String(body?.category || "TRANSIT").toUpperCase();
  const merchantName =
    String(body?.merchantName ?? body?.merchant_name ?? "").trim() ||
    defaultMerchantName;
  const expenseDate =
    String(body?.expenseDate ?? body?.expense_date ?? "").trim() ||
    defaultExpenseDate;
  const subjectId = String(
    body?.subjectId ?? body?.subject_id ?? wallet?.subjectId ?? defaultSubjectId
  ).trim();
  const receiptImageName = String(
    body?.receiptImageName ?? body?.receipt_image_name ?? "receipt.png"
  );
  const isMeal = Boolean(
    body?.isMealAllowance ?? body?.is_meal_allowance ?? category === "MEAL"
  );
  const perDiemCap = 75.0;
  const taxDeduction =
    isMeal && grossAmount > perDiemCap
      ? Number(((grossAmount - perDiemCap) * 0.15).toFixed(2))
      : 0;
  const netAmount = Number((grossAmount - taxDeduction).toFixed(2));
  const w9Status = String(
    wallet?.w9Status ?? wallet?.w9_status ?? ""
  ).toUpperCase();
  const taxableYtd = Number(wallet?.taxableYtd ?? wallet?.taxable_ytd ?? 0);
  const isW9Blocked = Boolean(
    wallet &&
    (wallet.is1099ThresholdExceeded || taxableYtd >= 600) &&
    w9Status !== "VERIFIED"
  );

  const candidateToken =
    body?.ocrToken ??
    body?.ocr_token ??
    body?.receiptOcrData?.ocrToken ??
    body?.receiptOcrData?.ocr_token ??
    body?.receipt_ocr_data?.ocrToken ??
    body?.receipt_ocr_data?.ocr_token ??
    null;
  const receiptImageBase64 =
    body?.receiptImageBase64 ?? body?.receipt_image_base64 ?? "";

  let confidence = 0;
  const holdReasons = [];
  let scanned = null;
  let receiptSha256 = null;

  if (candidateToken) {
    if (consumedOcrTokens?.has(candidateToken)) {
      holdReasons.push("Receipt OCR token was already consumed.");
    } else if (ocrTokens && !ocrTokens.has(candidateToken)) {
      holdReasons.push("Receipt OCR token could not be verified.");
    } else if (ocrTokens) {
      scanned = ocrTokens.get(candidateToken);
      consumedOcrTokens?.add(candidateToken);
      receiptSha256 = scanned?.receiptSha256 || null;
    }
  }

  if (receiptImageBase64) {
    const parsedFromBytes = extractDemoReceiptOcr(
      { imageBase64: receiptImageBase64, categoryHint: category },
      { issueToken: false }
    );
    receiptSha256 = parsedFromBytes.receiptSha256;
    scanned = parsedFromBytes;
  }

  if (receiptSha256 && recordedReceiptDigests) {
    if (recordedReceiptDigests.has(receiptSha256)) {
      holdReasons.push(
        "Duplicate receipt digest detected across expense claims."
      );
      scanned = null;
    } else {
      recordedReceiptDigests.add(receiptSha256);
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
    const claimId = `${claimPrefix}${timestamp}`;
    return {
      id: claimId,
      claim_id: claimId,
      subjectId,
      subject_id: subjectId,
      category,
      merchantName,
      merchant_name: merchantName,
      expenseDate,
      expense_date: expenseDate,
      receiptImageName,
      receipt_image_name: receiptImageName,
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
      ocr_confidence: confidence,
      disbursement_method: "DIRECT_ACH",
      dispatched_at: createdIso,
      createdAt: createdIso,
      created_at: createdIso,
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
  const claimId = `${claimPrefix}${nextTimestampMs()}`;
  const createdIso = nextTimestampIso();
  return {
    id: claimId,
    claim_id: claimId,
    subjectId,
    subject_id: subjectId,
    category,
    merchantName,
    merchant_name: merchantName,
    expenseDate,
    expense_date: expenseDate,
    receiptImageName,
    receipt_image_name: receiptImageName,
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
    ocr_confidence: confidence,
    createdAt: createdIso,
    created_at: createdIso,
  };
}
