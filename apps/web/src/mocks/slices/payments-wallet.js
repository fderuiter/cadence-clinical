import {
  createDemoParticipantExpenseOcrRoute,
  createDemoParticipantExpenseSubmitRoute,
} from "demo-fixtures/routes";
import { readJsonBody } from "./shared.js";

export function createPaymentsWalletSliceStore(ctx) {
  const { state } = ctx;

  function reset() {
    state.participantWallets = [
      {
        id: "wallet-1001",
        subject_id: "SUBJ-1001",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        current_balance: 150,
        lifetime_stipends: 150,
        lifetime_reimbursements: 0,
        lifetime_disbursements: 0,
        taxable_ytd: 150,
        is_1099_threshold_exceeded: false,
        w9_status: "VERIFIED",
        currency: "USD",
        status: "ACTIVE",
        card_type: "RELOADABLE_DEBIT",
        card_status: "ACTIVE",
        virtual_card_mask: "**** **** **** 4821",
        virtual_card_expiry: "12/29",
        ach_routing_mask: "*****0021",
        ach_account_mask: "ACH-***4321",
        ach_account_holder: "Participant SUBJ-1001",
        anti_coercion_cap: 200,
      },
      {
        id: "wallet-1002",
        subject_id: "SUBJ-1002",
        study_id: "DEMO-01",
        site_id: "SITE-102",
        current_balance: 175,
        lifetime_stipends: 625,
        lifetime_reimbursements: 0,
        lifetime_disbursements: 450,
        taxable_ytd: 625,
        is_1099_threshold_exceeded: true,
        w9_status: "REQUIRED",
        currency: "USD",
        status: "ACTIVE",
        card_type: "RELOADABLE_DEBIT",
        card_status: "ACTIVE",
        virtual_card_mask: "**** **** **** 9012",
        virtual_card_expiry: "06/28",
        ach_routing_mask: "*****0021",
        ach_account_mask: "ACH-***9012",
        ach_account_holder: "Participant SUBJ-1002",
        anti_coercion_cap: 200,
      },
    ];

    state.stipendRules = [
      {
        id: "srule-01",
        visit_code: "V1_SCREENING",
        visit_name: "Screening",
        amount: 75,
        currency: "USD",
        taxable: true,
        anti_coercion_cap: 150,
        is_milestone: false,
      },
      {
        id: "srule-02",
        visit_code: "VISIT_BASELINE",
        visit_name: "Baseline (Day 1)",
        amount: 75,
        currency: "USD",
        taxable: true,
        anti_coercion_cap: 200,
        is_milestone: false,
      },
    ];

    state.expenseClaims = [];

    state.walletTransactions = [
      {
        id: "wtx-1001",
        created_at: "2026-08-01T10:00:00Z", // deid-ignore
        subject_id: "SUBJ-1001",
        study_id: "DEMO-01",
        transaction_type: "VISIT_STIPEND",
        amount: 150,
        balance_after: 150,
        description: "Baseline visit stipend credit",
      },
    ];
  }

  return {
    reset,
    methods: {},
  };
}

export function createPaymentsWalletSliceHandlers(
  store,
  { http, HttpResponse }
) {
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
    createDemoParticipantExpenseOcrRoute(
      { http, HttpResponse },
      {
        fallbackOnPlainText: true,
        tokenPrefix: "demo_ocr_token_",
        emptyWarning:
          "Receipt text could not be extracted automatically in demo mode; enter claim details below.",
      }
    ),
    createDemoParticipantExpenseSubmitRoute(
      { http, HttpResponse },
      {
        status: 201,
        idPrefix: "exp-",
        defaultSubjectId: "SUBJ-1001",
        defaultMerchantName: "Demo Receipt",
        defaultExpenseDate: "2026-08-01", // deid-ignore
        requireServerScan: true,
        onEvaluated: (created) => {
          store.state.expenseClaims.unshift(created);
        },
      }
    ),
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
