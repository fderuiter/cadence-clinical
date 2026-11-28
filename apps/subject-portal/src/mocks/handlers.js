import { DEFAULT_DEMO_PARTICIPANT } from "demo-fixtures";
import {
  evaluateDemoExpenseClaim,
  extractDemoReceiptOcr,
} from "demo-fixtures/mocks";
import { http, HttpResponse, passthrough } from "msw";
import { evaluateDemoBoundaryRequest } from "ui/testing";
import { isDemoBuild } from "../utils/demoMode.js";
import { createPortalDemoHandlers, portalDemoStore } from "./demoHandlers.js";

const DEFAULT_MOCK_SUBJECT_ID =
  DEFAULT_DEMO_PARTICIPANT?.subjectCode || "SUBJ-101-001";

function createPortalActionHandlers(store = portalDemoStore) {
  const extractOcrForStore = (body) =>
    extractDemoReceiptOcr(body, {
      issueToken: true,
      ocrTokens: store.state.ocrTokens,
      tokenPrefix: "mock_ocr_token_",
      nextTimestampMs: store.nextWalletTimestampMs,
    });

  const evaluateExpenseForStore = (body, wallet = null) =>
    evaluateDemoExpenseClaim(body, {
      wallet,
      ocrTokens: store.state.ocrTokens,
      consumedOcrTokens: store.state.consumedOcrTokens,
      recordedReceiptDigests: store.state.recordedReceiptDigests,
      claimPrefix: "claim_",
      defaultSubjectId: DEFAULT_MOCK_SUBJECT_ID,
      nextTimestampMs: store.nextWalletTimestampMs,
      nextTimestampIso: store.nextWalletTimestampIso,
    });

  return [
    http.post("*/api/v1/ai/biometrics/verify-liveness", async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      if (body.fallback_mode) {
        return HttpResponse.json(
          {
            title: "Forbidden",
            status: 403,
            detail:
              "Coordinator biometric liveness fallback requires econsent_consent:capture permission.",
          },
          { status: 403 }
        );
      }

      const subject = body.subject_pseudonym || DEFAULT_MOCK_SUBJECT_ID;
      const frames = Array.isArray(body.video_frames) ? body.video_frames : [];
      const isLive = frames.length > 0;
      const score = isLive ? 0.98 : 0.15;
      const nowMs = store.nextWalletTimestampMs();
      const expiresAt = new Date(nowMs + 15 * 60 * 1000).toISOString();
      const token = `mock_liveness_token_${subject}_camera_${nowMs}`;

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
      const wallet = store.getOrCreateWallet(subjectId);
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
        return HttpResponse.json(extractOcrForStore(body));
      }
    ),

    http.post(
      "*/api/v1/payments/participant/expenses/ocr-extract",
      async ({ request }) => {
        const body = await request.json().catch(() => ({}));
        return HttpResponse.json(extractOcrForStore(body));
      }
    ),

    http.post(
      "*/api/v1/payments/participant/me/expenses",
      async ({ request }) => {
        const body = await request.json().catch(() => ({}));
        const wallet = store.getOrCreateWallet(
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
          wallet.expenseIdempotencyMap.has(idempotencyKey)
        ) {
          return HttpResponse.json(
            wallet.expenseIdempotencyMap.get(idempotencyKey)
          );
        }

        const evaluated = evaluateExpenseForStore(body, wallet);
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
      return HttpResponse.json(evaluateExpenseForStore(body));
    }),

    http.post(
      "*/api/v1/payments/participant/me/disburse",
      async ({ request }) => {
        const body = await request.json().catch(() => ({}));
        const wallet = store.getOrCreateWallet(
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
        const nowMs = store.nextWalletTimestampMs();
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
          createdAt: store.nextWalletTimestampIso(),
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
        const wallet = store.getOrCreateWallet(
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
          transactionId: `tx_${store.nextWalletTimestampMs()}`,
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
        id: "disb_" + store.nextWalletTimestampMs(),
        wallet_id: "wallet_" + subjectId,
        subject_id: subjectId,
        amount: amount,
        new_balance: amount * seq,
        currency: "USD",
        status: "COMPLETED",
        reference_number: "DISB-MOCK-" + seq,
        idempotency_key: body.idempotency_key || `seq_${seq}_${subjectId}`,
        dispatched_at: store.nextWalletTimestampIso(),
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
          id: "disb_" + store.nextWalletTimestampMs(),
          wallet_id: "wallet_" + subjectId,
          subject_id: subjectId,
          amount: amount,
          new_balance: amount,
          currency: "USD",
          status: "COMPLETED",
          reference_number: "DISB-MOCK",
          idempotency_key: body.idempotency_key || `disburse_${subjectId}`,
          dispatched_at: store.nextWalletTimestampIso(),
        });
      }
    ),
  ];
}

export function buildPortalGatewayHandlers(options = {}) {
  const demoActive =
    options.forceDemo !== undefined
      ? Boolean(options.forceDemo)
      : isDemoBuild();

  const activeStore = options.store || portalDemoStore;
  const actionHandlers = createPortalActionHandlers(activeStore);
  const demoHandlers = demoActive ? createPortalDemoHandlers(activeStore) : [];

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

  return [...actionHandlers, ...demoHandlers, boundaryHandler];
}

export function resetMockWalletLedger() {
  portalDemoStore.resetWalletLedger();
}

export const gatewayHandlers = buildPortalGatewayHandlers();
