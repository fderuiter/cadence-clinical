import {
  DEFAULT_DEMO_PARTICIPANT,
  DEFAULT_DEMO_SITE,
  DEMO_STUDY,
} from "demo-fixtures";
import { http, HttpResponse, passthrough } from "msw";
import { evaluateDemoBoundaryRequest } from "ui/testing";
import { demoNowIso, demoNowMs, isDemoBuild } from "../utils/demoMode.js";
import { createPortalDemoHandlers } from "./demoHandlers.js";

// Deterministic mock-sandbox state for explicit demo mode / Vitest MSW tests.
// Never fabricates fallback merchant/amount/confidence when receipt content is unreadable.
const mockWalletLedger = new Map();
let mockActionCounter = 0;

function nextMockTimestampMs() {
  mockActionCounter += 1;
  return demoNowMs(mockActionCounter);
}

function nextMockTimestampIso() {
  mockActionCounter += 1;
  return demoNowIso(mockActionCounter);
}

const DEFAULT_MOCK_SUBJECT_ID =
  DEFAULT_DEMO_PARTICIPANT?.subjectCode || "SUBJ-101-001";
const DEFAULT_MOCK_STUDY_ID = DEMO_STUDY?.code || "CADENCE-101";
const DEFAULT_MOCK_SITE_ID = DEFAULT_DEMO_SITE?.code || "SITE-101";

function getOrCreateMockWallet(subjectId = DEFAULT_MOCK_SUBJECT_ID) {
  if (!mockWalletLedger.has(subjectId)) {
    mockWalletLedger.set(subjectId, {
      id: `wallet_${subjectId}`,
      subjectId,
      studyId: DEFAULT_MOCK_STUDY_ID,
      siteId: DEFAULT_MOCK_SITE_ID,
      currentBalance: 125.0,
      lifetimeStipends: 125.0,
      lifetimeReimbursements: 0.0,
      lifetimeDisbursements: 0.0,
      taxableYtd: 125.0,
      is1099ThresholdExceeded: false,
      w9Status: "NOT_REQUIRED",
      currency: "USD",
      status: "ACTIVE",
      virtualCardMask: "**** **** **** 4821",
      virtualCardExpiry: "12/29",
      achRoutingNumber: "*****0021",
      achRoutingMask: "*****0021",
      achAccountMask: "ACH-***4321",
      achAccountHolder: `Participant ${subjectId}`,
      recentTransactions: [],
      expenseClaims: [],
      creditedKeys: new Set(),
      expenseIdempotencyMap: new Map(),
      disburseIdempotencyMap: new Map(),
    });
  }
  return mockWalletLedger.get(subjectId);
}

function decodeBase64ToText(imageBase64) {
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

function parseReceiptContent(body) {
  const rawInput =
    typeof body.rawText === "string" && body.rawText.trim()
      ? body.rawText.trim()
      : typeof body.raw_text === "string" && body.raw_text.trim()
        ? body.raw_text.trim()
        : decodeBase64ToText(body.imageBase64 || body.image_base64 || "");

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
      warning:
        "Unable to extract readable receipt text from the uploaded image. Please verify image clarity or enter details manually.",
    };
  }

  const lines = rawInput
    .split(/\r?\n/)
    .map((l) => l.trim())
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
  let category = body.categoryHint || body.category_hint || "OTHER";
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

function evaluateExpenseSubmission(body, wallet = null) {
  const grossAmount = Number(body.grossAmount ?? body.gross_amount ?? 0);
  const confidence = Number(body.ocrConfidence ?? body.ocr_confidence ?? 0);
  const category = String(body.category || "TRANSIT").toUpperCase();
  const merchantName = String(
    body.merchantName ?? body.merchant_name ?? ""
  ).trim();
  const expenseDate = String(
    body.expenseDate ?? body.expense_date ?? ""
  ).trim();
  const isMeal = Boolean(
    body.isMealAllowance ?? body.is_meal_allowance ?? category === "MEAL"
  );
  const perDiemCap = Number(body.perDiemCap ?? body.per_diem_cap ?? 75.0);
  const isW9Blocked = Boolean(
    wallet &&
    (wallet.is1099ThresholdExceeded || Number(wallet.taxableYtd || 0) >= 600) &&
    String(wallet.w9Status || "").toUpperCase() !== "VERIFIED"
  );

  const isAmountUnderCap = grossAmount > 0 && grossAmount <= 100.0;
  const isConfidenceHigh = confidence >= 0.9;
  const isMealCompliant = !isMeal || grossAmount <= perDiemCap;

  if (isAmountUnderCap && isConfidenceHigh && isMealCompliant && !isW9Blocked) {
    const timestamp = nextMockTimestampMs();
    const disbRef = `DISB-ACH-${timestamp}`;
    const createdIso = nextMockTimestampIso();
    return {
      id: `claim_${timestamp}`,
      claim_id: `claim_${timestamp}`,
      category,
      merchantName,
      expenseDate,
      status: "APPROVED",
      approvalDecision: "AUTO_APPROVED",
      autoApproved: true,
      auto_approved: true,
      disbursementReference: disbRef,
      disbursement_reference: disbRef,
      grossAmount,
      gross_amount: grossAmount,
      netAmount: grossAmount,
      net_amount: grossAmount,
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
  const id = `claim_${nextMockTimestampMs()}`;
  return {
    id,
    claim_id: id,
    category,
    merchantName,
    expenseDate,
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
    netAmount: grossAmount,
    net_amount: grossAmount,
    createdAt: nextMockTimestampIso(),
  };
}

const sharedPortalActionHandlers = [
  http.post("*/api/v1/ai/biometrics/verify-liveness", async ({ request }) => {
    const body = await request.json().catch(() => ({}));
    const subject = body.subject_pseudonym || DEFAULT_MOCK_SUBJECT_ID;
    const isFallback = Boolean(body.fallback_mode);
    const frames = body.video_frames || [];
    const isLive = isFallback || frames.length > 0;
    const score = isFallback ? 0.92 : isLive ? 0.98 : 0.15;
    const nowMs = nextMockTimestampMs();
    const expiresAt = new Date(nowMs + 15 * 60 * 1000).toISOString();
    const token = `mock_liveness_token_${subject}_${isFallback ? "fallback" : "camera"}_${nowMs}`;

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

  http.get("*/api/v1/payments/participant/me/wallet", ({ request }) => {
    const url = new URL(request.url);
    const subjectId =
      url.searchParams.get("subjectId") || DEFAULT_MOCK_SUBJECT_ID;
    const wallet = getOrCreateMockWallet(subjectId);
    return HttpResponse.json({
      wallet,
      recentTransactions: wallet.recentTransactions,
      expenseClaims: wallet.expenseClaims,
    });
  }),

  http.post(
    "*/api/v1/payments/participant/me/expenses/ocr-extract",
    async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      return HttpResponse.json(parseReceiptContent(body));
    }
  ),

  http.post(
    "*/api/v1/payments/participant/expenses/ocr-extract",
    async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      return HttpResponse.json(parseReceiptContent(body));
    }
  ),

  http.post(
    "*/api/v1/payments/participant/me/expenses",
    async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const wallet = getOrCreateMockWallet(
        body.subjectId || DEFAULT_MOCK_SUBJECT_ID
      );
      const idempotencyKey = (
        request.headers.get("Idempotency-Key") ||
        body.idempotencyKey ||
        body.idempotency_key ||
        ""
      ).trim();

      if (idempotencyKey && wallet.expenseIdempotencyMap.has(idempotencyKey)) {
        return HttpResponse.json(
          wallet.expenseIdempotencyMap.get(idempotencyKey)
        );
      }

      const evaluated = evaluateExpenseSubmission(body, wallet);
      if (evaluated.autoApproved) {
        wallet.lifetimeReimbursements += evaluated.netAmount;
        wallet.lifetimeDisbursements += evaluated.netAmount;
      }
      wallet.expenseClaims.unshift(evaluated);
      if (idempotencyKey) {
        wallet.expenseIdempotencyMap.set(idempotencyKey, evaluated);
      }
      return HttpResponse.json(evaluated);
    }
  ),

  http.post("*/api/v1/payments/participant/expenses", async ({ request }) => {
    const body = await request.json().catch(() => ({}));
    return HttpResponse.json(evaluateExpenseSubmission(body));
  }),

  http.post(
    "*/api/v1/payments/participant/me/disburse",
    async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const wallet = getOrCreateMockWallet(
        body.subjectId || DEFAULT_MOCK_SUBJECT_ID
      );
      const idempotencyKey = (
        request.headers.get("Idempotency-Key") ||
        body.idempotencyKey ||
        body.idempotency_key ||
        ""
      ).trim();

      if (idempotencyKey && wallet.disburseIdempotencyMap.has(idempotencyKey)) {
        return HttpResponse.json({
          ...wallet.disburseIdempotencyMap.get(idempotencyKey),
          currentBalance: wallet.currentBalance,
          lifetimeDisbursements: wallet.lifetimeDisbursements,
        });
      }

      if (
        (wallet.is1099ThresholdExceeded ||
          Number(wallet.taxableYtd || 0) >= 600) &&
        String(wallet.w9Status || "").toUpperCase() !== "VERIFIED"
      ) {
        return HttpResponse.json(
          {
            detail:
              "Annual taxable payments have reached $600.00 without a verified Form W-9. Automated payouts are halted until your Form W-9 is verified by site staff.",
          },
          { status: 409 }
        );
      }

      const requestedAmount =
        typeof body.amount === "number" && body.amount > 0
          ? Math.round(body.amount * 100) / 100
          : wallet.currentBalance;

      if (wallet.currentBalance <= 0 || requestedAmount <= 0) {
        return HttpResponse.json(
          {
            detail:
              "Available wallet balance is $0.00; there are no funds to disburse.",
          },
          { status: 409 }
        );
      }

      if (requestedAmount > wallet.currentBalance) {
        return HttpResponse.json(
          {
            detail: `Requested payout ($${requestedAmount.toFixed(2)}) exceeds available wallet balance ($${wallet.currentBalance.toFixed(2)}).`,
          },
          { status: 400 }
        );
      }

      const disbursementType =
        String(body.disbursementType || "DIRECT_ACH").toUpperCase() ===
        "VIRTUAL_CARD"
          ? "VIRTUAL_CARD"
          : "DIRECT_ACH";
      wallet.currentBalance =
        Math.round((wallet.currentBalance - requestedAmount) * 100) / 100;
      wallet.lifetimeDisbursements =
        Math.round((wallet.lifetimeDisbursements + requestedAmount) * 100) /
        100;
      const nowMs = nextMockTimestampMs();
      const prefix = disbursementType === "VIRTUAL_CARD" ? "VCARD" : "ACH";
      const refNum = `DISB-${prefix}-${nowMs}`;
      const destinationMask =
        disbursementType === "VIRTUAL_CARD"
          ? wallet.virtualCardMask
          : wallet.achAccountMask;
      const responsePayload = {
        id: `disb_${nowMs}`,
        walletId: wallet.id,
        subjectId: wallet.subjectId,
        amount: requestedAmount,
        currentBalance: wallet.currentBalance,
        lifetimeDisbursements: wallet.lifetimeDisbursements,
        currency: wallet.currency || "USD",
        disbursementType,
        destinationMask,
        referenceNumber: refNum,
        status: "DISPATCHED",
        createdAt: nextMockTimestampIso(),
      };
      if (idempotencyKey) {
        wallet.disburseIdempotencyMap.set(idempotencyKey, responsePayload);
      }
      return HttpResponse.json(responsePayload);
    }
  ),

  http.post(
    "*/api/v1/payments/participant/me/diary-stipend",
    async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      if (
        "amount" in body ||
        "grossAmount" in body ||
        "gross_amount" in body ||
        "stipendAmount" in body
      ) {
        return HttpResponse.json(
          {
            detail:
              "Client-supplied stipend amounts are prohibited; stipend credits are computed exclusively from server protocol rules.",
          },
          { status: 400 }
        );
      }
      const wallet = getOrCreateMockWallet(
        body.subjectId || DEFAULT_MOCK_SUBJECT_ID
      );
      const key =
        body.idempotencyKey ||
        `diary_${wallet.subjectId}_${body.diaryId || body.assignmentId}_${body.sequenceNumber || 1}`;
      if (wallet.creditedKeys.has(key)) {
        return HttpResponse.json({
          success: true,
          status: "ALREADY_PROCESSED",
          creditedAmount: 0,
          currentBalance: wallet.currentBalance,
          lifetimeStipends: wallet.lifetimeStipends,
          walletId: wallet.id,
          subjectId: wallet.subjectId,
          currency: "USD",
          idempotencyKey: key,
        });
      }
      wallet.creditedKeys.add(key);
      const serverRuleAmount = 25.0;
      wallet.currentBalance += serverRuleAmount;
      wallet.lifetimeStipends += serverRuleAmount;
      return HttpResponse.json({
        success: true,
        status: "CREDITED",
        creditedAmount: serverRuleAmount,
        currentBalance: wallet.currentBalance,
        lifetimeStipends: wallet.lifetimeStipends,
        walletId: wallet.id,
        subjectId: wallet.subjectId,
        currency: "USD",
        transactionId: `tx_${nextMockTimestampMs()}`,
        referenceNumber: key,
        idempotencyKey: key,
      });
    }
  ),

  http.post("*/api/v1/payments/disburse", async ({ request }) => {
    const body = await request.json().catch(() => ({}));
    const subjectId = body.subject_id || DEFAULT_MOCK_SUBJECT_ID;
    const amount = typeof body.amount === "number" ? body.amount : 25.0;
    const seq = body.sequence_number || 1;
    return HttpResponse.json({
      success: true,
      id: "disb_" + nextMockTimestampMs(),
      wallet_id: "wallet_" + subjectId,
      subject_id: subjectId,
      amount: amount,
      new_balance: amount * seq,
      currency: "USD",
      status: "COMPLETED",
      reference_number: "DISB-MOCK-" + seq,
      idempotency_key: body.idempotency_key || `seq_${seq}_${subjectId}`,
      dispatched_at: nextMockTimestampIso(),
    });
  }),

  http.post(
    "*/api/v1/payments/participant/wallets/disburse",
    async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const subjectId = body.subject_id || DEFAULT_MOCK_SUBJECT_ID;
      const amount = typeof body.amount === "number" ? body.amount : 25.0;
      return HttpResponse.json({
        success: true,
        id: "disb_" + nextMockTimestampMs(),
        wallet_id: "wallet_" + subjectId,
        subject_id: subjectId,
        amount: amount,
        new_balance: amount,
        currency: "USD",
        status: "COMPLETED",
        reference_number: "DISB-MOCK",
        idempotency_key: body.idempotency_key || `disburse_${subjectId}`,
        dispatched_at: nextMockTimestampIso(),
      });
    }
  ),
];

export function buildPortalGatewayHandlers(options = {}) {
  const demoActive =
    options.forceDemo !== undefined
      ? Boolean(options.forceDemo)
      : isDemoBuild();

  const demoHandlers = demoActive
    ? createPortalDemoHandlers(options.store)
    : [];

  const boundaryHandler = http.all("*", async ({ request }) => {
    const result = await evaluateDemoBoundaryRequest(request, {
      demoActive,
      appLabel: "portal demo",
    });

    if (result.passthrough) {
      return passthrough();
    }

    return HttpResponse.json(result.body, {
      status: result.status,
      headers: result.headers,
    });
  });

  return [...sharedPortalActionHandlers, ...demoHandlers, boundaryHandler];
}

export function resetMockWalletLedger() {
  mockWalletLedger.clear();
  mockActionCounter = 0;
}

export const gatewayHandlers = buildPortalGatewayHandlers();
