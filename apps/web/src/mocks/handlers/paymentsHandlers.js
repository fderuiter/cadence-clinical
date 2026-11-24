import {
  evaluateDemoExpenseClaim,
  extractDemoReceiptOcr,
} from "demo-fixtures/mocks";
import { http, HttpResponse } from "msw";
import { readJsonBody } from "./shared.js";

export function createPaymentsHandlers(store) {
  return [
    http.get("*/api/v1/payments/participant/wallets", () =>
      HttpResponse.json(store.state.participantWallets)
    ),
    http.get("*/api/v1/payments/participant/stipend-rules", () =>
      HttpResponse.json(store.state.stipendRules)
    ),
    http.get("*/api/v1/payments/participant/expenses", () =>
      HttpResponse.json(store.state.expenseClaims)
    ),
    http.get("*/api/v1/payments/participant/transactions", () =>
      HttpResponse.json(store.state.walletTransactions)
    ),
    http.get(
      "*/api/v1/payments/participant/wallets/:walletId/tax-1099-status",
      ({ params }) => {
        const wallet =
          store.state.participantWallets.find(
            (w) => w.id === params.walletId
          ) || store.state.participantWallets[0];
        const taxableYtd = Number(wallet?.taxable_ytd || 0);
        const pct = Number(((taxableYtd / 600) * 100).toFixed(1));
        return HttpResponse.json({
          wallet_id: wallet?.id || params.walletId,
          taxable_ytd: taxableYtd,
          threshold_amount: 600,
          threshold_percentage: pct,
          warning_level:
            pct >= 100 ? "EXCEEDED" : pct >= 80 ? "APPROACHING" : "NORMAL",
          w9_status: wallet?.w9_status || "NOT_REQUIRED",
        });
      }
    ),
    http.get(
      "*/api/v1/payments/participant/wallets/:walletId/balance",
      ({ params }) => {
        const wallet =
          store.state.participantWallets.find(
            (w) => w.id === params.walletId
          ) || store.state.participantWallets[0];
        const pendingTotal = store.state.expenseClaims
          .filter((c) => c.status === "PENDING_REVIEW")
          .reduce((sum, c) => sum + Number(c.gross_amount || 0), 0);
        return HttpResponse.json({
          wallet_id: wallet?.id || params.walletId,
          available_balance: Number(wallet?.current_balance || 0),
          pending_claims_total: pendingTotal,
          lifetime_stipends: Number(wallet?.lifetime_stipends || 0),
          lifetime_reimbursements: Number(wallet?.lifetime_reimbursements || 0),
          lifetime_disbursements: Number(wallet?.lifetime_disbursements || 0),
          taxable_ytd: Number(wallet?.taxable_ytd || 0),
        });
      }
    ),
    http.post(
      "*/api/v1/payments/participant/wallets/:walletId/:action",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const wallet =
          store.state.participantWallets.find(
            (w) => w.id === params.walletId
          ) || store.state.participantWallets[0];
        const action = String(params.action);
        if (wallet && action === "issue-card") {
          wallet.card_status = "ACTIVE";
          wallet.virtual_card_mask = "**** **** **** 4821";
          wallet.virtual_card_token = `CARD-DEMO-${Date.now()}`;
        } else if (wallet && action === "submit-w9") {
          wallet.w9_status = "SUBMITTED";
          wallet.tax_id_last_four = String(
            body?.tax_id_last_four || body?.taxIdLastFour || "0000"
          );
        } else if (wallet && action === "verify-w9") {
          wallet.w9_status = "VERIFIED";
        } else if (wallet && action === "disburse") {
          const amount = Number(body?.amount ?? wallet.current_balance ?? 0);
          wallet.current_balance = Math.max(
            0,
            Number(wallet.current_balance || 0) - amount
          );
          wallet.lifetime_disbursements =
            Number(wallet.lifetime_disbursements || 0) + amount;
          store.state.walletTransactions.unshift({
            id: `wtx-${Date.now()}`,
            created_at: new Date().toISOString(),
            subject_id: wallet.subject_id,
            study_id: wallet.study_id,
            transaction_type: "DISBURSEMENT",
            amount: -amount,
            balance_after: wallet.current_balance,
            description: `Digital payout (${body?.disbursement_type || body?.disbursementType || "DIRECT_ACH"})`,
          });
        }
        return HttpResponse.json(wallet || { id: params.walletId });
      }
    ),
    http.post(
      "*/api/v1/payments/participant/events/visit-verified",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const subjectId = body?.subject_id || body?.subjectId || "SUBJ-1001";
        const wallet =
          store.state.participantWallets.find(
            (w) => w.subject_id === subjectId
          ) || store.state.participantWallets[0];
        const credited = 75;
        if (wallet) {
          wallet.current_balance =
            Number(wallet.current_balance || 0) + credited;
          wallet.lifetime_stipends =
            Number(wallet.lifetime_stipends || 0) + credited;
          wallet.taxable_ytd = Number(wallet.taxable_ytd || 0) + credited;
          store.state.walletTransactions.unshift({
            id: `wtx-${Date.now()}`,
            created_at: new Date().toISOString(),
            subject_id: wallet.subject_id,
            study_id: wallet.study_id,
            transaction_type: "VISIT_STIPEND",
            amount: credited,
            balance_after: wallet.current_balance,
            description: `Verified visit ${body?.visit_code || body?.visitCode || "VISIT"}`,
          });
        }
        return HttpResponse.json({
          amount: credited,
          current_balance: wallet?.current_balance ?? credited,
        });
      }
    ),
    http.post(
      "*/api/v1/payments/participant/expenses/ocr-extract",
      async ({ request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json(
          extractDemoReceiptOcr(body, {
            issueToken: true,
            allowTextOnlyToken: true,
            ocrTokens: store.state.paymentOcrTokens,
            tokenPrefix: "demo_ocr_token_",
            fallbackExtracted: {
              merchantName: "Demo Transit Authority",
              expenseDate: "2026-08-01", // deid-ignore
              grossAmount: 24.5,
              category: "TRANSIT",
              confidenceScore: 0.95,
            },
          })
        );
      }
    ),
    http.post("*/api/v1/payments/participant/expenses", async ({ request }) => {
      const body = await readJsonBody(request);
      const subjectId = body?.subject_id || body?.subjectId || "SUBJ-1001";
      const wallet =
        store.state.participantWallets.find(
          (w) => w.subject_id === subjectId
        ) || null;
      const created = evaluateDemoExpenseClaim(body, {
        wallet,
        ocrTokens: store.state.paymentOcrTokens,
        consumedOcrTokens: store.state.consumedPaymentOcrTokens,
        recordedReceiptDigests: store.state.recordedPaymentReceiptDigests,
        claimPrefix: "exp-",
        defaultSubjectId: "SUBJ-1001",
        defaultMerchantName: "Demo Receipt",
        defaultExpenseDate: "2026-08-01", // deid-ignore
      });
      store.state.expenseClaims.unshift(created);
      return HttpResponse.json(created, { status: 201 });
    }),
    http.post(
      "*/api/v1/payments/participant/expenses/:claimId/review",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const found = store.state.expenseClaims.find(
          (c) => c.id === params.claimId
        );
        if (found) {
          found.status = body?.status || "APPROVED";
        }
        return HttpResponse.json(
          found || { id: params.claimId, status: body?.status || "APPROVED" }
        );
      }
    ),
    http.get("*/api/v1/payments/lay-summaries", () =>
      HttpResponse.json([
        {
          id: "lsum-01",
          study_id: "DEMO-01",
          title: "Plain-Language Summary of Study DEMO-01",
          language: "en",
          reading_grade_level: 6.5,
          status: "PUBLISHED",
        },
      ])
    ),
  ];
}
