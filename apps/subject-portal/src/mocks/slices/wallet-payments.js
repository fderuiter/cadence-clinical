import {
  DEFAULT_DEMO_PARTICIPANT,
  DEFAULT_DEMO_SITE,
  DEMO_STUDY,
} from "demo-fixtures";
import {
  createDemoParticipantExpenseOcrRoute,
  createDemoParticipantExpenseSubmitRoute,
} from "demo-fixtures/routes";
import { demoNowIso, demoNowMs } from "../../utils/demoMode.js";

const DEFAULT_MOCK_SUBJECT_ID =
  DEFAULT_DEMO_PARTICIPANT?.subjectCode || "SUBJ-101-001";
const DEFAULT_MOCK_STUDY_ID = DEMO_STUDY?.code || "CADENCE-101";
const DEFAULT_MOCK_SITE_ID = DEFAULT_DEMO_SITE?.code || "SITE-101";

export function createWalletPaymentsSliceStore() {
  const state = {
    walletBalance: 125.0,
    walletLedger: new Map(),
    ocrTokens: new Map(),
    consumedOcrTokens: new Set(),
    recordedReceiptDigests: new Set(),
    walletActionCounter: 0,
  };

  function nextTimestampMs() {
    state.walletActionCounter += 1;
    return demoNowMs(state.walletActionCounter);
  }

  function nextTimestampIso() {
    state.walletActionCounter += 1;
    return demoNowIso(state.walletActionCounter);
  }

  function getOrCreateWallet(subjectId = DEFAULT_MOCK_SUBJECT_ID) {
    if (!state.walletLedger.has(subjectId)) {
      state.walletLedger.set(subjectId, {
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
    return state.walletLedger.get(subjectId);
  }

  function reset() {
    state.walletBalance = 125.0;
    state.walletLedger.clear();
    state.ocrTokens.clear();
    state.consumedOcrTokens.clear();
    state.recordedReceiptDigests.clear();
    state.walletActionCounter = 0;
  }

  reset();

  return {
    state,
    nextTimestampMs,
    nextTimestampIso,
    getOrCreateWallet,
    reset,
  };
}

export function createWalletPaymentsSliceHandlers({
  http,
  HttpResponse,
  slice,
}) {
  return [
    http.post("*/api/v1/ai/biometrics/verify-liveness", async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const subject = body.subject_pseudonym || DEFAULT_MOCK_SUBJECT_ID;
      const isFallback = Boolean(body.fallback_mode);
      const frames = body.video_frames || [];
      const isLive = isFallback || frames.length > 0;
      const score = isFallback ? 0.92 : isLive ? 0.98 : 0.15;
      const nowMs = slice.nextTimestampMs();
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
      const wallet = slice.getOrCreateWallet(subjectId);
      return HttpResponse.json({
        wallet,
        recentTransactions: wallet.recentTransactions,
        expenseClaims: wallet.expenseClaims,
      });
    }),

    createDemoParticipantExpenseOcrRoute(
      { http, HttpResponse },
      {
        path: "*/api/v1/payments/participant/me/expenses/ocr-extract",
        tokenStore: slice.state,
        nextTimestampMs: () => slice.nextTimestampMs(),
      }
    ),
    createDemoParticipantExpenseOcrRoute(
      { http, HttpResponse },
      {
        tokenStore: slice.state,
        nextTimestampMs: () => slice.nextTimestampMs(),
      }
    ),

    createDemoParticipantExpenseSubmitRoute(
      { http, HttpResponse },
      {
        path: "*/api/v1/payments/participant/me/expenses",
        defaultSubjectId: DEFAULT_MOCK_SUBJECT_ID,
        tokenStore: slice.state,
        nextTimestampMs: () => slice.nextTimestampMs(),
        nextTimestampIso: () => slice.nextTimestampIso(),
        resolveWallet: (body) =>
          slice.getOrCreateWallet(body?.subjectId || DEFAULT_MOCK_SUBJECT_ID),
        getIdempotencyHit: ({ body, request, wallet }) => {
          const idempotencyKey = (
            request.headers.get("Idempotency-Key") ||
            body?.idempotencyKey ||
            body?.idempotency_key ||
            ""
          ).trim();
          return idempotencyKey &&
            wallet.expenseIdempotencyMap.has(idempotencyKey)
            ? wallet.expenseIdempotencyMap.get(idempotencyKey)
            : null;
        },
        onEvaluated: (evaluated, { body, request, wallet }) => {
          if (evaluated.autoApproved) {
            wallet.lifetimeReimbursements += evaluated.netAmount;
            wallet.lifetimeDisbursements += evaluated.netAmount;
          }
          wallet.expenseClaims.unshift(evaluated);
          const idempotencyKey = (
            request.headers.get("Idempotency-Key") ||
            body?.idempotencyKey ||
            body?.idempotency_key ||
            ""
          ).trim();
          if (idempotencyKey) {
            wallet.expenseIdempotencyMap.set(idempotencyKey, evaluated);
          }
        },
      }
    ),

    createDemoParticipantExpenseSubmitRoute(
      { http, HttpResponse },
      {
        defaultSubjectId: DEFAULT_MOCK_SUBJECT_ID,
        tokenStore: slice.state,
        nextTimestampMs: () => slice.nextTimestampMs(),
        nextTimestampIso: () => slice.nextTimestampIso(),
      }
    ),

    http.post(
      "*/api/v1/payments/participant/me/disburse",
      async ({ request }) => {
        const body = await request.json().catch(() => ({}));
        const wallet = slice.getOrCreateWallet(
          body.subjectId || DEFAULT_MOCK_SUBJECT_ID
        );
        const idempotencyKey = (
          request.headers.get("Idempotency-Key") ||
          body.idempotencyKey ||
          body.idempotency_key ||
          ""
        ).trim();

        if (
          idempotencyKey &&
          wallet.disburseIdempotencyMap.has(idempotencyKey)
        ) {
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
        slice.state.walletBalance = wallet.currentBalance;

        const nowMs = slice.nextTimestampMs();
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
          createdAt: slice.nextTimestampIso(),
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
        const wallet = slice.getOrCreateWallet(
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
        slice.state.walletBalance = wallet.currentBalance;
        return HttpResponse.json({
          success: true,
          status: "CREDITED",
          creditedAmount: serverRuleAmount,
          currentBalance: wallet.currentBalance,
          lifetimeStipends: wallet.lifetimeStipends,
          walletId: wallet.id,
          subjectId: wallet.subjectId,
          currency: "USD",
          transactionId: `tx_${slice.nextTimestampMs()}`,
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
        id: `disb_${slice.nextTimestampMs()}`,
        wallet_id: `wallet_${subjectId}`,
        subject_id: subjectId,
        amount,
        new_balance: amount * seq,
        currency: "USD",
        status: "COMPLETED",
        reference_number: `DISB-MOCK-${seq}`,
        idempotency_key: body.idempotency_key || `seq_${seq}_${subjectId}`,
        dispatched_at: slice.nextTimestampIso(),
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
          id: `disb_${slice.nextTimestampMs()}`,
          wallet_id: `wallet_${subjectId}`,
          subject_id: subjectId,
          amount,
          new_balance: amount,
          currency: "USD",
          status: "COMPLETED",
          reference_number: "DISB-MOCK",
          idempotency_key: body.idempotency_key || `disburse_${subjectId}`,
          dispatched_at: slice.nextTimestampIso(),
        });
      }
    ),
  ];
}
